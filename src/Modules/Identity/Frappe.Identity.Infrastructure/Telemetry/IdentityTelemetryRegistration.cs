using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Telemetry;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Domain.Sessions;
using Frappe.Observability;
using Frappe.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Identity.Infrastructure.Telemetry;

/// <summary>Registers Identity's metrics classes, one per feature, and what is counted once the unit that carried an event commits.</summary>
internal static class IdentityTelemetryRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>Adds one metrics class per feature, each created once over the host's meter factory.</summary>
        public IServiceCollection AddIdentityMetrics()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<CredentialsMetrics>();
            services.TryAddSingleton<LimitsMetrics>();
            services.TryAddSingleton<SessionsMetrics>();
            services.TryAddSingleton<OwnersMetrics>();
            services.TryAddSingleton<CodesMetrics>();
            services.TryAddSingleton<RetentionMetrics>();
            return services;
        }

        /// <summary>
        /// Counts registrations, codes issued, log-ins, password changes, verifications, sessions opened or revoked and step-ups after the commit
        /// that carried them (OBS-06). A failed verification or log-in is counted by its handler: nothing commits to count it.
        /// </summary>
        public IServiceCollection CountIdentityEventsOnCommit()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.CountOnCommit<OwnerRegistered, OwnersMetrics>((metrics, registered) =>
                metrics.Registered(registered.Replaced ? RegistrationOutcome.Replaced : RegistrationOutcome.Created));
            services.CountOnCommit<OwnerRegistrationAttempted, OwnersMetrics>((metrics, _) => metrics.Registered(RegistrationOutcome.Taken));
            services.CountOnCommit<OwnerCodeIssued, CodesMetrics>((metrics, issued) =>
                metrics.CodeIssued(StoredEnum<CodePurpose>.ToText(issued.Purpose)));
            services.CountOnCommit<OwnerLoggedIn, SessionsMetrics>((metrics, loggedIn) =>
                metrics.LoggedIn(loggedIn.VerificationPending ? LoginOutcome.Pending : LoginOutcome.Opened));
            services.CountOnCommit<PasswordChanged, CredentialsMetrics>((metrics, _) => metrics.PasswordChanged(reset: false));
            services.CountOnCommit<PasswordReset, CredentialsMetrics>((metrics, _) => metrics.PasswordChanged(reset: true));
            services.CountOnCommit<OwnerVerified, OwnersMetrics>((metrics, _) => metrics.Verified(VerificationOutcome.Verified));
            services.CountOnCommit<SessionOpened, SessionsMetrics>((metrics, opened) =>
                metrics.SessionOpened(StoredEnum<SessionAudience>.ToText(opened.Audience)));
            services.CountOnCommit<SessionRevoked, SessionsMetrics>((metrics, revoked) =>
                metrics.SessionRevoked(StoredEnum<RevocationReason>.ToText(revoked.Reason)));
            services.CountOnCommit<StepUpCompleted, SessionsMetrics>((metrics, _) => metrics.SteppedUp(StepUpOutcome.Completed));
            return services;
        }
    }
}
