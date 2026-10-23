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

    public static readonly Error AccountPending = new("identity.account.pending", "The email has not been verified yet.");

    public static readonly Error AccountSuspended = new("identity.account.suspended", "The account is suspended.");

    public static readonly Error AccountNotPending = new("identity.account.not_pending", "The email is already verified.");

    public static readonly Error CodeInvalid = new("identity.code.invalid", "The code is not valid.", "code");

    public static readonly Error CodeExpired = new("identity.code.expired", "The code has expired.", "code");

    public static readonly Error CodeAlreadyUsed = new("identity.code.already_used", "The code was already used.", "code");

    public static readonly Error ResendLimitReached =
        new("identity.code.resend_limit_reached", "Too many codes were sent. Try again later.");

    public static readonly Error SessionNotFound = new("identity.session.not_found", "The session does not exist.");

    public static readonly Error SessionExpired = new("identity.session.expired", "The session has expired.");

    public static readonly Error NoPendingEmailChange =
        new("identity.email_change.none_pending", "There is no email change to confirm.");
}
