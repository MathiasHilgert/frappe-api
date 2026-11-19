namespace Frappe.Platform.UnitTests;

/// <summary>Runs an action and hands back what it threw, so a test can assert on the failure in its own Assert phase.</summary>
internal static class Caught
{
    /// <summary>Runs <paramref name="action"/> and returns its exception, or <see langword="null"/> when it completed.</summary>
    public static Exception? ExceptionOf(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception failure)
        {
            return failure;
        }
    }

    /// <summary>Awaits <paramref name="action"/> and returns its exception, or <see langword="null"/> when it completed.</summary>
    public static async Task<Exception?> ExceptionOfAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception failure)
        {
            return failure;
        }
    }
}
