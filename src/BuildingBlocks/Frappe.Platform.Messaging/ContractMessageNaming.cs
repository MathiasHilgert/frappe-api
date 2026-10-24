using System.Reflection;
using Frappe.Contracts.Abstractions.Messaging;
using Wolverine.Util;

namespace Frappe.Platform.Messaging;

/// <summary>
/// Names a message by its <see cref="MessageIdentityAttribute"/>, so Contracts carry the stable wire name without a
/// dependency on Wolverine. Messages without one keep Wolverine's default naming.
/// </summary>
internal sealed class ContractMessageNaming : IMessageTypeNaming
{
    public bool TryDetermineName(Type messageType, out string messageTypeName)
    {
        messageTypeName = messageType.GetCustomAttribute<MessageIdentityAttribute>()?.Identity ?? string.Empty;
        return messageTypeName.Length > 0;
    }
}
