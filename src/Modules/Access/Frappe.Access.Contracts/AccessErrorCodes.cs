namespace Frappe.Access.Contracts;

/// <summary>The codes of what Access answers. Codes are part of the public contract: never reuse or rename one.</summary>
public static class AccessErrorCodes
{
    /// <summary>403: a rule of the use case or of an action on a resource refused the caller.</summary>
    public const string ActionForbidden = "access.action.forbidden";

    /// <summary>403: the caller may do it only after a recent password check.</summary>
    public const string StepUpRequired = "access.step_up.required";

    /// <summary>404: a resource Access has no rules for (a book was never declared) is hidden under this code, never described.</summary>
    public const string ResourceNotFound = "access.resource.not_found";
}
