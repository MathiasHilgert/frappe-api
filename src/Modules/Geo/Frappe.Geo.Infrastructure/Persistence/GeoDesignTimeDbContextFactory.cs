using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model to create migrations. It never connects to a database.</summary>
internal sealed class GeoDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GeoDbContext>
{
    public GeoDbContext CreateDbContext(string[] args) => new(
        GeoPersistenceRegistration.Configure(
                new DbContextOptionsBuilder<GeoDbContext>(),
                "Host=localhost;Database=frappe;Username=design_time",
                SystemClock.Instance)
            .Options);
}
