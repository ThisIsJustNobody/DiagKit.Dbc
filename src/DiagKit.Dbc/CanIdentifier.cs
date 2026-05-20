namespace DiagKit.Dbc;

/// <summary>
/// 表示运行时使用的 normalized CAN 仲裁 ID，并显式区分标准帧和扩展帧格式。<br/>
/// Represents a normalized CAN arbitration ID for runtime use, explicitly distinguishing standard and extended frame formats.
/// </summary>
public readonly struct CanIdentifier : IEquatable<CanIdentifier>, IComparable<CanIdentifier>
{
    /// <summary>
    /// 11-bit 标准 CAN ID 的最大值。<br/>
    /// Maximum value of an 11-bit standard CAN ID.
    /// </summary>
    public const uint StandardMaxValue = 0x7FF;

    /// <summary>
    /// 29-bit 扩展 CAN ID 的最大值。<br/>
    /// Maximum value of a 29-bit extended CAN ID.
    /// </summary>
    public const uint ExtendedMaxValue = 0x1FFF_FFFF;

    /// <summary>
    /// 创建 CAN identifier，并按帧格式校验 ID 范围。<br/>
    /// Creates a CAN identifier and validates the ID range against the frame format.
    /// </summary>
    public CanIdentifier(uint value, CanIdFormat format)
    {
        if (format == CanIdFormat.Standard && value > StandardMaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Standard CAN identifier must be <= 0x7FF.");
        }

        if (format == CanIdFormat.Extended && value > ExtendedMaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Extended CAN identifier must be <= 0x1FFFFFFF.");
        }

        Value = value;
        Format = format;
    }

    /// <summary>
    /// 不包含扩展帧标志位的仲裁 ID 数值。<br/>
    /// Arbitration ID value without the extended frame flag bit.
    /// </summary>
    public uint Value { get; }

    /// <summary>
    /// 标准帧或扩展帧格式。<br/>
    /// Standard or extended frame format.
    /// </summary>
    public CanIdFormat Format { get; }

    /// <summary>
    /// 指示该 ID 是否为 29-bit 扩展帧 ID。<br/>
    /// Whether this ID is a 29-bit extended frame ID.
    /// </summary>
    public bool IsExtended => Format == CanIdFormat.Extended;

    /// <inheritdoc/>
    public int CompareTo(CanIdentifier other)
    {
        var valueCompare = Value.CompareTo(other.Value);
        return valueCompare != 0 ? valueCompare : Format.CompareTo(other.Format);
    }

    /// <inheritdoc/>
    public bool Equals(CanIdentifier other)
    {
        return Value == other.Value && Format == other.Format;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is CanIdentifier other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(Value, Format);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return IsExtended ? $"0x{Value:X}x" : $"0x{Value:X}";
    }

    /// <inheritdoc/>
    public static bool operator ==(CanIdentifier left, CanIdentifier right)
    {
        return left.Equals(right);
    }

    /// <inheritdoc/>
    public static bool operator !=(CanIdentifier left, CanIdentifier right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc/>
    public static bool operator <(CanIdentifier left, CanIdentifier right)
    {
        return left.CompareTo(right) < 0;
    }

    /// <inheritdoc/>
    public static bool operator >(CanIdentifier left, CanIdentifier right)
    {
        return left.CompareTo(right) > 0;
    }

    /// <inheritdoc/>
    public static bool operator <=(CanIdentifier left, CanIdentifier right)
    {
        return left.CompareTo(right) <= 0;
    }

    /// <inheritdoc/>
    public static bool operator >=(CanIdentifier left, CanIdentifier right)
    {
        return left.CompareTo(right) >= 0;
    }
}
