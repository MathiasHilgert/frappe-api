using Frappe.Testing.Containers;
using Npgsql;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// The database refuses the rows the invariants forbid (DATA-07): each case breaks exactly one rule of an otherwise valid row and names the
/// constraint that refused it, and the valid row of every table is accepted.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class IdentityConstraintTests(PostgresServer server)
{
    private const string Principal = "'0198f2a0-0000-7000-8000-000000000001'";
    private const string Hash = "decode(repeat('ab', 32), 'hex')";

    private static readonly Dictionary<string, string> ValidPrincipal = new()
    {
        ["id"] = Principal,
        ["kind"] = "'owner'",
        ["status"] = "'pending'",
        ["credential_stamp"] = "gen_random_uuid()",
        ["created_at"] = "now()",
    };

    private static readonly Dictionary<string, string> ValidEmail = new()
    {
        ["id"] = "gen_random_uuid()",
        ["principal_id"] = Principal,
        ["address"] = "'Ana@Example.com '",
        ["normalized_address"] = "'ana@example.com'",
    };

    private static readonly Dictionary<string, string> ValidCredential = new()
    {
        ["principal_id"] = Principal,
        ["hash"] = "'$argon2id$v=19$m=19456,t=2,p=1$c2FsdA$aGFzaA'",
        ["changed_at"] = "now()",
    };

    private static readonly Dictionary<string, string> ValidSession = new()
    {
        ["id"] = "gen_random_uuid()",
        ["principal_id"] = Principal,
        ["token_hash"] = Hash,
        ["audience"] = "'console'",
        ["methods"] = "ARRAY['pwd']",
        ["credential_stamp"] = "gen_random_uuid()",
        ["opened_at"] = "now()",
        ["last_seen_at"] = "now()",
        ["idle_expires_at"] = "now() + interval '12 hours'",
        ["absolute_expires_at"] = "now() + interval '30 days'",
    };

    private static readonly Dictionary<string, string> ValidCode = new()
    {
        ["id"] = "gen_random_uuid()",
        ["principal_id"] = Principal,
        ["purpose"] = "'email_verification'",
        ["target"] = "'ana@example.com'",
        ["code_hmac"] = Hash,
        ["issued_at"] = "now()",
        ["expires_at"] = "now() + interval '15 minutes'",
    };

    private static readonly Dictionary<string, string> ValidUndo = new()
    {
        ["id"] = "gen_random_uuid()",
        ["principal_id"] = Principal,
        ["old_address"] = "'ana@example.com'",
        ["new_address"] = "'luz@example.com'",
        ["token_hmac"] = Hash,
        ["issued_at"] = "now()",
        ["expires_at"] = "now() + interval '7 days'",
    };

    private static readonly Dictionary<string, string> ValidOwner = new()
    {
        ["principal_id"] = Principal,
        ["given_name"] = "'Ana'",
        ["locale"] = "'es-AR'",
        ["registered_at"] = "now()",
    };

    private static string Insert(string table, Dictionary<string, string> valid, params (string Column, string Value)[] overrides)
    {
        var row = new Dictionary<string, string>(valid);
        foreach (var (column, value) in overrides)
        {
            row[column] = value;
        }

        return $"INSERT INTO identity.{table} ({string.Join(", ", row.Keys)}) VALUES ({string.Join(", ", row.Values)})";
    }

    private static Task SeedPrincipalAsync(string owner) => ScratchDatabase.ExecuteAsync(owner, Insert("principals", ValidPrincipal));

    public static IEnumerable<(string Sql, string Constraint)> RefusedRows()
    {
        yield return (Insert("principals", ValidPrincipal, ("kind", "'staff'")), "ck_principals_kind");
        yield return (Insert("principals", ValidPrincipal, ("status", "'locked'")), "ck_principals_status");
        yield return (Insert("sessions", ValidSession, ("audience", "'mobile'")), "ck_sessions_audience");
        yield return (Insert("sessions", ValidSession, ("token_hash", "decode(repeat('ab', 31), 'hex')")), "ck_sessions_token_hash_length");
        yield return (Insert("sessions", ValidSession, ("idle_expires_at", "now() + interval '31 days'")), "ck_sessions_idle_within_absolute");
        yield return (Insert("sessions", ValidSession, ("last_seen_at", "now() - interval '1 hour'")), "ck_sessions_seen_after_opened");
        yield return (Insert("sessions", ValidSession, ("revoked_at", "now()")), "ck_sessions_revocation_pair");
        yield return (Insert("sessions", ValidSession, ("revocation_reason", "'logout'")), "ck_sessions_revocation_pair");
        yield return (Insert("sessions", ValidSession, ("revoked_at", "now()"), ("revocation_reason", "'banned'")), "ck_sessions_revocation_reason");
        yield return (Insert("sessions", ValidSession,
            ("revoked_at", "now()"), ("revocation_reason", "'logout'"), ("ended_at", "now() - interval '1 hour'")),
            "ck_sessions_ended_after_revoked");
        yield return (Insert("sessions", ValidSession, ("methods", "ARRAY[]::text[]")), "ck_sessions_methods");
        yield return (Insert("sessions", ValidSession, ("methods", "ARRAY['pwd', 'totp']")), "ck_sessions_methods");
        yield return (Insert("sessions", ValidSession, ("ip_prefix", "'10.0.0.0/16'")), "ck_sessions_ip_prefix");
        yield return (Insert("sessions", ValidSession, ("ip_prefix", "'2001:db8::/64'")), "ck_sessions_ip_prefix");
        yield return (Insert("sessions", ValidSession, ("ip_prefix", "'2001::/24'")), "ck_sessions_ip_prefix");
        yield return (Insert("one_time_codes", ValidCode, ("purpose", "'login'")), "ck_one_time_codes_purpose");
        yield return (Insert("one_time_codes", ValidCode, ("code_hmac", "decode(repeat('ab', 20), 'hex')")), "ck_one_time_codes_code_hmac_length");
        yield return (Insert("one_time_codes", ValidCode, ("expires_at", "now() - interval '1 minute'")), "ck_one_time_codes_expires_after_issued");
        yield return (Insert("one_time_codes", ValidCode, ("redeemed_at", "now() + interval '1 hour'")), "ck_one_time_codes_redeemed_in_window");
        yield return (Insert("one_time_codes", ValidCode, ("redeemed_at", "now() - interval '1 hour'")), "ck_one_time_codes_redeemed_in_window");
        yield return (Insert("one_time_codes", ValidCode, ("invalidated_at", "now() - interval '1 hour'")),
            "ck_one_time_codes_invalidated_after_issued");
        yield return (Insert("email_change_undos", ValidUndo, ("new_address", "'ana@example.com'")), "ck_email_change_undos_addresses_differ");
        yield return (Insert("owners", ValidOwner, ("verified_at", "now() - interval '1 hour'")), "ck_owners_verified_after_registered");
    }

    [Test]
    [MethodDataSource(nameof(RefusedRows))]
    public async Task A_row_that_breaks_one_invariant_is_refused_naming_the_constraint(string sql, string constraint)
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            if (!sql.StartsWith("INSERT INTO identity.principals ", StringComparison.Ordinal))
            {
                await SeedPrincipalAsync(owner);
            }

            // Act
            var refused = await Assert.ThrowsAsync<PostgresException>(() => ScratchDatabase.ExecuteAsync(owner, sql));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(refused!.SqlState).IsEqualTo(PostgresErrorCodes.CheckViolation);
                await Assert.That(refused.ConstraintName).IsEqualTo(constraint);
            }
        });
    }

    [Test]
    public async Task The_valid_row_of_every_table_is_accepted_and_a_session_may_carry_a_truncated_ip_of_either_family()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            string[] accepted =
            [
                Insert("principals", ValidPrincipal),
                Insert("email_identifiers", ValidEmail),
                Insert("password_credentials", ValidCredential),
                Insert("sessions", ValidSession),
                Insert("sessions", ValidSession, ("token_hash", "decode(repeat('cd', 32), 'hex')"), ("ip_prefix", "'203.0.113.0/24'")),
                Insert("sessions", ValidSession, ("token_hash", "decode(repeat('ef', 32), 'hex')"), ("ip_prefix", "'2001:db8:1::/48'"),
                    ("user_agent", "'Mozilla/5.0'"), ("revoked_at", "now()"), ("revocation_reason", "'logout'"), ("ended_at", "now()")),
                Insert("one_time_codes", ValidCode),
                Insert("owners", ValidOwner, ("family_name", "'Pérez'"), ("verified_at", "now()")),
            ];

            // Act
            foreach (var sql in accepted)
            {
                await ScratchDatabase.ExecuteAsync(owner, sql);
            }

            // Assert
            await Assert.That(await ScratchDatabase.ScalarAsync(owner, "SELECT count(*) FROM identity.sessions")).IsEqualTo(3L);
        });
    }

    [Test]
    public async Task An_address_belongs_to_one_principal_by_its_normalized_form_and_a_principal_has_one_address()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await SeedPrincipalAsync(owner);
            await ScratchDatabase.ExecuteAsync(owner, Insert("email_identifiers", ValidEmail));
            const string other = "'0198f2a0-0000-7000-8000-000000000002'";
            await ScratchDatabase.ExecuteAsync(
                owner, Insert("principals", ValidPrincipal, ("id", other)));

            // Act
            var sameAddress = await Assert.ThrowsAsync<PostgresException>(() => ScratchDatabase.ExecuteAsync(
                owner, Insert("email_identifiers", ValidEmail, ("principal_id", other), ("address", "'ANA@example.com'"))));
            var secondAddress = await Assert.ThrowsAsync<PostgresException>(() => ScratchDatabase.ExecuteAsync(
                owner, Insert("email_identifiers", ValidEmail, ("normalized_address", "'other@example.com'"))));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(sameAddress!.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
                await Assert.That(sameAddress.ConstraintName).IsEqualTo("ux_email_identifiers_normalized_address");
                await Assert.That(secondAddress!.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
                await Assert.That(secondAddress.ConstraintName).IsEqualTo("ux_email_identifiers_principal_id");
            }
        });
    }

    [Test]
    public async Task An_address_longer_than_254_characters_is_refused_by_its_column()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await SeedPrincipalAsync(owner);

            // Act
            var refused = await Assert.ThrowsAsync<PostgresException>(() => ScratchDatabase.ExecuteAsync(
                owner, Insert("email_identifiers", ValidEmail, ("address", $"'{new string('a', 255)}'"))));

            // Assert
            await Assert.That(refused!.SqlState).IsEqualTo(PostgresErrorCodes.StringDataRightTruncation);
        });
    }

    [Test]
    public async Task A_session_token_hash_identifies_one_session_and_a_user_agent_over_512_characters_is_refused()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await SeedPrincipalAsync(owner);
            await ScratchDatabase.ExecuteAsync(owner, Insert("sessions", ValidSession));

            // Act
            var sameToken = await Assert.ThrowsAsync<PostgresException>(() => ScratchDatabase.ExecuteAsync(owner, Insert("sessions", ValidSession)));
            var longAgent = await Assert.ThrowsAsync<PostgresException>(() => ScratchDatabase.ExecuteAsync(
                owner,
                Insert("sessions", ValidSession, ("token_hash", "decode(repeat('cd', 32), 'hex')"), ("user_agent", $"'{new string('a', 513)}'"))));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(sameToken!.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
                await Assert.That(sameToken.ConstraintName).IsEqualTo("ux_sessions_token_hash");
                await Assert.That(longAgent!.SqlState).IsEqualTo(PostgresErrorCodes.StringDataRightTruncation);
            }
        });
    }

    [Test]
    public async Task Only_one_code_per_principal_and_purpose_is_live_and_a_redeemed_or_invalidated_one_makes_room()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await SeedPrincipalAsync(owner);
            await ScratchDatabase.ExecuteAsync(owner, Insert("one_time_codes", ValidCode));

            // Act
            var second = await Assert.ThrowsAsync<PostgresException>(() => ScratchDatabase.ExecuteAsync(owner, Insert("one_time_codes", ValidCode)));
            await ScratchDatabase.ExecuteAsync(owner, Insert("one_time_codes", ValidCode, ("purpose", "'password_reset'")));
            await ScratchDatabase.ExecuteAsync(
                owner, "UPDATE identity.one_time_codes SET invalidated_at = now() WHERE purpose = 'email_verification'");
            await ScratchDatabase.ExecuteAsync(owner, Insert("one_time_codes", ValidCode));
            await ScratchDatabase.ExecuteAsync(owner, Insert("one_time_codes", ValidCode, ("redeemed_at", "now()")));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(second!.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
                await Assert.That(second.ConstraintName).IsEqualTo("ux_one_time_codes_live");
                await Assert.That(await ScratchDatabase.ScalarAsync(owner, "SELECT count(*) FROM identity.one_time_codes")).IsEqualTo(4L);
            }
        });
    }

    [Test]
    [Arguments("email_identifiers", "fk_email_identifiers_principal_id")]
    [Arguments("password_credentials", "fk_password_credentials_principal_id")]
    [Arguments("sessions", "fk_sessions_principal_id")]
    [Arguments("one_time_codes", "fk_one_time_codes_principal_id")]
    [Arguments("owners", "fk_owners_principal_id")]
    public async Task A_row_of_a_principal_that_does_not_exist_is_refused_naming_its_foreign_key(string table, string constraint)
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var valid = table switch
            {
                "email_identifiers" => ValidEmail,
                "password_credentials" => ValidCredential,
                "sessions" => ValidSession,
                "one_time_codes" => ValidCode,
                _ => ValidOwner,
            };

            // Act
            var refused = await Assert.ThrowsAsync<PostgresException>(() => ScratchDatabase.ExecuteAsync(owner, Insert(table, valid)));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(refused!.SqlState).IsEqualTo(PostgresErrorCodes.ForeignKeyViolation);
                await Assert.That(refused.ConstraintName).IsEqualTo(constraint);
            }
        });
    }

    [Test]
    public async Task Deleting_a_principal_deletes_everything_that_belongs_to_it()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await SeedPrincipalAsync(owner);
            foreach (var sql in new[]
            {
                Insert("email_identifiers", ValidEmail), Insert("password_credentials", ValidCredential), Insert("sessions", ValidSession),
                Insert("one_time_codes", ValidCode), Insert("owners", ValidOwner),
            })
            {
                await ScratchDatabase.ExecuteAsync(owner, sql);
            }

            // Act
            await ScratchDatabase.ExecuteAsync(owner, "DELETE FROM identity.principals");
            var remaining = await ScratchDatabase.ScalarAsync(owner, """
                SELECT (SELECT count(*) FROM identity.email_identifiers) + (SELECT count(*) FROM identity.password_credentials)
                  + (SELECT count(*) FROM identity.sessions) + (SELECT count(*) FROM identity.one_time_codes) + (SELECT count(*) FROM identity.owners)
                """);

            // Assert
            await Assert.That(remaining).IsEqualTo(0L);
        });
    }
}
