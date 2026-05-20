namespace DiagKit.Dbc;

/// <summary>
/// signal sample 或 snapshot 的质量状态。<br/>
/// Quality status of a signal sample or snapshot.
/// </summary>
public enum SignalQuality
{
    /// <summary>
    /// 数据有效 / Data is valid.
    /// </summary>
    Valid,

    /// <summary>
    /// 尚无数据 / No data yet.
    /// </summary>
    NoData,

    /// <summary>
    /// 复用信号处于非激活分支 / Multiplexed signal inactive branch.
    /// </summary>
    InactiveMultiplex,

    /// <summary>
    /// 解码错误 / Decode error.
    /// </summary>
    DecodeError,

    /// <summary>
    /// 值超出范围 / Out of range.
    /// </summary>
    OutOfRange,

    /// <summary>
    /// 数据已超时未刷新 / Data is stale (timed out).
    /// </summary>
    Stale,
}

/// <summary>
/// 一条带时间戳的信号样本，适合实时波形、历史回放和分析层消费。<br/>
/// A timestamped signal sample suitable for real-time waveforms, historical replay, and analysis layer consumption.
/// </summary>
public readonly record struct SignalSample(
    CanIdentifier Identifier,
    string MessageName,
    string SignalName,
    DbcTimestamp Timestamp,
    ulong RawValue,
    double PhysicalValue,
    SignalQuality Quality);

/// <summary>
/// signal sample 的低分配流式接收器。<br/>
/// Low-allocation streaming sink for signal samples.
/// </summary>
public interface ISignalSampleSink
{
    /// <summary>
    /// 接收一条已解码的 signal sample。<br/>
    /// Receives a decoded signal sample.
    /// </summary>
    void OnSignalSample(in SignalSample sample);
}
