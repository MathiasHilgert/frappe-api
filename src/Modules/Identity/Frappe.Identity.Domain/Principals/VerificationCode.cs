using System.Diagnostics.CodeAnalysis;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Domain.Principals;

/// <summary>A short-lived, single-use code sent by email. Only its hash is kept.</summary>
public sealed class VerificationCode
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResendWindow = TimeSpan.FromHours(1);
    public const int MaxResendsPerHour = 5;

    private VerificationCode() => CodeHash = null!;

    private VerificationCode(
        Guid id, CodePurpose purpose, string codeHash, DateTimeOffset now, DateTimeOffset windowStartedAt, int resendCount)
    {
        Id = id;
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

    private static VerificationCode First(Guid id, CodePurpose purpose, string codeHash, DateTimeOffset now) =>
        new(id, purpose, codeHash, now, now, 0);

    /// <summary>The first code for <paramref name="purpose"/>, or a resend of <paramref name="previous"/>.</summary>
    internal static Result<VerificationCode> Replacing(
        VerificationCode? previous, Guid id, CodePurpose purpose, string codeHash, DateTimeOffset now) =>
        previous is null ? Result.Success(First(id, purpose, codeHash, now)) : previous.Resend(id, codeHash, now);

    /// <summary>A new code replacing this one, counted against the hourly resend limit.</summary>
    private Result<VerificationCode> Resend(Guid id, string codeHash, DateTimeOffset now)
    {
        if (now - WindowStartedAt >= ResendWindow)
        {
            return Result.Success(First(id, Purpose, codeHash, now));
        }

        return ResendCount >= MaxResendsPerHour
            ? Result.Failure<VerificationCode>(IdentityErrors.ResendLimitReached)
            : Result.Success(new VerificationCode(id, Purpose, codeHash, now, WindowStartedAt, ResendCount + 1));
    }

    /// <summary>Whether <paramref name="codeHash"/> is <paramref name="code"/>'s and it could be used now; uses nothing.</summary>
    internal static Result Check(VerificationCode? code, string codeHash, DateTimeOffset now) =>
        Matches(code, codeHash) ? code.CanConsume(now) : Result.Failure(IdentityErrors.CodeInvalid);

    /// <summary>Uses <paramref name="code"/> up when <paramref name="codeHash"/> is its hash and it is still valid.</summary>
    internal static Result Consume(VerificationCode? code, string codeHash, DateTimeOffset now) =>
        Matches(code, codeHash) ? code.Consume(now) : Result.Failure(IdentityErrors.CodeInvalid);

    // Hashes are compared, never codes: the caller hashes what the person typed with the same port.
    private static bool Matches([NotNullWhen(true)] VerificationCode? code, string codeHash) =>
        code is not null && string.Equals(code.CodeHash, codeHash, StringComparison.Ordinal);

    private Result CanConsume(DateTimeOffset now)
    {
        var notification = new ValidationNotification()
            .AddIf(UsedAt is not null, IdentityErrors.CodeAlreadyUsed)
            .AddIf(UsedAt is null && now >= ExpiresAt, IdentityErrors.CodeExpired);

        return notification.HasErrors ? Result.Failure(notification) : Result.Success();
    }

    private Result Consume(DateTimeOffset now)
    {
        var consumable = CanConsume(now);
        if (consumable.IsSuccess)
        {
            UsedAt = now;
        }
        return consumable;
    }
}
