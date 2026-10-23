using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Frappe.Identity.Infrastructure.Persistence;

/// <summary>
/// One aggregate over four tables: principals, owner_accounts, sessions and verification_codes. Children are owned,
/// so they load and save only through their principal. The unique index on lower(email) is raw SQL in the
/// migration (<see cref="EmailUniqueIndex"/>): EF cannot model expression indexes.
/// </summary>
internal sealed class PrincipalConfiguration : IEntityTypeConfiguration<Principal>
{
    public const string EmailUniqueIndex = "owner_accounts_email_key";
    private const string PrincipalIdColumn = "PrincipalId";

    /// <summary>Generous for any canonical BCP-47 tag the platform could ever serve (RFC 5646 suggests 35).</summary>
    private const int LocaleMaxLength = 35;

    public void Configure(EntityTypeBuilder<Principal> builder)
    {
        builder.ToTable("principals");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Type).HasConversion<SnakeCaseEnumConverter<PrincipalType>>();
        builder.Property(p => p.RegisteredAt);

        builder.OwnsOne(p => p.Owner, ConfigureOwner);
        builder.Navigation(p => p.Owner).IsRequired();
        builder.OwnsMany(p => p.Sessions, ConfigureSession);
        builder.OwnsMany(p => p.Codes, ConfigureCode);
    }

    private static void ConfigureOwner(OwnedNavigationBuilder<Principal, OwnerAccount> owner)
    {
        owner.ToTable("owner_accounts");
        owner.WithOwner().HasForeignKey(PrincipalIdColumn);
        owner.HasKey(PrincipalIdColumn);
        owner.Property<Guid>(PrincipalIdColumn).HasColumnName("principal_id");
        owner.Property(o => o.Name)
            .HasConversion(name => name.Value, value => PersonName.Create(value).Value)
            .HasMaxLength(PersonName.MaxLength);
        owner.Property(o => o.Email)
            .HasConversion(email => email.Value, value => Email.Create(value).Value)
            .HasMaxLength(Email.MaxLength);
        owner.Property(o => o.PendingEmail)
            .HasConversion(email => email!.Value, value => Email.Create(value).Value)
            .HasMaxLength(Email.MaxLength);
        owner.Property(o => o.PasswordHash);
        owner.Property(o => o.Locale)
            .HasConversion(locale => locale.Value, value => Locale.Parse(value))
            .HasMaxLength(LocaleMaxLength);
        owner.Property(o => o.Status).HasConversion<SnakeCaseEnumConverter<OwnerStatus>>();

        // The abandoned-registration purge scans only pending owners.
        owner.HasIndex(o => o.Status).HasDatabaseName("owner_accounts_pending_idx").HasFilter("status = 'pending'");
    }

    private static void ConfigureSession(OwnedNavigationBuilder<Principal, Session> session)
    {
        session.ToTable("sessions");
        session.WithOwner().HasForeignKey(PrincipalIdColumn);
        session.HasKey(s => s.Id);
        session.Property(s => s.Id).ValueGeneratedNever();
        session.Property(s => s.TokenHash);
        session.Property(s => s.Surface).HasConversion<SnakeCaseEnumConverter<SessionSurface>>();
        session.Property(s => s.CreatedAt);
        session.Property(s => s.LastSeenAt);
        session.Property(s => s.StepUpExpiresAt);
        session.Ignore(s => s.AbsoluteExpiresAt);
        session.HasIndex(s => s.TokenHash).IsUnique();
    }

    private static void ConfigureCode(OwnedNavigationBuilder<Principal, VerificationCode> code)
    {
        code.ToTable("verification_codes");
        code.WithOwner().HasForeignKey(PrincipalIdColumn);
        code.HasKey(c => c.Id);
        code.Property(c => c.Id).ValueGeneratedNever();
        code.Property(c => c.Purpose).HasConversion<SnakeCaseEnumConverter<CodePurpose>>();
        code.Property(c => c.CodeHash);
        code.Property(c => c.IssuedAt);
        code.Property(c => c.UsedAt);
        code.Property(c => c.WindowStartedAt);
        code.Property(c => c.ResendCount);
        code.Ignore(c => c.ExpiresAt);

        // Only one live code per purpose, the same rule Principal.IssueCode keeps.
        code.HasIndex(PrincipalIdColumn, nameof(VerificationCode.Purpose)).IsUnique();
    }
}
