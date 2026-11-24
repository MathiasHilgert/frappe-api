using Frappe.Access.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Access.UnitTests;

/// <summary>Builds the context a rule reads, without a container around it.</summary>
internal static class RuleContexts
{
    public static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly IServiceProvider Services = new ServiceCollection().BuildServiceProvider();

    public static RuleContext For(ICaller caller, Guid? holder = null) => new(caller, Now, Services, HolderId: holder);

    public static Caller Signed(string kind = "owner", TimeSpan? stepUpFor = null, Guid? principal = null) =>
        new(principal ?? AccessWorld.Principal, kind, Guid.NewGuid(), stepUpFor is { } window ? Now + window : null);
}
