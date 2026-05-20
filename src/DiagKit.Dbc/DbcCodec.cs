namespace DiagKit.Dbc;

/// <summary>
/// 无状态 codec facade，适合调用方不需要维护 runtime session 时直接编解码 signal/message。<br/>
/// Stateless codec facade for direct signal/message encode/decode when callers do not need a runtime session.
/// </summary>
public static class DbcCodec
{
    /// <summary>
    /// 从 payload 中提取 signal raw value。<br/>
    /// Extracts the signal raw value from payload.
    /// </summary>
    public static ulong ExtractRaw(ReadOnlySpan<byte> data, DbcSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return signal.DecodeRaw(data);
    }

    /// <summary>
    /// 向 payload 写入 signal raw value。<br/>
    /// Writes the signal raw value into payload.
    /// </summary>
    public static void WriteRaw(Span<byte> data, DbcSignal signal, ulong rawValue)
    {
        ArgumentNullException.ThrowIfNull(signal);
        signal.EncodeRaw(data, rawValue);
    }

    /// <summary>
    /// 从 payload 解码 signal 物理值。<br/>
    /// Decodes the signal physical value from payload.
    /// </summary>
    public static double DecodePhysical(ReadOnlySpan<byte> data, DbcSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return signal.DecodePhysical(data);
    }

    /// <summary>
    /// 把物理值转换为 raw value 并写入 payload。<br/>
    /// Converts physical value to raw value and writes into payload.
    /// </summary>
    public static SignalWriteResult WritePhysical(
        Span<byte> data,
        DbcSignal signal,
        double physicalValue,
        SignalWritePolicy policy = SignalWritePolicy.Strict)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return signal.TryEncodePhysical(data, physicalValue, policy);
    }

    /// <summary>
    /// 将 message payload 解码为 signal samples，非激活复用分支会标记为 InactiveMultiplex。<br/>
    /// Decodes message payload into signal samples; inactive multiplexed branches are marked InactiveMultiplex.
    /// </summary>
    public static int DecodeMessage(
        DbcMessage message,
        ReadOnlySpan<byte> data,
        Span<SignalSample> destination,
        DbcTimestamp timestamp = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Decode(data, destination, timestamp);
    }
}
