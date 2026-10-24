using Alba;

namespace Frappe.IntegrationTests.Identity;

/// <summary>Fires one request many times at once, to prove a limit holds against parallel attempts.</summary>
internal static class Burst
{
    public const int Size = 15;

    /// <summary>The status code of each of <see cref="Size"/> concurrent requests built by <paramref name="configure"/>.</summary>
    public static async Task<int[]> StatusesAsync(IAlbaHost host, Action<Scenario> configure)
    {
        var results = await Task.WhenAll(Enumerable.Range(0, Size).Select(_ => host.Scenario(s =>
        {
            configure(s);
            s.IgnoreStatusCode();
        })));
        return [.. results.Select(r => r.Context.Response.StatusCode)];
    }
}
