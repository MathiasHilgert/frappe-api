namespace Frappe.Http;

/// <summary>
/// What one problem response is about: its category and every error, still without text. Whoever answers a problem
/// sets it as a request feature before writing through <c>IProblemDetailsService</c>, and
/// <see cref="FrappeProblemDetails.Customize"/> turns it into the localized document.
/// </summary>
internal sealed record ProblemOccurrence(ProblemCategory Category, IReadOnlyList<ProblemError> Errors)
{
    /// <summary>
    /// The status answered: the category's, unless the framework rejected the request with a <c>4xx</c> of its own
    /// (a <c>408</c> for a body arriving too slowly), which a <see cref="ProblemCategory.BadRequest"/> keeps.
    /// </summary>
    public int Status { get; init; } = Category.Status;
}
