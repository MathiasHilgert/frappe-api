using System.Net;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Domain.Identifiers;
using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Localization;
using Frappe.Persistence;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql;

namespace Frappe.Identity.IntegrationTests;

/// <summary>The Identity migration on a real Postgres, run by the Migrator's phases: its tables, indexes, model and reruns.</summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class IdentityMigrationTests(PostgresServer server)
{
    private static IdentityDbContext ContextOf(string owner) =>
        new(ModuleDatabase.Options<IdentityDbContext>(owner, IdentityDbContext.Schema));

    [Test]
    public async Task The_migration_creates_the_module_schema_with_its_six_tables_and_its_outbox_and_inbox()
    {
        await MigratedIdentity.RunFreshAsync(server, async owner =>
        {
            // Act
            var tables = await NamesAsync(owner, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'identity'");

            // Assert
            await Assert.That(tables).IsEquivalentTo(
                [
                    "__ef_migrations_history", "email_change_undos", "email_identifiers", "inbox_messages", "one_time_codes", "outbox_messages",
                    "owners",
                    "password_credentials", "principals", "sessions",
                ]);
        });
    }

    [Test]
    public async Task The_migrated_database_matches_the_model_and_a_rerun_changes_nothing()
    {
        await MigratedIdentity.RunFreshAsync(server, async owner =>
        {
            // Arrange
            await using var context = ContextOf(owner);
            var appliedFirst = (await context.Database.GetAppliedMigrationsAsync()).ToList();

            // Act
            await MigratedIdentity.MigrateAsync(owner);
            var pending = await context.Database.GetPendingMigrationsAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(pending).IsEmpty();
                await Assert.That(applied).IsEquivalentTo(appliedFirst);
                await Assert.That(applied).IsNotEmpty();
                await Assert.That(context.Database.HasPendingModelChanges()).IsFalse();
            }
        });
    }

    [Test]
    public async Task Undoing_every_migration_drops_no_xmin_column_because_xmin_is_a_system_column_the_table_always_has()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await using var context = ContextOf(owner);
            var last = context.Database.GetMigrations().Last();

            // Act
            var down = context.GetService<IMigrator>().GenerateScript(fromMigration: last, toMigration: Migration.InitialDatabase);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(down).Contains("DROP TABLE");
                await Assert.That(down).DoesNotContain("xmin");
            }
        });
    }

    [Test]
    public async Task Every_index_is_the_one_the_data_design_names_and_nothing_else_is_indexed_but_the_keys_and_the_outbox()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Act
            var indexes = await NamesAsync(owner, """
                SELECT indexname FROM pg_indexes WHERE schemaname = 'identity'
                  AND tablename IN ('principals', 'email_identifiers', 'password_credentials', 'sessions', 'one_time_codes',
                  'email_change_undos', 'owners')
                """);

            // Assert
            await Assert.That(indexes).IsEquivalentTo(
                [
                    "pk_principals", "pk_email_identifiers", "pk_password_credentials", "pk_sessions", "pk_one_time_codes", "pk_email_change_undos",
                    "pk_owners",
                    "ux_email_identifiers_normalized_address", "ux_email_identifiers_principal_id", "ux_sessions_token_hash",
                    "ix_sessions_principal_id", "ix_sessions_ended_at", "ux_one_time_codes_live", "ix_one_time_codes_expires_at",
                    "ux_email_change_undos_live", "ix_email_change_undos_expires_at",
                    "ix_owners_registered_at",
                ]);
        });
    }

    [Test]
    public async Task The_partial_indexes_carry_the_predicates_the_purges_and_the_live_code_rule_read()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Act
            async Task<string> DefinitionAsync(string index) =>
                (string)(await ScratchDatabase.ScalarAsync(owner, $"SELECT indexdef FROM pg_indexes WHERE indexname = '{index}'"))!;
            var ended = await DefinitionAsync("ix_sessions_ended_at");
            var principal = await DefinitionAsync("ix_sessions_principal_id");
            var live = await DefinitionAsync("ux_one_time_codes_live");
            var pending = await DefinitionAsync("ix_owners_registered_at");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(ended).Contains("(ended_at)").And.Contains("WHERE (ended_at IS NOT NULL)");
                await Assert.That(principal).DoesNotContain("WHERE");
                await Assert.That(live).Contains("UNIQUE").And.Contains("(principal_id, purpose)")
                    .And.Contains("WHERE ((redeemed_at IS NULL) AND (invalidated_at IS NULL))");
                await Assert.That(pending).Contains("(registered_at)").And.Contains("WHERE (verified_at IS NULL)");
            }
        });
    }

    [Test]
    public async Task The_column_types_are_the_ones_the_data_design_names()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Act
            var types = await NamesAsync(owner, """
                SELECT table_name || '.' || column_name || ':' || data_type || coalesce('(' || udt_name || ')', '')
                FROM information_schema.columns WHERE table_schema = 'identity'
                  AND (table_name, column_name) IN (('sessions', 'ip_prefix'), ('sessions', 'methods'), ('sessions', 'token_hash'),
                    ('one_time_codes', 'code_hmac'), ('principals', 'credential_stamp'), ('owners', 'locale'))
                """);

            // Assert
            await Assert.That(types).IsEquivalentTo(
                [
                    "sessions.ip_prefix:cidr(cidr)", "sessions.methods:ARRAY(_text)", "sessions.token_hash:bytea(bytea)",
                    "one_time_codes.code_hmac:bytea(bytea)", "principals.credential_stamp:uuid(uuid)",
                    "owners.locale:character varying(varchar)",
                ]);
        });
    }

    [Test]
    public async Task The_aggregates_are_written_through_the_model_and_read_back_whole_and_the_principal_cascades_to_all_of_them()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var now = Instant.FromUtc(2026, 11, 24, 12, 0);
            var principal = Principal.Create(PrincipalKind.Owner, PrincipalStatus.Pending, now);
            var email = EmailIdentifier.Create(principal.Id, "Ana@Example.com", "ana@example.com", null);
            var credential = PasswordCredential.Set(principal.Id, new PasswordHash("$argon2id$v=19$m=19456,t=2,p=1$c2FsdA$aGFzaA"), now);
            var tokenHash = new byte[32];
            tokenHash[0] = 7;
            var session = Session.Open(
                principal.Id, tokenHash, SessionAudience.Console, principal.CredentialStamp, now, Duration.FromHours(12), Duration.FromDays(30),
                "Mozilla/5.0", new IPNetwork(IPAddress.Parse("203.0.113.0"), 24));
            var codeHmac = CodeHmac.Create(new byte[32]).Value;
            var code = OneTimeCode.Issue(principal.Id, CodePurpose.EmailChange, "new@example.com", codeHmac, now, Duration.FromMinutes(15));
            var ownerRow = Owner.Register(principal.Id, "Ana", "Pérez", LanguageTag.Create("es-AR").Value, now);
            await using (var write = ContextOf(owner))
            {
                write.AddRange(principal, email, credential, session, code, ownerRow);
                await write.SaveChangesAsync();
            }

            // Act
            await using var read = ContextOf(owner);
            var readPrincipal = await read.Principals.SingleAsync();
            var readEmail = await read.EmailIdentifiers.SingleAsync();
            var readCredential = await read.PasswordCredentials.SingleAsync();
            var readSession = await read.Sessions.SingleAsync();
            var readCode = await read.OneTimeCodes.SingleAsync();
            var readOwner = await read.Owners.SingleAsync();
            await read.Principals.ExecuteDeleteAsync();
            var remaining = await ScratchDatabase.ScalarAsync(owner, """
                SELECT (SELECT count(*) FROM identity.email_identifiers) + (SELECT count(*) FROM identity.password_credentials)
                  + (SELECT count(*) FROM identity.sessions) + (SELECT count(*) FROM identity.one_time_codes) + (SELECT count(*) FROM identity.owners)
                """);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(readPrincipal.Kind).IsEqualTo(PrincipalKind.Owner);
                await Assert.That(readPrincipal.Status).IsEqualTo(PrincipalStatus.Pending);
                await Assert.That(readPrincipal.CredentialStamp).IsEqualTo(principal.CredentialStamp);
                await Assert.That(readPrincipal.CreatedAt).IsEqualTo(now);
                await Assert.That(readEmail.Address).IsEqualTo("Ana@Example.com");
                await Assert.That(readEmail.NormalizedAddress).IsEqualTo("ana@example.com");
                await Assert.That(readEmail.VerifiedAt).IsNull();
                await Assert.That(readCredential.Hash).IsEqualTo(credential.Hash);
                await Assert.That(readCredential.ChangedAt).IsEqualTo(now);
                await Assert.That(readSession.TokenHash).IsEquivalentTo(tokenHash);
                await Assert.That(readSession.Audience).IsEqualTo(SessionAudience.Console);
                await Assert.That(readSession.Methods).IsEquivalentTo([AuthenticationMethod.Pwd]);
                await Assert.That(readSession.CredentialStamp).IsEqualTo(principal.CredentialStamp);
                await Assert.That(readSession.IdleExpiresAt).IsEqualTo(now + Duration.FromHours(12));
                await Assert.That(readSession.AbsoluteExpiresAt).IsEqualTo(now + Duration.FromDays(30));
                await Assert.That(readSession.UserAgent).IsEqualTo("Mozilla/5.0");
                await Assert.That(readSession.IpPrefix).IsEqualTo(new IPNetwork(IPAddress.Parse("203.0.113.0"), 24));
                await Assert.That(readSession.EndedAt).IsNull();
                await Assert.That(readSession.RevocationReason).IsNull();
                await Assert.That(readCode.Purpose).IsEqualTo(CodePurpose.EmailChange);
                await Assert.That(readCode.Target).IsEqualTo("new@example.com");
                await Assert.That(readCode.ExpiresAt).IsEqualTo(now + Duration.FromMinutes(15));
                await Assert.That(readOwner.GivenName).IsEqualTo("Ana");
                await Assert.That(readOwner.FamilyName).IsEqualTo("Pérez");
                await Assert.That(readOwner.Locale.Value).IsEqualTo("es-AR");
                await Assert.That(readOwner.RegisteredAt).IsEqualTo(now);
                await Assert.That(readOwner.VerifiedAt).IsNull();
                await Assert.That(remaining).IsEqualTo(0L);
            }
        });
    }

    private static async Task<string[]> NamesAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        List<string> names = [];
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return [.. names];
    }
}
