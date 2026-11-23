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
    /// Gives every later session of the role <see cref="DatabaseOptions.StatementTimeout"/> as <c>statement_timeout</c>
    /// and <see cref="DatabaseOptions.IdleInTransactionSessionTimeout"/> as <c>idle_in_transaction_session_timeout</c>,
    /// overwriting the previous values. Set on the role, not in the connection string, so they hold for any client that logs in
    /// with it (a psql session included) and a client cannot drop them. Sessions already open keep their old values
    /// until they reconnect.
    /// </summary>
    public static async Task LimitSessionsAsync(NpgsqlConnection owner, string role, DatabaseOptions limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(limits);
        var r = Identifier(role);

        // ALTER ROLE ... SET takes no bind parameters; the values are whole milliseconds, written as integers.
        string[] statements =
        [
            $"ALTER ROLE {r} SET statement_timeout = {Milliseconds(limits.StatementTimeout)}",
            $"ALTER ROLE {r} SET idle_in_transaction_session_timeout = {Milliseconds(limits.IdleInTransactionSessionTimeout)}",
        ];
        foreach (var statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, owner);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Takes <c>EXECUTE</c> on functions back from <c>PUBLIC</c> for every function the owner creates from now on, in
    /// every schema of the database: Postgres lets <c>PUBLIC</c>, so every role, run a new function, and a revoke in
    /// default privileges counts only database-wide (one scoped <c>IN SCHEMA</c> can add privileges, never remove the
    /// built-in ones). A function then reaches a role only through an explicit grant (MIG-12). Functions that exist
    /// already keep their grants (an extension's, such as <c>pg_trgm</c>'s, included), so run it once the modules migrated
    /// and give a role the functions it needs with <see cref="GrantDataAccessAsync"/>; running it again changes nothing.
    /// </summary>
    public static async Task RevokePublicFunctionExecuteAsync(NpgsqlConnection owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        await using var command = new NpgsqlCommand("ALTER DEFAULT PRIVILEGES REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC", owner);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static long Milliseconds(TimeSpan value) => (long)value.TotalMilliseconds;

    /// <summary>
    /// Grants data access (no DDL) on every table and sequence of the schema, and <c>EXECUTE</c> on its <c>SECURITY
    /// INVOKER</c> functions (the database-wide default gives <c>PUBLIC</c> none, see
    /// <see cref="RevokePublicFunctionExecuteAsync"/>). <c>SECURITY DEFINER</c> functions stay closed to the role: they
    /// run with their owner's rights and reach only the role granted them explicitly (MIG-12). Run after the migrations
    /// of that schema, on every Migrator run, so functions later migrations create are covered too.
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
            $"GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA {s} TO {r}",

            // A SECURITY DEFINER function runs with its owner's rights, so it goes to a narrower role by an explicit grant,
            // never to the module role (MIG-12). Every run re-grants then revokes, so a definer function a later migration
            // adds is closed to the module role too; no default privilege grants functions, the per-run grant covers them.
            $"""
            DO $$ DECLARE f regprocedure; BEGIN
              FOR f IN SELECT p.oid::regprocedure FROM pg_proc p WHERE p.pronamespace = '{s}'::regnamespace AND p.prosecdef LOOP
                EXECUTE format('REVOKE EXECUTE ON FUNCTION %s FROM {r}', f);
              END LOOP;
            END $$
            """,
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
        var (saltText, storedText, serverText) = (Convert.ToBase64String(salt), Convert.ToBase64String(storedKey), Convert.ToBase64String(serverKey));
        return $"SCRAM-SHA-256${ScramIterations}:{saltText}${storedText}:{serverText}";
    }

    /// <summary>Role and schema names come from code; they are still checked so they can be written into DDL as is.</summary>
    private static string Identifier(string name) =>
        name is not null && IdentifierShape().IsMatch(name)
            ? name
            : throw new ArgumentException($"'{name}' is not a plain lowercase Postgres identifier.", nameof(name));

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex IdentifierShape();
}
