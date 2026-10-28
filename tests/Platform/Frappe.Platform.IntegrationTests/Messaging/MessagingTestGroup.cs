namespace Frappe.Platform.IntegrationTests.Messaging;

// Every test on the messaging host runs in this group, one after another, against one fixture.
[CollectionDefinition(Name)]
public sealed class MessagingTestGroup : ICollectionFixture<MessagingFixture>
{
    public const string Name = "Messaging";
}
