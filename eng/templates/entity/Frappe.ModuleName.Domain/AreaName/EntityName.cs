using Frappe.Aggregates;
using NodaTime;

// EF Core materializes the aggregate through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.ModuleName.Domain.AreaName;

/// <summary>
/// FILL IN: say what an EntityName is, what it guards and which facts it raises (DOM-07, DOM-08).
/// </summary>
public sealed class EntityName : AggregateRoot
{
    private EntityName()
    {
    }

    private EntityName(Guid id, Instant now)
    {
        Id = id;
        CreatedAt = now;
    }

    /// <summary>UUIDv7 made by the application (DATA-02).</summary>
    public Guid Id { get; private set; }

    /// <summary>When it was created.</summary>
    public Instant CreatedAt { get; private set; }

    /// <summary>
    /// A new EntityName created at <paramref name="now"/>. FILL IN: take its fields, validate them (DOM-09) and raise its fact.
    /// </summary>
    public static EntityName Create(Instant now) => new(Guid.CreateVersion7(), now);
}
