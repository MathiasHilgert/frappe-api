using Frappe.Persistence;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>
/// A closed set stored through <c>StoredEnum</c> is lower snake_case text, and the database itself refuses any other text
/// (DATA-06).
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class StoredEnumStorageTests(PostgresServer server)
{
    [Test]
    public async Task A_member_is_written_as_its_text_and_read_back_as_the_member()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Arrange
            await using (var setup = Context(owner))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.Tickets.Add(new Ticket { Id = 1, Channel = Channel.TourVoucherDesk });
                await setup.SaveChangesAsync();
            }

            // Act
            var stored = await ScratchDatabase.ScalarAsync(owner, "SELECT channel FROM tickets.tickets WHERE id = 1");
            await using var read = Context(owner);
            var channel = (await read.Tickets.SingleAsync(ticket => ticket.Id == 1)).Channel;

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(stored).IsEqualTo("tour_voucher_desk");
                await Assert.That(channel).IsEqualTo(Channel.TourVoucherDesk);
            }
        });
    }

    [Test]
    public async Task A_text_that_is_no_member_is_refused_by_the_check_constraint()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Arrange
            await using (var setup = Context(owner))
            {
                await setup.Database.EnsureCreatedAsync();
            }

            // Act
            var refusal = await Assert.ThrowsAsync<PostgresException>(
                () => ScratchDatabase.ExecuteAsync(owner, "INSERT INTO tickets.tickets (id, channel) VALUES (2, 'kiosk')"));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(refusal!.SqlState).IsEqualTo(PostgresErrorCodes.CheckViolation);
                await Assert.That(refusal.ConstraintName).IsEqualTo("ck_tickets_channel");
            }
        });
    }

    [Test]
    public async Task The_column_is_as_long_as_the_longest_stored_text()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Arrange
            await using (var setup = Context(owner))
            {
                await setup.Database.EnsureCreatedAsync();
            }

            // Act
            var length = await ScratchDatabase.ScalarAsync(
                owner,
                """
                SELECT character_maximum_length FROM information_schema.columns
                WHERE table_schema = 'tickets' AND table_name = 'tickets' AND column_name = 'channel'
                """);

            // Assert
            await Assert.That(length).IsEqualTo(StoredEnum<Channel>.MaxLength);
        });
    }

    private static TicketsContext Context(string connectionString) =>
        new(ModuleDatabase.Options<TicketsContext>(connectionString, "tickets"));

    /// <summary>Where a ticket was bought; the spelling of each member is the one the database stores.</summary>
    public enum Channel
    {
        Web,
        MobileApp,
        TourVoucherDesk,
    }

    /// <summary>A row holding a closed set.</summary>
    public sealed class Ticket
    {
        public int Id { get; set; }

        public Channel Channel { get; set; }
    }

    /// <summary>The tickets' context, mapped the way DATA-06 asks.</summary>
    public sealed class TicketsContext(DbContextOptions<TicketsContext> options) : DbContext(options)
    {
        public DbSet<Ticket> Tickets => Set<Ticket>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("tickets");
            modelBuilder.Entity<Ticket>(ticket =>
            {
                ticket.ToTable("tickets", table => table.HasCheckConstraint("ck_tickets_channel", $"channel IN ({StoredEnum<Channel>.CheckList})"));
                ticket.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
                ticket.Property(t => t.Channel)
                    .HasColumnName("channel")
                    .HasConversion(StoredEnum<Channel>.Converter)
                    .HasMaxLength(StoredEnum<Channel>.MaxLength);
            });
        }
    }
}
