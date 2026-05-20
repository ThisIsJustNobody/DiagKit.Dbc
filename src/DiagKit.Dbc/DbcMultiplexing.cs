namespace DiagKit.Dbc;

/// <summary>
/// 信号复用角色 / Signal multiplexing role.
/// </summary>
public enum DbcMultiplexingRole
{
    /// <summary>
    /// 非复用 / Not multiplexed.
    /// </summary>
    None,

    /// <summary>
    /// 复用选择器 / Multiplexor (switch).
    /// </summary>
    Multiplexor,

    /// <summary>
    /// 被复用的信号 / Multiplexed signal.
    /// </summary>
    Multiplexed,
}

/// <summary>
/// 复用器范围，用于 SG_MUL_VAL_ 扩展复用定义。<br/>
/// Multiplexor range, used for SG_MUL_VAL_ extended multiplexing definitions.
/// </summary>
public readonly record struct DbcMultiplexorRange(long Minimum, long Maximum)
{
    /// <summary>
    /// 判断给定值是否在此范围内。<br/>
    /// Checks whether the given value falls within this range.
    /// </summary>
    public bool Contains(long value)
    {
        return value >= Minimum && value <= Maximum;
    }
}

/// <summary>
/// DBC 信号复用的不可变描述，支持基本复用和 SG_MUL_VAL_ 扩展复用。<br/>
/// Immutable description of DBC signal multiplexing, supporting basic and SG_MUL_VAL_ extended multiplexing.
/// </summary>
public readonly record struct DbcMultiplexing
{
    private static readonly IReadOnlyList<DbcMultiplexorRange> EmptyRanges = Array.AsReadOnly(Array.Empty<DbcMultiplexorRange>());

    /// <summary>
    /// 创建复用描述。<br/>
    /// Creates a multiplexing descriptor.
    /// </summary>
    public DbcMultiplexing(
        DbcMultiplexingRole role,
        int? switchValue,
        string? multiplexorSignalName = null,
        IReadOnlyList<DbcMultiplexorRange>? switchRanges = null)
    {
        Role = role;
        SwitchValue = switchValue;
        MultiplexorSignalName = multiplexorSignalName;
        this.switchRanges = switchRanges is null
            ? EmptyRanges
            : Array.AsReadOnly(MergeRanges(switchRanges));
    }

    private readonly IReadOnlyList<DbcMultiplexorRange>? switchRanges;

    /// <summary>
    /// 非复用默认值 / Default non-multiplexed value.
    /// </summary>
    public static DbcMultiplexing None { get; } = new(DbcMultiplexingRole.None, null);

    /// <summary>
    /// 复用选择器默认值 / Default multiplexor value.
    /// </summary>
    public static DbcMultiplexing Multiplexor { get; } = new(DbcMultiplexingRole.Multiplexor, null);

    /// <summary>
    /// 复用角色 / Multiplexing role.
    /// </summary>
    public DbcMultiplexingRole Role { get; }

    /// <summary>
    /// 基本复用的 mN 开关值；扩展复用时为 null。<br/>
    /// Basic multiplexing mN switch value; null for extended multiplexing.
    /// </summary>
    public int? SwitchValue { get; }

    /// <summary>
    /// 扩展复用时关联的复用器信号名称 / Name of the associated multiplexor signal for extended multiplexing.
    /// </summary>
    public string? MultiplexorSignalName { get; }

    /// <summary>
    /// 扩展复用时已合并的复用器范围列表 / Merged list of multiplexor ranges for extended multiplexing.
    /// </summary>
    public IReadOnlyList<DbcMultiplexorRange> SwitchRanges => switchRanges ?? EmptyRanges;

    /// <summary>
    /// 创建基本复用信号，指定 mN 开关值。<br/>
    /// Creates a multiplexed signal with a given mN switch value.
    /// </summary>
    public static DbcMultiplexing Multiplexed(int switchValue)
    {
        return new DbcMultiplexing(DbcMultiplexingRole.Multiplexed, switchValue);
    }

    /// <summary>
    /// 创建扩展复用信号，指定复用器名称和范围列表。<br/>
    /// Creates an extended multiplexed signal with a multiplexor name and range list.
    /// </summary>
    public static DbcMultiplexing Multiplexed(string multiplexorSignalName, IReadOnlyList<DbcMultiplexorRange> switchRanges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(multiplexorSignalName);
        return new DbcMultiplexing(DbcMultiplexingRole.Multiplexed, null, multiplexorSignalName, switchRanges);
    }

    /// <summary>
    /// 向已有复用信号追加扩展范围定义，合并重叠。<br/>
    /// Appends extended range definitions to an existing multiplexed signal, merging overlaps.
    /// </summary>
    public DbcMultiplexing WithExtendedRanges(string multiplexorSignalName, IReadOnlyList<DbcMultiplexorRange> switchRanges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(multiplexorSignalName);
        if (switchRanges.Count == 0)
        {
            return this;
        }

        var ranges = new DbcMultiplexorRange[SwitchRanges.Count + switchRanges.Count];
        for (var i = 0; i < SwitchRanges.Count; i++)
        {
            ranges[i] = SwitchRanges[i];
        }

        for (var i = 0; i < switchRanges.Count; i++)
        {
            ranges[SwitchRanges.Count + i] = switchRanges[i];
        }

        return new DbcMultiplexing(DbcMultiplexingRole.Multiplexed, SwitchValue, multiplexorSignalName, ranges);
    }

    private static DbcMultiplexorRange[] MergeRanges(IReadOnlyList<DbcMultiplexorRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return [];
        }

        var ordered = ranges.ToArray();
        Array.Sort(ordered, static (left, right) =>
        {
            var minimum = left.Minimum.CompareTo(right.Minimum);
            return minimum != 0 ? minimum : left.Maximum.CompareTo(right.Maximum);
        });

        var merged = new List<DbcMultiplexorRange>(ordered.Length);
        var current = ordered[0];
        for (var i = 1; i < ordered.Length; i++)
        {
            var next = ordered[i];
            if (next.Minimum <= current.Maximum ||
                (current.Maximum < long.MaxValue && next.Minimum == current.Maximum + 1))
            {
                current = new DbcMultiplexorRange(current.Minimum, Math.Max(current.Maximum, next.Maximum));
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged.ToArray();
    }
}
