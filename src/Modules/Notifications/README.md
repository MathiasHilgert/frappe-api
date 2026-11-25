# Notifications

Notifications turns facts into messages and records each delivery, and it never decides who deserves one (producers do).

## Doors

| Host | Start here | Loads |
|------|------------|-------|
| Api | [NotificationsModule](Frappe.Notifications/NotificationsModule.cs) `.Api` | `NotificationsCoreServices`, the kinds, `EmailSenderApiModule`, `NotificationsHttp` |
| Worker | [NotificationsModule](Frappe.Notifications/NotificationsModule.cs) `.Worker` | `NotificationsCoreServices`, `NotificationsWorkerServices` (dispatch consumer, nightly purge), the kinds, `IdentityTriggersModule`, `EmailSenderWorkerModule`, `NotificationsMessagePurgeServices` |
| Migrator | `NotificationsDatabase` | the Notifications schema |

## Features

Routes are under `/v1`. Folders are those of [Application](Frappe.Notifications.Application) and [Domain](Frappe.Notifications.Domain).

| Feature | Domain | Use cases | Endpoints |
|---------|--------|-----------|-----------|
| Requests | `SendRequest` | SendNotification | `POST /notifications/sends` (Development only) |
| Deliveries | `Delivery`, `Recipient` | `NotificationDispatcher` (run by the dispatch consumer) | none |
| Preferences | `Preference` | Unsubscribe | `POST /notifications/unsubscriptions` |
| ProviderEvents | `ProviderEvent` | RecordProviderFeedback | `POST /notifications/webhooks/resend` |
| Suppressions | `Suppression` | none (added by provider feedback) | none |
| Contacts, Channels, Kinds, Producers | none | ports and catalog shared by the features above | none |

## Metrics (meter `Frappe.Notifications`)

| Name | Kind | Unit | Tags | Class |
|------|------|------|------|-------|
| `frappe.notifications.deliveries` | Counter | `{delivery}` | `kind`, `channel`, `frappe.outcome` | [NotificationsMetrics](Frappe.Notifications.Application/Telemetry/NotificationsMetrics.cs) |
| `frappe.notifications.unsubscribes` | Counter | `{unsubscribe}` | `category` | [PreferenceMetrics](Frappe.Notifications.Application/Telemetry/PreferenceMetrics.cs) |
| `frappe.notifications.provider_events` | Counter | `{event}` | `type`, `matched` | [ProviderFeedbackMetrics](Frappe.Notifications.Application/Telemetry/ProviderFeedbackMetrics.cs) |
| `frappe.notifications.suppressions` | Counter | `{suppression}` | `reason` | [ProviderFeedbackMetrics](Frappe.Notifications.Application/Telemetry/ProviderFeedbackMetrics.cs) |
| `frappe.notifications.delivery.time_to_delivered` | Histogram | `s` | `kind`, `channel` | [ProviderFeedbackMetrics](Frappe.Notifications.Application/Telemetry/ProviderFeedbackMetrics.cs) |
| `frappe.notifications.purged` | Counter | `{row}` | `table` | [RetentionMetrics](Frappe.Notifications.Application/Telemetry/RetentionMetrics.cs) |
| `frappe.notifications.purge.runs` | Counter | `{run}` | `frappe.outcome` | [RetentionMetrics](Frappe.Notifications.Application/Telemetry/RetentionMetrics.cs) |

Post-commit counting is registered in [NotificationsTelemetryRegistration](Frappe.Notifications.Infrastructure/Telemetry/).

## Events

Names come from [contracts/events/v1.json](../../../contracts/events/v1.json).

- Publishes: `frappe.notifications.notification_requested.v1`.
- Consumes: `frappe.notifications.notification_requested.v1` (dispatch).
- Consumes from Identity, through the triggers: `frappe.identity.owner_code_issued.v1`,
  `frappe.identity.owner_registration_attempted.v1`, `frappe.identity.owner_email_change_requested.v1`,
  `frappe.identity.owner_email_changed.v1`.

## Extensions

| Project | Adds |
|---------|------|
| [Messages.Account](Extensions/Frappe.Notifications.Messages.Account) | Which messages exist: activation code, email-change code, email change requested and changed, password-reset code, registration attempted |
| [Messages.Marketing](Extensions/Frappe.Notifications.Messages.Marketing) | Which messages exist: product announcement |
| [Senders.Email](Extensions/Frappe.Notifications.Senders.Email) | How messages leave: the email channel, rendering, Resend sender and webhook, signed unsubscribe links |
| [Triggers.Identity](Extensions/Frappe.Notifications.Triggers.Identity) | Who asks for them: producers mapping Identity's facts to notification requests |
