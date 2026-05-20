namespace DiagKit.Dbc;

/// <summary>
/// 消息的当前状态快照，拥有数据副本，适合跨线程或长期持有。<br/>
/// Snapshot of current message state, owning a data copy suitable for cross-thread or long-lived use.
/// </summary>
public sealed class MessageSnapshot
{
    private readonly byte[] data;

    /// <summary>
    /// 创建消息快照。<br/>
    /// Creates a message snapshot.
    /// </summary>
    public MessageSnapshot(
        CanIdentifier identifier,
        string messageName,
        byte[] data,
        DbcFrameFlags frameFlags,
        DbcTimestamp timestamp,
        SignalQuality quality)
    {
        Identifier = identifier;
        MessageName = string.IsNullOrWhiteSpace(messageName)
            ? throw new ArgumentException("Message name cannot be empty.", nameof(messageName))
            : messageName;
        this.data = (data ?? throw new ArgumentNullException(nameof(data))).ToArray();
        FrameFlags = frameFlags;
        Timestamp = timestamp;
        Quality = quality;
    }

    /// <summary>
    /// CAN 标识符 / CAN identifier.
    /// </summary>
    public CanIdentifier Identifier { get; }

    /// <summary>
    /// 消息名称 / Message name.
    /// </summary>
    public string MessageName { get; }

    /// <summary>
    /// payload 数据只读视图 / Read-only view of payload data.
    /// </summary>
    public ReadOnlySpan<byte> Data => data;

    /// <summary>
    /// CAN/CAN FD 帧标志 / CAN/CAN FD frame flags.
    /// </summary>
    public DbcFrameFlags FrameFlags { get; }

    /// <summary>
    /// 快照时的帧时间戳 / Frame timestamp at snapshot time.
    /// </summary>
    public DbcTimestamp Timestamp { get; }

    /// <summary>
    /// 消息级质量状态 / Message-level quality status.
    /// </summary>
    public SignalQuality Quality { get; }
}
