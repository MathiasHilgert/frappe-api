namespace Frappe.SharedKernel.Validation;

/// <summary>
/// A capacity limit or dependency is temporarily exhausted (e.g. no password-hashing slot freed in time). Thrown
/// rather than returned because it is not a business outcome; the web edge answers it with <see cref="Error"/> as a
/// problem, whose <see cref="ErrorKind"/> should be <see cref="ErrorKind.Unavailable"/>.
/// </summary>
public sealed class UnavailableException : Exception
{
    public UnavailableException(Error error)
        : base(error?.Message) => Error = error ?? throw new ArgumentNullException(nameof(error));

    public UnavailableException()
        : this(new Error("platform.unavailable", "The service is temporarily unavailable.", Kind: ErrorKind.Unavailable))
    {
    }

    public UnavailableException(string message)
        : base(message) => Error = new Error("platform.unavailable", message, Kind: ErrorKind.Unavailable);

    public UnavailableException(string message, Exception innerException)
        : base(message, innerException) => Error = new Error("platform.unavailable", message, Kind: ErrorKind.Unavailable);

    public Error Error { get; }
}
