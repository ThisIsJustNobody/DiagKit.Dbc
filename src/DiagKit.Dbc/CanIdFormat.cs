namespace DiagKit.Dbc;

/// <summary>
/// CAN 仲裁 ID 的帧格式。<br/>
/// Frame format of a CAN arbitration ID.
/// </summary>
public enum CanIdFormat
{
    /// <summary>
    /// 11-bit 标准帧 / 11-bit standard frame.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// 29-bit 扩展帧 / 29-bit extended frame.
    /// </summary>
    Extended = 1,
}
