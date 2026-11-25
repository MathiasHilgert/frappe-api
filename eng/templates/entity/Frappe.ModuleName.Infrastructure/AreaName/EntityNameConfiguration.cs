using Frappe.ModuleName.Domain.AreaName;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.ModuleName.Infrastructure.AreaName;

/// <summary>
/// Maps <see cref="EntityName"/> to <c>table_name_here</c>. FILL IN: say which queries its indexes serve and what its checks guard.
/// </summary>
internal sealed class EntityNameConfiguration : IEntityTypeConfiguration<EntityName>
{
    public const string Table = "table_name_here";

    public const string PrimaryKey = "pk_table_name_here";

    public void Configure(EntityTypeBuilder<EntityName> builder)
    {
        builder.ToTable(Table);
        builder.HasKey(x => x.Id).HasName(PrimaryKey);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsRowVersion();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}
