using System.Reflection;
using Frappe.Kernel.Messaging;
using Wolverine.Util;

namespace Frappe.Platform.Messaging;

// Stores a message under its Kernel [MessageIdentity] name, so module code never references Wolverine's
// attribute and a renamed class still reads persisted envelopes.
internal sealed class KernelMessageIdentityNaming : IMessageTypeNaming
{
    private static readonly Lock _install = new();
    private static bool _installed;

    // Wolverine keeps its naming strategies in one process-wide, unsynchronized list, and every host built in
    // the process (tests build several in parallel) registers the bus; only the first one touches the list.
    public static void InstallOnce()
    {
        lock (_install)
        {
            if (!_installed)
            {
                WolverineMessageNaming.InsertFirst<KernelMessageIdentityNaming>();
                _installed = true;
            }
        }
    }

    public bool TryDetermineName(Type messageType, out string messageTypeName)
    {
        messageTypeName = messageType.GetCustomAttribute<MessageIdentityAttribute>()?.Name ?? "";
        return messageTypeName.Length > 0;
    }
}
