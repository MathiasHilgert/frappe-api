using Frappe.Kernel.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Frappe.Platform.Persistence;

// Runs once the context's own configuration is complete, so every aggregate root gets Revision as its
// concurrency token without a module repeating it. A convention composes with the conventions other
// libraries add, unlike a replaced IModelCustomizer.
internal sealed class RevisionConcurrencyTokenConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        foreach (
            IConventionPropertyBuilder revision in modelBuilder
                .Metadata.GetEntityTypes()
                .Where(static type => type.ClrType.IsAssignableTo(typeof(IAggregateRoot)))
                .Select(static type => type.FindProperty(nameof(IAggregateRoot.Revision))?.Builder)
                .OfType<IConventionPropertyBuilder>()
        )
        {
            revision.IsConcurrencyToken(true);
        }
    }
}
