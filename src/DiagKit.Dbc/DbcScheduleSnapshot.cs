namespace DiagKit.Dbc;

/// <summary>
/// 消息周期调度的统计快照，由 PollDueFrames 或 GetScheduleSnapshot 产生。<br/>
/// Statistical snapshot of message periodic scheduling, produced by PollDueFrames or GetScheduleSnapshot.
/// </summary>
public readonly record struct DbcScheduleSnapshot(
    bool IsPublishing,
    TimeSpan Period,
    DbcTimestamp NextDueTime,
    DbcTimestamp LastEmittedTime,
    long EmittedCount,
    long DeadlineMissCount,
    long MissedCycleCount,
    long LastJitterTicks);
