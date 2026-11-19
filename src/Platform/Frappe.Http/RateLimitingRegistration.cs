using System.Globalization;
using System.Threading.RateLimiting;
using Frappe.Caching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Http;

/// <summary>
/// Wires rate limiting into a host: the <see cref="HttpRegistration.AnonymousPolicy"/> limiter (a fixed window per client
/// address, with no queue, answering <c>429</c> with <c>Retry-After</c> once the window is spent) and the policies modules
/// add next to it. The windows are counted in an <see cref="IRateLimitStore"/>: the host's own when it registered one, else
/// Valkey when <c>Frappe.Caching</c> is registered, else this process's memory. While the store fails, each replica
/// counts on its own again.
/// </summary>
public static class RateLimitingRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the anonymous limiter, bound from <c>Http:RateLimit</c> (<see cref="AnonymousRateLimitOptions"/>); fewer
        /// than one permit or a window that is not positive fails at startup naming the key.
        /// </summary>
        internal IServiceCollection AddFrappeRateLimiting()
        {
            services.AddOptions<AnonymousRateLimitOptions>()
                .BindConfiguration(AnonymousRateLimitOptions.Section)
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<AnonymousRateLimitOptions>>(
                new AnonymousRateLimitOptionsValidator(Options.DefaultName, AnonymousRateLimitOptions.Section));
            services.AddMetrics();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<RateLimitMetrics>();
            services.TryAddSingleton<IRateLimitStore>(provider => provider.GetService<ValkeyWindowCounter>() is { } counter
                ? new ValkeyRateLimitStore(counter)
                : new InMemoryRateLimitStore(provider.GetRequiredService<TimeProvider>()));
            services.TryAddSingleton(provider => new SharedRateLimitStore(
                provider.GetRequiredService<IRateLimitStore>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<RateLimitMetrics>(),
                provider.GetRequiredService<ILoggerFactory>().CreateLogger<SharedRateLimitStore>()));
            services.AddRateLimiter(_ => { });
            services.AddOptions<RateLimiterOptions>().Configure<IOptions<AnonymousRateLimitOptions>, SharedRateLimitStore>((options, limit, shared) =>
            {
                var anonymous = limit.Value;
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                AddClientPolicy(options, HttpRegistration.AnonymousPolicy, anonymous, shared);
                options.OnRejected = (context, _) =>
                {
                    // A fixed window always knows when it reopens; the whole window is the safe answer if it ever does not.
                    var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : anonymous.Window;
                    var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
                    context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

                    return new ValueTask(PlatformProblem.WriteAsync(
                        context.HttpContext,
                        PlatformProblem.Of(ProblemCategory.TooManyRequests, HttpRegistration.RateLimitedCode, new Dictionary<string, object?> { ["retryAfter"] = seconds })));
                };
            });
            return services;
        }

        /// <summary>
        /// Adds a per-client rate limiting policy named <paramref name="policy"/> next to
        /// <see cref="HttpRegistration.AnonymousPolicy"/>, for endpoints that need a limit of their own (an endpoint that
        /// requires it is counted by it instead of by the group's policy): the same fixed window per client address, with no
        /// queue and the same <c>429</c>, counted under its own name in the <see cref="IRateLimitStore"/>. The limit is
        /// <paramref name="defaults"/>, unless <paramref name="section"/> sets <c>PermitLimit</c> or <c>Window</c>; fewer
        /// than one permit or a window that is not positive fails at startup naming the key.
        /// </summary>
        public IServiceCollection AddFrappeRateLimitPolicy(string policy, string section, AnonymousRateLimit defaults)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(policy);
            ArgumentException.ThrowIfNullOrWhiteSpace(section);
            ArgumentNullException.ThrowIfNull(defaults);
            ArgumentOutOfRangeException.ThrowIfLessThan(defaults.PermitLimit, 1);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(defaults.Window, TimeSpan.Zero);
            if (string.Equals(policy, HttpRegistration.AnonymousPolicy, StringComparison.Ordinal))
            {
                throw new ArgumentException($"The policy name '{HttpRegistration.AnonymousPolicy}' is the platform's own.", nameof(policy));
            }

            services.AddOptions<AnonymousRateLimitOptions>(policy)
                .Configure(options =>
                {
                    options.PermitLimit = defaults.PermitLimit;
                    options.Window = defaults.Window;
                })
                .BindConfiguration(section)
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<AnonymousRateLimitOptions>>(new AnonymousRateLimitOptionsValidator(policy, section));
            services.AddOptions<RateLimiterOptions>().Configure<IOptionsMonitor<AnonymousRateLimitOptions>, SharedRateLimitStore>(
                (options, limits, shared) => AddClientPolicy(options, policy, limits.Get(policy), shared));
            return services;
        }
    }

    /// <summary>Counts each client address in a fixed window of the shared store, under <c>policy:client</c>.</summary>
    private static void AddClientPolicy(RateLimiterOptions options, string policy, AnonymousRateLimitOptions limit, SharedRateLimitStore shared)
    {
        var permitLimit = limit.PermitLimit;
        var window = limit.Window;
        options.AddPolicy(policy, httpContext => RateLimitPartition.Get(
            ClientAddress.Of(httpContext),
            key => new SharedFixedWindowRateLimiter($"{policy}:{key}", permitLimit, window, shared, shared.Time)));
    }
}
