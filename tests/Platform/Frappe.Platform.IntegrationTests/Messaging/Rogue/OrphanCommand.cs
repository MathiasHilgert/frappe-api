using Frappe.Kernel.Authorization;
using Frappe.Kernel.Messaging;

namespace Frappe.Platform.IntegrationTests.Messaging.Rogue.Application;

// A command no handler runs.
[MessageIdentity("rogue.orphan")]
public sealed record OrphanCommand : ICommand<OrphanCommand>
{
    public static Policy Policy { get; } = new("rogue.orphan.run");
}
