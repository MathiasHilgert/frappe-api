using Frappe.Health;
using Frappe.Persistence;
using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>
/// A module registered with <c>AddModuleDatabase</c> works against a real database: it commits through its unit of work and
/// answers readiness.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class ModuleDatabaseStorageTests(PostgresServer server)
{
    [Test]
    public async Task A_command_commits_through_the_unit_of_work_of_the_module()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Arrange
            using var host = await StartAsync(owner);
            await using (var setup = host.Services.CreateAsyncScope())
            {
                await setup.ServiceProvider.GetRequiredService<NotesContext>().Database.EnsureCreatedAsync();
            }

            // Act
            await using (var scope = host.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<NotesContext>().Notes.Add(new Note { Id = 1, Text = "hello" });
                await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("notes").CommitAsync(CancellationToken.None);
            }

            // Assert
            await Assert.That(await ScratchDatabase.ScalarAsync(owner, "SELECT text FROM notes.notes WHERE id = 1")).IsEqualTo("hello");
        });
    }

    [Test]
    public async Task Readiness_is_healthy_while_the_database_answers()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Arrange
            using var host = await StartAsync(owner);

            // Act
            var report = await host.Services.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(registration => registration.Tags.Contains(HealthProbes.Ready));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(report.Status).IsEqualTo(HealthStatus.Healthy);
                await Assert.That(report.Entries.Keys).IsEquivalentTo(["notes-database"]);
            }
        });
    }

    [Test]
    public async Task Readiness_checks_with_a_pool_of_one_connection_even_when_the_module_pool_is_larger()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Arrange
            using var host = await StartAsync(owner, ("Database:MaxPoolSize", "9"));
            var connection = new NpgsqlConnectionStringBuilder(host.Services.ReadinessConnectionString(owner));

            // Act
            await using var scope = host.Services.CreateAsyncScope();
            var module = new NpgsqlConnectionStringBuilder(scope.ServiceProvider.GetRequiredService<NotesContext>().Database.GetConnectionString());

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(connection.MaxPoolSize).IsEqualTo(1);
                await Assert.That(module.MaxPoolSize).IsEqualTo(9);
            }
        });
    }

    private static async Task<IHost> StartAsync(string connectionString, params (string Key, string Value)[] settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(
            settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value))
                .Append(KeyValuePair.Create("ConnectionStrings:Notes", (string?)connectionString)));
        builder.Services.AddModuleDatabase<NotesContext>("notes", "notes");
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    /// <summary>A row of the test-only module.</summary>
    public sealed class Note
    {
        public int Id { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    /// <summary>The database of the test-only module.</summary>
    public sealed class NotesContext(DbContextOptions<NotesContext> options) : DbContext(options)
    {
        public DbSet<Note> Notes => Set<Note>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("notes");
            modelBuilder.Entity<Note>(note =>
            {
                note.ToTable("notes");
                note.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
                note.Property(n => n.Text).HasColumnName("text");
            });
        }
    }
}
