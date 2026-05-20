namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcSchedulingTests
{
    [TestMethod]
    public void PollDueFrames_UsesDbcCycleTimeAndCurrentPayload()
    {
        var document = LoadDocument();
        var message = document.ResolveMessage("VehicleStatus");
        var session = DbcRuntimeSession.Create(document);
        var channel = session.CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");
        var speed = channel.ResolveSignal(messageHandle, "VehicleSpeed");
        var sink = new RecordingFrameSink();

        channel.SetPhysicalValue(speed, 12.34);
        channel.AddPublishingMessage(messageHandle);

        Assert.AreEqual(1, channel.PollDueFrames(Ms(0), sink));
        Assert.AreEqual(0, channel.PollDueFrames(Ms(5), sink));
        Assert.AreEqual(1, channel.PollDueFrames(Ms(10), sink));
        Assert.AreEqual(2, sink.Frames.Count);
        Assert.AreEqual(12.34, message.DecodeSignal("VehicleSpeed", sink.Frames[0].Data), 0.000_001);
    }

    [TestMethod]
    public void PollDueFrames_RuntimePeriodOverridesDbcCycleTime()
    {
        var session = DbcRuntimeSession.Create(LoadDocument());
        var channel = session.CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");
        var sink = new RecordingFrameSink();

        channel.AddPublishingMessage(messageHandle, TimeSpan.FromMilliseconds(20));

        Assert.AreEqual(1, channel.PollDueFrames(Ms(0), sink));
        Assert.AreEqual(0, channel.PollDueFrames(Ms(10), sink));
        Assert.AreEqual(1, channel.PollDueFrames(Ms(20), sink));
    }

    [TestMethod]
    public void PollDueFrames_SkipsMissedPeriodsAndKeepsPhase()
    {
        var session = DbcRuntimeSession.Create(LoadDocument());
        var channel = session.CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");
        var sink = new RecordingFrameSink();

        channel.AddPublishingMessage(messageHandle, TimeSpan.FromMilliseconds(10));

        Assert.AreEqual(1, channel.PollDueFrames(Ms(0), sink));
        Assert.AreEqual(1, channel.PollDueFrames(Ms(35), sink));
        Assert.AreEqual(0, channel.PollDueFrames(Ms(39), sink));
        Assert.AreEqual(1, channel.PollDueFrames(Ms(40), sink));

        var schedule = channel.GetScheduleSnapshot(messageHandle);
        Assert.AreEqual(2, schedule.MissedCycleCount);
        Assert.AreEqual(Ms(50), schedule.NextDueTime);
        Assert.AreEqual(Ms(40), schedule.LastEmittedTime);
    }

    [TestMethod]
    public void PollDueFrames_LateWithinOnePeriodEmitsCurrentFrameAndKeepsAbsolutePhase()
    {
        var session = DbcRuntimeSession.Create(LoadDocument());
        var channel = session.CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");
        var sink = new RecordingFrameSink();

        channel.AddPublishingMessage(messageHandle, TimeSpan.FromMilliseconds(10));

        Assert.AreEqual(1, channel.PollDueFrames(Ms(0), sink));
        Assert.AreEqual(1, channel.PollDueFrames(Ms(11), sink));

        var schedule = channel.GetScheduleSnapshot(messageHandle);
        Assert.AreEqual(2, sink.Frames.Count);
        Assert.AreEqual(0, schedule.MissedCycleCount);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1).Ticks, schedule.LastJitterTicks);
        Assert.AreEqual(Ms(20), schedule.NextDueTime);
        Assert.AreEqual(Ms(11), schedule.LastEmittedTime);
    }

    [TestMethod]
    public void PollDueFrames_CountsDeadlineMissesSeparatelyFromSkippedCycles()
    {
        var session = DbcRuntimeSession.Create(LoadDocument());
        var channel = session.CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");
        var sink = new RecordingFrameSink();

        channel.AddPublishingMessage(messageHandle, TimeSpan.FromMilliseconds(10));

        Assert.AreEqual(1, channel.PollDueFrames(Ms(0), sink));
        Assert.AreEqual(1, channel.PollDueFrames(Ms(11), sink));
        Assert.AreEqual(1, channel.PollDueFrames(Ms(35), sink));

        var schedule = channel.GetScheduleSnapshot(messageHandle);
        Assert.AreEqual(3, sink.Frames.Count);
        Assert.AreEqual(2, schedule.DeadlineMissCount);
        Assert.AreEqual(1, schedule.MissedCycleCount);
        Assert.AreEqual(TimeSpan.FromMilliseconds(5).Ticks, schedule.LastJitterTicks);
        Assert.AreEqual(Ms(40), schedule.NextDueTime);
    }

    [TestMethod]
    public void PollDueFrames_RejectsMismatchedTimestampKind()
    {
        var channel = DbcRuntimeSession.Create(LoadDocument()).CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");
        var sink = new RecordingFrameSink();

        channel.AddPublishingMessage(messageHandle, TimeSpan.FromMilliseconds(10), Ms(0));

        Assert.ThrowsExactly<DbcException>(() => channel.PollDueFrames(new DbcTimestamp(0, DbcTimestampKind.UtcDateTimeTicks), sink));
    }

    [TestMethod]
    public void PollDueFrames_DefaultFirstDueDoesNotCountBacklogBeforeFirstPoll()
    {
        var channel = DbcRuntimeSession.Create(LoadDocument()).CreateChannel("CAN1");
        var message = channel.ResolveMessage("VehicleStatus");
        var sink = new RecordingFrameSink();
        channel.AddPublishingMessage(message, TimeSpan.FromMilliseconds(10));

        Assert.AreEqual(1, channel.PollDueFrames(Ms(1000), sink));
        Assert.AreEqual(0, channel.GetScheduleSnapshot(message).MissedCycleCount);
    }

    [TestMethod]
    public void PollDueFrames_AdvancesCurrentScheduleWhenSinkThrows()
    {
        var channel = DbcRuntimeSession.Create(LoadDocument()).CreateChannel("CAN1");
        var message = channel.ResolveMessage("VehicleStatus");
        channel.AddPublishingMessage(message, TimeSpan.FromMilliseconds(10));

        Assert.ThrowsExactly<InvalidOperationException>(() => channel.PollDueFrames(Ms(0), new ThrowingFrameSink()));

        Assert.AreEqual(0, channel.PollDueFrames(Ms(0), new RecordingFrameSink()));
        Assert.AreEqual(Ms(10), channel.GetScheduleSnapshot(message).NextDueTime);
    }

    [TestMethod]
    public void PollDueFrames_OverflowDoesNotEmitOrAdvanceSchedule()
    {
        var channel = DbcRuntimeSession.Create(LoadDocument()).CreateChannel("CAN1");
        var message = channel.ResolveMessage("VehicleStatus");
        var sink = new RecordingFrameSink();
        var firstDue = new DbcTimestamp(long.MaxValue, DbcTimestampKind.MonotonicTicks);
        channel.AddPublishingMessage(message, TimeSpan.FromTicks(1), firstDue);

        Assert.ThrowsExactly<DbcException>(() => channel.PollDueFrames(firstDue, sink));

        var schedule = channel.GetScheduleSnapshot(message);
        Assert.AreEqual(0, sink.Frames.Count);
        Assert.AreEqual(firstDue, schedule.NextDueTime);
        Assert.AreEqual(0, schedule.EmittedCount);
        Assert.AreEqual(0, schedule.MissedCycleCount);
        Assert.AreEqual(0, schedule.DeadlineMissCount);
    }

    [TestMethod]
    public void PollDueFrames_SevereLagCountsSkippedCyclesAndJitterAgainstCurrentPeriod()
    {
        var session = DbcRuntimeSession.Create(LoadDocument());
        var channel = session.CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");
        var sink = new RecordingFrameSink();

        channel.AddPublishingMessage(messageHandle, TimeSpan.FromMilliseconds(10));

        Assert.AreEqual(1, channel.PollDueFrames(Ms(0), sink));
        Assert.AreEqual(1, channel.PollDueFrames(Ms(35), sink));

        var schedule = channel.GetScheduleSnapshot(messageHandle);
        Assert.AreEqual(2, sink.Frames.Count);
        Assert.AreEqual(2, schedule.MissedCycleCount);
        Assert.AreEqual(TimeSpan.FromMilliseconds(5).Ticks, schedule.LastJitterTicks);
        Assert.AreEqual(Ms(40), schedule.NextDueTime);
        Assert.AreEqual(Ms(35), schedule.LastEmittedTime);
    }

    [TestMethod]
    public void PublishingAndObservingSelectionsDoNotPolluteEachOther()
    {
        var document = LoadTwoMessageDocument();
        var session = DbcRuntimeSession.Create(document);
        var channel = session.CreateChannel("CAN1");
        var first = document.ResolveMessage("FirstStatus");
        var second = document.ResolveMessage("SecondStatus");
        var firstHandle = channel.ResolveMessage("FirstStatus");
        var secondHandle = channel.ResolveMessage("SecondStatus");
        var frameSink = new RecordingFrameSink();
        var sampleSink = new RecordingSampleSink();
        var payload = new byte[8];

        channel.AddObservingMessage(firstHandle);
        channel.AddPublishingMessage(secondHandle, TimeSpan.FromMilliseconds(10));

        Assert.IsTrue(channel.IsObserving(firstHandle));
        Assert.IsFalse(channel.IsObserving(secondHandle));
        Assert.IsFalse(channel.IsPublishing(firstHandle));
        Assert.IsTrue(channel.IsPublishing(secondHandle));
        Assert.AreEqual(1, channel.PollDueFrames(Ms(0), frameSink));
        Assert.AreEqual(second.Identifier, frameSink.Frames[0].Identifier);
        Assert.AreEqual(1, channel.ProcessReceivedFrame(new DbcFrameView(first.Identifier, payload), sampleSink));
        Assert.AreEqual(0, channel.ProcessReceivedFrame(new DbcFrameView(second.Identifier, payload), sampleSink));
        Assert.AreEqual(1, sampleSink.Samples.Count);
        Assert.AreEqual("FirstValue", sampleSink.Samples[0].SignalName);
    }

    [TestMethod]
    public void BuildFrameNow_EmitsCurrentPayloadWithoutAdvancingSchedule()
    {
        var document = LoadDocument();
        var message = document.ResolveMessage("VehicleStatus");
        var session = DbcRuntimeSession.Create(document);
        var channel = session.CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");
        var speed = channel.ResolveSignal(messageHandle, "VehicleSpeed");
        var sink = new RecordingFrameSink();

        channel.AddPublishingMessage(messageHandle, TimeSpan.FromMilliseconds(10));
        channel.SetPhysicalValue(speed, 7.89);
        channel.BuildFrameNow(messageHandle, Ms(3), sink);

        var schedule = channel.GetScheduleSnapshot(messageHandle);
        Assert.AreEqual(1, sink.Frames.Count);
        Assert.AreEqual(7.89, message.DecodeSignal("VehicleSpeed", sink.Frames[0].Data), 0.000_001);
        Assert.AreEqual(DbcTimestamp.Unspecified, schedule.NextDueTime);
        Assert.AreEqual(0, schedule.EmittedCount);
    }

    private static DbcDocument LoadDocument()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ VehicleSpeed : 0|16@1+ (0.01,0) [0|250] "km/h" HOST
            BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000;
            BA_ "GenMsgCycleTime" BO_ 256 10;
            """;

        return DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict).GetDocumentOrThrow();
    }

    private static DbcDocument LoadTwoMessageDocument()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 FirstStatus: 8 VCU
             SG_ FirstValue : 0|8@1+ (1,0) [0|255] "" HOST

            BO_ 257 SecondStatus: 8 VCU
             SG_ SecondValue : 0|8@1+ (1,0) [0|255] "" HOST
            """;

        return DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict).GetDocumentOrThrow();
    }

    private static DbcTimestamp Ms(int milliseconds)
    {
        return new DbcTimestamp(TimeSpan.FromMilliseconds(milliseconds).Ticks, DbcTimestampKind.MonotonicTicks);
    }

    private sealed class RecordingFrameSink : IDbcFrameSink
    {
        public List<DbcFrame> Frames { get; } = [];

        public void OnFrame(CanIdentifier identifier, ReadOnlySpan<byte> data, DbcFrameFlags flags, DbcTimestamp timestamp)
        {
            Frames.Add(new DbcFrame(identifier, data, flags, timestamp));
        }
    }

    private sealed class ThrowingFrameSink : IDbcFrameSink
    {
        public void OnFrame(CanIdentifier identifier, ReadOnlySpan<byte> data, DbcFrameFlags flags, DbcTimestamp timestamp)
        {
            throw new InvalidOperationException();
        }
    }

    private sealed class RecordingSampleSink : ISignalSampleSink
    {
        public List<SignalSample> Samples { get; } = [];

        public void OnSignalSample(in SignalSample sample)
        {
            Samples.Add(sample);
        }
    }
}
