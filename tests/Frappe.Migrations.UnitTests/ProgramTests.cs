namespace Frappe.Migrations.UnitTests;

// The migrator is a one-shot deployment step: a failure must end it with a non-zero exit code, never
// an unhandled exception (which, as PID 1 in a container, ends in SIGSEGV).
public sealed class ProgramTests
{
    [Fact]
    public async Task An_unreachable_database_exits_with_1()
    {
        int exitCode = await Program.Main([
            "--connection",
            "Host=127.0.0.1;Port=1;Database=frappe;Username=frappe_owner;Password=x;Timeout=1",
        ]);

        exitCode.ShouldBe(1);
    }

    [Fact]
    public async Task A_malformed_connection_string_exits_with_1()
    {
        int exitCode = await Program.Main(["--connection", "Bogus Key=1"]);

        exitCode.ShouldBe(1);
    }

    [Fact]
    public async Task No_connection_argument_exits_with_2()
    {
        int exitCode = await Program.Main([]);

        exitCode.ShouldBe(2);
    }
}
