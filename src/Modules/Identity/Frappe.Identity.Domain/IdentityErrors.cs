using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Domain;

public static class IdentityErrors
{
    public static readonly Error NameRequired = new("identity.name.required", "Name is required.", "name");

    public static readonly Error NameTooLong =
        new("identity.name.too_long", $"Name must be at most {PersonName.MaxLength} characters.", "name");

    public static readonly Error EmailRequired = new("identity.email.required", "Email is required.", "email");

    public static readonly Error EmailInvalid =
        new("identity.email.invalid", $"Email must look like name@domain and be at most {Email.MaxLength} characters.", "email");

    public static readonly Error LocaleUnsupported =
        new("identity.locale.unsupported", "Frappé does not speak this language yet.", "locale");

    public static readonly Error EmailTaken =
        new("identity.email.taken", "An account with this email already exists.", "email", ErrorKind.Conflict);

    /// <summary>Someone else changed the account between this request's read and its write; retrying is safe.</summary>
    public static readonly Error ConcurrentChange =
        new("identity.account.changed", "The account changed while this request was running. Try again.", Kind: ErrorKind.Conflict);

    public static readonly Error PasswordTooShort =
        new("identity.password.too_short", $"Password must be at least {Password.MinLength} characters.", "password");

    public static readonly Error PasswordTooLong =
        new("identity.password.too_long", $"Password must be at most {Password.MaxLength} characters.", "password");

    public static readonly Error PasswordBreached = new("identity.password.breached",
        "This password has appeared in a data breach. Choose a different one.", "password");

    public static readonly Error InvalidCredentials =
        new("identity.credentials.invalid", "The email or password is not correct.", Kind: ErrorKind.Unauthorized);

    public static readonly Error TooManyAttempts =
        new("identity.attempts.too_many", "Too many attempts. Try again later.", Kind: ErrorKind.TooManyRequests);

    /// <summary>No password-hashing slot freed in time: the server is saturated, so the caller backs off and retries.</summary>
    public static readonly Error PasswordHashingBusy =
        new("identity.password_hashing.busy", "The server is busy. Try again in a moment.", Kind: ErrorKind.Unavailable);

    public static readonly Error OwnerNotFound = new("identity.owner.not_found", "The owner does not exist.", Kind: ErrorKind.NotFound);

    public static readonly Error AccountPending = new("identity.account.pending", "The email has not been verified yet.", Kind: ErrorKind.Forbidden);

    public static readonly Error AccountSuspended = new("identity.account.suspended", "The account is suspended.", Kind: ErrorKind.Forbidden);

    public static readonly Error AccountNotPending = new("identity.account.not_pending", "The email is already verified.", Kind: ErrorKind.Conflict);

    public static readonly Error RegistrationInGrace =
        new("identity.account.registration_in_grace", "The registration is still within its verification grace period.");

    public static readonly Error CodeInvalid = new("identity.code.invalid", "The code is not valid.", "code");

    public static readonly Error CodeExpired = new("identity.code.expired", "The code has expired.", "code");

    public static readonly Error CodeAlreadyUsed = new("identity.code.already_used", "The code was already used.", "code");

    public static readonly Error ResendLimitReached =
        new("identity.code.resend_limit_reached", "Too many codes were sent. Try again later.", Kind: ErrorKind.TooManyRequests);

    public static readonly Error StepUpPasswordIncorrect =
        new("identity.step_up.password_incorrect", "The password is not correct.", "password");

    public static readonly Error CurrentPasswordIncorrect =
        new("identity.password.current_incorrect", "The current password is not correct.", "currentPassword");

    public static readonly Error StepUpRequired =
        new("identity.step_up.required", "Confirm your password again to continue.", Kind: ErrorKind.Forbidden);

    public static readonly Error SessionNotFound = new("identity.session.not_found", "The session does not exist.", Kind: ErrorKind.NotFound);

    public static readonly Error SessionExpired = new("identity.session.expired", "The session has expired.", Kind: ErrorKind.Unauthorized);

    public static readonly Error NoPendingEmailChange =
        new("identity.email_change.none_pending", "There is no email change to confirm.", Kind: ErrorKind.NotFound);

    public static readonly Error EmailChangeNotFound =
        new("identity.email_change.not_found", "This email change does not exist or was replaced by a newer one.", Kind: ErrorKind.NotFound);
}
