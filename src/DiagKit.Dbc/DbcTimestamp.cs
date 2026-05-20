namespace DiagKit.Dbc;

/// <summary>
/// DBC 时间戳的时钟来源类型 / Clock source kind for DBC timestamps.
/// </summary>
public enum DbcTimestampKind
{
    /// <summary>
    /// 未指定 / Unspecified.
    /// </summary>
    Unspecified,

    /// <summary>
    /// 单调 elapsed ticks，单位为 TimeSpan.Ticks。<br/>
    /// Monotonic elapsed ticks in TimeSpan.Ticks units.
    /// </summary>
    MonotonicTicks,

    /// <summary>
    /// UTC DateTime ticks，如 DateTime.UtcNow.Ticks。<br/>
    /// UTC DateTime ticks, e.g. DateTime.UtcNow.Ticks.
    /// </summary>
    UtcDateTimeTicks,
}

/// <summary>
/// CAN/CAN FD 帧时间戳，携带时钟来源语义。<br/>
/// CAN/CAN FD frame timestamp with clock source semantics.
/// </summary>
/// <remarks>
/// 调用方统一使用本时间戳类型传递时间，核心库不自行获取时间。<br/>
/// Callers use this type to pass timestamps; the core library does not obtain time on its own.
/// </remarks>
public readonly record struct DbcTimestamp(long Ticks, DbcTimestampKind Kind)
{
    /// <summary>
    /// 未指定的零值时间戳 / Unspecified zero-value timestamp.
    /// </summary>
    public static DbcTimestamp Unspecified { get; } = new(0, DbcTimestampKind.Unspecified);

    /// <summary>
    /// 从单调 elapsed 时间创建时间戳，ticks 单位为 TimeSpan.Ticks。<br/>
    /// Creates a timestamp from monotonic elapsed time in TimeSpan.Ticks units.
    /// </summary>
    public static DbcTimestamp FromElapsed(TimeSpan elapsed)
    {
        return new DbcTimestamp(elapsed.Ticks, DbcTimestampKind.MonotonicTicks);
    }

    /// <summary>
    /// 从 UTC 或可转换为 UTC 的 DateTime 创建时间戳。<br/>
    /// Creates a timestamp from a UTC DateTime or a DateTime convertible to UTC.
    /// </summary>
    public static DbcTimestamp FromUtc(DateTime utc)
    {
        return new DbcTimestamp(utc.ToUniversalTime().Ticks, DbcTimestampKind.UtcDateTimeTicks);
    }

    /// <summary>
    /// 从 UTC 或可转换为 UTC 的 DateTimeOffset 创建时间戳。<br/>
    /// Creates a timestamp from a UTC DateTimeOffset or a DateTimeOffset convertible to UTC.
    /// </summary>
    public static DbcTimestamp FromUtc(DateTimeOffset utc)
    {
        return new DbcTimestamp(utc.UtcDateTime.Ticks, DbcTimestampKind.UtcDateTimeTicks);
    }
}
