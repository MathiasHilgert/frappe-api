using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Owners;
using Frappe.Localization;
using NodaTime;

namespace Frappe.Identity.UnitTests.Owners;

/// <summary>What an owner records of itself (S3, S6, S17): registering, being replaced, the codes sent to it and its verification.</summary>
public sealed class OwnerTests
{
    private static readonly Instant Start = Instant.FromUtc(2026, 11, 24, 12, 0);

    private static readonly LanguageTag Spanish = LanguageTag.Create("es-AR").Value;

    private static OwnerName Ana => OwnerName.Create("Ana", "Pérez").Value;

    private static Owner Pending() => Owner.Register(Guid.CreateVersion7(), Ana, Spanish, Start);

    [Test]
    public async Task Registering_raises_the_registration_as_new()
    {
        // Act
        var owner = Pending();

        // Assert
        var registered = owner.DomainEvents.OfType<OwnerRegistered>().Single();
        using (Assert.Multiple())
        {
            await Assert.That(registered.OwnerId).IsEqualTo(owner.PrincipalId);
            await Assert.That(registered.Replaced).IsFalse();
            await Assert.That(owner.RegisteredAt).IsEqualTo(Start);
            await Assert.That(owner.VerifiedAt).IsNull();
        }
    }

    [Test]
    public async Task Replacing_a_pending_owner_makes_a_new_owner_of_the_new_principal_with_the_new_name_and_a_restarted_purge_clock()
    {
        // Arrange
        var owner = Pending();
        owner.DequeueDomainEvents();
        var later = Start + Duration.FromDays(3);
        var principalId = Guid.CreateVersion7();

        // Act
        var result = owner.ReplaceWith(principalId, OwnerName.Create("Luz", null).Value, LanguageTag.Create("pt-BR").Value, later);

        // Assert
        var replacement = result.Value;
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(replacement.PrincipalId).IsEqualTo(principalId);
            await Assert.That(replacement.GivenName).IsEqualTo("Luz");
            await Assert.That(replacement.FamilyName).IsNull();
            await Assert.That(replacement.Locale.Value).IsEqualTo("pt-BR");
            await Assert.That(replacement.RegisteredAt).IsEqualTo(later);
            await Assert.That(replacement.DomainEvents.OfType<OwnerRegistered>().Single().Replaced).IsTrue();
            await Assert.That(owner.GivenName).IsEqualTo("Ana");
            await Assert.That(owner.DomainEvents).IsEmpty();
        }
    }

    [Test]
    public async Task A_verified_owner_cannot_be_replaced()
    {
        // Arrange
        var owner = Pending();
        owner.Verify(Start + Duration.FromMinutes(1));

        // Act
        var result = owner.ReplaceWith(Guid.CreateVersion7(), Ana, Spanish, Start + Duration.FromDays(1));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(owner.RegisteredAt).IsEqualTo(Start);
        }
    }

    [Test]
    public async Task Verifying_stamps_the_instant_once_and_raises_the_fact()
    {
        // Arrange
        var owner = Pending();
        owner.DequeueDomainEvents();

        // Act
        var first = owner.Verify(Start + Duration.FromMinutes(2));
        var second = owner.Verify(Start + Duration.FromMinutes(3));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.IsSuccess).IsTrue();
            await Assert.That(second.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.CodeInvalid);
            await Assert.That(owner.VerifiedAt).IsEqualTo(Start + Duration.FromMinutes(2));
            await Assert.That(owner.DomainEvents.OfType<OwnerVerified>().Count()).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Recording_a_code_carries_what_the_mail_needs_and_prints_none_of_it()
    {
        // Arrange
        var owner = Pending();
        owner.DequeueDomainEvents();

        // Act
        owner.RecordCodeIssued(CodePurpose.EmailVerification, "ana@example.com", "482913", Start);

        // Assert
        var issued = owner.DomainEvents.OfType<OwnerCodeIssued>().Single();
        var text = issued.ToString();
        using (Assert.Multiple())
        {
            await Assert.That(issued.Purpose).IsEqualTo(CodePurpose.EmailVerification);
            await Assert.That(issued.Email).IsEqualTo("ana@example.com");
            await Assert.That(issued.Name).IsEqualTo("Ana Pérez");
            await Assert.That(issued.Language).IsEqualTo("es-AR");
            await Assert.That(issued.Code).IsEqualTo("482913");
            await Assert.That(text).DoesNotContain("482913").And.DoesNotContain("ana@example.com").And.DoesNotContain("Ana");
        }
    }

    [Test]
    public async Task A_registration_attempt_carries_the_owners_address_and_language_and_prints_no_address()
    {
        // Arrange
        var owner = Pending();
        owner.DequeueDomainEvents();

        // Act
        owner.RecordRegistrationAttempted("ana@example.com", Start);

        // Assert
        var attempted = owner.DomainEvents.OfType<OwnerRegistrationAttempted>().Single();
        using (Assert.Multiple())
        {
            await Assert.That(attempted.Email).IsEqualTo("ana@example.com");
            await Assert.That(attempted.Language).IsEqualTo("es-AR");
            await Assert.That(attempted.ToString()).DoesNotContain("ana@example.com");
        }
    }
}
