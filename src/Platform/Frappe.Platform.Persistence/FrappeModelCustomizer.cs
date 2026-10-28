using Frappe.Kernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Frappe.Platform.Persistence;

// Runs after the context's own configuration in every context built with UseFrappeDatabase, so each
// aggregate root gets Revision as its concurrency token without a module repeating it.
// shortcut: replaces the provider's IModelCustomizer, so a second ReplaceService (Wolverine's
// WolverineModelCustomizer) would silently win or lose; the query-bus PR must compose them and
// keep a test that Revision is still a concurrency token.
internal sealed class FrappeModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        foreach (
            IMutableProperty revision in modelBuilder
                .Model.GetEntityTypes()
                .Where(static type => type.ClrType.IsAssignableTo(typeof(IAggregateRoot)))
                .Select(static type => type.FindProperty(nameof(IAggregateRoot.Revision)))
                .OfType<IMutableProperty>()
        )
        {
            revision.IsConcurrencyToken = true;
        }
    }
}
