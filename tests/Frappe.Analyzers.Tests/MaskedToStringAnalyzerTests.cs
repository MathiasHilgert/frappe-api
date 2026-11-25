namespace Frappe.Analyzers.Tests;

/// <summary>FRP001 (SEC-07, EVT-20, DOM-06): a record that prints a secret or an email masks its <c>ToString</c>.</summary>
public sealed class MaskedToStringAnalyzerTests
{
    [Test]
    [Arguments("Password")]
    [Arguments("NewPassword")]
    [Arguments("Token")]
    [Arguments("UndoToken")]
    [Arguments("Secret")]
    [Arguments("WebhookSecret")]
    [Arguments("Plaintext")]
    [Arguments("Passphrase")]
    [Arguments("Email")]
    [Arguments("NewEmail")]
    public async Task A_record_with_a_sensitive_string_and_the_generated_ToString_is_reported(string member)
    {
        // Arrange
        var source = $"public sealed record Sample(string {member});";

        // Act
        var diagnostics = await AnalyzerRun.DiagnosticsOf(new MaskedToStringAnalyzer(), source);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(diagnostics).Count().IsEqualTo(1);
            await Assert.That(diagnostics[0].Id).IsEqualTo("FRP001");
            await Assert.That(diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture)).Contains(member);
        }
    }

    [Test]
    public async Task A_sensitive_property_declared_in_the_body_of_a_record_struct_is_reported()
    {
        // Arrange
        const string source = "public readonly record struct Sample { public string? Token { get; init; } }";

        // Act
        var diagnostics = await AnalyzerRun.DiagnosticsOf(new MaskedToStringAnalyzer(), source);

        // Assert
        await Assert.That(diagnostics.Select(d => d.Id)).IsEquivalentTo(["FRP001"]);
    }

    [Test]
    public async Task A_record_that_overrides_ToString_is_not_reported()
    {
        // Arrange
        const string source = """
            /// <summary>A sample type.</summary>
            public sealed record Sample(string Password)
            {
                public override string ToString() => "Sample { Password = *** }";
            }
            """;

        // Act
        var diagnostics = await AnalyzerRun.DiagnosticsOf(new MaskedToStringAnalyzer(), source);

        // Assert
        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task A_record_whose_base_seals_ToString_is_not_reported()
    {
        // Arrange
        const string source = """
            /// <summary>A sample type.</summary>
            public abstract record Masked
            {
                public sealed override string ToString() => "***";
            }

            /// <summary>A sample type.</summary>
            public sealed record Sample(string Token) : Masked;
            """;

        // Act
        var diagnostics = await AnalyzerRun.DiagnosticsOf(new MaskedToStringAnalyzer(), source);

        // Assert
        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    [Arguments("public sealed record Sample(string Code, string Name, string TokenKind);")]
    [Arguments("public sealed record Sample(byte[] Token, int PasswordLength);")]
    [Arguments("public sealed record Sample { private string Secret { get; } = \"\"; public static string Token => \"\"; }")]
    [Arguments("public sealed class Sample { public string Password { get; init; } = \"\"; }")]
    public async Task A_type_whose_generated_ToString_prints_no_sensitive_string_is_not_reported(string source)
    {
        // Act
        var diagnostics = await AnalyzerRun.DiagnosticsOf(new MaskedToStringAnalyzer(), source);

        // Assert
        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task A_record_holding_a_type_that_masks_itself_is_not_reported()
    {
        // Arrange
        const string source = """
            /// <summary>A sample type.</summary>
            public sealed record Password(string Value)
            {
                public override string ToString() => "***";
            }

            /// <summary>A sample type.</summary>
            public sealed record Sample(Password Password);
            """;

        // Act
        var diagnostics = await AnalyzerRun.DiagnosticsOf(new MaskedToStringAnalyzer(), source);

        // Assert
        await Assert.That(diagnostics).IsEmpty();
    }
}
