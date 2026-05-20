namespace DiagKit.Dbc;

/// <summary>
/// 拥有 payload 的 CAN/CAN FD 帧，适合跨异步、队列或长期保存边界传递。<br/>
/// CAN/CAN FD frame owning its payload, suitable for cross-async, queue, or long-lived boundaries.
/// </summary>
public sealed class DbcFrame
{
    private readonly byte[] data;

    /// <summary>
    /// 创建拥有型帧实例。<br/>
    /// Creates an owning frame instance.
    /// </summary>
    public DbcFrame(CanIdentifier identifier, ReadOnlySpan<byte> data, DbcFrameFlags flags = DbcFrameFlags.None, DbcTimestamp timestamp = default)
    {
        if (data.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "CAN/CAN FD payload cannot exceed 64 bytes.");
        }

        Identifier = identifier;
        this.data = data.ToArray();
        Flags = flags;
        Timestamp = timestamp;
    }

    /// <summary>
    /// CAN 仲裁 ID / CAN arbitration ID.
    /// </summary>
    public CanIdentifier Identifier { get; }

    /// <summary>
    /// payload 数据只读视图 / Read-only view of payload data.
    /// </summary>
    public ReadOnlySpan<byte> Data => data;

    /// <summary>
    /// CAN/CAN FD 帧标志 / CAN/CAN FD frame flags.
    /// </summary>
    public DbcFrameFlags Flags { get; }

    /// <summary>
    /// 帧时间戳 / Frame timestamp.
    /// </summary>
    public DbcTimestamp Timestamp { get; }
}

/// <summary>
/// 不拥有 payload 的帧视图，适合热路径同步处理以减少分配。<br/>
/// Frame view without payload ownership, suitable for hot-path synchronous processing to reduce allocations.
/// </summary>
public readonly ref struct DbcFrameView
{
    /// <summary>
    /// 创建帧视图。<br/>
    /// Creates a frame view.
    /// </summary>
    public DbcFrameView(CanIdentifier identifier, ReadOnlySpan<byte> data, DbcFrameFlags flags = DbcFrameFlags.None, DbcTimestamp timestamp = default)
    {
        Identifier = identifier;
        Data = data;
        Flags = flags;
        Timestamp = timestamp;
    }

    /// <summary>
    /// CAN 仲裁 ID / CAN arbitration ID.
    /// </summary>
    public CanIdentifier Identifier { get; }

    /// <summary>
    /// payload 数据只读视图 / Read-only view of payload data.
    /// </summary>
    public ReadOnlySpan<byte> Data { get; }

    /// <summary>
    /// CAN/CAN FD 帧标志 / CAN/CAN FD frame flags.
    /// </summary>
    public DbcFrameFlags Flags { get; }

    /// <summary>
    /// 帧时间戳 / Frame timestamp.
    /// </summary>
    public DbcTimestamp Timestamp { get; }
}
