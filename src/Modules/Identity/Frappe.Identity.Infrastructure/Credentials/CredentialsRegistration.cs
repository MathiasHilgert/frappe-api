using Frappe.Identity.Application.Credentials;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Credentials;

/// <summary>Registers how Identity hashes passwords and checks them against the breach list.</summary>
internal static class CredentialsRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the Argon2id hasher, the HaveIBeenPwned client and the services over them. Both options are validated when the host starts,
        /// naming the key (CFG-01). The client makes one attempt within <see cref="BreachCheckOptions.Timeout"/>, never retries (a retry would
        /// only make registration slower), and its breaker leaves a failing list alone for <see cref="BreachCheckOptions.BreakDuration"/>
        /// (CFG-08). The request path carries a hash prefix, so the client logs no URLs.
        /// </summary>
        public IServiceCollection AddPasswords()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddMetrics();
            services.TryAddSingleton(TimeProvider.System);
            services.AddOptions<PasswordOptions>().BindConfiguration(PasswordOptions.Section).ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PasswordOptions>, PasswordOptionsValidator>());
            services.AddOptions<BreachCheckOptions>().BindConfiguration(BreachCheckOptions.Section).ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BreachCheckOptions>, BreachCheckOptionsValidator>());

            services.TryAddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
            services.TryAddSingleton<PasswordPolicy>();
            services.TryAddSingleton<PasswordCheck>();

            var client = services.AddHttpClient<IBreachedPasswords, HaveIBeenPwnedClient>((provider, http) =>
                {
                    var settings = provider.GetRequiredService<IOptions<BreachCheckOptions>>().Value;
                    http.BaseAddress = new Uri(settings.BaseUrl.AbsoluteUri.TrimEnd(PathSeparator) + PathSeparator);
                    http.MaxResponseContentBufferSize = MaxAnswerBytes;
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("frappe-api");
                })
                .RemoveAllLoggers();
            var resilience = client.AddStandardResilienceHandler();
            // The resilience timeouts cover the headers only; this one also covers reading the body. It is set after the resilience handler
            // is added because that handler turns the client's own timeout off.
            client.ConfigureHttpClient((provider, http) => http.Timeout = provider.GetRequiredService<IOptions<BreachCheckOptions>>().Value.Timeout);
            services.AddOptions<HttpStandardResilienceOptions>(resilience.PipelineName)
                .Configure<IOptions<BreachCheckOptions>>((pipeline, breach) =>
                {
                    var settings = breach.Value;
                    pipeline.AttemptTimeout.Timeout = settings.Timeout;
                    pipeline.TotalRequestTimeout.Timeout = settings.Timeout;
                    // The pipeline requires at least one retry attempt, so retrying is turned off by handling nothing.
                    pipeline.Retry.MaxRetryAttempts = 1;
                    pipeline.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
                    pipeline.CircuitBreaker.MinimumThroughput = BreakerMinimumThroughput;
                    pipeline.CircuitBreaker.FailureRatio = BreakerFailureRatio;
                    pipeline.CircuitBreaker.BreakDuration = settings.BreakDuration;
                    pipeline.CircuitBreaker.SamplingDuration = settings.Timeout * 2 > BreakerSampling ? settings.Timeout * 2 : BreakerSampling;
                });
            return services;
        }
    }

    /// <summary>A padded answer is about 40 KB; this bounds what a misbehaving server can make us read.</summary>
    private const int MaxAnswerBytes = 1024 * 1024;

    private const char PathSeparator = '/';

    private const int BreakerMinimumThroughput = 5;

    private const double BreakerFailureRatio = 0.5;

    private static readonly TimeSpan BreakerSampling = TimeSpan.FromSeconds(30);
}
