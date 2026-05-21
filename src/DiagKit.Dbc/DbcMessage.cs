using System.Collections.ObjectModel;

namespace DiagKit.Dbc;

/// <summary>
/// DBC message 元数据及 message 级信号编解码辅助入口。<br/>
/// DBC message metadata and message-level signal encode/decode helper entry point.
/// </summary>
public sealed class DbcMessage
{
    private readonly Dictionary<string, DbcSignal[]> signalsByName;

    /// <summary>
    /// 创建 DBC message 实例。<br/>
    /// Creates a DBC message instance.
    /// </summary>
    public DbcMessage(
        DbcRawMessageId rawId,
        string name,
        int dataLength,
        DbcNode primaryTransmitter,
        IReadOnlyList<DbcSignal> signals,
        IReadOnlyList<DbcNode>? transmitters = null,
        IReadOnlyDictionary<string, DbcAttributeValue>? attributes = null,
        string? comment = null,
        int? cycleTimeMs = null,
        DbcFrameFlags frameFlags = DbcFrameFlags.None,
        int sourceLine = 0,
        DbcSendType sendType = DbcSendType.Unknown,
        int? timeoutTimeMs = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Message name cannot be empty.", nameof(name));
        }

        if (dataLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dataLength), "Message data length cannot be negative.");
        }

        var signalArray = (signals ?? throw new ArgumentNullException(nameof(signals))).ToArray();
        foreach (var signal in signalArray)
        {
            if (signal is null)
            {
                throw new ArgumentException("Signal list cannot contain null entries.", nameof(signals));
            }

            signal.EnsureCanAttachToMessage(this);
        }

        RawId = rawId;
        Identifier = rawId.ToCanIdentifier();
        Name = name;
        DataLength = dataLength;
        PrimaryTransmitter = primaryTransmitter ?? throw new ArgumentNullException(nameof(primaryTransmitter));
        Transmitters = Array.AsReadOnly(transmitters is null || transmitters.Count == 0 ? [primaryTransmitter] : transmitters.ToArray());
        Signals = Array.AsReadOnly(signalArray);
        Attributes = attributes is null
            ? new ReadOnlyDictionary<string, DbcAttributeValue>(new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal))
            : new ReadOnlyDictionary<string, DbcAttributeValue>(new Dictionary<string, DbcAttributeValue>(attributes, StringComparer.Ordinal));
        Comment = comment;
        CycleTimeMs = cycleTimeMs;
        FrameFlags = SupportsSingleFrameRuntime && dataLength > 8 ? frameFlags | DbcFrameFlags.FlexibleDataRate : frameFlags;
        SourceLine = sourceLine;
        SendType = sendType;
        TimeoutTimeMs = timeoutTimeMs;

        signalsByName = signalArray
            .GroupBy(signal => signal.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var signal in Signals)
        {
            signal.AttachToMessage(this);
        }
    }

    /// <summary>
    /// DBC 原始消息 ID，如 BO_ 行中所编码。<br/>
    /// DBC raw message ID as encoded in the BO_ line.
    /// </summary>
    public DbcRawMessageId RawId { get; }

    /// <summary>
    /// 运行时使用的 normalized CAN 仲裁 ID。<br/>
    /// Normalized CAN arbitration ID for runtime use.
    /// </summary>
    public CanIdentifier Identifier { get; }

    /// <summary>
    /// 消息名称 / Message name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// payload 数据长度，DBC 数据库可保存超过当前 CAN/CAN FD 单帧 runtime 支持范围的长度。<br/>
    /// Payload data length; DBC metadata may preserve lengths beyond the current CAN/CAN FD single-frame runtime scope.
    /// </summary>
    public int DataLength { get; }

    /// <summary>
    /// 当前 message 是否可由 CAN/CAN FD 单帧 runtime 直接编解码。<br/>
    /// Whether this message can be directly processed by the CAN/CAN FD single-frame runtime.
    /// </summary>
    public bool SupportsSingleFrameRuntime => DataLength <= 64;

    /// <summary>
    /// CAN FD DLC 编码值 / CAN FD DLC encoded value.
    /// </summary>
    public byte Dlc => SupportsSingleFrameRuntime
        ? DbcDlc.FromDataLength(DataLength)
        : throw new InvalidOperationException($"Message '{Name}' payload length {DataLength} cannot be represented as a CAN/CAN FD single-frame DLC.");

    /// <summary>
    /// CAN/CAN FD 帧标志（包含 FlexibleDataRate 等）。<br/>
    /// CAN/CAN FD frame flags (including FlexibleDataRate, etc.).
    /// </summary>
    public DbcFrameFlags FrameFlags { get; }

    /// <summary>
    /// 是否为 CAN FD 帧 / Whether this is a CAN FD frame.
    /// </summary>
    public bool IsCanFd => (FrameFlags & DbcFrameFlags.FlexibleDataRate) != 0;

    /// <summary>
    /// 主要发送方节点 / Primary transmitter node.
    /// </summary>
    public DbcNode PrimaryTransmitter { get; }

    /// <summary>
    /// 所有发送方节点列表 / List of all transmitter nodes.
    /// </summary>
    public IReadOnlyList<DbcNode> Transmitters { get; }

    /// <summary>
    /// 消息包含的信号列表 / List of signals in this message.
    /// </summary>
    public IReadOnlyList<DbcSignal> Signals { get; }

    /// <summary>
    /// 消息级属性 / Message-level attributes.
    /// </summary>
    public IReadOnlyDictionary<string, DbcAttributeValue> Attributes { get; }

    /// <summary>
    /// 消息注释 (CM_ BO_) / Message comment (CM_ BO_).
    /// </summary>
    public string? Comment { get; }

    /// <summary>
    /// 周期发送间隔 (ms)，来自 GenMsgCycleTime / Cycle time in ms, from GenMsgCycleTime.
    /// </summary>
    public int? CycleTimeMs { get; }

    /// <summary>
    /// 消息发送类型 / Message send type.
    /// </summary>
    public DbcSendType SendType { get; }

    /// <summary>
    /// 消息超时时间 (ms) / Message timeout in ms.
    /// </summary>
    public int? TimeoutTimeMs { get; }

    /// <summary>
    /// BO_ 语句所在行号 / Source line of the BO_ statement.
    /// </summary>
    public int SourceLine { get; }

    /// <summary>
    /// 按信号名查找当前 message 下的 signal，名称匹配使用 ordinal 大小写敏感规则。<br/>
    /// Resolves a signal by name within this message using ordinal case-sensitive matching.
    /// </summary>
    public bool TryResolveSignal(string signalName, out DbcSignal signal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);
        if (signalsByName.TryGetValue(signalName, out var matches) &&
            matches.Length == 1)
        {
            signal = matches[0];
            return true;
        }

        signal = null!;
        return false;
    }

    /// <summary>
    /// 按信号名查找当前 message 下的所有同名 signal。<br/>
    /// Finds all signals with the specified name in this message.
    /// </summary>
    public IReadOnlyList<DbcSignal> FindSignals(string signalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);
        return signalsByName.TryGetValue(signalName, out var matches)
            ? Array.AsReadOnly(matches.ToArray())
            : Array.AsReadOnly(Array.Empty<DbcSignal>());
    }

    /// <summary>
    /// 按信号名查找当前 message 下的 signal，找不到时抛出 DbcException。<br/>
    /// Resolves a signal by name within this message, throws DbcException if not found.
    /// </summary>
    public DbcSignal ResolveSignal(string signalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);
        if (!signalsByName.TryGetValue(signalName, out var matches))
        {
            throw new DbcException(
                $"Signal '{Name}.{signalName}' was not found. DBC name lookup is case-sensitive; check message '{Name}' Signals for available signal names.");
        }

        return matches.Length == 1
            ? matches[0]
            : throw new DbcException($"Signal '{signalName}' is ambiguous in message '{Name}'. Use FindSignals(...) or object-based runtime handle resolution.");
    }

    /// <summary>
    /// 将 payload 中的指定 signal 解码为物理值。<br/>
    /// Decodes the named signal from payload into a physical value.
    /// </summary>
    public double DecodeSignal(string signalName, ReadOnlySpan<byte> data)
    {
        ValidatePayload(data);
        return ResolveSignal(signalName).DecodePhysical(data);
    }

    /// <summary>
    /// 将物理值写入 payload 中指定 signal 的 bit 区间。<br/>
    /// Encodes a physical value and writes it into the named signal's bit range within payload.
    /// </summary>
    public SignalWriteResult TryEncodeSignal(
        string signalName,
        Span<byte> data,
        double physicalValue,
        SignalWritePolicy policy = SignalWritePolicy.Strict)
    {
        ValidatePayload(data);
        return ResolveSignal(signalName).TryEncodePhysical(data, physicalValue, policy);
    }

    /// <summary>
    /// 将整个 message 解码为信号样本数组，非激活复用分支会标记为 InactiveMultiplex。<br/>
    /// Decodes the entire message into signal samples; inactive multiplexed branches are marked InactiveMultiplex.
    /// </summary>
    public int Decode(ReadOnlySpan<byte> data, Span<SignalSample> destination, DbcTimestamp timestamp = default)
    {
        ValidatePayload(data);

        if (destination.Length < Signals.Count)
        {
            throw new ArgumentException("Destination span is too small.", nameof(destination));
        }

        for (var i = 0; i < Signals.Count; i++)
        {
            var signal = Signals[i];
            destination[i] = CreateSignalSample(data, signal, timestamp);
        }

        return Signals.Count;
    }

    /// <summary>
    /// 将整个 message 解码并流式写入 sample sink，非激活复用分支会标记为 InactiveMultiplex。<br/>
    /// Decodes the entire message into a sample sink; inactive multiplexed branches are marked InactiveMultiplex.
    /// </summary>
    public void Decode(ReadOnlySpan<byte> data, ISignalSampleSink sink, DbcTimestamp timestamp = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ValidatePayload(data);

        foreach (var signal in Signals)
        {
            var sample = CreateSignalSample(data, signal, timestamp);
            sink.OnSignalSample(in sample);
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{Name} ({Identifier})";
    }

    private void ValidatePayload(ReadOnlySpan<byte> data)
    {
        EnsureSingleFrameRuntimeSupported();
        if (data.Length < DataLength)
        {
            throw new ArgumentException($"Message '{Name}' needs at least {DataLength} bytes.", nameof(data));
        }
    }

    private void EnsureSingleFrameRuntimeSupported()
    {
        if (!SupportsSingleFrameRuntime)
        {
            throw new InvalidOperationException($"Message '{Name}' payload length {DataLength} is not supported by the CAN/CAN FD single-frame codec.");
        }
    }

    private SignalSample CreateSignalSample(ReadOnlySpan<byte> data, DbcSignal signal, DbcTimestamp timestamp)
    {
        if (!IsSignalActive(this, data, signal))
        {
            return new SignalSample(
                Identifier,
                Name,
                signal.Name,
                timestamp,
                0,
                double.NaN,
                SignalQuality.InactiveMultiplex);
        }

        var rawValue = signal.DecodeRaw(data);
        return new SignalSample(
            Identifier,
            Name,
            signal.Name,
            timestamp,
            rawValue,
            signal.RawToPhysical(rawValue),
            SignalQuality.Valid);
    }

    internal static bool IsSignalActive(DbcMessage message, ReadOnlySpan<byte> data, DbcSignal signal)
    {
        if (signal.Multiplexing.Role != DbcMultiplexingRole.Multiplexed)
        {
            return true;
        }

        if (signal.Multiplexing.SwitchValue is null &&
            signal.Multiplexing.SwitchRanges.Count == 0)
        {
            return false;
        }

        var multiplexor = FindMultiplexor(message, signal.Multiplexing.MultiplexorSignalName);
        if (multiplexor is null)
        {
            return true;
        }

        var switchValue = (long)multiplexor.DecodeRaw(data);
        if (signal.Multiplexing.SwitchValue is { } plainSwitchValue &&
            switchValue == plainSwitchValue)
        {
            return true;
        }

        for (var i = 0; i < signal.Multiplexing.SwitchRanges.Count; i++)
        {
            if (signal.Multiplexing.SwitchRanges[i].Contains(switchValue))
            {
                return true;
            }
        }

        return false;
    }

    private static DbcSignal? FindMultiplexor(DbcMessage message, string? signalName)
    {
        if (!string.IsNullOrEmpty(signalName))
        {
            for (var i = 0; i < message.Signals.Count; i++)
            {
                var signal = message.Signals[i];
                if (signal.Multiplexing.Role == DbcMultiplexingRole.Multiplexor &&
                    string.Equals(signal.Name, signalName, StringComparison.Ordinal))
                {
                    return signal;
                }
            }

            return null;
        }

        for (var i = 0; i < message.Signals.Count; i++)
        {
            if (message.Signals[i].Multiplexing.Role == DbcMultiplexingRole.Multiplexor)
            {
                return message.Signals[i];
            }
        }

        return null;
    }
}
