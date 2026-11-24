using Frappe.Identity.Application.Sessions.ListMySessions;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>The browser and the system a session's user agent names (S13): the major ones, in the order that tells lookalikes apart.</summary>
public sealed class UserAgentParserTests
{
    [Test]
    [Arguments(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36", "Chrome", "Windows")]
    [Arguments(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0",
        "Edge", "Windows")]
    [Arguments(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 OPR/115.0.0.0",
        "Opera", "Windows")]
    [Arguments("Mozilla/5.0 (X11; Linux x86_64; rv:132.0) Gecko/20100101 Firefox/132.0", "Firefox", "Linux")]
    [Arguments(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Safari/605.1.15", "Safari", "macOS")]
    [Arguments(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1",
        "Safari", "iOS")]
    [Arguments(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/130.0.0.0 Mobile/15E148 Safari/604.1",
        "Chrome", "iOS")]
    [Arguments(
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36", "Chrome", "Android")]
    [Arguments(
        "Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36", "Chrome", "Chrome OS")]
    public async Task A_major_browser_and_system_are_named_whatever_else_their_user_agent_claims(string userAgent, string browser, string os)
    {
        // Act
        var agent = UserAgentParser.Parse(userAgent);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(agent.Browser).IsEqualTo(browser);
            await Assert.That(agent.Os).IsEqualTo(os);
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("curl/8.5.0")]
    public async Task Nothing_is_guessed_for_a_user_agent_that_is_missing_or_unknown(string? userAgent)
    {
        // Act
        var agent = UserAgentParser.Parse(userAgent);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(agent.Browser).IsNull();
            await Assert.That(agent.Os).IsNull();
        }
    }
}
