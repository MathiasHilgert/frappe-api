namespace Frappe.SharedKernel.Validation;

/// <summary>
/// Fowler's Notification pattern: collects every broken rule instead of throwing on the first one,
/// so the caller can report all of them at once.
/// </summary>
public sealed class Notification
{
    private readonly List<Error> _errors = [];

    public IReadOnlyList<Error> Errors => _errors;
    public bool HasErrors => _errors.Count > 0;

    public Notification Add(Error error)
    {
        _errors.Add(error);
        return this;
    }

    public Notification AddIf(bool broken, Error error) => broken ? Add(error) : this;

    public Notification Merge(Notification other)
    {
        _errors.AddRange(other._errors);
        return this;
    }

    /// <summary>Prefixes the field of every error, e.g. "name" becomes "owner.name".</summary>
    public Notification Nest(string field)
    {
        for (var i = 0; i < _errors.Count; i++)
        {
            var e = _errors[i];
            _errors[i] = e with { Field = e.Field is null ? field : $"{field}.{e.Field}" };
        }
        return this;
    }
}
