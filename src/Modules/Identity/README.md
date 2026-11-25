# Identity

Identity owns owners, their passwords, one-time codes and sessions, and it never sends a message (Notifications reacts to its events).

## Doors

| Host | Start here | Loads |
|------|------------|-------|
| Api | [IdentityModule](Frappe.Identity/IdentityModule.cs) `.Api` | `IdentityCoreServices`, `IdentityApiServices`, `IdentityLimitsValkeyModule`, `IdentityHttp` |
| Worker | [IdentityModule](Frappe.Identity/IdentityModule.cs) `.Worker` | `IdentityCoreServices`, `IdentityWorkerServices` (outbox relay, retention), `IdentityMessagePurgeServices` |
| Migrator | `IdentityDatabase` | the Identity schema |

## Features

Routes are under `/v1/identity`. Folders are those of [Application](Frappe.Identity.Application) and [Domain](Frappe.Identity.Domain).

| Feature | Domain | Use cases | Endpoints |
|---------|--------|-----------|-----------|
| Owners | `Owner`, `OwnerName` | RegisterOwner, VerifyOwnerEmail, ResendOwnerVerification, GetMe, UpdateMe, RequestEmailChange, ConfirmEmailChange, UndoEmailChange | `POST /owners`, `POST /owners/{ownerId}/verification`, `POST /owners/{ownerId}/verification-codes`, `GET /me`, `PATCH /me`, `POST /me/email-changes`, `POST /me/email-changes/{changeId}/confirmation`, `POST /email-change-undos` |
| Credentials | `PasswordCredential`, `Password` | ChangePassword, RequestPasswordReset and ResetPassword (in Owners) | `POST /me/password-changes`, `POST /password-resets`, `POST /password-resets/{resetId}/confirmation` |
| Sessions | `Session` | LogInOwner, AuthenticateSession, TouchSession, GetCurrentSession, ListMySessions, LogOut, RevokeMySession, RevokeMyOtherSessions, StepUp | `POST /sessions`, `GET /sessions/current`, `DELETE /sessions/current`, `POST /sessions/current/step-ups`, `GET /me/sessions`, `DELETE /me/sessions/{sessionId}`, `DELETE /me/sessions/others` |
| Codes | `OneTimeCode`, `EmailChangeUndo` | issuing and redeeming (`CodeIssuer`, `CodeRedeemer`) | none |
| Identifiers | `Email`, `EmailIdentifier` | none | none |
| Principals | `Principal` | none | none |
| Limits | none | `IAttemptLimiter` port | none |

## Metrics (meter `Frappe.Identity`)

Tag `frappe.outcome` is shared by the outcome counters.

| Name | Kind | Unit | Tags | Class |
|------|------|------|------|-------|
| `frappe.identity.codes.issued` | Counter | `{code}` | `frappe.purpose` | [CodesMetrics](Frappe.Identity.Application/Telemetry/CodesMetrics.cs) |
| `frappe.identity.password_hash.duration` | Histogram | `s` | none | [CredentialsMetrics](Frappe.Identity.Application/Telemetry/CredentialsMetrics.cs) |
| `frappe.identity.breach_check.duration` | Histogram | `s` | `frappe.outcome` | [CredentialsMetrics](Frappe.Identity.Application/Telemetry/CredentialsMetrics.cs) |
| `frappe.identity.password_changes` | Counter | `{change}` | `frappe.kind` | [CredentialsMetrics](Frappe.Identity.Application/Telemetry/CredentialsMetrics.cs) |
| `frappe.identity.limiter.unavailable` | Counter | `{event}` | `frappe.flow` | [LimitsMetrics](Frappe.Identity.Application/Telemetry/LimitsMetrics.cs) |
| `frappe.identity.registrations` | Counter | `{registration}` | `frappe.outcome` | [OwnersMetrics](Frappe.Identity.Application/Telemetry/OwnersMetrics.cs) |
| `frappe.identity.verifications` | Counter | `{verification}` | `frappe.outcome` | [OwnersMetrics](Frappe.Identity.Application/Telemetry/OwnersMetrics.cs) |
| `frappe.identity.purged` | Counter | `{row}` | `frappe.target` | [RetentionMetrics](Frappe.Identity.Application/Telemetry/RetentionMetrics.cs) |
| `frappe.identity.session_touch.lost` | Counter | `{touch}` | none | [SessionsMetrics](Frappe.Identity.Application/Telemetry/SessionsMetrics.cs) |
| `frappe.identity.logins` | Counter | `{login}` | `frappe.outcome` | [SessionsMetrics](Frappe.Identity.Application/Telemetry/SessionsMetrics.cs) |
| `frappe.identity.sessions.opened` | Counter | `{session}` | `frappe.audience` | [SessionsMetrics](Frappe.Identity.Application/Telemetry/SessionsMetrics.cs) |
| `frappe.identity.sessions.revoked` | Counter | `{session}` | `frappe.reason` | [SessionsMetrics](Frappe.Identity.Application/Telemetry/SessionsMetrics.cs) |
| `frappe.identity.step_ups` | Counter | `{step-up}` | `frappe.outcome` | [SessionsMetrics](Frappe.Identity.Application/Telemetry/SessionsMetrics.cs) |
| `frappe.identity.sessions.ended` | Counter | `{session}` | `frappe.reason` | [SessionsMetrics](Frappe.Identity.Application/Telemetry/SessionsMetrics.cs) |

Post-commit counting is registered in [IdentityTelemetryRegistration](Frappe.Identity.Infrastructure/Telemetry/).

## Events

Names come from [contracts/events/v1.json](../../../contracts/events/v1.json);
the types are in [Frappe.Identity.Contracts](Frappe.Identity.Contracts).

- Publishes (all prefixed `frappe.identity.`, suffixed `.v1`): `owner_code_issued`, `owner_email_change_requested`,
  `owner_email_change_undone`, `owner_email_changed`, `owner_pending_purged`, `owner_registered`,
  `owner_registration_attempted`, `owner_verified`, `password_changed`, `password_reset`, `session_closed`,
  `session_ended`, `session_opened`, `session_revoked`, `step_up_completed`.
- Consumes: none.

## Extensions

| Project | Adds |
|---------|------|
| [Frappe.Identity.Limits.Valkey](Extensions/Frappe.Identity.Limits.Valkey) | Attempt limits counted in Valkey (`ValkeyAttemptLimiter`); Api only |
