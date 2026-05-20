using System.Collections.ObjectModel;

namespace DiagKit.Dbc;

/// <summary>
/// 面向 UI、脚本和诊断面板展示的 signal 当前值与元数据快照。<br/>
/// Display-oriented snapshot combining a signal's current value and metadata for UI, scripts, and diagnostic panels.
/// </summary>
public sealed class DbcSignalViewSnapshot
{
    /// <summary>
    /// 创建 signal view snapshot。<br/>
    /// Creates a signal view snapshot.
    /// </summary>
    public DbcSignalViewSnapshot(
        CanIdentifier identifier,
        string messageName,
        string signalName,
        DbcTimestamp timestamp,
        ulong rawValue,
        double physicalValue,
        SignalQuality quality,
        string unit,
        double minimum,
        double maximum,
        IReadOnlyDictionary<long, string> valueDescriptions)
    {
        Identifier = identifier;
        MessageName = messageName ?? throw new ArgumentNullException(nameof(messageName));
        SignalName = signalName ?? throw new ArgumentNullException(nameof(signalName));
        Timestamp = timestamp;
        RawValue = rawValue;
        PhysicalValue = physicalValue;
        Quality = quality;
        Unit = unit ?? string.Empty;
        Minimum = minimum;
        Maximum = maximum;
        ValueDescriptions = new ReadOnlyDictionary<long, string>(
            new Dictionary<long, string>(valueDescriptions ?? throw new ArgumentNullException(nameof(valueDescriptions))));
        ValueDescription = rawValue <= long.MaxValue &&
            ValueDescriptions.TryGetValue((long)rawValue, out var description)
            ? description
            : null;
    }

    /// <summary>
    /// CAN 仲裁 ID。<br/>
    /// CAN arbitration ID.
    /// </summary>
    public CanIdentifier Identifier { get; }

    /// <summary>
    /// Message 名称。<br/>
    /// Message name.
    /// </summary>
    public string MessageName { get; }

    /// <summary>
    /// Signal 名称。<br/>
    /// Signal name.
    /// </summary>
    public string SignalName { get; }

    /// <summary>
    /// 当前值时间戳。<br/>
    /// Timestamp of the current value.
    /// </summary>
    public DbcTimestamp Timestamp { get; }

    /// <summary>
    /// 当前 raw value。<br/>
    /// Current raw value.
    /// </summary>
    public ulong RawValue { get; }

    /// <summary>
    /// 当前物理值。<br/>
    /// Current physical value.
    /// </summary>
    public double PhysicalValue { get; }

    /// <summary>
    /// 当前值质量。<br/>
    /// Current value quality.
    /// </summary>
    public SignalQuality Quality { get; }

    /// <summary>
    /// 物理单位。<br/>
    /// Physical unit.
    /// </summary>
    public string Unit { get; }

    /// <summary>
    /// DBC 中声明的物理最小值。<br/>
    /// Physical minimum declared in the DBC.
    /// </summary>
    public double Minimum { get; }

    /// <summary>
    /// DBC 中声明的物理最大值。<br/>
    /// Physical maximum declared in the DBC.
    /// </summary>
    public double Maximum { get; }

    /// <summary>
    /// raw value 到文本描述的值表。<br/>
    /// Value table mapping raw values to text descriptions.
    /// </summary>
    public IReadOnlyDictionary<long, string> ValueDescriptions { get; }

    /// <summary>
    /// 当前 raw value 对应的文本描述；没有匹配描述时为 null。<br/>
    /// Text description for the current raw value, or null when none matches.
    /// </summary>
    public string? ValueDescription { get; }
}
