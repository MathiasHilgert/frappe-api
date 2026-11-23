namespace Frappe.Messaging;

/// <summary>Why a consumer failed outside the handler.</summary>
public enum ConsumerFailure
{
    /// <summary>The subscription broke and is created again after a pause.</summary>
    Subscription,

    /// <summary>A step that runs after the commit failed; the message stays committed.</summary>
    AfterCommit,
}
