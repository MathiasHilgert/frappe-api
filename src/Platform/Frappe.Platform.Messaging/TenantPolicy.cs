using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace Frappe.Platform.Messaging;

// Global middleware lands after the EF Core transaction frames, which Wolverine inserts first, so the tenant
// would be set after the transaction began. Registered after AutoApplyTransactions, this puts the tenant
// call before them; the generated code disposes the scope it returns.
internal sealed class TenantPolicy : IHandlerPolicy
{
    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container
    )
    {
        foreach (
            HandlerChain chain in chains
                .SelectMany(static chain => chain.ByEndpoint.Prepend(chain))
                .Distinct()
        )
        {
            chain.Middleware.Insert(
                0,
                new MethodCall(
                    typeof(TenantMiddleware),
                    typeof(TenantMiddleware).GetMethod(nameof(TenantMiddleware.Before))!
                )
            );
        }
    }
}
