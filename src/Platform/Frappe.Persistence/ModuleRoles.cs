using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace Frappe.Persistence;

/// <summary>
/// One Postgres login role per module, run by the Migrator with the owner role. A module role may read and write the
/// data of its own schema and nothing else: no DDL, no other schema, so a module cannot reach another module's tables
/// even by mistake.
/// </summary>
public static partial class ModuleRoles
{
    // PostgreSQL's own SCRAM-SHA-256 iteration count (scram_iterations). Every connection pays it on login; raising it
    // is a separate decision about connection cost, not a fix for this verifier.
    private const int ScramIterations = 4096;

    /// <summary>
    /// Creates the login role, or rotates its password when it already exists. Only a SCRAM-SHA-256 verifier computed
    /// here reaches the server, so the plain password can never appear in a server log, even when the statement fails.
    /// </summary>
    public static async Task EnsureLoginRoleAsync(NpgsqlConnection owner, string role, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        Identifier(role);

        await using var exists = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @role)", owner);
        exists.Parameters.AddWithValue("role", role);
        var statement = (bool)(await exists.ExecuteScalarAsync(cancellationToken))!
            ? "ALTER ROLE %I WITH LOGIN PASSWORD %L"
            : "CREATE ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT PASSWORD %L";

        // The verifier is quoted by the server (format %L), never interpolated: DDL takes no bind parameters.
        await using var render = new NpgsqlCommand($"SELECT format('{statement}', @role, @password)", owner);
        render.Parameters.AddWithValue("role", role);
        render.Parameters.AddWithValue("password", ScramVerifier(password));
        var ddl = (string)(await render.ExecuteScalarAsync(cancellationToken))!;

        await using var apply = new NpgsqlCommand(ddl, owner);
        await apply.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Grants data access (no DDL) on every table and sequence of the schema, now and for the ones later migrations
    /// create. Run after the migrations of that schema.
    /// </summary>
    public static async Task GrantDataAccessAsync(NpgsqlConnection owner, string role, string schema, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var (r, s) = (Identifier(role), Identifier(schema));

        string[] statements =
        [
            $"REVOKE ALL ON SCHEMA {s} FROM PUBLIC",
            $"GRANT USAGE ON SCHEMA {s} TO {r}",
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {s} TO {r}",
            $"GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA {s} TO {r}",
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA {s} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {r}",
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA {s} GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO {r}",

            // Migrations belong to the owner: a module role must not be able to rewrite its own migration history.
            $"""
            DO $$ BEGIN
              IF to_regclass('{s}.{ModuleDatabase.MigrationsHistoryTable}') IS NOT NULL THEN
                REVOKE ALL ON TABLE {s}.{ModuleDatabase.MigrationsHistoryTable} FROM {r};
              END IF;
            END $$
            """,
        ];

        await using var transaction = await owner.BeginTransactionAsync(cancellationToken);
        foreach (var statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, owner, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The SCRAM-SHA-256 verifier Postgres stores for a password (RFC 5802/7677, the format of <c>pg_authid</c>).
    /// The password is normalized with NFKC, the part of SASLprep that changes common passwords.
    /// </summary>
    internal static string ScramVerifier(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var normalized = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormKC));
#pragma warning disable S5344 // The iteration count is part of the PostgreSQL verifier; see ScramIterations.
        var salted = Rfc2898DeriveBytes.Pbkdf2(normalized, salt, ScramIterations, HashAlgorithmName.SHA256, 32);
#pragma warning restore S5344
        var clientKey = HMACSHA256.HashData(salted, "Client Key"u8);
        var storedKey = SHA256.HashData(clientKey);
        var serverKey = HMACSHA256.HashData(salted, "Server Key"u8);
        return $"SCRAM-SHA-256${ScramIterations}:{Convert.ToBase64String(salt)}${Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}";
    }

    /// <summary>Role and schema names come from code; they are still checked so they can be written into DDL as is.</summary>
    private static string Identifier(string name) =>
        name is not null && IdentifierShape().IsMatch(name)
            ? name
            : throw new ArgumentException($"'{name}' is not a plain lowercase Postgres identifier.", nameof(name));

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex IdentifierShape();
}
