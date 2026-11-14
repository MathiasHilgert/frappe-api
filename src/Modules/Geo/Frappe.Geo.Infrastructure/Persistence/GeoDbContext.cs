using Frappe.Geo.Application;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Domain.Holidays;
using Frappe.Geo.Infrastructure.Territory;
using Frappe.Messaging.Inbox;
using Frappe.Messaging.Outbox;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace Frappe.Geo.Infrastructure.Persistence;

/// <summary>The Geo module's database: schema <c>geo</c>, nothing outside it.</summary>
public sealed class GeoDbContext(DbContextOptions<GeoDbContext> options) : DbContext(options)
{
    public const string Schema = "geo";

    /// <summary>The module name of Geo's namespaces (<c>Frappe.Geo.*</c>), which keys Geo's unit of work and its outbox.</summary>
    public const string Module = "geo";

    /// <summary>
    /// Saves, telling a lost race over a holiday calendar apart: a calendar changed by someone else since it was read
    /// (its row version moved) is a <see cref="ConcurrentChangeException"/>, which Geo's adapters answer as a conflict.
    /// A manual subdivision alias that another writer removed or added first (the same alias, or one equal ignoring case
    /// and accents) is a
    /// <see cref="SubdivisionAliasRaceLostException"/> answered as the command run second would be: not found for the
    /// removal, duplicate for the addition, taken (naming the winner) for an addition that lost to the same manual alias
    /// added to another subdivision of the country. Any other failure (the outbox relay's, or a territory sync's) passes through unchanged.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(e => e.Entity is HolidayCalendar or CalendarEntry))
        {
            throw new ConcurrentChangeException("Someone else changed the same data first.", exception);
        }
        catch (DbUpdateConcurrencyException exception) when (ManualAlias(exception.Entries, EntityState.Deleted) is { } removed)
        {
            throw SubdivisionAliasRaceLostException.Removed(removed.Code, removed.Alias, exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: AliasPrimaryKey or SubdivisionAliasRow.OneBySearchForm }
            && ManualAlias(exception.Entries.Count > 0 ? exception.Entries : ChangeTracker.Entries<SubdivisionAliasRow>(), EntityState.Added) is { } added)
        {
            throw SubdivisionAliasRaceLostException.Added(added.Code, added.Alias, exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: SubdivisionAliasRow.OneManualByCountry }
            && ManualAlias(exception.Entries.Count > 0 ? exception.Entries : ChangeTracker.Entries<SubdivisionAliasRow>(), EntityState.Added) is { } taken)
        {
            // The same alias added to this very subdivision violates both indexes; whichever Postgres checks first, no
            // other holder means the race was over this subdivision, answered duplicate as above.
            throw await HolderAsync(taken.Code, taken.SearchForm, cancellationToken) is { } holder
                ? SubdivisionAliasRaceLostException.Taken(taken.Code, taken.Alias, holder, exception)
                : SubdivisionAliasRaceLostException.Added(taken.Code, taken.Alias, exception);
        }
    }

    /// <summary>The primary key of <c>subdivision_aliases</c> (subdivision, alias), which a second addition of the same alias violates.</summary>
    private const string AliasPrimaryKey = "PK_subdivision_aliases";

    /// <summary>The manual alias an administrator's command was writing, in the given state; never a sync's (source) alias.</summary>
    private static (string Code, string Alias, string SearchForm)? ManualAlias(IEnumerable<EntityEntry> entries, EntityState state) =>
        entries.Where(e => e.State == state && e.Entity is SubdivisionAliasRow { Manual: true })
            .Select(e => ((string)e.Property("subdivision_code").CurrentValue!, ((SubdivisionAliasRow)e.Entity).Alias, ((SubdivisionAliasRow)e.Entity).SearchForm))
            .Cast<(string, string, string)?>()
            .FirstOrDefault();

    /// <summary>
    /// The other subdivision of the country holding the manual alias of <paramref name="searchForm"/>: the race's
    /// winner, committed by now; none when the race was over this subdivision. The failed save was rolled back to EF's
    /// savepoint, so the unit's transaction still reads.
    /// </summary>
    private async Task<string?> HolderAsync(string code, string searchForm, CancellationToken cancellationToken)
    {
        var country = code[..code.IndexOf('-', StringComparison.Ordinal)];
        var holders = await Database.SqlQuery<string>($"""
            SELECT subdivision_code AS "Value" FROM geo.subdivision_aliases
            WHERE manual AND search_form = {searchForm} AND split_part(subdivision_code, '-', 1) = {country} AND subdivision_code <> {code}
            """).ToListAsync(cancellationToken);
        return holders.FirstOrDefault();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        // The locality search (trigram word similarity). A trusted extension since Postgres 13: the database owner creates it.
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GeoDbContext).Assembly);
        modelBuilder.AddOutbox();
        modelBuilder.AddInbox();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.StoreLanguageTags();
    }
}
