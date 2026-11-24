using Frappe.Testing;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The alert rules as code (OBS-15, OBS-16): every failure metric has a rule, every rule carries what to do when it fires, and none reads a
/// datasource Grafana does not provision. Checked on the file's text, without Grafana.
/// </summary>
public sealed class AlertProvisioningTests
{
    private static readonly string[] ExpectedRules =
    [
        "frappe-notifications-failed-bounced-ratio",
        "frappe-notifications-provider-rejected-ratio",
        "frappe-notifications-provider-transient-sustained",
        "frappe-notifications-idempotency-conflict",
        "frappe-notifications-provider-unauthorized",
        "frappe-notifications-purge-missing",
    ];

    private static string File_() =>
        File.ReadAllText(Path.Combine(RepositoryRoot.FullPath, "deploy/observability/grafana/provisioning/alerting/frappe-alerts.yaml"));

    /// <summary>The text of each rule, from its <c>uid</c> line to the next rule's.</summary>
    private static Dictionary<string, string> Rules()
    {
        var parts = File_().Split("      - uid: ", StringSplitOptions.RemoveEmptyEntries).Skip(1);
        return parts.ToDictionary(part => part.Split('\n')[0].Trim(), part => part, StringComparer.Ordinal);
    }

    [Test]
    public async Task The_provisioned_rules_are_exactly_the_expected_ones()
    {
        // Act
        var uids = Rules().Keys;

        // Assert
        await Assert.That(uids).IsEquivalentTo(ExpectedRules);
    }

    [Test]
    public async Task Every_rule_carries_a_summary_a_description_and_a_runbook()
    {
        // Act
        var rules = Rules();

        // Assert
        using (Assert.Multiple())
        {
            foreach (var (uid, text) in rules)
            {
                await Assert.That(text).Contains("          summary:").And.Contains("          description:").And.Contains("          runbook:")
                    .Because(uid);
            }
        }
    }

    [Test]
    public async Task Every_rule_reads_mimir_or_loki_only_keeps_no_data_ok_and_has_no_escaped_dollar()
    {
        // Act
        var rules = Rules();
        var datasources = File_().Split('\n')
            .Where(line => line.Contains("datasourceUid:", StringComparison.Ordinal))
            .Select(line => line.Split("datasourceUid:")[1].Trim())
            .Distinct()
            .ToArray();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(datasources.Except(["mimir", "loki", "__expr__"])).IsEmpty();
            await Assert.That(File_()).DoesNotContain("$$");
            foreach (var (uid, text) in rules)
            {
                await Assert.That(text).Contains("noDataState: OK").Because(uid);
            }
        }
    }

    [Test]
    public async Task The_failed_and_bounced_rule_also_counts_series_born_inside_the_window()
    {
        // Act
        var rule = Rules()["frappe-notifications-failed-bounced-ratio"];

        // Assert
        await Assert.That(rule).Contains("unless frappe_notifications_deliveries_total{frappe_outcome=\"failed\"} offset 15m");
    }

    [Test]
    [Arguments("frappe-notifications-provider-rejected-ratio", "rejected")]
    [Arguments("frappe-notifications-provider-transient-sustained", "transient")]
    [Arguments("frappe-notifications-idempotency-conflict", "idempotency_conflict")]
    [Arguments("frappe-notifications-provider-unauthorized", "unauthorized")]
    public async Task The_provider_rules_read_their_outcome_and_also_count_series_born_inside_the_window(string uid, string outcome)
    {
        // Act
        var rule = Rules()[uid];

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(rule).Contains("frappe_notifications_provider_requests_total").And.Contains(outcome);
            await Assert.That(rule).Contains("offset 15m");
        }
    }

    [Test]
    public async Task The_purge_rule_reads_the_succeeded_runs_of_26_hours_and_only_once_a_run_was_reported()
    {
        // Act
        var rule = Rules()["frappe-notifications-purge-missing"];

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(rule).Contains("frappe_notifications_purge_runs_total{frappe_outcome=\"succeeded\"}[26h]");
            await Assert.That(rule).Contains("and on() sum(frappe_notifications_purge_runs_total)");
        }
    }
}
