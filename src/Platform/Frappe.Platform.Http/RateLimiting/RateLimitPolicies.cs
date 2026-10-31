using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.Http.RateLimiting;

/// <summary>
/// The rate-limit policies an endpoint picks one of with <c>RequireRateLimiting</c>; a rejection is a 429 problem
/// with <c>Retry-After</c>.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Anonymous reads: a fixed window per client IP.</summary>
    public const string Public = "public";

    internal static void Add(IServiceCollection services)
    {
        services
            .AddOptionsWithValidateOnStart<RateLimitOptions, RateLimitOptionsValidator>()
            .BindConfiguration(RateLimitOptions.SectionName);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = SetRetryAfterAsync;
            // TODO: add the per-tenant GlobalLimiter ceiling when authentication provides a tenant (no session
            // exists yet)
            options.AddPolicy(
                Public,
                static context =>
                {
                    RateLimitOptions limits = context
                        .RequestServices.GetRequiredService<IOptions<RateLimitOptions>>()
                        .Value;
                    // TODO: partition by the client IP from trusted forwarded headers (deferred: infrastructure;
                    // behind a proxy every caller shares one partition until then)
                    return RateLimitPartition.GetFixedWindowLimiter(
                        ClientPartition(context.Connection.RemoteIpAddress),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limits.PublicPermitLimit,
                            Window = limits.PublicWindow,
                            QueueLimit = 0,
                        }
                    );
                }
            );
        });
    }

    // An IPv6 client is usually given a whole /64, so it shares one partition across it; IPv4 stays per address.
    private static string ClientPartition(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();
        return $"{new IPAddress(bytes)}/64";
    }

    // The 429 body is the house problem every bodiless status gets; the rejection only says when to retry.
    private static ValueTask SetRetryAfterAsync(
        OnRejectedContext rejected,
        CancellationToken cancellationToken
    )
    {
        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            rejected.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                .ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    }
}
