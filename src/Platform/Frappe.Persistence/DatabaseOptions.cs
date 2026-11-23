namespace Frappe.Persistence;

/// <summary>
/// The limits every Postgres connection of Frappé runs under, bound from section <c>Database</c> and validated at
/// startup by <see cref="DatabaseOptionsValidator"/>. The Api and the Worker apply <see cref="MaxPoolSize"/> and
/// <see cref="CommandTimeout"/> to each module database and to the scheduler's job store
/// (<see cref="ModuleDatabase.WithConnectionLimits"/>); the Migrator sets <see cref="StatementTimeout"/> and
/// <see cref="IdleInTransactionSessionTimeout"/> on every login role it creates. The defaults keep three Api replicas,
/// one Worker, the Migrator and an administrator inside Postgres' default <c>max_connections</c> of 100.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>The configuration section, <c>Database</c>.</summary>
    public const string Section = "Database";

    /// <summary>The default of <see cref="MaxPoolSize"/>.</summary>
    public const int DefaultMaxPoolSize = 6;

    /// <summary>The largest <see cref="MaxPoolSize"/> accepted: far above any budget, it only catches a typo.</summary>
    public const int MaxPoolSizeLimit = 1000;

    /// <summary>The default of <see cref="StatementTimeout"/>: Npgsql's own default command timeout.</summary>
    public static readonly TimeSpan DefaultStatementTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The default of <see cref="CommandTimeout"/>: 5 s longer than <see cref="DefaultStatementTimeout"/>, so a slow
    /// statement is normally cancelled by the server (SQLSTATE 57014, the session stays usable) and the client timeout
    /// is only the backstop for a server or network that stopped answering.
    /// </summary>
    public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(35);

    /// <summary>
    /// The default of <see cref="IdleInTransactionSessionTimeout"/>. No path idles in a transaction while it waits on the
    /// network, so a minute only ends a transaction a client abandoned.
    /// </summary>
    public static readonly TimeSpan DefaultIdleInTransactionSessionTimeout = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The least <see cref="IdleInTransactionSessionTimeout"/> accepted. The timer counts only the gap between two
    /// statements of an open transaction, never a statement's own run (that is <see cref="StatementTimeout"/>). The
    /// longest legitimate gap is client code between two statements of one handler (no network call sits inside a
    /// transaction), which is milliseconds; a full garbage collection or a starved thread pool stretches it to a few
    /// seconds. Ten seconds clears that by a wide margin and still frees a leaked transaction's locks and connection six
    /// times sooner than the default.
    /// </summary>
    public static readonly TimeSpan MinIdleInTransactionSessionTimeout = TimeSpan.FromSeconds(MinIdleInTransactionSessionSeconds);

    private const int MinIdleInTransactionSessionSeconds = 10;

    /// <summary>
    /// <c>Database:MaxPoolSize</c>: the most connections one process keeps open to one database (Npgsql's
    /// <c>Maximum Pool Size</c>), per connection string. Default <see cref="DefaultMaxPoolSize"/> (6). Range 1 to
    /// <see cref="MaxPoolSizeLimit"/>. A connection string that sets <c>Maximum Pool Size</c> itself keeps its own
    /// value. Raising it serves more concurrent requests and spends Postgres' <c>max_connections</c> budget;
    /// lowering it queues requests sooner.
    /// </summary>
    [PoolSize("Database:MaxPoolSize")]
    public int MaxPoolSize { get; set; } = DefaultMaxPoolSize;

    /// <summary>
    /// <c>Database:CommandTimeout</c>: how long the client waits for one command before it cancels it (Npgsql's
    /// <c>Command Timeout</c>), in whole seconds. Default <see cref="DefaultCommandTimeout"/> (35 s). Range 1 s to
    /// 1 h, whole seconds. A connection string that sets <c>Command Timeout</c> itself keeps its own value. Keep it
    /// above <see cref="StatementTimeout"/> so the server cancels first and the client timeout stays a backstop.
    /// </summary>
    [DatabaseTimeout("Database:CommandTimeout", 1, TimeoutUnit.Seconds)]
    public TimeSpan CommandTimeout { get; set; } = DefaultCommandTimeout;

    /// <summary>
    /// <c>Database:StatementTimeout</c>: the server-side limit of one statement (<c>statement_timeout</c>) the
    /// Migrator gives every login role. Default <see cref="DefaultStatementTimeout"/> (30 s). Range 1 s to 1 h,
    /// whole milliseconds. Raising it lets slower statements finish and hold their locks longer; it applies the
    /// next time the Migrator runs.
    /// </summary>
    [DatabaseTimeout("Database:StatementTimeout", 1, TimeoutUnit.Milliseconds)]
    public TimeSpan StatementTimeout { get; set; } = DefaultStatementTimeout;

    /// <summary>
    /// <c>Database:IdleInTransactionSessionTimeout</c>: how long a login role's session may sit idle inside an open
    /// transaction before the server ends it (<c>idle_in_transaction_session_timeout</c>), set by the Migrator.
    /// Default <see cref="DefaultIdleInTransactionSessionTimeout"/> (1 min). Range <see
    /// cref="MinIdleInTransactionSessionTimeout"/> (10 s) to 1 h, whole milliseconds. A leaked transaction stops
    /// holding its locks and its connection after it; a shorter value frees them sooner.
    /// </summary>
    [DatabaseTimeout("Database:IdleInTransactionSessionTimeout", MinIdleInTransactionSessionSeconds, TimeoutUnit.Milliseconds)]
    public TimeSpan IdleInTransactionSessionTimeout { get; set; } = DefaultIdleInTransactionSessionTimeout;
}
