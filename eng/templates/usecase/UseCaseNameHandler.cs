using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.ModuleName.Application.AreaName.UseCaseName;

/// <summary>
/// FILL IN: say what UseCaseName does, for whom, which refusals it answers and in which order.
/// </summary>
//#if (IsQuery)
internal sealed class UseCaseNameHandler : IQueryHandler<UseCaseName, Unit>
{
    public Task<Result<Unit>> HandleAsync(UseCaseName query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
//#else
internal sealed class UseCaseNameHandler : ICommandHandler<UseCaseName, Unit>
{
    public Task<Result<Unit>> HandleAsync(UseCaseName command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
//#endif
//#if (HasAccess)

        // FILL IN: decide who may do this first, through AccessTypeName injected in the constructor, as the other handlers of the module do (see CancelOrderHandler).
//#endif
        throw new NotImplementedException("FILL IN: write the failing test first, then the logic.");
    }
}
