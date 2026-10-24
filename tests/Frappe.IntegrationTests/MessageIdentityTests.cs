using Frappe.Identity.Contracts;
using Frappe.Identity.Contracts.PasswordResets;
using Shouldly;
using Wolverine.Util;

namespace Frappe.IntegrationTests;

/// <summary>Once a host has started, Wolverine names Contracts messages by their [MessageIdentity], not the CLR type.</summary>
public sealed class MessageIdentityTests(ApiFixture fixture)
{
    [Fact]
    public void ContractsMessages_TravelUnderTheirStableIdentity()
    {
        _ = fixture.Host;

        typeof(OwnerVerificationCodeIssued).ToMessageTypeName().ShouldBe("identity.owner_verification_code_issued.v1");
        typeof(PasswordResetCodeIssued).ToMessageTypeName().ShouldBe("identity.password_reset_code_issued.v1");
    }
}
