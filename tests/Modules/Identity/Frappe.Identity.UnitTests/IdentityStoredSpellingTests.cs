using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using Frappe.Persistence;

namespace Frappe.Identity.UnitTests;

/// <summary>
/// The text each closed set stores (DATA-06): a member's name in lower snake case is what a database holds and what its CHECK lists, so
/// renaming a member would silently change both. Each test pins one enum's spellings, in declaration order.
/// </summary>
public sealed class IdentityStoredSpellingTests
{
    [Test]
    public async Task PrincipalKind_is_stored_as_owner()
    {
        // Act
        var list = StoredEnum<PrincipalKind>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'owner'");
    }

    [Test]
    public async Task PrincipalStatus_is_stored_as_pending_and_active()
    {
        // Act
        var list = StoredEnum<PrincipalStatus>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'pending', 'active'");
    }

    [Test]
    public async Task SessionAudience_is_stored_as_console()
    {
        // Act
        var list = StoredEnum<SessionAudience>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'console'");
    }

    [Test]
    public async Task AuthenticationMethod_is_stored_as_pwd()
    {
        // Act
        var list = StoredEnum<AuthenticationMethod>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'pwd'");
    }

    [Test]
    public async Task RevocationReason_is_stored_as_its_seven_reasons()
    {
        // Act
        var list = StoredEnum<RevocationReason>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo(
            "'logout', 'revoked_by_holder', 'others_revoked', 'password_changed', 'password_reset', 'email_changed', 'email_change_undone'");
    }

    [Test]
    public async Task CodePurpose_is_stored_as_email_verification_password_reset_and_email_change()
    {
        // Act
        var list = StoredEnum<CodePurpose>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'email_verification', 'password_reset', 'email_change'");
    }
}
