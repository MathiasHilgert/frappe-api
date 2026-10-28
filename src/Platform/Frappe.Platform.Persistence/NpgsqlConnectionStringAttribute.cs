using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace Frappe.Platform.Persistence;

// The data source is built lazily, so a malformed connection string would otherwise surface only on
// the first request. The message is fixed because the value holds the password.
[AttributeUsage(AttributeTargets.Property)]
internal sealed class NpgsqlConnectionStringAttribute : ValidationAttribute
{
    private const string Invalid =
        "ConnectionStrings:Frappe is not a valid PostgreSQL connection string.";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string connectionString || string.IsNullOrWhiteSpace(connectionString))
        {
            return ValidationResult.Success;
        }

        try
        {
            _ = new NpgsqlConnectionStringBuilder(connectionString);
            return ValidationResult.Success;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return new ValidationResult(Invalid, [validationContext.MemberName ?? ""]);
        }
    }
}
