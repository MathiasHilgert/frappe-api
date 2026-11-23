using System.Text.RegularExpressions;
using Frappe.Messaging.Inbox;
using Frappe.Messaging.Outbox;
using Npgsql;

namespace Frappe.Messaging.Relay;

/// <summary>
/// The grants of a module's narrow relay role (the login role the Worker uses): exactly what the outbox relay and the
/// message purge need, and nothing else, so a compromised Worker reads none of the module's data.
/// </summary>
public static partial class RelayRoles
{
    /// <summary>The names a schema or role may have: lower-case letters, digits and underscores, not starting with a digit (SEC-11).</summary>
    [GeneratedRegex("^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    /// <summary>
    /// The outbox columns a relay role may update: what the relay's claim (<c>leased_until</c>), settle (<c>published_at</c>,
    /// <c>attempts</c>, <c>next_attempt_at</c>, <c>last_error</c>) and lease release write.
    /// </summary>
    private static readonly string[] RelayWritableColumns =
        ["published_at", "attempts", "next_attempt_at", "leased_until", "last_error"];

    /// <summary>
    /// Leaves <paramref name="role"/> with exactly what the relay and the purge run: <c>USAGE</c> on <paramref name="schema"/>,
    /// <c>SELECT, DELETE</c> on the outbox (claim, purge), <c>UPDATE</c> only on the outbox columns the claim, the settle and a
    /// lease release write, so it cannot rewrite a message's payload, type or order, <c>SELECT, DELETE</c> on the inbox
    /// (purge), and <c>EXECUTE</c> on <paramref name="purgeFunctions"/> (none when empty). No <c>INSERT</c>, no sequence, no
    /// other table or function (<c>PUBLIC</c>'s <c>EXECUTE</c> is revoked from every function of the schema, and by default from
    /// every function the owner creates later, by the Migrator), no <c>CREATE</c> on the schema and no default privileges, so a
    /// table a later migration adds stays out of its reach. Whatever the role held before is revoked first, in the same
    /// transaction: every privilege on the schema and on its tables, sequences and functions, and every default privilege naming
    /// it that applies to the schema (the schema's own and the database-wide ones, whoever granted them), so a rerun converges
    /// on these grants even after a grant made by hand. Run after the module's migrations.
    /// </summary>
    /// <param name="owner">An open connection of the role that owns the schema.</param>
    /// <param name="schema">The module's schema.</param>
    /// <param name="role">The relay role.</param>
    /// <param name="purgeFunctions">
    /// The purge functions the role may call, with their argument types (<c>schema.name(timestamptz, integer)</c>).
    /// </param>
    /// <param name="cancellationToken">Cancels the grants; the transaction then rolls back.</param>
    public static async Task GrantAsync(
        NpgsqlConnection owner, string schema, string role, IReadOnlyList<string> purgeFunctions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        await using var transaction = await owner.BeginTransactionAsync(cancellationToken);
        foreach (var statement in Statements(schema, role, purgeFunctions))
        {
            await using var command = new NpgsqlCommand(statement, owner, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>The statements <see cref="GrantAsync"/> runs, in order.</summary>
    internal static IReadOnlyList<string> Statements(string schema, string role, IReadOnlyList<string> purgeFunctions)
    {
        ValidateIdentifier(schema, nameof(schema));
        ValidateIdentifier(role, nameof(role));
        ArgumentNullException.ThrowIfNull(purgeFunctions);
        var quotedSchema = $"\"{schema}\"";
        var quotedRole = $"\"{role}\"";

        List<string> statements =
        [
            $"REVOKE ALL ON ALL TABLES IN SCHEMA {quotedSchema} FROM {quotedRole}",
            $"REVOKE ALL ON ALL SEQUENCES IN SCHEMA {quotedSchema} FROM {quotedRole}",
            $"REVOKE ALL ON ALL FUNCTIONS IN SCHEMA {quotedSchema} FROM {quotedRole}",

            // Postgres lets PUBLIC, so every role, execute a new function: taken back from every function of the schema
            // on each run. The ones the owner creates later are covered database-wide by the Migrator
            // (ModuleRoles.RevokePublicFunctionExecuteAsync), for every module.
            $"REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA {quotedSchema} FROM PUBLIC",
            $"REVOKE ALL ON SCHEMA {quotedSchema} FROM {quotedRole}",
            RevokeDefaultPrivileges(schema, role),
            $"GRANT USAGE ON SCHEMA {quotedSchema} TO {quotedRole}",
            $"GRANT SELECT, DELETE ON TABLE {quotedSchema}.{OutboxModel.Table} TO {quotedRole}",
            $"GRANT UPDATE ({string.Join(", ", RelayWritableColumns)}) ON TABLE {quotedSchema}.{OutboxModel.Table} TO {quotedRole}",
            $"GRANT SELECT, DELETE ON TABLE {quotedSchema}.{InboxModel.Table} TO {quotedRole}",
        ];

        if (purgeFunctions.Count > 0)
        {
            statements.Add($"GRANT EXECUTE ON FUNCTION {string.Join(", ", purgeFunctions)} TO {quotedRole}");
        }

        return statements;
    }

    private static void ValidateIdentifier(string name, string parameter)
    {
        if (string.IsNullOrWhiteSpace(name) || !IdentifierPattern().IsMatch(name))
        {
            throw new ArgumentException($"'{parameter}' must match ^[a-z_][a-z0-9_]*$.", parameter);
        }
    }

    /// <summary>
    /// Revokes every default privilege granted to <paramref name="role"/> that reaches <paramref name="schema"/>: those set
    /// for the schema and the database-wide ones, for every granting role. The owner must be able to act for that role
    /// (a member of it, or a superuser); otherwise the statement fails and the Migrator stops, rather than leave one.
    /// </summary>
    private static string RevokeDefaultPrivileges(string schema, string role) => $$"""
        DO $$
        DECLARE
          d record;
        BEGIN
          FOR d IN
            SELECT pg_get_userbyid(a.defaclrole) AS grantor, n.nspname AS schema,
                   CASE a.defaclobjtype WHEN 'r' THEN 'TABLES' WHEN 'S' THEN 'SEQUENCES' WHEN 'f' THEN 'FUNCTIONS'
                                        WHEN 'T' THEN 'TYPES' WHEN 'n' THEN 'SCHEMAS' END AS kind
            FROM pg_default_acl a
            LEFT JOIN pg_namespace n ON n.oid = a.defaclnamespace
            WHERE (n.nspname = '{{schema}}' OR a.defaclnamespace = 0)
              AND EXISTS (SELECT 1 FROM aclexplode(a.defaclacl) item WHERE item.grantee = '{{role}}'::regrole)
          LOOP
            IF d.schema IS NULL THEN
              EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL ON %s FROM {{role}}', d.grantor, d.kind);
            ELSE
              EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I REVOKE ALL ON %s FROM {{role}}',
                d.grantor, d.schema, d.kind);
            END IF;
          END LOOP;
        END $$
        """;
}
