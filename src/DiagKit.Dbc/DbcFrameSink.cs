namespace DiagKit.Dbc;

/// <summary>
/// 周期发送和立即构帧输出的帧接收器，由上层适配到具体硬件发送层。<br/>
/// Frame sink for periodic sending and immediate frame output, adapted by upper layers to concrete hardware transmit layers.
/// </summary>
public interface IDbcFrameSink
{
    /// <summary>
    /// 接收一帧待发送 CAN/CAN FD 报文。data 只保证在回调期间有效。<br/>
    /// Receives a frame to be sent. The data span is only guaranteed valid during the callback.
    /// </summary>
    void OnFrame(CanIdentifier identifier, ReadOnlySpan<byte> data, DbcFrameFlags flags, DbcTimestamp timestamp);
}
