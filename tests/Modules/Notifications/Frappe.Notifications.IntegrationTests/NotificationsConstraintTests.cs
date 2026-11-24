using Npgsql;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>
/// The database refuses the rows the invariants forbid (DATA-07): each case breaks exactly one rule of an otherwise valid row and names
/// the constraint that refused it, and the valid row itself is accepted.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class NotificationsConstraintTests(PostgresServer server)
{
    private const string Hash = "decode(repeat('ab', 32), 'hex')";

    private static readonly Dictionary<string, string> ValidDelivery = new()
    {
        ["id"] = "gen_random_uuid()",
        ["source_message_id"] = "gen_random_uuid()",
        ["kind"] = "'account_activation_code'",
        ["category"] = "'security'",
        ["channel"] = "'email'",
        ["recipient_key"] = "'ref:principal:42'",
        ["status"] = "'unresolved'",
        ["created_at"] = "now()",
        ["status_changed_at"] = "now()",
    };

    private static readonly Dictionary<string, string> ValidSuppression = new()
    {
        ["channel"] = "'email'",
        ["address_hash"] = Hash,
        ["hash_key_id"] = "1",
        ["reason"] = "'complaint'",
        ["source"] = "'webhook'",
        ["created_at"] = "now()",
    };

    private static readonly Dictionary<string, string> ValidPreference = new()
    {
        ["recipient_key"] = "'ref:principal:42'",
        ["category"] = "'marketing'",
        ["opted_out_at"] = "now()",
    };

    private static readonly Dictionary<string, string> ValidEvent = new()
    {
        ["provider"] = "'resend'",
        ["event_id"] = "'evt_1'",
        ["received_at"] = "now()",
        ["matched"] = "true",
    };

    private static string Insert(string table, Dictionary<string, string> valid, params (string Column, string Value)[] overrides)
    {
        var row = new Dictionary<string, string>(valid);
        foreach (var (column, value) in overrides)
        {
            row[column] = value;
        }

        return $"INSERT INTO notifications.{table} ({string.Join(", ", row.Keys)}) VALUES ({string.Join(", ", row.Values)})";
    }

    public static IEnumerable<(string Sql, string Constraint)> RefusedRows()
    {
        yield return (Insert("deliveries", ValidDelivery, ("kind", "'Bad Kind'")), "ck_deliveries_kind_format");
        yield return (Insert("deliveries", ValidDelivery, ("recipient_key", "'principal:42'")), "ck_deliveries_recipient_key_format");
        yield return (Insert("deliveries", ValidDelivery, ("recipient_key", "'addr:1:ABC'")), "ck_deliveries_recipient_key_format");
        yield return (Insert("deliveries", ValidDelivery, ("address_hash", "'\\x01'::bytea"), ("hash_key_id", "1")),
            "ck_deliveries_address_hash_length");
        yield return (Insert("deliveries", ValidDelivery, ("address_hash", Hash)), "ck_deliveries_address_hash_pair");
        yield return (Insert("deliveries", ValidDelivery, ("hash_key_id", "1")), "ck_deliveries_address_hash_pair");
        yield return (Insert("deliveries", ValidDelivery, ("category", "'urgent'")), "ck_deliveries_category");
        yield return (Insert("deliveries", ValidDelivery, ("status", "'lost'")), "ck_deliveries_status");
        yield return (Insert("deliveries", ValidDelivery, ("status", "'failed'"), ("failure_reason", "'bored'")), "ck_deliveries_failure_reason");
        yield return (Insert("deliveries", ValidDelivery, ("provider", "'bogus'"), ("provider_message_id", "'x'")), "ck_deliveries_provider");
        yield return (Insert("deliveries", ValidDelivery, ("provider", "'resend'")), "ck_deliveries_provider_pair");
        yield return (Insert("deliveries", ValidDelivery, ("provider_message_id", "'re_1'")), "ck_deliveries_provider_pair");
        yield return (Insert("deliveries", ValidDelivery, ("status", "'sent'"), ("sent_at", "now()")), "ck_deliveries_sent_like");
        yield return (Insert("deliveries", ValidDelivery, ("status", "'delivered'"), ("provider", "'resend'"), ("provider_message_id", "'re_1'")),
            "ck_deliveries_sent_like");
        yield return (Insert("deliveries", ValidDelivery, ("status", "'failed'")), "ck_deliveries_failed_reason");
        yield return (Insert("deliveries", ValidDelivery, ("failure_reason", "'provider_rejected'")), "ck_deliveries_failed_reason");
        yield return (Insert("suppressions", ValidSuppression, ("address_hash", "'\\x01'::bytea")), "ck_suppressions_address_hash_length");
        yield return (Insert("suppressions", ValidSuppression, ("reason", "'annoyed'")), "ck_suppressions_reason");
        yield return (Insert("suppressions", ValidSuppression, ("source", "'bogus'")), "ck_suppressions_source");
        yield return (Insert("suppressions", ValidSuppression, ("provider", "'bogus'"), ("provider_event_id", "'e'")), "ck_suppressions_provider");
        yield return (Insert("suppressions", ValidSuppression, ("provider", "'resend'")), "ck_suppressions_provider_pair");
        yield return (Insert("preferences", ValidPreference, ("category", "'security'")), "ck_preferences_only_marketing");
        yield return (Insert("preferences", ValidPreference, ("category", "'transactional'")), "ck_preferences_only_marketing");
        yield return (Insert("preferences", ValidPreference, ("recipient_key", "'42'")), "ck_preferences_recipient_key_format");
        yield return (Insert("provider_events", ValidEvent, ("provider", "'bogus'")), "ck_provider_events_provider");
        yield return (Insert("provider_events", ValidEvent, ("provider_message_id", "'re_1'")), "ck_provider_events_parked_together");
        yield return (Insert("provider_events", ValidEvent, ("feedback", "'complaint'")), "ck_provider_events_parked_together");
        yield return (Insert("provider_events", ValidEvent, ("occurred_at", "now()")), "ck_provider_events_parked_together");
        yield return (Insert("provider_events", ValidEvent,
            ("provider_message_id", "'re_1'"), ("feedback", "'angry'"), ("occurred_at", "now()")), "ck_provider_events_feedback");
    }

    [Test]
    [MethodDataSource(nameof(RefusedRows))]
    public async Task A_row_that_breaks_one_invariant_is_refused_naming_the_constraint(string sql, string constraint)
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
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
    public async Task The_valid_row_of_every_table_and_a_fully_sent_or_failed_delivery_are_accepted()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            string[] accepted =
            [
                Insert("deliveries", ValidDelivery),
                Insert("deliveries", ValidDelivery,
                    ("status", "'sent'"), ("provider", "'resend'"), ("provider_message_id", "'re_1'"), ("sent_at", "now()")),
                Insert("deliveries", ValidDelivery, ("status", "'failed'"), ("failure_reason", "'provider_rejected'")),
                Insert("deliveries", ValidDelivery,
                    ("recipient_key", $"'addr:1:{new string('a', 64)}'"), ("address_hash", Hash), ("hash_key_id", "1")),
                Insert("suppressions", ValidSuppression),
                Insert("suppressions", ValidSuppression, ("channel", "'sms'"), ("provider", "'resend'"), ("provider_event_id", "'evt_1'")),
                Insert("preferences", ValidPreference),
                Insert("provider_events", ValidEvent),
                Insert("provider_events", ValidEvent,
                    ("event_id", "'evt_2'"), ("matched", "false"), ("provider_message_id", "'re_9'"),
                    ("feedback", "'delivered'"), ("occurred_at", "now()")),
            ];

            // Act
            foreach (var sql in accepted)
            {
                await ScratchDatabase.ExecuteAsync(owner, sql);
            }

            // Assert
            await Assert.That(await ScratchDatabase.ScalarAsync(owner, "SELECT count(*) FROM notifications.deliveries")).IsEqualTo(4L);
        });
    }

    [Test]
    public async Task The_same_message_kind_recipient_and_channel_record_once_and_a_provider_message_id_belongs_to_one_delivery()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            var message = Guid.CreateVersion7();
            var first = Insert("deliveries", ValidDelivery, ("source_message_id", $"'{message}'"));
            await ScratchDatabase.ExecuteAsync(owner, first);
            string SentRow(string pmi) => Insert("deliveries", ValidDelivery,
                ("status", "'sent'"), ("provider", "'resend'"), ("provider_message_id", $"'{pmi}'"), ("sent_at", "now()"));
            await ScratchDatabase.ExecuteAsync(owner, SentRow("re_1"));

            // Act
            var sameKey = await Assert.ThrowsAsync<PostgresException>(() => ScratchDatabase.ExecuteAsync(
                owner, Insert("deliveries", ValidDelivery, ("source_message_id", $"'{message}'"))));
            var sameProviderId = await Assert.ThrowsAsync<PostgresException>(() => ScratchDatabase.ExecuteAsync(owner, SentRow("re_1")));
            await ScratchDatabase.ExecuteAsync(
                owner, Insert("deliveries", ValidDelivery, ("source_message_id", $"'{message}'"), ("channel", "'sms'")));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(sameKey!.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
                await Assert.That(sameKey.ConstraintName).IsEqualTo("ux_deliveries_message_kind_recipient_channel");
                await Assert.That(sameProviderId!.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
                await Assert.That(sameProviderId.ConstraintName).IsEqualTo("ux_deliveries_provider_message");
            }
        });
    }

    [Test]
    [Arguments("provider_events", "pk_provider_events")]
    [Arguments("preferences", "pk_preferences")]
    [Arguments("suppressions", "pk_suppressions")]
    public async Task A_second_row_with_the_same_primary_key_is_refused(string table, string constraint)
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            var sql = table switch
            {
                "provider_events" => Insert(table, ValidEvent),
                "preferences" => Insert(table, ValidPreference),
                _ => Insert(table, ValidSuppression),
            };
            await ScratchDatabase.ExecuteAsync(owner, sql);

            // Act
            var refused = await Assert.ThrowsAsync<PostgresException>(() => ScratchDatabase.ExecuteAsync(owner, sql));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(refused!.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
                await Assert.That(refused.ConstraintName).IsEqualTo(constraint);
            }
        });
    }
}
