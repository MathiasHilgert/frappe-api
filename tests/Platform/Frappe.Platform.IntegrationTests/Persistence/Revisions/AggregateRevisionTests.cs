using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.IntegrationTests.Persistence.Revisions;

public sealed class AggregateRevisionTests(BasketDatabase database) : IClassFixture<BasketDatabase>
{
    [Fact]
    public async Task A_new_aggregate_is_saved_at_revision_zero()
    {
        Basket basket = new(BasketId.NewId());
        basket.AddLine(quantity: 1);

        await AddAsync(basket);

        (await StoredRevisionAsync(basket.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Saving_a_modified_aggregate_increments_its_revision_once()
    {
        Basket seeded = await SeedAsync();

        int revision = await ChangeAsync(seeded.Id, basket => basket.Rename("weekly"));

        revision.ShouldBe(1);
        (await StoredRevisionAsync(seeded.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Saving_an_unchanged_aggregate_keeps_its_revision()
    {
        Basket seeded = await SeedAsync();

        await ChangeAsync(seeded.Id, _ => { });

        (await StoredRevisionAsync(seeded.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Changing_an_owned_value_increments_the_root_revision()
    {
        Basket seeded = await SeedAsync();

        await ChangeAsync(seeded.Id, basket => basket.Annotate("no nuts"));

        (await StoredRevisionAsync(seeded.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Adding_a_child_increments_the_root_revision()
    {
        Basket seeded = await SeedAsync();

        await ChangeAsync(seeded.Id, basket => basket.AddLine(quantity: 2));

        (await StoredRevisionAsync(seeded.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Changing_a_child_increments_the_root_revision()
    {
        Basket seeded = await SeedAsync(lines: 1);
        BasketLineId line = seeded.Lines[0].Id;

        await ChangeAsync(seeded.Id, basket => basket.ChangeQuantity(line, quantity: 3));

        (await StoredRevisionAsync(seeded.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Removing_a_child_increments_the_root_revision()
    {
        Basket seeded = await SeedAsync(lines: 1);
        BasketLineId line = seeded.Lines[0].Id;

        await ChangeAsync(seeded.Id, basket => basket.RemoveLine(line));

        (await StoredRevisionAsync(seeded.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Several_changes_in_one_save_increment_the_revision_once()
    {
        Basket seeded = await SeedAsync(lines: 1);
        BasketLineId line = seeded.Lines[0].Id;

        await ChangeAsync(
            seeded.Id,
            basket =>
            {
                basket.Rename("weekly");
                basket.Annotate("no nuts");
                basket.ChangeQuantity(line, quantity: 3);
                basket.AddLine(quantity: 2);
            }
        );

        (await StoredRevisionAsync(seeded.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_stale_expected_revision_fails_the_save()
    {
        Basket seeded = await SeedAsync();
        await ChangeAsync(seeded.Id, basket => basket.Rename("weekly"));
        await using BasketContext db = database.CreateContext();
        Basket basket = await LoadAsync(db, seeded.Id);
        db.Entry(basket).Property(b => b.Revision).OriginalValue = 0;
        basket.Rename("monthly");

        Func<Task> save = () => db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await save.ShouldThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Concurrent_changes_to_different_children_of_one_aggregate_conflict()
    {
        Basket seeded = await SeedAsync(lines: 2);
        await using BasketContext first = database.CreateContext();
        await using BasketContext second = database.CreateContext();
        Basket firstCopy = await LoadAsync(first, seeded.Id);
        Basket secondCopy = await LoadAsync(second, seeded.Id);
        firstCopy.ChangeQuantity(seeded.Lines[0].Id, quantity: 5);
        secondCopy.ChangeQuantity(seeded.Lines[1].Id, quantity: 7);
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        Func<Task> save = () => second.SaveChangesAsync(TestContext.Current.CancellationToken);

        await save.ShouldThrowAsync<DbUpdateConcurrencyException>();
    }

    private static Task<Basket> LoadAsync(BasketContext db, BasketId id) =>
        db.Set<Basket>()
            .Include(b => b.Lines)
            .SingleAsync(b => b.Id == id, TestContext.Current.CancellationToken);

    private async Task<Basket> SeedAsync(int lines = 0)
    {
        Basket basket = new(BasketId.NewId());
        for (int quantity = 1; quantity <= lines; quantity++)
        {
            basket.AddLine(quantity);
        }

        await AddAsync(basket);
        return basket;
    }

    private async Task AddAsync(Basket basket)
    {
        await using BasketContext db = database.CreateContext();
        db.Add(basket);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // Loads the aggregate in a fresh context, applies the change and saves, as one request would.
    private async Task<int> ChangeAsync(BasketId id, Action<Basket> change)
    {
        await using BasketContext db = database.CreateContext();
        Basket basket = await LoadAsync(db, id);
        change(basket);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return basket.Revision;
    }

    private async Task<int> StoredRevisionAsync(BasketId id)
    {
        await using BasketContext db = database.CreateContext();
        return await db.Set<Basket>()
            .Where(b => b.Id == id)
            .Select(b => b.Revision)
            .SingleAsync(TestContext.Current.CancellationToken);
    }
}
