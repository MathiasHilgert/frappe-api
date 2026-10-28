using Alba;
using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

public sealed class FrappeDbContextTests(AppFixture app)
{
    [Fact]
    public async Task The_database_context_connects_to_the_database()
    {
        await using AsyncServiceScope scope = app.Host.Services.CreateAsyncScope();
        FrappeDbContext db = scope.ServiceProvider.GetRequiredService<FrappeDbContext>();

        bool connected = await db.Database.CanConnectAsync(TestContext.Current.CancellationToken);

        connected.ShouldBeTrue();
    }

    [Fact]
    public async Task The_host_connects_as_the_app_role()
    {
        await using AsyncServiceScope scope = app.Host.Services.CreateAsyncScope();
        FrappeDbContext db = scope.ServiceProvider.GetRequiredService<FrappeDbContext>();

        string user = await db
            .Database.SqlQuery<string>($"SELECT current_user::text AS \"Value\"")
            .SingleAsync(TestContext.Current.CancellationToken);

        user.ShouldBe("frappe_app");
    }

    [Fact]
    public async Task An_instant_round_trips_through_the_database_context()
    {
        Instant sent = Instant.FromUtc(2026, 10, 27, 13, 45, 30).PlusNanoseconds(123_456_000);
        await using AsyncServiceScope scope = app.Host.Services.CreateAsyncScope();
        FrappeDbContext db = scope.ServiceProvider.GetRequiredService<FrappeDbContext>();

        Instant received = await db
            .Database.SqlQuery<Instant>($"SELECT {sent} AS \"Value\"")
            .SingleAsync(TestContext.Current.CancellationToken);

        received.ShouldBe(sent);
    }

    [Fact]
    public async Task A_snake_case_column_maps_to_its_pascal_case_property()
    {
        const int Answer = 42;
        await using AsyncServiceScope scope = app.Host.Services.CreateAsyncScope();
        FrappeDbContext db = scope.ServiceProvider.GetRequiredService<FrappeDbContext>();

        NamingProbe probe = await db
            .Database.SqlQuery<NamingProbe>($"SELECT {Answer} AS answer_value")
            .SingleAsync(TestContext.Current.CancellationToken);

        probe.AnswerValue.ShouldBe(Answer);
    }

    [Theory]
    [InlineData("all", "frappe-all")]
    [InlineData("api", "frappe-api")]
    [InlineData("worker", "frappe-worker")]
    [InlineData("scheduler", "frappe-scheduler")]
    public async Task Each_role_names_its_database_connections(string role, string applicationName)
    {
        await using IAlbaHost host = await AlbaHost.For<Program>(builder =>
            builder
                .UseSetting("Platform:Role", role)
                .UseSetting("ConnectionStrings:Frappe", app.ConnectionString)
        );
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        FrappeDbContext db = scope.ServiceProvider.GetRequiredService<FrappeDbContext>();

        string name = await db
            .Database.SqlQuery<string>($"SELECT current_setting('application_name') AS \"Value\"")
            .SingleAsync(TestContext.Current.CancellationToken);

        name.ShouldBe(applicationName);
    }

    [Fact]
    public async Task Connections_never_attempt_gss_encryption_even_when_asked_to()
    {
        await using IAlbaHost host = await AlbaHost.For<Program>(builder =>
            builder.UseSetting(
                "ConnectionStrings:Frappe",
                $"{app.ConnectionString};GSS Encryption Mode=Prefer"
            )
        );
        NpgsqlDataSource dataSource = host.Services.GetRequiredService<NpgsqlDataSource>();

        NpgsqlConnectionStringBuilder settings = new(dataSource.ConnectionString);

        settings.GssEncryptionMode.ShouldBe(GssEncryptionMode.Disable);
    }

    [Fact]
    public async Task Connections_never_multiplex_even_when_asked_to()
    {
        await using IAlbaHost host = await AlbaHost.For<Program>(builder =>
            builder.UseSetting(
                "ConnectionStrings:Frappe",
                $"{app.ConnectionString};Multiplexing=true"
            )
        );
        NpgsqlDataSource dataSource = host.Services.GetRequiredService<NpgsqlDataSource>();

        NpgsqlConnectionStringBuilder settings = new(dataSource.ConnectionString);

        settings.Multiplexing.ShouldBeFalse();
    }

    public sealed record NamingProbe(int AnswerValue);
}
