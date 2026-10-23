using System.Text.RegularExpressions;

namespace Frappe.Platform.Realtime;

/// <summary>Single owner of the subject layout: <c>frappe.rt.{businessId:N}.{module}.{entity}.{fact}</c>.</summary>
public static partial class RealtimeSubjects
{
    public const string StreamName = "FRAPPE_REALTIME";
    private const string Prefix = "frappe.rt";

    public static string All => $"{Prefix}.>";

    public static string ForBusiness(Guid businessId) => $"{BusinessPrefix(businessId)}>";

    public static string For(Guid businessId, string topic)
    {
        if (!TopicPattern().IsMatch(topic))
            throw new ArgumentException($"Topic '{topic}' is not 'module.entity.fact' in lowercase.", nameof(topic));

        return BusinessPrefix(businessId) + topic;
    }

    public static string TopicOf(Guid businessId, string subject) => subject[BusinessPrefix(businessId).Length..];

    private static string BusinessPrefix(Guid businessId) => $"{Prefix}.{businessId:N}.";

    [GeneratedRegex("^[a-z0-9_]+\\.[a-z0-9_]+\\.[a-z0-9_]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TopicPattern();
}
