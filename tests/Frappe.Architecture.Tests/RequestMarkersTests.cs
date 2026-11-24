using System.Reflection;
using Frappe.Access.Contracts;

namespace Frappe.Architecture.Tests;

/// <summary>Which requests are public and which are guarded is a decision (S7): flipping one is a visible diff here, not a silent change.</summary>
public sealed class RequestMarkersTests
{
    private static readonly Assembly[] Applications =
    [
        System.Reflection.Assembly.Load("Frappe.Identity.Application"),
        System.Reflection.Assembly.Load("Frappe.Notifications.Application"),
    ];

    private static string[] Marked(Type marker) =>
        [.. Applications.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && marker.IsAssignableFrom(type))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)];

    [Test]
    public async Task The_public_requests_are_exactly_the_ones_with_a_proof_of_their_own()
    {
        // Act
        var requests = Marked(typeof(IPublic));

        // Assert
        await Assert.That(requests).IsEquivalentTo(
        [
            "AuthenticateSession", "LogInOwner", "RecordProviderFeedback", "RegisterOwner", "RequestPasswordReset", "ResendOwnerVerification",
            "ResetPassword", "SendNotification", "TouchSession", "UndoEmailChange", "Unsubscribe", "VerifyOwnerEmail",
        ]);
    }

    [Test]
    public async Task The_guarded_requests_are_exactly_the_owner_and_session_ones()
    {
        // Act
        var requests = Marked(typeof(IGuarded));

        // Assert
        await Assert.That(requests).IsEquivalentTo(
        [
            "ChangePassword", "ConfirmEmailChange", "GetCurrentSession", "GetMe", "ListMySessions", "LogOut", "RequestEmailChange",
            "RevokeMyOtherSessions", "RevokeMySession", "StepUp", "UpdateMe",
        ]);
    }
}
