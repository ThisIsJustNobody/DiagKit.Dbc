namespace DiagKit.Dbc;

/// <summary>
/// 周期发布注册对单个 message 的处理结果。<br/>
/// Per-message outcome for periodic publishing registration.
/// </summary>
public enum DbcPublishingRegistrationStatus
{
    /// <summary>
    /// 已注册到发布调度。<br/>
    /// Registered into the publishing schedule.
    /// </summary>
    Registered,

    /// <summary>
    /// 已经注册过，本次未重复修改调度。<br/>
    /// Already registered; this call did not rewrite the schedule.
    /// </summary>
    AlreadyRegistered,

    /// <summary>
    /// 跳过：没有正的周期时间。<br/>
    /// Skipped because no positive cycle time is available.
    /// </summary>
    SkippedNoCycleTime,

    /// <summary>
    /// 跳过：DBC SendType 不是明确周期类。<br/>
    /// Skipped because the DBC SendType is not unambiguously cyclic.
    /// </summary>
    SkippedSendType,

    /// <summary>
    /// 跳过：当前 CAN/CAN FD 单帧 runtime 不支持该 message。<br/>
    /// Skipped because the current CAN/CAN FD single-frame runtime does not support the message.
    /// </summary>
    SkippedRuntimeUnsupported,

    /// <summary>
    /// 跳过：message 不是指定节点发送。<br/>
    /// Skipped because the message is not transmitted by the requested node.
    /// </summary>
    SkippedNodeMismatch,
}

/// <summary>
/// 周期发布注册报告中的单条 message 结果。<br/>
/// One message outcome in a periodic publishing registration report.
/// </summary>
public readonly record struct DbcPublishingRegistrationEntry(
    string MessageName,
    CanIdentifier Identifier,
    DbcPublishingRegistrationStatus Status,
    TimeSpan Period,
    string Reason);

/// <summary>
/// 周期发布批量注册的可展示报告。<br/>
/// Display-friendly report for batch periodic publishing registration.
/// </summary>
public sealed class DbcPublishingRegistrationReport
{
    private readonly IReadOnlyList<DbcPublishingRegistrationEntry> entries;
    private readonly IReadOnlyList<DbcPublishingRegistrationEntry> registered;
    private readonly IReadOnlyList<DbcPublishingRegistrationEntry> skipped;

    /// <summary>
    /// 创建注册报告。<br/>
    /// Creates a registration report.
    /// </summary>
    public DbcPublishingRegistrationReport(IEnumerable<DbcPublishingRegistrationEntry> entries)
    {
        var entryArray = (entries ?? throw new ArgumentNullException(nameof(entries))).ToArray();
        this.entries = Array.AsReadOnly(entryArray);
        registered = Array.AsReadOnly(entryArray.Where(x => x.Status == DbcPublishingRegistrationStatus.Registered).ToArray());
        skipped = Array.AsReadOnly(entryArray.Where(x => x.Status != DbcPublishingRegistrationStatus.Registered).ToArray());
    }

    /// <summary>
    /// 每个 message 的注册或跳过结果。<br/>
    /// Registration or skipped outcome for each message.
    /// </summary>
    public IReadOnlyList<DbcPublishingRegistrationEntry> Entries => entries;

    /// <summary>
    /// 本次新增注册的 message 数量。<br/>
    /// Number of messages newly registered by this call.
    /// </summary>
    public int RegisteredCount => registered.Count;

    /// <summary>
    /// 本次未新增注册的 message 数量。<br/>
    /// Number of messages not newly registered by this call.
    /// </summary>
    public int SkippedCount => skipped.Count;

    /// <summary>
    /// 本次新增注册的 entries。<br/>
    /// Entries newly registered by this call.
    /// </summary>
    public IReadOnlyList<DbcPublishingRegistrationEntry> Registered => registered;

    /// <summary>
    /// 本次未新增注册的 entries。<br/>
    /// Entries not newly registered by this call.
    /// </summary>
    public IReadOnlyList<DbcPublishingRegistrationEntry> Skipped => skipped;
}
