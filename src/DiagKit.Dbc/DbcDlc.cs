namespace DiagKit.Dbc;

/// <summary>
/// CAN/CAN FD 帧数据长度码 (DLC) 与字节长度的双向转换。<br/>
/// Bidirectional conversion between CAN/CAN FD Data Length Code (DLC) and byte length.
/// </summary>
public static class DbcDlc
{
    /// <summary>
    /// 将 DLC 值 (0..15) 转换为实际数据字节长度。<br/>
    /// Converts a DLC value (0..15) to actual data byte length.
    /// </summary>
    public static int ToDataLength(byte dlc)
    {
        return dlc switch
        {
            <= 8 => dlc,
            9 => 12,
            10 => 16,
            11 => 20,
            12 => 24,
            13 => 32,
            14 => 48,
            15 => 64,
            _ => throw new ArgumentOutOfRangeException(nameof(dlc), "DLC must be in range 0..15."),
        };
    }

    /// <summary>
    /// 将数据字节长度转换为 DLC 值 (0..15)。<br/>
    /// Converts a data byte length to a DLC value (0..15).
    /// </summary>
    public static byte FromDataLength(int dataLength)
    {
        return dataLength switch
        {
            < 0 => throw new ArgumentOutOfRangeException(nameof(dataLength), "Data length cannot be negative."),
            <= 8 => (byte)dataLength,
            <= 12 => 9,
            <= 16 => 10,
            <= 20 => 11,
            <= 24 => 12,
            <= 32 => 13,
            <= 48 => 14,
            <= 64 => 15,
            _ => throw new ArgumentOutOfRangeException(nameof(dataLength), "CAN FD payload cannot exceed 64 bytes."),
        };
    }
}
