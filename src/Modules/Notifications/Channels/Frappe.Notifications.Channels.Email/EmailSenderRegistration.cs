using System.Net.Http.Headers;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Channels.Email;

/// <summary>Registers how rendered emails leave the process: the Resend client with its resilience, the logging sender and the choice.</summary>
public static class EmailSenderRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the <see cref="IEmailSender"/> the channel needs: <c>Notifications:Email:Sender</c> picks Resend or Logging once, and the
        /// options are validated when the host starts, naming the missing key (DES-04). The Resend client retries 429, 5xx and timeouts,
        /// budgets every attempt and the whole send, and stays under its provider's rate limit (CFG-08, S14).
        /// </summary>
        public IServiceCollection AddEmailSender()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.Section).ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>());
            services.AddMetrics();
            services.TryAddSingleton<EmailProviderMetrics>();
            services.TryAddSingleton<LoggingEmailSender>();
            services.TryAddSingleton<ResendRateLimiter>();

            // The sender is a singleton the channel keeps, so the client's connections are renewed by the handler, not by the factory.
            var client = services.AddHttpClient<ResendEmailSender>((provider, http) =>
                {
                    var resend = provider.GetRequiredService<IOptions<EmailOptions>>().Value.Resend;
                    http.BaseAddress = resend.BaseUrl;
                    if (!string.IsNullOrWhiteSpace(resend.ApiKey))
                    {
                        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resend.ApiKey);
                    }
                })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
            var resilience = client.AddStandardResilienceHandler();
            services.AddOptions<HttpStandardResilienceOptions>(resilience.PipelineName)
                .Configure<IOptions<EmailOptions>, ResendRateLimiter>((pipeline, email, limiter) =>
                {
                    var resend = email.Value.Resend;
                    pipeline.AttemptTimeout.Timeout = resend.AttemptTimeout;
                    pipeline.TotalRequestTimeout.Timeout = resend.TotalTimeout;
                    pipeline.Retry.MaxRetryAttempts = resend.Retries;
                    pipeline.Retry.Delay = resend.RetryDelay;
                    pipeline.Retry.UseJitter = true;
                    pipeline.CircuitBreaker.SamplingDuration = resend.AttemptTimeout * 2 > pipeline.CircuitBreaker.SamplingDuration
                        ? resend.AttemptTimeout * 2
                        : pipeline.CircuitBreaker.SamplingDuration;
                    pipeline.RateLimiter.RateLimiter = arguments => limiter.AcquireAsync(arguments.Context.CancellationToken);
                });

            services.AddSingleton<IEmailSender>(provider => EmailSenderSelection.Select(
                provider.GetRequiredService<IOptions<EmailOptions>>().Value.Sender,
                provider.GetRequiredService<ResendEmailSender>(),
                provider.GetRequiredService<LoggingEmailSender>()));
            return services;
        }
    }

    /// <summary>The queue behind the rate limiter: sends wait for a token instead of failing while a burst drains.</summary>
    private const int RateLimiterQueue = 1000;

    /// <summary>
    /// The token bucket in front of the provider: the rate in <see cref="ResendOptions.RequestsPerSecond"/>, queued, shared by every send.
    /// The budget is per Worker process (one replica, prefetch 1) and sits outside the retry strategy, so a retry takes no token.
    /// </summary>
    internal sealed class ResendRateLimiter(IOptions<EmailOptions> options) : IDisposable
    {
        private readonly TokenBucketRateLimiter _limiter = new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.Value.Resend.RequestsPerSecond,
            TokensPerPeriod = options.Value.Resend.RequestsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = RateLimiterQueue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

        public ValueTask<RateLimitLease> AcquireAsync(CancellationToken cancellationToken) => _limiter.AcquireAsync(1, cancellationToken);

        public void Dispose() => _limiter.Dispose();
    }
}
