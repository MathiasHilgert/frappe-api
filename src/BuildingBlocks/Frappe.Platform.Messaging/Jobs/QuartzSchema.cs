using System.Reflection;
using JasperFx.Resources;
using Npgsql;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Frappe.Platform.Messaging.Jobs;

/// <summary>
/// The clustered Quartz store's tables, in their own <see cref="Schema"/>. A JasperFx stateful resource, so
/// <c>resources setup</c> provisions it before a deploy next to Wolverine's storage; Development applies it on start.
/// The script is idempotent: running it on a provisioned database changes nothing.
/// </summary>
internal sealed class QuartzSchema(string connectionString) : IStatefulResource
{
    public const string Schema = "quartz";
    public const string TablePrefix = Schema + ".qrtz_";

    public string Type => "Database";
    public string Name => "Quartz job store";
    public Uri SubjectUri => new("frappe://jobs");
    public Uri ResourceUri => new($"postgresql://{Schema}");

    public async Task Setup(CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var command = new NpgsqlCommand(await ReadScriptAsync(token), connection, transaction))
            await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task Check(CancellationToken token)
    {
        if (!await IsProvisionedAsync(token))
            throw new InvalidOperationException($"The Quartz job store is missing: run `resources setup` to create schema '{Schema}'.");
    }

    public async Task<IRenderable> DetermineStatus(CancellationToken token) =>
        new Markup(await IsProvisionedAsync(token) ? "provisioned" : "missing");

    // The store is shared cluster state: clearing or dropping it is never done by the application.
    public Task ClearState(CancellationToken token) => Task.CompletedTask;

    public Task Teardown(CancellationToken token) => Task.CompletedTask;

    private async Task<bool> IsProvisionedAsync(CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand("SELECT to_regclass('quartz.qrtz_locks') IS NOT NULL", connection);
        return (bool)(await command.ExecuteScalarAsync(token))!;
    }

    private static async Task<string> ReadScriptAsync(CancellationToken token)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Frappe.Platform.Messaging.Jobs.quartz-postgres.sql")
            ?? throw new InvalidOperationException("The embedded Quartz schema script is missing.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(token);
    }
}
