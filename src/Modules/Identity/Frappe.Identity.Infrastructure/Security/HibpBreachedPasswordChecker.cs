using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>
/// HaveIBeenPwned range API with k-anonymity: only the first 5 hex characters of the SHA-1 leave the process, the
/// response is padded with fake zero-count entries, and the suffix is compared locally. A failure or a response
/// slower than the configured timeout fails open (logged and counted), so the breach source never blocks sign-up.
/// </summary>
internal sealed partial class HibpBreachedPasswordChecker(
    HttpClient http,
    IOptions<BreachedPasswordOptions> options,
    IdentityMetrics metrics,
    TimeProvider clock,
    ILogger<HibpBreachedPasswordChecker> logger) : IBreachedPasswordChecker
{
    private const int PrefixLength = 5;

    public async Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken)
    {
        var hash = Sha1Hex(password);
        using var deadline = new CancellationTokenSource(options.Value.Timeout, clock);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"range/{hash[..PrefixLength]}", UriKind.Relative));
            request.Headers.Add("Add-Padding", "true");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return Skip($"status {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return Contains(body, hash.AsSpan(PrefixLength));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Skip("timeout");
        }
        catch (HttpRequestException exception)
        {
            return Skip(exception.Message);
        }
    }

    private static bool Contains(string body, ReadOnlySpan<char> suffix)
    {
        foreach (var line in body.AsSpan().EnumerateLines())
        {
            var separator = line.IndexOf(':');
            if (separator < 0 || !line[..separator].Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            // Padding entries carry a zero count and must never count as a breach.
            return long.TryParse(line[(separator + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0;
        }

        return false;
    }

#pragma warning disable CA5350, S4790 // SHA-1 is the HIBP range key, not a password hash.
    private static string Sha1Hex(string password) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350, S4790

    private bool Skip(string reason)
    {
        LogBreachCheckSkipped(reason);
        metrics.BreachCheckSkipped();
        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Breached-password check skipped ({Reason}); allowing the password")]
    private partial void LogBreachCheckSkipped(string reason);
}
