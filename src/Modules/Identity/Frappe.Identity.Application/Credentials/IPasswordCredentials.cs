using Frappe.Identity.Domain.Credentials;

namespace Frappe.Identity.Application.Credentials;

/// <summary>The passwords of principals. It stages and never saves: the command's unit of work does (UOW-01).</summary>
public interface IPasswordCredentials
{
    /// <summary>The password of <paramref name="principalId"/>, tracked for change, or <see langword="null"/>.</summary>
    Task<PasswordCredential?> FindAsync(Guid principalId, CancellationToken cancellationToken);

    /// <summary>Stages <paramref name="credential"/> to be inserted with the unit of work.</summary>
    void Add(PasswordCredential credential);
}
