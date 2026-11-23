using Frappe.Localization;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Platform.UnitTests.Persistence;

/// <summary>A language tag is stored through <see cref="ModuleDatabase.StoreLanguageTags"/> with the length every tag fits in (DATA-10).</summary>
public sealed class LanguageTagStorageTests
{
    [Test]
    public async Task A_language_tag_column_is_as_long_as_the_longest_tag_a_tag_can_be()
    {
        // Arrange
        await using var context = new TaggedContext();

        // Act
        var length = context.Model.FindEntityType(typeof(Tagged))!.FindProperty(nameof(Tagged.Language))!.GetMaxLength();

        // Assert
        await Assert.That(length).IsEqualTo(LanguageTag.StorageLength);
    }

    /// <summary>A row holding a language tag.</summary>
    private sealed class Tagged
    {
        public LanguageTag Language { get; set; } = null!;
    }

    /// <summary>The database context of the tagged rows.</summary>
    private sealed class TaggedContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseNpgsql("Host=unreachable.invalid;Database=frappe");

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Tagged>().HasKey(t => t.Language);

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.StoreLanguageTags();
    }
}
