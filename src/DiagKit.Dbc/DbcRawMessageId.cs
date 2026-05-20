namespace DiagKit.Dbc;

/// <summary>
/// DBC 原始消息 ID，对应 DBC 文件中的 BO_ 行编码 ID。<br/>
/// DBC raw message ID as encoded in the BO_ line of a DBC file.
/// </summary>
/// <remarks>
/// 最高位 (0x8000_0000) 为扩展帧标志位，与 DBC 规范一致。<br/>
/// The high bit (0x8000_0000) is the extended frame flag, consistent with the DBC specification.
/// </remarks>
public readonly record struct DbcRawMessageId(uint Value)
{
    /// <summary>
    /// DBC 扩展帧标志位 / DBC extended frame flag bit.
    /// </summary>
    public const uint ExtendedFrameFlag = 0x8000_0000;

    /// <summary>
    /// 将 DBC 原始 ID 转换为运行时使用的 normalized CAN identifier。<br/>
    /// Converts the DBC raw ID to a normalized CAN identifier for runtime use.
    /// </summary>
    public CanIdentifier ToCanIdentifier()
    {
        if ((Value & ExtendedFrameFlag) != 0)
        {
            return new CanIdentifier(Value & CanIdentifier.ExtendedMaxValue, CanIdFormat.Extended);
        }

        var format = Value <= CanIdentifier.StandardMaxValue ? CanIdFormat.Standard : CanIdFormat.Extended;
        return new CanIdentifier(Value, format);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Value.ToString();
    }
}
