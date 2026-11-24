using Frappe.Identity.Application.Credentials.ChangePassword;
using Frappe.Identity.Application.Owners.ConfirmEmailChange;
using Frappe.Identity.Application.Owners.GetMe;
using Frappe.Identity.Application.Owners.RequestEmailChange;
using Frappe.Identity.Application.Owners.UpdateMe;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Sessions.GetCurrentSession;
using Frappe.Identity.Application.Sessions.ListMySessions;
using Frappe.Identity.Application.Sessions.LogOut;
using Frappe.Identity.Application.Sessions.RevokeMyOtherSessions;
using Frappe.Identity.Application.Sessions.RevokeMySession;
using Frappe.Identity.Application.Sessions.StepUp;

namespace Frappe.Identity.UnitTests.Owners;

/// <summary>The rules Identity declares on its use cases (S7), one snapshot of each, so a change is a visible diff.</summary>
public sealed class IdentityRulesTests
{
    [Test]
    public async Task The_owner_account_rule_is_the_owner_kind()
    {
        // Act
        var description = IdentityRules.OwnerAccount.Describe();

        // Assert
        await Assert.That(description).IsEqualTo("Is.Kind(owner)");
    }

    [Test]
    public async Task Every_guarded_request_declares_the_rule_the_design_names()
    {
        // Act
        var declared = new Dictionary<string, string>
        {
            [nameof(GetMe)] = GetMe.Allowed.Describe(),
            [nameof(UpdateMe)] = UpdateMe.Allowed.Describe(),
            [nameof(RequestEmailChange)] = RequestEmailChange.Allowed.Describe(),
            [nameof(ConfirmEmailChange)] = ConfirmEmailChange.Allowed.Describe(),
            [nameof(GetCurrentSession)] = GetCurrentSession.Allowed.Describe(),
            [nameof(LogOut)] = LogOut.Allowed.Describe(),
            [nameof(StepUp)] = StepUp.Allowed.Describe(),
            [nameof(ChangePassword)] = ChangePassword.Allowed.Describe(),
            [nameof(ListMySessions)] = ListMySessions.Allowed.Describe(),
            [nameof(RevokeMyOtherSessions)] = RevokeMyOtherSessions.Allowed.Describe(),
            [nameof(RevokeMySession)] = RevokeMySession.Allowed.Describe(),
        };

        // Assert
        await Assert.That(declared).IsEquivalentTo(new Dictionary<string, string>
        {
            [nameof(GetMe)] = "Is.Kind(owner)",
            [nameof(UpdateMe)] = "Is.Kind(owner)",
            [nameof(RequestEmailChange)] = "Is.Kind(owner) & Has.RecentStepUp",
            [nameof(ConfirmEmailChange)] = "Is.Kind(owner)",
            [nameof(GetCurrentSession)] = "Is.Authenticated",
            [nameof(LogOut)] = "Is.Authenticated",
            [nameof(StepUp)] = "Is.Authenticated",
            [nameof(ChangePassword)] = "Is.Authenticated",
            [nameof(ListMySessions)] = "Is.Authenticated",
            [nameof(RevokeMyOtherSessions)] = "Is.Authenticated",
            [nameof(RevokeMySession)] = "Is.Authenticated",
        });
    }
}
