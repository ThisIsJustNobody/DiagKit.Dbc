using System.Collections.ObjectModel;

namespace DiagKit.Dbc;

/// <summary>
/// DBC signal 元数据及 signal 级 raw/physical 编解码入口。<br/>
/// DBC signal metadata and signal-level raw/physical encode/decode entry point.
/// </summary>
public sealed class DbcSignal
{
    private readonly Dictionary<long, string> valueDescriptionsByRawValue;
    private DbcMessage? message;

    /// <summary>
    /// 创建 DBC signal 实例。<br/>
    /// Creates a DBC signal instance.
    /// </summary>
    public DbcSignal(
        string name,
        int startBit,
        int bitLength,
        DbcByteOrder byteOrder,
        DbcSignalValueType valueType,
        double factor,
        double offset,
        double minimum,
        double maximum,
        string unit,
        IReadOnlyList<DbcNode> receivers,
        DbcMultiplexing multiplexing = default,
        IReadOnlyDictionary<long, string>? valueDescriptions = null,
        IReadOnlyDictionary<string, DbcAttributeValue>? attributes = null,
        string? comment = null,
        double? initialValue = null,
        int sourceLine = 0,
        DbcSendType sendType = DbcSendType.Unknown,
        int? timeoutTimeMs = null)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Signal name cannot be empty.", nameof(name))
            : name;
        StartBit = startBit;
        BitLength = bitLength;
        ByteOrder = byteOrder;
        ValueType = valueType;
        Factor = factor;
        Offset = offset;
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit ?? string.Empty;
        Receivers = Array.AsReadOnly((receivers ?? throw new ArgumentNullException(nameof(receivers))).ToArray());
        Multiplexing = multiplexing == default ? DbcMultiplexing.None : multiplexing;
        valueDescriptionsByRawValue = valueDescriptions is null
            ? new Dictionary<long, string>()
            : new Dictionary<long, string>(valueDescriptions);
        ValueDescriptions = new ReadOnlyDictionary<long, string>(valueDescriptionsByRawValue);
        Attributes = attributes is null
            ? new ReadOnlyDictionary<string, DbcAttributeValue>(new Dictionary<string, DbcAttributeValue>(StringComparer.Ordinal))
            : new ReadOnlyDictionary<string, DbcAttributeValue>(new Dictionary<string, DbcAttributeValue>(attributes, StringComparer.Ordinal));
        Comment = comment;
        InitialValue = initialValue;
        SourceLine = sourceLine;
        SendType = sendType;
        TimeoutTimeMs = timeoutTimeMs;
    }

    /// <summary>
    /// 信号所属的 DBC message，由 DbcMessage 构造时反向引用。<br/>
    /// The DBC message this signal belongs to, back-populated by DbcMessage during construction.
    /// </summary>
    public DbcMessage? Message => message;

    /// <summary>
    /// 信号名称 / Signal name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 信号起始 bit 位 (0-based LSB first)。<br/>
    /// Signal start bit (0-based LSB first).
    /// </summary>
    public int StartBit { get; }

    /// <summary>
    /// 信号 bit 长度 (1..64) / Signal bit length (1..64).
    /// </summary>
    public int BitLength { get; }

    /// <summary>
    /// 信号字节序 (Intel / Motorola) / Signal byte order (Intel / Motorola).
    /// </summary>
    public DbcByteOrder ByteOrder { get; }

    /// <summary>
    /// 信号值类型 (Signed / Unsigned / Float / Double) / Signal value type.
    /// </summary>
    public DbcSignalValueType ValueType { get; }

    /// <summary>
    /// 物理转换因子 / Physical conversion factor.
    /// </summary>
    public double Factor { get; }

    /// <summary>
    /// 物理转换偏移量 / Physical conversion offset.
    /// </summary>
    public double Offset { get; }

    /// <summary>
    /// 物理值下限 / Physical value minimum.
    /// </summary>
    public double Minimum { get; }

    /// <summary>
    /// 物理值上限 / Physical value maximum.
    /// </summary>
    public double Maximum { get; }

    /// <summary>
    /// 信号物理单位 / Signal physical unit.
    /// </summary>
    public string Unit { get; }

    /// <summary>
    /// 接收此信号的节点列表 / List of nodes receiving this signal.
    /// </summary>
    public IReadOnlyList<DbcNode> Receivers { get; }

    /// <summary>
    /// 信号复用描述 / Signal multiplexing description.
    /// </summary>
    public DbcMultiplexing Multiplexing { get; }

    /// <summary>
    /// raw value 到描述字符串的映射 (VAL_ / VAL_TABLE_) / Map of raw values to description strings.
    /// </summary>
    public IReadOnlyDictionary<long, string> ValueDescriptions { get; }

    /// <summary>
    /// 信号级属性 / Signal-level attributes.
    /// </summary>
    public IReadOnlyDictionary<string, DbcAttributeValue> Attributes { get; }

    /// <summary>
    /// 信号注释 (CM_ SG_) / Signal comment (CM_ SG_).
    /// </summary>
    public string? Comment { get; }

    /// <summary>
    /// 信号初始值 (GenSigStartValue) / Signal initial value (GenSigStartValue).
    /// </summary>
    public double? InitialValue { get; }

    /// <summary>
    /// 信号发送类型 / Signal send type.
    /// </summary>
    public DbcSendType SendType { get; }

    /// <summary>
    /// 信号超时时间 (ms)，null 表示继承消息级超时或不启用。<br/>
    /// Signal timeout in ms; null means inherit from message or not enabled.
    /// </summary>
    public int? TimeoutTimeMs { get; }

    /// <summary>
    /// SG_ 语句所在行号 / Source line of the SG_ statement.
    /// </summary>
    public int SourceLine { get; }

    /// <summary>
    /// 根据 raw value 查找枚举/值描述。<br/>
    /// Looks up the value description by raw value.
    /// </summary>
    public bool TryGetValueDescription(long rawValue, out string description)
    {
        return valueDescriptionsByRawValue.TryGetValue(rawValue, out description!);
    }

    /// <summary>
    /// 从 payload 中提取当前 signal 的 raw value。<br/>
    /// Extracts the raw value of this signal from payload.
    /// </summary>
    public ulong DecodeRaw(ReadOnlySpan<byte> data)
    {
        ValidateDefinition();
        return DbcBitCodec.Extract(data, StartBit, BitLength, ByteOrder);
    }

    /// <summary>
    /// 从 payload 中解码当前 signal 的物理值。<br/>
    /// Decodes the physical value of this signal from payload.
    /// </summary>
    public double DecodePhysical(ReadOnlySpan<byte> data)
    {
        return RawToPhysical(DecodeRaw(data));
    }

    /// <summary>
    /// 将 raw value 按 factor/offset 转换为物理值。<br/>
    /// Converts raw value to physical value using factor/offset.
    /// </summary>
    public double RawToPhysical(ulong rawValue)
    {
        ValidateDefinition();
        return DbcPhysicalCodec.Decode(this, rawValue);
    }

    /// <summary>
    /// 将物理值按指定策略转换并写入 payload。<br/>
    /// Converts physical value and writes raw value into payload using the given policy.
    /// </summary>
    public SignalWriteResult TryEncodePhysical(
        Span<byte> data,
        double physicalValue,
        SignalWritePolicy policy = SignalWritePolicy.Strict)
    {
        var result = DbcPhysicalCodec.TryEncode(this, physicalValue, policy);
        if (!result.Succeeded)
        {
            return result;
        }

        DbcBitCodec.Write(data, result.RawValue, StartBit, BitLength, ByteOrder);
        return result;
    }

    /// <summary>
    /// 将 raw value 写入 payload 中当前 signal 的 bit 区间。<br/>
    /// Writes raw value into the signal's bit range within payload.
    /// </summary>
    public void EncodeRaw(Span<byte> data, ulong rawValue)
    {
        ValidateDefinition();
        if (BitLength < 64 && rawValue > ((1UL << BitLength) - 1))
        {
            throw new ArgumentOutOfRangeException(nameof(rawValue), $"Raw value does not fit in signal '{Name}' bit length {BitLength}.");
        }

        DbcBitCodec.Write(data, rawValue, StartBit, BitLength, ByteOrder);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Message is null ? Name : $"{Message.Name}.{Name}";
    }

    internal void ValidateDefinition()
    {
        if (message is { SupportsSingleFrameRuntime: false })
        {
            throw new InvalidOperationException($"Signal '{Name}' belongs to message '{message.Name}', whose payload length {message.DataLength} is not supported by the CAN/CAN FD single-frame codec.");
        }

        if (StartBit < 0 || BitLength is < 1 or > 64)
        {
            throw new InvalidOperationException($"Signal '{Name}' has an invalid bit range.");
        }
    }

    internal void AttachToMessage(DbcMessage owner)
    {
        EnsureCanAttachToMessage(owner);
        message ??= owner;
    }

    internal void EnsureCanAttachToMessage(DbcMessage owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (message is null || ReferenceEquals(message, owner))
        {
            return;
        }

        throw new InvalidOperationException($"Signal '{Name}' already belongs to message '{message.Name}'.");
    }
}
