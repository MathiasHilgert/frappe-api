using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Frappe.Platform.UnitTests.Persistence;

/// <summary>
/// A closed set is stored as lower snake_case text guarded by a CHECK (DATA-06), all three from one source. Each enum's
/// stored spelling is pinned here, so renaming a member cannot change what the database holds.
/// </summary>
public sealed class StoredEnumTests
{
    [Test]
    [Arguments(Surface.Web, "web")]
    [Arguments(Surface.MobileApp, "mobile_app")]
    [Arguments(Surface.TourVoucherDesk, "tour_voucher_desk")]
    public async Task A_member_is_stored_as_its_lower_snake_case_name(Surface value, string stored)
    {
        // Act
        var text = StoredEnum<Surface>.ToText(value);

        // Assert
        await Assert.That(text).IsEqualTo(stored);
    }

    [Test]
    [Arguments(Reason.Created, "created")]
    [Arguments(Reason.EmailChange, "email_change")]
    [Arguments(Reason.TouristNonWorkingDay, "tourist_non_working_day")]
    public async Task The_spelling_of_every_enum_is_pinned_apart_from_the_others(Reason value, string stored)
    {
        // Act
        var text = StoredEnum<Reason>.ToText(value);

        // Assert
        await Assert.That(text).IsEqualTo(stored);
    }

    [Test]
    public async Task The_converter_writes_the_text_and_reads_the_member_back()
    {
        // Act
        var written = StoredEnum<Surface>.Converter.ConvertToProvider(Surface.MobileApp);
        var read = StoredEnum<Surface>.Converter.ConvertFromProvider("tour_voucher_desk");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(written).IsEqualTo("mobile_app");
            await Assert.That(read).IsEqualTo(Surface.TourVoucherDesk);
        }
    }

    [Test]
    public async Task Every_member_survives_a_round_trip()
    {
        // Act
        var restored = Enum.GetValues<Reason>().Select(value => StoredEnum<Reason>.FromText(StoredEnum<Reason>.ToText(value)));

        // Assert
        await Assert.That(restored).IsEquivalentTo(Enum.GetValues<Reason>());
    }

    [Test]
    public async Task The_check_list_names_every_stored_value_quoted_in_declaration_order()
    {
        // Act
        var list = StoredEnum<Surface>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'web', 'mobile_app', 'tour_voucher_desk'");
    }

    [Test]
    public async Task The_length_is_that_of_the_longest_stored_value()
    {
        // Act
        var length = StoredEnum<Surface>.MaxLength;

        // Assert
        await Assert.That(length).IsEqualTo("tour_voucher_desk".Length);
    }

    [Test]
    public async Task Text_no_member_has_is_refused_when_read_naming_the_enum()
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => StoredEnum<Surface>.FromText("kiosk"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure.Message).Contains("kiosk");
            await Assert.That(failure.Message).Contains(nameof(Surface));
        }
    }

    [Test]
    public async Task A_value_that_is_no_member_is_refused_when_written()
    {
        // Act
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => StoredEnum<Surface>.ToText((Surface)99));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("value");
    }

    [Test]
    public async Task An_enum_without_members_is_refused_naming_the_enum_instead_of_failing_the_type_initializer()
    {
        // Act
        var checkList = Assert.Throws<InvalidOperationException>(() => _ = StoredEnum<Nothing>.CheckList);
        var maxLength = Assert.Throws<InvalidOperationException>(() => _ = StoredEnum<Nothing>.MaxLength);
        var converter = Assert.Throws<InvalidOperationException>(() => _ = StoredEnum<Nothing>.Converter);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(checkList.Message).Contains(nameof(Nothing));
            await Assert.That(maxLength.Message).Contains(nameof(Nothing));
            await Assert.That(converter.Message).Contains(nameof(Nothing));
        }
    }

    [Test]
    public async Task A_property_mapped_with_it_is_text_as_long_as_the_longest_value()
    {
        // Arrange
        await using var context = new SessionsContext();

        // Act
        var property = context.Model.FindEntityType(typeof(Session))!.FindProperty(nameof(Session.Surface))!;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(property.GetMaxLength()).IsEqualTo(StoredEnum<Surface>.MaxLength);
            await Assert.That(property.GetValueConverter()).IsSameReferenceAs(StoredEnum<Surface>.Converter);
        }
    }

    [Test]
    public async Task A_property_mapped_with_it_gets_a_check_constraint_over_the_same_list()
    {
        // Arrange
        await using var context = new SessionsContext();

        // Act
        var check = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Session))!.GetCheckConstraints().Single();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(check.Name).IsEqualTo("ck_sessions_surface");
            await Assert.That(check.Sql).IsEqualTo($"surface IN ({StoredEnum<Surface>.CheckList})");
        }
    }

    /// <summary>Where a session was opened; the spelling of each member is the one a database stores.</summary>
    public enum Surface
    {
        Web,
        MobileApp,
        TourVoucherDesk,
    }

    /// <summary>An enum with no member, which has nothing to store.</summary>
    public enum Nothing
    {
    }

    /// <summary>Why something happened; a second enum, to pin its spelling on its own.</summary>
    public enum Reason
    {
        Created,
        EmailChange,
        TouristNonWorkingDay,
    }

    /// <summary>A row holding a closed set.</summary>
    private sealed class Session
    {
        public Surface Surface { get; set; }
    }

    /// <summary>The database context of the sessions, mapped the way DATA-06 asks.</summary>
    private sealed class SessionsContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseNpgsql("Host=unreachable.invalid;Database=frappe");

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Session>(session =>
            {
                session.HasNoKey();
                session.Property(s => s.Surface)
                    .HasConversion(StoredEnum<Surface>.Converter)
                    .HasMaxLength(StoredEnum<Surface>.MaxLength);
                session.ToTable("sessions", table => table.HasCheckConstraint(
                    "ck_sessions_surface",
                    $"surface IN ({StoredEnum<Surface>.CheckList})"));
            });
    }
}
