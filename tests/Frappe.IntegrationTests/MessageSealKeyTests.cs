using System.Text.Json;
using Frappe.Platform.Messaging.Sealing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.IntegrationTests;

/// <summary>The development seal key is public (it is committed), so nothing outside Development may run on it.</summary>
public sealed class MessageSealKeyTests(ApiFixture fixture)
{
    [Fact]
    public async Task Production_WithTheDevelopmentSealKey_RefusesToStart()
    {
        var error = await Should.ThrowAsync<Exception>(() => fixture.CreateHostAsync(
            environment: "Production", configureSettings: s => s["Messaging:Seal:Key"] = DevelopmentSealKey()));

        error.ToString().ShouldContain("'Messaging:Seal:Key' is the committed development key; set a real one outside Development.");
    }

    [Theory]
    [InlineData("upper")]
    [InlineData("padded")]
    [InlineData("base64")]
    public async Task Production_WithAVariantOfTheDevelopmentSealKey_RefusesToStart(string variant)
    {
        var key = DevelopmentSealKey();
        var configured = variant switch
        {
            "upper" => key.ToUpperInvariant(),
            "padded" => $"  {key}\t",
            _ => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(key)),
        };

        var error = await Should.ThrowAsync<Exception>(() => fixture.CreateHostAsync(
            environment: "Production", configureSettings: s => s["Messaging:Seal:Key"] = configured));

        error.ToString().ShouldContain("'Messaging:Seal:Key' is the committed development key; set a real one outside Development.");
    }

    [Fact]
    public async Task Development_AcceptsTheDevelopmentSealKey()
    {
        await using var host = await fixture.CreateHostAsync(configureSettings: s => s["Messaging:Seal:Key"] = DevelopmentSealKey());

        host.Services.GetRequiredService<IOptions<MessageSealOptions>>().Value.Key.ShouldBe(MessageSealOptions.DevelopmentKey);
    }

    [Fact]
    public async Task Production_WithItsOwnSealKey_Starts()
    {
        await using var host = await fixture.CreateHostAsync(environment: "Production");

        host.Services.GetRequiredService<IOptions<MessageSealOptions>>().Value.Key.ShouldNotBe(MessageSealOptions.DevelopmentKey, StringComparer.Ordinal);
    }

    [Fact]
    public void TheRefusedKey_IsTheOneInAppsettingsDevelopment() =>
        MessageSealOptions.DevelopmentKey.ShouldBe(DevelopmentSealKey());

    // Read from the committed file itself, so the guard and the file can never drift apart.
    private static string DevelopmentSealKey()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty("Messaging").GetProperty("Seal").GetProperty("Key").GetString()!;
    }
}
