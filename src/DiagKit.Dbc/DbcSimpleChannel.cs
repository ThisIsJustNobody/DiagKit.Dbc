namespace DiagKit.Dbc;

/// <summary>
/// 面向脚本、UI 和迁移场景的非热路径便捷 channel facade。<br/>
/// Non-hot-path convenience channel facade for scripts, UI, and migration scenarios.
/// </summary>
/// <remarks>
/// 本类型内部使用 DbcChannelRuntime；允许字符串查找和少量分配。高频实时路径应继续使用预解析 handle、Span 和 sink API。<br/>
/// This type uses DbcChannelRuntime internally and allows string lookup/allocation. Hot paths should keep using pre-resolved handles, spans, and sink APIs.
/// </remarks>
public sealed class DbcSimpleChannel
{
    private readonly DbcDocument document;
    private readonly DbcChannelRuntime channel;

    private DbcSimpleChannel(DbcDocument document, string channelName)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        Session = DbcRuntimeSession.Create(document);
        channel = Session.CreateChannel(channelName);
    }

    /// <summary>
    /// 关联的 runtime session。<br/>
    /// Associated runtime session.
    /// </summary>
    public DbcRuntimeSession Session { get; }

    /// <summary>
    /// 底层 channel runtime，供需要继续下沉到高性能 API 的调用方使用。<br/>
    /// Underlying channel runtime for callers that need to move down to the high-performance API.
    /// </summary>
    public DbcChannelRuntime Channel => channel;

    /// <summary>
    /// 创建 simple channel。<br/>
    /// Creates a simple channel.
    /// </summary>
    public static DbcSimpleChannel Create(DbcDocument document, string channelName = "CAN1")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);
        return new DbcSimpleChannel(document, channelName);
    }

    /// <summary>
    /// 通过 "Message.Signal" 路径设置物理值。<br/>
    /// Sets a physical value by "Message.Signal" path.
    /// </summary>
    public SignalWriteResult SetPhysicalValue(
        string signalPath,
        double physicalValue,
        SignalWritePolicy policy = SignalWritePolicy.Strict,
        DbcTimestamp timestamp = default)
    {
        return SetPhysicalValue(SignalPath.Parse(signalPath), physicalValue, policy, timestamp);
    }

    /// <summary>
    /// 通过 signal path 设置物理值。<br/>
    /// Sets a physical value by signal path.
    /// </summary>
    public SignalWriteResult SetPhysicalValue(
        SignalPath signalPath,
        double physicalValue,
        SignalWritePolicy policy = SignalWritePolicy.Strict,
        DbcTimestamp timestamp = default)
    {
        var signalHandle = ResolveSignalPath(signalPath);
        return channel.SetPhysicalValue(signalHandle, physicalValue, policy, timestamp);
    }

    /// <summary>
    /// 尝试通过 "Message.Signal" 路径设置物理值。路径缺失、歧义或 runtime 不支持时返回 false。<br/>
    /// Attempts to set a physical value by "Message.Signal" path. Returns false for missing, ambiguous, or runtime-unsupported paths.
    /// </summary>
    public bool TrySetPhysicalValue(
        string signalPath,
        double physicalValue,
        out SignalWriteResult result,
        SignalWritePolicy policy = SignalWritePolicy.Strict,
        DbcTimestamp timestamp = default)
    {
        if (!SignalPath.TryParse(signalPath, out var parsed))
        {
            result = SignalWriteResult.Fail(SignalWriteStatus.Invalid, $"Signal path '{signalPath}' could not be resolved.");
            return false;
        }

        return TrySetPhysicalValue(parsed, physicalValue, out result, policy, timestamp);
    }

    /// <summary>
    /// 尝试通过 signal path 设置物理值。路径缺失、歧义或 runtime 不支持时返回 false。<br/>
    /// Attempts to set a physical value by signal path. Returns false for missing, ambiguous, or runtime-unsupported paths.
    /// </summary>
    public bool TrySetPhysicalValue(
        SignalPath signalPath,
        double physicalValue,
        out SignalWriteResult result,
        SignalWritePolicy policy = SignalWritePolicy.Strict,
        DbcTimestamp timestamp = default)
    {
        if (!TryResolveSignalPath(signalPath, out var signalHandle))
        {
            result = SignalWriteResult.Fail(SignalWriteStatus.Invalid, $"Signal path '{signalPath}' could not be resolved.");
            return false;
        }

        result = channel.SetPhysicalValue(signalHandle, physicalValue, policy, timestamp);
        return true;
    }

    /// <summary>
    /// 通过 message name 立即构建拥有 payload 的 CAN/CAN FD 帧。<br/>
    /// Builds an owning CAN/CAN FD frame immediately by message name.
    /// </summary>
    public DbcFrame BuildFrame(string messageName, DbcTimestamp timestamp = default)
    {
        var messageHandle = channel.ResolveMessage(messageName);
        var sink = new CapturingFrameSink();
        channel.BuildFrameNow(messageHandle, timestamp, sink);
        return sink.Frame ?? throw new DbcException($"Message '{messageName}' did not produce a frame.");
    }

    /// <summary>
    /// 解码一帧并返回按 signal name 读取物理值的便捷结果。<br/>
    /// Decodes one frame and returns a convenience result for reading physical values by signal name.
    /// </summary>
    public DbcSimpleFrameValues Decode(DbcFrameView frame)
    {
        var messageName = document.TryResolveMessage(frame.Identifier, out var message)
            ? message.Name
            : string.Empty;
        var sink = new CollectingSampleSink();
        _ = channel.ProcessReceivedFrame(frame, sink);
        return new DbcSimpleFrameValues(messageName, sink.Samples);
    }

    /// <summary>
    /// 通过 "Message.Signal" 路径读取当前物理值。<br/>
    /// Gets the current physical value by "Message.Signal" path.
    /// </summary>
    public double GetPhysicalValue(string signalPath, DbcTimestamp now = default)
    {
        return GetPhysicalValue(SignalPath.Parse(signalPath), now);
    }

    /// <summary>
    /// 通过 signal path 读取当前物理值。<br/>
    /// Gets the current physical value by signal path.
    /// </summary>
    public double GetPhysicalValue(SignalPath signalPath, DbcTimestamp now = default)
    {
        var signalHandle = ResolveSignalPath(signalPath);
        var snapshot = channel.GetSignalSnapshot(signalHandle, now);
        if (snapshot.Quality != SignalQuality.Valid)
        {
            throw new DbcException($"Signal path '{signalPath}' quality is {snapshot.Quality}.");
        }

        return snapshot.PhysicalValue;
    }

    /// <summary>
    /// 尝试通过 "Message.Signal" 路径读取当前物理值。<br/>
    /// Attempts to get the current physical value by "Message.Signal" path.
    /// </summary>
    public bool TryGetPhysicalValue(string signalPath, out double physicalValue, DbcTimestamp now = default)
    {
        if (!SignalPath.TryParse(signalPath, out var parsed))
        {
            physicalValue = double.NaN;
            return false;
        }

        return TryGetPhysicalValue(parsed, out physicalValue, now);
    }

    /// <summary>
    /// 尝试通过 signal path 读取当前物理值。<br/>
    /// Attempts to get the current physical value by signal path.
    /// </summary>
    public bool TryGetPhysicalValue(SignalPath signalPath, out double physicalValue, DbcTimestamp now = default)
    {
        if (!TryResolveSignalPath(signalPath, out var signalHandle))
        {
            physicalValue = double.NaN;
            return false;
        }

        var snapshot = channel.GetSignalSnapshot(signalHandle, now);
        if (snapshot.Quality != SignalQuality.Valid)
        {
            physicalValue = double.NaN;
            return false;
        }

        physicalValue = snapshot.PhysicalValue;
        return true;
    }

    /// <summary>
    /// 通过 "Message.Signal" 路径读取 UI-friendly signal 快照。<br/>
    /// Gets a UI-friendly signal snapshot by "Message.Signal" path.
    /// </summary>
    public DbcSignalViewSnapshot GetSignalViewSnapshot(string signalPath, DbcTimestamp now = default)
    {
        return GetSignalViewSnapshot(SignalPath.Parse(signalPath), now);
    }

    /// <summary>
    /// 通过 signal path 读取 UI-friendly signal 快照。<br/>
    /// Gets a UI-friendly signal snapshot by signal path.
    /// </summary>
    public DbcSignalViewSnapshot GetSignalViewSnapshot(SignalPath signalPath, DbcTimestamp now = default)
    {
        var signalHandle = ResolveSignalPath(signalPath);
        return channel.GetSignalViewSnapshot(signalHandle, now);
    }

    /// <summary>
    /// 枚举所有 signal 的 UI-friendly 当前值与元数据快照。<br/>
    /// Enumerates UI-friendly snapshots for all signals.
    /// </summary>
    public IReadOnlyList<DbcSignalViewSnapshot> GetSignalViewSnapshots(DbcTimestamp now = default)
    {
        return GetSignalViewSnapshots(document.Messages, null, now);
    }

    /// <summary>
    /// 枚举指定 message 下所有 signal 的 UI-friendly 当前值与元数据快照。<br/>
    /// Enumerates UI-friendly snapshots for all signals in a message.
    /// </summary>
    public IReadOnlyList<DbcSignalViewSnapshot> GetSignalViewSnapshotsForMessage(
        string messageName,
        DbcTimestamp now = default)
    {
        var message = document.ResolveMessage(messageName);
        return GetSignalViewSnapshots([message], null, now);
    }

    /// <summary>
    /// 枚举指定节点发送的所有 message 下 signal 的 UI-friendly 当前值与元数据快照。<br/>
    /// Enumerates UI-friendly snapshots for signals in messages transmitted by a node.
    /// </summary>
    public IReadOnlyList<DbcSignalViewSnapshot> GetSignalViewSnapshotsTransmittedBy(
        string nodeName,
        DbcTimestamp now = default)
    {
        return GetSignalViewSnapshots(document.GetMessagesTransmittedBy(nodeName), null, now);
    }

    /// <summary>
    /// 枚举指定节点接收的 signal 的 UI-friendly 当前值与元数据快照。<br/>
    /// Enumerates UI-friendly snapshots for signals received by a node.
    /// </summary>
    public IReadOnlyList<DbcSignalViewSnapshot> GetSignalViewSnapshotsReceivedBy(
        string nodeName,
        DbcTimestamp now = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeName);
        return GetSignalViewSnapshots(
            document.GetMessagesReceivedBy(nodeName),
            signal => ContainsNode(signal.Receivers, nodeName),
            now);
    }

    private bool TryResolveSignalPath(SignalPath signalPath, out SignalHandle signalHandle)
    {
        try
        {
            signalHandle = ResolveSignalPath(signalPath);
            return true;
        }
        catch (ArgumentException)
        {
            signalHandle = default;
            return false;
        }
        catch (DbcException)
        {
            signalHandle = default;
            return false;
        }
        catch (FormatException)
        {
            signalHandle = default;
            return false;
        }
    }

    private SignalHandle ResolveSignalPath(SignalPath signalPath)
    {
        var message = document.ResolveMessage(signalPath.MessageName);
        if (!message.SupportsSingleFrameRuntime)
        {
            throw new DbcException($"Message '{signalPath.MessageName}' is not supported by the CAN/CAN FD single-frame runtime.");
        }

        var matches = message.FindSignals(signalPath.SignalName);
        if (matches.Count == 0)
        {
            throw new DbcException($"Signal '{signalPath.SignalName}' was not found in message '{signalPath.MessageName}'.");
        }

        if (matches.Count > 1)
        {
            throw new DbcException($"Signal '{signalPath.SignalName}' is ambiguous in message '{signalPath.MessageName}'.");
        }

        var messageHandle = channel.ResolveMessage(signalPath.MessageName);
        return channel.ResolveSignal(messageHandle, matches[0]);
    }

    private IReadOnlyList<DbcSignalViewSnapshot> GetSignalViewSnapshots(
        IEnumerable<DbcMessage> messages,
        Func<DbcSignal, bool>? includeSignal,
        DbcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var snapshots = new List<DbcSignalViewSnapshot>();
        foreach (var message in messages)
        {
            var hasRuntimeHandle = channel.TryResolveMessage(message.Name, out var messageHandle);
            foreach (var signal in message.Signals)
            {
                if (includeSignal is not null && !includeSignal(signal))
                {
                    continue;
                }

                if (hasRuntimeHandle && channel.TryResolveSignal(messageHandle, signal, out var signalHandle))
                {
                    snapshots.Add(channel.GetSignalViewSnapshot(signalHandle, now));
                    continue;
                }

                snapshots.Add(CreateNoDataViewSnapshot(message, signal));
            }
        }

        return Array.AsReadOnly(snapshots.ToArray());
    }

    private static DbcSignalViewSnapshot CreateNoDataViewSnapshot(DbcMessage message, DbcSignal signal)
    {
        return new DbcSignalViewSnapshot(
            message.Identifier,
            message.Name,
            signal.Name,
            DbcTimestamp.Unspecified,
            0,
            double.NaN,
            SignalQuality.NoData,
            signal.Unit,
            signal.Minimum,
            signal.Maximum,
            signal.ValueDescriptions);
    }

    private static bool ContainsNode(IReadOnlyList<DbcNode> nodes, string nodeName)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Name, nodeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class CapturingFrameSink : IDbcFrameSink
    {
        public DbcFrame? Frame { get; private set; }

        public void OnFrame(CanIdentifier identifier, ReadOnlySpan<byte> data, DbcFrameFlags flags, DbcTimestamp timestamp)
        {
            Frame = new DbcFrame(identifier, data, flags, timestamp);
        }
    }

    private sealed class CollectingSampleSink : ISignalSampleSink
    {
        public List<SignalSample> Samples { get; } = [];

        public void OnSignalSample(in SignalSample sample)
        {
            Samples.Add(sample);
        }
    }
}

/// <summary>
/// Simple channel 解码后的按 signal name 读取结果。<br/>
/// Decoded simple-channel result for reading values by signal name.
/// </summary>
public sealed class DbcSimpleFrameValues
{
    private readonly IReadOnlyList<SignalSample> samples;

    /// <summary>
    /// 创建 simple frame values。<br/>
    /// Creates simple frame values.
    /// </summary>
    public DbcSimpleFrameValues(string? messageName, IEnumerable<SignalSample> samples)
    {
        MessageName = messageName ?? string.Empty;
        this.samples = Array.AsReadOnly((samples ?? throw new ArgumentNullException(nameof(samples))).ToArray());
    }

    /// <summary>
    /// 解码出的 message name。未知帧为空字符串。<br/>
    /// Decoded message name. Empty for unknown frames.
    /// </summary>
    public string MessageName { get; }

    /// <summary>
    /// 解码出的 signal samples。<br/>
    /// Decoded signal samples.
    /// </summary>
    public IReadOnlyList<SignalSample> Samples => samples;

    /// <summary>
    /// 尝试按 signal name 读取唯一且有效的物理值。缺失、同名歧义或非 Valid 质量时返回 false。<br/>
    /// Attempts to read a unique valid physical value by signal name. Returns false when missing, ambiguous, or non-Valid.
    /// </summary>
    public bool TryGetPhysicalValue(string signalName, out double physicalValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);
        var found = false;
        physicalValue = double.NaN;
        foreach (var sample in samples)
        {
            if (!string.Equals(sample.SignalName, signalName, StringComparison.Ordinal))
            {
                continue;
            }

            if (found || sample.Quality != SignalQuality.Valid)
            {
                physicalValue = double.NaN;
                return false;
            }

            found = true;
            physicalValue = sample.PhysicalValue;
        }

        return found;
    }

    /// <summary>
    /// 按 signal name 读取唯一且有效的物理值，失败时抛出 DbcException。<br/>
    /// Reads a unique valid physical value by signal name, throwing DbcException on failure.
    /// </summary>
    public double GetPhysicalValue(string signalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);
        var matches = samples.Where(x => string.Equals(x.SignalName, signalName, StringComparison.Ordinal)).ToArray();
        return matches.Length switch
        {
            0 => throw new DbcException($"Signal '{signalName}' was not found in decoded message '{MessageName}'."),
            > 1 => throw new DbcException($"Signal '{signalName}' is ambiguous in decoded message '{MessageName}'."),
            _ when matches[0].Quality != SignalQuality.Valid => throw new DbcException($"Signal '{signalName}' quality is {matches[0].Quality}."),
            _ => matches[0].PhysicalValue,
        };
    }
}
