using Frappe.Access.Contracts;
using Frappe.Results;

namespace Frappe.Identity.UnitTests;

/// <summary>The authorizer a handler under test sees: it answers what the test says and counts its calls. The rules are tested with Access.</summary>
internal sealed class FakeAuthorizer(Error? answer = null) : IAuthorizer
{
    public int Calls { get; private set; }

    public ValueTask<Error?> RefuseAsync<T>(ResourceAction<T> action, T resource, CancellationToken cancellationToken)
        where T : class
    {
        Calls++;
        return ValueTask.FromResult(answer);
    }
}
