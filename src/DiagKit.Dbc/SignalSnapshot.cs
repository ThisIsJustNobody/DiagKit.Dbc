namespace DiagKit.Dbc;

/// <summary>
/// signal 的当前状态快照。<br/>
/// Current state snapshot of a signal.
/// </summary>
public readonly record struct SignalSnapshot(
    CanIdentifier Identifier,
    string MessageName,
    string SignalName,
    DbcTimestamp Timestamp,
    ulong RawValue,
    double PhysicalValue,
    SignalQuality Quality);
