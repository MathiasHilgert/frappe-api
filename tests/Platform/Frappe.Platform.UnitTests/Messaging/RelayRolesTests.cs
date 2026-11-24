using Frappe.Messaging.Storage.Relay;

namespace Frappe.Platform.UnitTests.Messaging;

/// <summary>MIG-12 / SEC-11: the grants of a narrow relay role are exactly what the outbox relay and the purge need.</summary>
public sealed class RelayRolesTests
{
    private static string DefaultPrivilegeRevoke(string schema, string role) => $$"""
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

    [Test]
    public async Task Identitys_relay_role_gets_the_statements_it_got_before_with_both_purge_functions()
    {
        const string schema = "orders";
        const string role = "frappe_orders_relay";

        var statements = RelayRoles.Statements(
            schema,
            role,
            ["orders.purge_expired_drafts(timestamptz, integer)", "orders.purge_expired_tokens(timestamptz, integer)"]);

        await Assert.That(statements).IsEquivalentTo(
            [
                "REVOKE ALL ON ALL TABLES IN SCHEMA \"orders\" FROM \"frappe_orders_relay\"",
                "REVOKE ALL ON ALL SEQUENCES IN SCHEMA \"orders\" FROM \"frappe_orders_relay\"",
                "REVOKE ALL ON ALL FUNCTIONS IN SCHEMA \"orders\" FROM \"frappe_orders_relay\"",
                "REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA \"orders\" FROM PUBLIC",
                "REVOKE ALL ON SCHEMA \"orders\" FROM \"frappe_orders_relay\"",
                DefaultPrivilegeRevoke(schema, role),
                "GRANT USAGE ON SCHEMA \"orders\" TO \"frappe_orders_relay\"",
                "GRANT SELECT, DELETE ON TABLE \"orders\".outbox_messages TO \"frappe_orders_relay\"",
                "GRANT UPDATE (published_at, attempts, next_attempt_at, leased_until, last_error) "
                + "ON TABLE \"orders\".outbox_messages TO \"frappe_orders_relay\"",
                "GRANT SELECT, DELETE ON TABLE \"orders\".inbox_messages TO \"frappe_orders_relay\"",
                "GRANT EXECUTE ON FUNCTION orders.purge_expired_drafts(timestamptz, integer), "
                + "orders.purge_expired_tokens(timestamptz, integer) TO \"frappe_orders_relay\"",
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_relay_role_with_no_purge_function_gets_the_relay_and_purge_grants_and_no_function_grant()
    {
        const string schema = "example";
        const string role = "frappe_example_relay";

        var statements = RelayRoles.Statements(schema, role, []);

        await Assert.That(statements).IsEquivalentTo(
            [
                "REVOKE ALL ON ALL TABLES IN SCHEMA \"example\" FROM \"frappe_example_relay\"",
                "REVOKE ALL ON ALL SEQUENCES IN SCHEMA \"example\" FROM \"frappe_example_relay\"",
                "REVOKE ALL ON ALL FUNCTIONS IN SCHEMA \"example\" FROM \"frappe_example_relay\"",
                "REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA \"example\" FROM PUBLIC",
                "REVOKE ALL ON SCHEMA \"example\" FROM \"frappe_example_relay\"",
                DefaultPrivilegeRevoke(schema, role),
                "GRANT USAGE ON SCHEMA \"example\" TO \"frappe_example_relay\"",
                "GRANT SELECT, DELETE ON TABLE \"example\".outbox_messages TO \"frappe_example_relay\"",
                "GRANT UPDATE (published_at, attempts, next_attempt_at, leased_until, last_error) "
                + "ON TABLE \"example\".outbox_messages TO \"frappe_example_relay\"",
                "GRANT SELECT, DELETE ON TABLE \"example\".inbox_messages TO \"frappe_example_relay\"",
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("orders; DROP ROLE x")]
    [Arguments("orders\"")]
    [Arguments("Orders")]
    [Arguments("1orders")]
    [Arguments("orders-x")]
    public async Task A_schema_or_role_name_outside_the_identifier_pattern_is_rejected_before_any_statement_is_built(string hostile)
    {
        var inSchema = Assert.Throws<ArgumentException>(() => RelayRoles.Statements(hostile, "frappe_orders_relay", []));
        var inRole = Assert.Throws<ArgumentException>(() => RelayRoles.Statements("orders", hostile, []));

        using (Assert.Multiple())
        {
            await Assert.That(inSchema.ParamName).IsEqualTo("schema");
            await Assert.That(inRole.ParamName).IsEqualTo("role");
        }
    }
}
