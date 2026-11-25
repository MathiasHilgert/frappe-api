using Frappe.Access.Application.Telemetry;
using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.Extensions.Logging;

namespace Frappe.Access.Application;

/// <summary>
/// The use-case gate of the Access module: it reads what the request declared (<see cref="UseCaseTable"/>), evaluates a guarded request's
/// rule against the live caller and answers a denial as 403 (<see cref="AccessErrorCodes.ActionForbidden"/>, or
/// <see cref="AccessErrorCodes.StepUpRequired"/> when a step-up is the only thing missing); Access never answers 401. A public request passes; an
/// undeclared one cannot reach a host (its startup check refuses it, S7); it passes and is logged
/// only where nothing validated the container. After the handler it reports (after the commit, so it
/// detects and does not undo) a request that <see cref="IActsOn{TResource}"/> a resource and succeeded without checking it.
/// </summary>
internal sealed partial class AccessGate(
    UseCaseTable table,
    AccessScope scope,
    ICaller caller,
    TimeProvider clock,
    IServiceProvider services,
    AccessMetrics metrics,
    ILogger<AccessGate> logger) : IUseCaseGate
{
    public async ValueTask<Error?> AdmitAsync(object request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var declaration = table.For(request.GetType());
        if (declaration.Rule is null)
        {
            return Pass(declaration, declaration.IsPublic ? AccessOutcome.Public : AccessOutcome.Undeclared);
        }

        var verdict = await EvaluateAsync(declaration, cancellationToken);
        if (verdict.IsAllowed)
        {
            return Pass(declaration, AccessOutcome.Allowed);
        }

        var stepUp = verdict.Reason == DenialReason.StepUpRequired;
        var outcome = stepUp ? AccessOutcome.StepUpRequired : AccessOutcome.Forbidden;
        metrics.Decided(declaration.Name, outcome);
        LogDenied(declaration.Name, outcome, verdict.Reason!.Value);
        return new ForbiddenError(stepUp ? AccessErrorCodes.StepUpRequired : AccessErrorCodes.ActionForbidden);
    }

    public ValueTask CompleteAsync(object request, bool succeeded, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var unmet = scope.Leave();
        return succeeded && unmet.Count > 0
            ? throw new InvalidOperationException(
                $"{request.GetType().Name} acts on {string.Join(", ", unmet.Select(resource => resource.Name))} and succeeded without "
                + "checking it through IAuthorizer.RefuseAsync.")
            : ValueTask.CompletedTask;
    }

    private Error? Pass(UseCaseDeclaration declaration, AccessOutcome outcome)
    {
        scope.Enter(declaration);
        metrics.Decided(declaration.Name, outcome);
        if (outcome == AccessOutcome.Undeclared)
        {
            LogUndeclared(declaration.Name);
        }

        return null;
    }

    private async ValueTask<Verdict> EvaluateAsync(UseCaseDeclaration declaration, CancellationToken cancellationToken)
    {
        try
        {
            return await declaration.Rule!.EvaluateAsync(new RuleContext(caller, clock.GetUtcNow(), services), cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // A rule that cannot decide refuses: failing open would let the request through on a fault.
            LogThrew(declaration.Name, exception.GetType().Name);
            return Verdict.Deny(DenialReason.NotAllowed);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Access {Outcome} for request {Request}: {Reason}")]
    private partial void LogDenied(string request, AccessOutcome outcome, DenialReason reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request {Request} declares neither IGuarded nor IPublic and ran unguarded")]
    private partial void LogUndeclared(string request);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The rule of request {Request} threw {ExceptionType} and the request was refused")]
    private partial void LogThrew(string request, string exceptionType);
}
