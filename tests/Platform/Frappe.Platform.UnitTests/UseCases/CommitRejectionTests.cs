using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.UseCases;

/// <summary>The commit rejection port: the conflict code lives with it so the HTTP layer needs no storage reference.</summary>
public sealed class CommitRejectionTests
{
    [Test]
    public async Task The_commit_conflict_code_is_a_stable_contract_string()
    {
        // Assert
        await Assert.That(ICommitRejection.CommitConflictCode).IsEqualTo("platform.persistence.commit_conflict");
    }
}
