using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Domain.Credentials;
using Frappe.Observability;
using Microsoft.Extensions.Logging;

namespace Frappe.Identity.Infrastructure.Credentials;

/// <summary>
/// Asks HaveIBeenPwned's range API whether a password is in a breach (S4). Only the first five hex characters of the password's SHA-1 are
/// sent (k-anonymity); the rest is compared here against the list the service returns. <c>Add-Padding</c> makes every answer about the same
/// size, and its padded lines (count 0) never count as a hit. Any failure, including the timeout and the open breaker of the client's
/// pipeline, answers <see langword="null"/> so the caller accepts the password; the budgets live in the registration (CFG-08).
/// </summary>
public sealed partial class HaveIBeenPwnedClient(HttpClient http, ILogger<HaveIBeenPwnedClient> logger) : IBreachedPasswords
{
    private const int PrefixLength = 5;

    /// <inheritdoc />
    public async Task<bool?> IsBreachedAsync(Password password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);
#pragma warning disable CA5350, S4790 // The range API is defined over SHA-1; the hash only picks a bucket and is not a security primitive here.
        var digest = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password.Value)));
#pragma warning restore CA5350, S4790
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{digest[..PrefixLength]}");
            // The prefix ties the request to a password, so traces show the path redacted (SEC-09).
            request.ShowInTelemetryAs(new Uri(http.BaseAddress!, $"range/{OutgoingRequestTelemetry.Redacted}").AbsoluteUri);
            request.Headers.Add("Add-Padding", "true");
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogRefused(logger, (int)response.StatusCode);
                return null;
            }

            return Contains(await response.Content.ReadAsStringAsync(cancellationToken), digest.AsSpan(PrefixLength));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Any failure (network, timeout, open breaker, bad answer) leaves the check unanswered: the password is accepted.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogFailed(logger, exception.GetType().Name);
            return null;
        }
    }

    /// <summary>Whether a <c>SUFFIX:COUNT</c> line names <paramref name="suffix"/> with a count above zero.</summary>
    private static bool Contains(string body, ReadOnlySpan<char> suffix)
    {
        foreach (var line in body.AsSpan().EnumerateLines())
        {
            var separator = line.IndexOf(':');
            if (separator == suffix.Length
                && line[..separator].Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && long.TryParse(line[(separator + 1)..].Trim(), out var count)
                && count > 0)
            {
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The breach list answered {Status}; the password is accepted unchecked")]
    private static partial void LogRefused(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The breach list could not be asked ({Error}); the password is accepted unchecked")]
    private static partial void LogFailed(ILogger logger, string error);
}
