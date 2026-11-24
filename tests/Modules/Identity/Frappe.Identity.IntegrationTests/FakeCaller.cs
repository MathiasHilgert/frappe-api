using Frappe.Access.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// The caller of a handler under test: a scoped holder the test fills with the principal and session the handler should see, as the session
/// scheme fills the real one on the Api. Unfilled, it is nobody.
/// </summary>
internal sealed class FakeCaller : ICaller
{
    public Guid? PrincipalId { get; private set; }

    public string? Kind { get; private set; }

    public Guid? SessionId { get; private set; }

    public DateTimeOffset? StepUpValidUntil => null;

    public bool IsSystem => false;

    /// <summary>Registers the holder as the scope's <see cref="ICaller"/>.</summary>
    public static IServiceCollection AddTo(IServiceCollection services)
    {
        services.AddScoped<FakeCaller>();
        return services.AddScoped<ICaller>(provider => provider.GetRequiredService<FakeCaller>());
    }

    /// <summary>Makes the scope's caller the owner <paramref name="principalId"/>, on <paramref name="sessionId"/> when it has one.</summary>
    public static FakeCaller ActAs(IServiceProvider scope, Guid? principalId, Guid? sessionId = null)
    {
        var caller = scope.GetRequiredService<FakeCaller>();
        caller.PrincipalId = principalId;
        caller.Kind = principalId is null ? null : "owner";
        caller.SessionId = sessionId;
        return caller;
    }
}
