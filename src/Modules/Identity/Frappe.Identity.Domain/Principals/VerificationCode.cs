using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Domain.Principals;

/// <summary>A short-lived, single-use code sent by email. Only its hash is kept.</summary>
public sealed class VerificationCode
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResendWindow = TimeSpan.FromHours(1);
    public const int MaxResendsPerHour = 5;

    // For EF Core: rehydration sets every member through its backing field.
    private VerificationCode() => CodeHash = null!;

    private VerificationCode(CodePurpose purpose, string codeHash, DateTimeOffset now, DateTimeOffset windowStartedAt, int resendCount)
    {
        Id = Guid.CreateVersion7(now);
        Purpose = purpose;
        CodeHash = codeHash;
        IssuedAt = now;
        WindowStartedAt = windowStartedAt;
        ResendCount = resendCount;
    }

    public Guid Id { get; }
    public CodePurpose Purpose { get; }
    public string CodeHash { get; }
    public DateTimeOffset IssuedAt { get; }
    public DateTimeOffset ExpiresAt => IssuedAt + Lifetime;
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>When the first code of the current resend window was sent.</summary>
    public DateTimeOffset WindowStartedAt { get; }

    /// <summary>How many codes replaced the first one within <see cref="ResendWindow"/>.</summary>
    public int ResendCount { get; }

    internal static VerificationCode First(CodePurpose purpose, string codeHash, DateTimeOffset now) =>
        new(purpose, codeHash, now, now, 0);

    /// <summary>A new code replacing this one, counted against the hourly resend limit.</summary>
    internal Result<VerificationCode> Resend(string codeHash, DateTimeOffset now)
    {
        if (now - WindowStartedAt >= ResendWindow)
        {
            return Result.Success(First(Purpose, codeHash, now));
        }

        return ResendCount >= MaxResendsPerHour
            ? Result.Failure<VerificationCode>(IdentityErrors.ResendLimitReached)
            : Result.Success(new VerificationCode(Purpose, codeHash, now, WindowStartedAt, ResendCount + 1));
    }

    internal Result Consume(DateTimeOffset now)
    {
        var notification = new Notification()
            .AddIf(UsedAt is not null, IdentityErrors.CodeAlreadyUsed)
            .AddIf(UsedAt is null && now >= ExpiresAt, IdentityErrors.CodeExpired);

        if (notification.HasErrors)
        {
            return Result.Failure(notification);
        }

        UsedAt = now;
        return Result.Success();
    }
}
