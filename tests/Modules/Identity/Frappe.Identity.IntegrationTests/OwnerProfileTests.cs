using Frappe.Identity.Application.Owners.UpdateMe;
using Frappe.Identity.Domain;
using Frappe.Results;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// Reading and changing an owner's own profile on a real Postgres (S10): the profile names the version of the owner's row, a merge patch changes
/// the name and the language whole or not at all, and an <c>If-Match</c> that no longer holds changes nothing.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class OwnerProfileTests(PostgresServer server)
{
    private static Patched<string> Text(string? value) => new(value);

    private static (Guid Id, UpdateMe Command) Patch(
        Guid id, Patched<string>? given = null, Patched<string>? family = null, Patched<string>? locale = null, string[]? ifMatch = null) =>
        (id, new UpdateMe(given, family, locale, ifMatch));

    [Test]
    public async Task The_profile_has_the_owners_name_address_language_and_the_version_of_their_row()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var registered = await host.RegisterAsync(given: "Ana", family: "Pérez", locale: "es-AR");

            // Act
            var me = await host.MeAsync(registered.Value.Id);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(me.IsSuccess).IsTrue();
                await Assert.That(me.Value.Id).IsEqualTo(registered.Value.Id);
                await Assert.That(me.Value.Kind).IsEqualTo("owner");
                await Assert.That(me.Value.GivenName).IsEqualTo("Ana");
                await Assert.That(me.Value.FamilyName).IsEqualTo("Pérez");
                await Assert.That(me.Value.Email).IsEqualTo("ana@example.com");
                await Assert.That(me.Value.Locale).IsEqualTo("es-AR");
                await Assert.That(uint.TryParse(me.Value.Version, out _)).IsTrue();
            }
        });
    }

    [Test]
    public async Task An_owner_that_is_gone_is_answered_like_an_invalid_session()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);

            // Act
            var me = await host.MeAsync(Guid.CreateVersion7());
            var updated = await host.UpdateMeAsync(Patch(Guid.CreateVersion7(), locale: Text("es")));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(me.Errors.Single()).IsEqualTo(new UnauthenticatedError(IdentityErrorCodes.SessionInvalid));
                await Assert.That(updated.Errors.Single()).IsEqualTo(new UnauthenticatedError(IdentityErrorCodes.SessionInvalid));
            }
        });
    }

    [Test]
    public async Task A_patch_changes_what_it_names_and_the_version_of_the_profile()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var id = (await host.RegisterAsync(given: "Ana", family: "Pérez", locale: "es-AR")).Value.Id;
            var before = (await host.MeAsync(id)).Value;

            // Act
            var updated = await host.UpdateMeAsync(Patch(id, given: Text("Anabel"), locale: Text("pt-BR")));

            // Assert
            var after = (await host.MeAsync(id)).Value;
            using (Assert.Multiple())
            {
                await Assert.That(updated.IsSuccess).IsTrue();
                await Assert.That(after.GivenName).IsEqualTo("Anabel");
                await Assert.That(after.FamilyName).IsEqualTo("Pérez");
                await Assert.That(after.Locale).IsEqualTo("pt-BR");
                await Assert.That(after.Version).IsNotEqualTo(before.Version);
            }
        });
    }

    [Test]
    public async Task The_family_name_can_be_removed_and_an_empty_patch_changes_nothing()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var id = (await host.RegisterAsync(given: "Ana", family: "Pérez")).Value.Id;
            var before = (await host.MeAsync(id)).Value;

            // Act
            var nothing = await host.UpdateMeAsync(Patch(id));
            var unchanged = (await host.MeAsync(id)).Value;
            var removed = await host.UpdateMeAsync(Patch(id, family: Text(null)));

            // Assert
            var after = (await host.MeAsync(id)).Value;
            using (Assert.Multiple())
            {
                await Assert.That(nothing.IsSuccess).IsTrue();
                await Assert.That(unchanged.Version).IsEqualTo(before.Version);
                await Assert.That(removed.IsSuccess).IsTrue();
                await Assert.That(after.GivenName).IsEqualTo("Ana");
                await Assert.That(after.FamilyName).IsNull();
            }
        });
    }

    [Test]
    public async Task A_patch_with_a_fault_in_any_member_changes_none_and_reports_every_fault()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var id = (await host.RegisterAsync(given: "Ana", family: "Pérez", locale: "es-AR")).Value.Id;
            var before = (await host.MeAsync(id)).Value;

            // Act
            var result = await host.UpdateMeAsync(Patch(id, given: Text("See http://spam.example"), locale: Text("fr")));

            // Assert
            var after = (await host.MeAsync(id)).Value;
            using (Assert.Multiple())
            {
                await Assert.That(result.Errors.Select(e => e.Code))
                    .IsEquivalentTo([IdentityErrorCodes.NameInvalid, IdentityErrorCodes.LocaleUnsupported]);
                await Assert.That(after).IsEqualTo(before);
            }
        });
    }

    [Test]
    [Arguments("fr", "identity.locale.unsupported")]
    [Arguments("xx", "identity.locale.unsupported")]
    [Arguments("not a tag", "localization.language_tag.invalid_format")]
    [Arguments("", "localization.language_tag.invalid_format")]
    public async Task A_language_Frappe_is_not_written_in_or_that_is_malformed_is_refused(string locale, string code)
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var id = (await host.RegisterAsync()).Value.Id;

            // Act
            var result = await host.UpdateMeAsync(Patch(id, locale: Text(locale)));

            // Assert
            await Assert.That(result.Errors.Single().Code).IsEqualTo(code);
        });
    }

    [Test]
    public async Task A_language_that_narrows_to_one_served_is_accepted_as_typed_but_normalized()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var id = (await host.RegisterAsync()).Value.Id;

            // Act
            var result = await host.UpdateMeAsync(Patch(id, locale: Text("ES-cl")));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That((await host.MeAsync(id)).Value.Locale).IsEqualTo("es-CL");
            }
        });
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task The_given_name_cannot_be_removed(string? given)
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var id = (await host.RegisterAsync()).Value.Id;

            // Act
            var result = await host.UpdateMeAsync(Patch(id, given: Text(given)));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(result.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.NameInvalid);
                await Assert.That((await host.MeAsync(id)).Value.GivenName).IsEqualTo("Ana");
            }
        });
    }

    [Test]
    public async Task An_If_Match_of_the_current_version_or_any_applies_the_change()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var id = (await host.RegisterAsync()).Value.Id;
            var version = (await host.MeAsync(id)).Value.Version;

            // Act
            var current = await host.UpdateMeAsync(Patch(id, given: Text("Uno"), ifMatch: ["999999999", version]));
            var any = await host.UpdateMeAsync(Patch(id, given: Text("Dos"), ifMatch: [UpdateMe.AnyVersion]));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(current.IsSuccess).IsTrue();
                await Assert.That(any.IsSuccess).IsTrue();
                await Assert.That((await host.MeAsync(id)).Value.GivenName).IsEqualTo("Dos");
            }
        });
    }

    [Test]
    public async Task A_stale_If_Match_changes_nothing_and_is_a_precondition_failure()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var id = (await host.RegisterAsync()).Value.Id;
            var stale = (await host.MeAsync(id)).Value.Version;
            await host.UpdateMeAsync(Patch(id, given: Text("Otra")));
            var current = (await host.MeAsync(id)).Value;

            // Act
            var result = await host.UpdateMeAsync(Patch(id, given: Text("Ana"), ifMatch: [stale]));
            var unreadable = await host.UpdateMeAsync((id, new UpdateMe(Text("Ana"), null, null, [])));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(result.Errors.Single()).IsEqualTo(new PreconditionFailedError(IdentityErrorCodes.ProfileChanged));
                await Assert.That(unreadable.Errors.Single()).IsEqualTo(new PreconditionFailedError(IdentityErrorCodes.ProfileChanged));
                await Assert.That((await host.MeAsync(id)).Value).IsEqualTo(current);
            }
        });
    }

    [Test]
    public async Task A_patch_that_is_invalid_outranks_a_stale_precondition()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var id = (await host.RegisterAsync()).Value.Id;

            // Act
            var result = await host.UpdateMeAsync(Patch(id, locale: Text("fr"), ifMatch: ["1"]));

            // Assert
            await Assert.That(result.Errors.Select(e => e.Code)).IsEquivalentTo([IdentityErrorCodes.LocaleUnsupported]);
        });
    }

    [Test]
    public async Task The_profile_of_an_owner_is_not_another_owners()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var host = OwnersHost.Over(owner);
            var ana = (await host.RegisterAsync("ana@example.com", given: "Ana")).Value.Id;
            var bob = (await host.RegisterAsync("bob@example.com", given: "Bob")).Value.Id;

            // Act
            await host.UpdateMeAsync(Patch(bob, given: Text("Roberto")));

            // Assert
            await using var db = host.Context();
            using (Assert.Multiple())
            {
                await Assert.That((await host.MeAsync(ana)).Value.GivenName).IsEqualTo("Ana");
                await Assert.That((await host.MeAsync(bob)).Value.GivenName).IsEqualTo("Roberto");
                await Assert.That((await host.MeAsync(bob)).Value.Email).IsEqualTo("bob@example.com");
                await Assert.That(await db.Owners.CountAsync()).IsEqualTo(2);
            }
        });
    }
}
