using Frappe.Geo.Domain.ReferenceData;

namespace Frappe.Geo.UnitTests.Domain;

public sealed class ReferenceDataPlanTests
{
    private static Dictionary<string, bool> Stored(params (string Key, bool Active)[] rows) => rows.ToDictionary(r => r.Key, r => r.Active);

    [Test]
    public async Task New_keys_are_inserted_changed_ones_updated_and_missing_ones_retired_never_deleted()
    {
        var plan = ReferenceDataPlan.For(Stored(("AR", true), ("UY", true), ("CL", true)), ["AR", "UY", "BR"], key => key == "UY");

        await Assert.That(plan.Insert).IsEquivalentTo(["BR"]);
        await Assert.That(plan.Update).IsEquivalentTo(["UY"]);
        await Assert.That(plan.Retire).IsEquivalentTo(["CL"]);
        await Assert.That(plan.Revive).IsEmpty();
        await Assert.That(plan.ActiveBefore).IsEqualTo(3);
    }

    [Test]
    public async Task A_retired_key_that_comes_back_is_revived_and_a_retired_one_still_missing_is_left_alone()
    {
        var plan = ReferenceDataPlan.For(Stored(("AR", false), ("UY", false)), ["AR"], _ => false);

        await Assert.That(plan.Revive).IsEquivalentTo(["AR"]);
        await Assert.That(plan.Retire).IsEmpty();
    }

    [Test]
    public async Task The_same_snapshot_again_changes_nothing()
    {
        var plan = ReferenceDataPlan.For(Stored(("AR", true)), ["AR"], _ => false);

        await Assert.That(plan.ChangesAnything).IsFalse();
    }

    [Test]
    [Arguments(100, 5, false)]
    [Arguments(100, 6, true)]
    [Arguments(3, 1, true)]
    public async Task Retiring_more_than_five_percent_of_the_active_records_is_suspicious(int active, int retired, bool suspicious)
    {
        var stored = Enumerable.Range(0, active).ToDictionary(i => i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), _ => true);
        var desired = stored.Keys.Skip(retired);

        var plan = ReferenceDataPlan.For(stored, desired, _ => false);

        await Assert.That(plan.RetiresTooMuch).IsEqualTo(suspicious);
    }
}
