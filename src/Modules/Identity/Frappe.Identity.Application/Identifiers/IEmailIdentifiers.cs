using Frappe.Identity.Domain.Identifiers;

namespace Frappe.Identity.Application.Identifiers;

/// <summary>The email addresses of principals. It stages and never saves: the command's unit of work does (UOW-01).</summary>
public interface IEmailIdentifiers
{
    /// <summary>
    /// The identifier whose normalized address is <paramref name="normalizedAddress"/>, tracked for change, or <see langword="null"/>.
    /// </summary>
    Task<EmailIdentifier?> FindByAddressAsync(string normalizedAddress, CancellationToken cancellationToken);

    /// <summary>The address of <paramref name="principalId"/>, tracked for change, or <see langword="null"/>.</summary>
    Task<EmailIdentifier?> FindOfAsync(Guid principalId, CancellationToken cancellationToken);

    /// <summary>Stages <paramref name="identifier"/> to be inserted with the unit of work.</summary>
    void Add(EmailIdentifier identifier);
}
