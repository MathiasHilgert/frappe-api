using Frappe.Access.Contracts;
using Frappe.Results;
using Microsoft.Extensions.Logging;

namespace Frappe.Access.Application;

/// <summary>
/// Checks the caller against a loaded resource with the rules the modules declared in the <see cref="RuleBook"/>. First visibility: a caller
/// that fails it, or is not the holder anywhere, is answered the resource's own not-found. Then the action's rule: forbidden, or step-up
/// required when that is all that is missing. Precedence is hidden, then forbidden, then step-up, whatever the order the rules were written
/// in. A resource type with no book, an action with no rule, or a rule that throws is hidden: fail closed (S9). Every call is noted in the
/// scope, which is how a request marked <see cref="IActsOn{TResource}"/> proves its handler checked.
/// </summary>
internal sealed partial class Authorizer(
    RuleBook book,
    AccessScope scope,
    ICaller caller,
    TimeProvider clock,
    IServiceProvider services,
    AccessMetrics metrics,
    ILogger<Authorizer> logger) : IAuthorizer
{
    private const string NoRules = "no_rules";
    private const string NoAction = "no_action";
    private const string NotVisible = "not_visible";
    private const string Threw = "threw";

    public async ValueTask<Error?> RefuseAsync<T>(ResourceAction<T> action, T resource, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(resource);
        scope.Checked(typeof(T));
        if (!book.TryGet(typeof(T), out var rules))
        {
            return Hidden(AccessErrorCodes.ResourceNotFound, NoRules);
        }

        try
        {
            return await DecideAsync(rules, action.Name, resource, cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            LogThrew(scope.CurrentRequest, exception.GetType().Name);
            return Hidden(rules.NotFoundCode, Threw);
        }
    }

    private async ValueTask<Error?> DecideAsync(ResourceRuleSet rules, string action, object resource, CancellationToken cancellationToken)
    {
        var context = new RuleContext(caller, clock.GetUtcNow(), services, resource, rules.HolderOf(resource));
        if (rules.Visible is not { } visible || !(await visible(context, cancellationToken)).IsAllowed)
        {
            return Hidden(rules.NotFoundCode, NotVisible);
        }

        if (rules.Allowed(action) is not { } allowed)
        {
            return Hidden(rules.NotFoundCode, NoAction);
        }

        var verdict = await allowed(context, cancellationToken);
        if (verdict.IsAllowed)
        {
            metrics.Decided(scope.CurrentRequest, AccessOutcome.Allowed);
            return null;
        }

        return verdict.Reason switch
        {
            DenialReason.NotHolder => Hidden(rules.NotFoundCode, nameof(DenialReason.NotHolder)),
            DenialReason.StepUpRequired => Refused(AccessOutcome.StepUpRequired, AccessErrorCodes.StepUpRequired, verdict.Reason.Value),
            _ => Refused(AccessOutcome.Forbidden, AccessErrorCodes.ActionForbidden, verdict.Reason!.Value),
        };
    }

    private NotFoundError Hidden(string code, string reason)
    {
        metrics.Decided(scope.CurrentRequest, AccessOutcome.Hidden);
        LogHidden(scope.CurrentRequest, AccessOutcome.Hidden, reason);
        return new NotFoundError(code);
    }

    private ForbiddenError Refused(AccessOutcome outcome, string code, DenialReason reason)
    {
        metrics.Decided(scope.CurrentRequest, outcome);
        LogDenied(scope.CurrentRequest, outcome, reason);
        return new ForbiddenError(code);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Access {Outcome} on a resource in request {Request}: {Reason}")]
    private partial void LogDenied(string request, AccessOutcome outcome, DenialReason reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Access {Outcome} on a resource in request {Request}: {Reason}")]
    private partial void LogHidden(string request, AccessOutcome outcome, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A resource rule in request {Request} threw {ExceptionType} and the resource was hidden")]
    private partial void LogThrew(string request, string exceptionType);
}
