namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcRuntimeSemanticTests
{
    [TestMethod]
    public void Timestamp_FromElapsedUsesTimeSpanTicks()
    {
        Assert.AreEqual(
            new DbcTimestamp(TimeSpan.FromMilliseconds(10).Ticks, DbcTimestampKind.MonotonicTicks),
            DbcTimestamp.FromElapsed(TimeSpan.FromMilliseconds(10)));
    }

    [TestMethod]
    public void Timestamp_FromUtcUsesUniversalDateTimeTicks()
    {
        var local = new DateTime(2026, 5, 20, 8, 30, 0, DateTimeKind.Local);

        Assert.AreEqual(
            new DbcTimestamp(local.ToUniversalTime().Ticks, DbcTimestampKind.UtcDateTimeTicks),
            DbcTimestamp.FromUtc(local));
    }

    [TestMethod]
    public void Timestamp_FromUtcDateTimeOffsetUsesUniversalDateTimeTicks()
    {
        var timestamp = new DateTimeOffset(2026, 5, 20, 8, 30, 0, TimeSpan.FromHours(8));

        Assert.AreEqual(
            new DbcTimestamp(timestamp.UtcDateTime.Ticks, DbcTimestampKind.UtcDateTimeTicks),
            DbcTimestamp.FromUtc(timestamp));
    }

    [TestMethod]
    public void Channel_AddCyclicPublishingMessagesFromDbcRegistersOnlyUnambiguousCyclicMessages()
    {
        var document = LoadSendTypeDocument();
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");
        var sink = new RecordingFrameSink();

        var registered = channel.AddCyclicPublishingMessagesFromDbc();
        var emitted = channel.PollDueFrames(Ms(0), sink);

        Assert.AreEqual(2, registered);
        Assert.AreEqual(2, emitted);
        CollectionAssert.AreEqual(
            new[]
            {
                document.ResolveMessage("CyclicStatus").Identifier,
                document.ResolveMessage("MixedStatus").Identifier,
            },
            sink.Frames.Select(x => x.Identifier).ToArray());
        Assert.IsTrue(channel.IsPublishing(channel.ResolveMessage("CyclicStatus")));
        Assert.IsFalse(channel.IsPublishing(channel.ResolveMessage("EventStatus")));
        Assert.IsFalse(channel.IsPublishing(channel.ResolveMessage("ActiveStatus")));
        Assert.IsTrue(channel.IsPublishing(channel.ResolveMessage("MixedStatus")));
    }

    [TestMethod]
    public void Channel_AddCycleTimePublishingMessagesFromDbcRegistersAllMessagesWithCycleTime()
    {
        var document = LoadSendTypeDocument();
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");
        var sink = new RecordingFrameSink();

        var registered = channel.AddCycleTimePublishingMessagesFromDbc();
        var emitted = channel.PollDueFrames(Ms(0), sink);

        Assert.AreEqual(4, registered);
        Assert.AreEqual(4, emitted);
        CollectionAssert.AreEqual(
            new[]
            {
                document.ResolveMessage("CyclicStatus").Identifier,
                document.ResolveMessage("EventStatus").Identifier,
                document.ResolveMessage("ActiveStatus").Identifier,
                document.ResolveMessage("MixedStatus").Identifier,
            },
            sink.Frames.Select(x => x.Identifier).ToArray());
    }

    [TestMethod]
    public void Channel_GetMessageSnapshotAtMarksMessageStaleAfterMessageTimeout()
    {
        var document = LoadTimeoutDocument();
        var message = document.ResolveMessage("VehicleStatus");
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");
        var handle = channel.ResolveMessage("VehicleStatus");
        var payload = new byte[8];

        channel.ProcessReceivedFrame(new DbcFrameView(message.Identifier, payload, timestamp: Ms(10)));

        Assert.AreEqual(SignalQuality.Valid, channel.GetMessageSnapshot(handle).Quality);
        Assert.AreEqual(SignalQuality.Valid, channel.GetMessageSnapshot(handle, Ms(110)).Quality);
        Assert.AreEqual(SignalQuality.Stale, channel.GetMessageSnapshot(handle, Ms(111)).Quality);
    }

    [TestMethod]
    public void Channel_GetSignalSnapshotAtUsesSignalTimeoutBeforeMessageTimeout()
    {
        var document = LoadTimeoutDocument();
        var message = document.ResolveMessage("VehicleStatus");
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");
        var speed = channel.ResolveSignal(messageHandle, "Speed");
        var alive = channel.ResolveSignal(messageHandle, "Alive");
        var payload = new byte[8];

        channel.ProcessReceivedFrame(new DbcFrameView(message.Identifier, payload, timestamp: Ms(10)));

        Assert.AreEqual(SignalQuality.Stale, channel.GetSignalSnapshot(speed, Ms(31)).Quality);
        Assert.AreEqual(SignalQuality.Valid, channel.GetSignalSnapshot(alive, Ms(31)).Quality);
        Assert.AreEqual(SignalQuality.Stale, channel.GetSignalSnapshot(alive, Ms(111)).Quality);
    }

    [TestMethod]
    public void GetSignalSnapshot_RejectsMismatchedTimestampKind()
    {
        var document = LoadTimeoutDocument();
        var message = document.ResolveMessage("VehicleStatus");
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");
        var signal = channel.ResolveSignal(messageHandle, "Speed");
        channel.ProcessReceivedFrame(new DbcFrameView(message.Identifier, new byte[8], timestamp: Ms(0)));

        Assert.ThrowsExactly<DbcException>(() => channel.GetSignalSnapshot(signal, new DbcTimestamp(1, DbcTimestampKind.UtcDateTimeTicks)));
    }

    [TestMethod]
    public void GetSignalSnapshot_RejectsBackwardTimestamp()
    {
        var document = LoadTimeoutDocument();
        var message = document.ResolveMessage("VehicleStatus");
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");
        var signal = channel.ResolveSignal(messageHandle, "Speed");
        channel.ProcessReceivedFrame(new DbcFrameView(message.Identifier, new byte[8], timestamp: Ms(10)));

        Assert.ThrowsExactly<DbcException>(() => channel.GetSignalSnapshot(signal, Ms(9)));
    }

    [TestMethod]
    public void Channel_GetSignalSnapshotAtKeepsNoDataAndInactiveMultiplexQuality()
    {
        var document = LoadMultiplexedTimeoutDocument();
        var message = document.ResolveMessage("MuxStatus");
        var mode = message.ResolveSignal("Mode");
        var payload = new byte[8];
        mode.EncodeRaw(payload, 2);

        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("MuxStatus");
        var speed = channel.ResolveSignal(messageHandle, "Speed");
        var torque = channel.ResolveSignal(messageHandle, "Torque");

        Assert.AreEqual(SignalQuality.NoData, channel.GetSignalSnapshot(speed, Ms(500)).Quality);

        channel.ProcessReceivedFrame(new DbcFrameView(message.Identifier, payload, timestamp: Ms(10)));

        Assert.AreEqual(SignalQuality.InactiveMultiplex, channel.GetSignalSnapshot(speed, Ms(500)).Quality);
        Assert.AreEqual(SignalQuality.Stale, channel.GetSignalSnapshot(torque, Ms(500)).Quality);
    }

    private static DbcDocument LoadSendTypeDocument()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000;
            BA_DEF_ BO_ "GenMsgSendType" ENUM "cyclic","event","cyclicIfActive","cyclicAndEvent","noMsgSendType";

            BO_ 256 CyclicStatus: 8 VCU
             SG_ CyclicValue : 0|8@1+ (1,0) [0|255] "" HOST
            BO_ 257 EventStatus: 8 VCU
             SG_ EventValue : 0|8@1+ (1,0) [0|255] "" HOST
            BO_ 258 ActiveStatus: 8 VCU
             SG_ ActiveValue : 0|8@1+ (1,0) [0|255] "" HOST
            BO_ 259 MixedStatus: 8 VCU
             SG_ MixedValue : 0|8@1+ (1,0) [0|255] "" HOST

            BA_ "GenMsgCycleTime" BO_ 256 10;
            BA_ "GenMsgCycleTime" BO_ 257 10;
            BA_ "GenMsgCycleTime" BO_ 258 10;
            BA_ "GenMsgCycleTime" BO_ 259 10;
            BA_ "GenMsgSendType" BO_ 256 0;
            BA_ "GenMsgSendType" BO_ 257 1;
            BA_ "GenMsgSendType" BO_ 258 2;
            BA_ "GenMsgSendType" BO_ 259 3;
            """;

        return DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict).GetDocumentOrThrow();
    }

    private static DbcDocument LoadTimeoutDocument()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BA_DEF_ BO_ "GenMsgTimeoutTime" INT 0 100000;
            BA_DEF_ SG_ "GenSigTimeoutTime" INT 0 100000;

            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (0.01,0) [0|250] "km/h" HOST
             SG_ Alive : 16|8@1+ (1,0) [0|255] "" HOST

            BA_ "GenMsgTimeoutTime" BO_ 256 100;
            BA_ "GenSigTimeoutTime" SG_ 256 Speed 20;
            """;

        return DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict).GetDocumentOrThrow();
    }

    private static DbcDocument LoadMultiplexedTimeoutDocument()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BA_DEF_ SG_ "GenSigTimeoutTime" INT 0 100000;

            BO_ 512 MuxStatus: 8 VCU
             SG_ Mode M : 0|4@1+ (1,0) [0|15] "" HOST
             SG_ Speed m1 : 8|16@1+ (0.01,0) [0|250] "km/h" HOST
             SG_ Torque m2 : 24|16@1+ (1,0) [0|1000] "Nm" HOST

            BA_ "GenSigTimeoutTime" SG_ 512 Speed 20;
            BA_ "GenSigTimeoutTime" SG_ 512 Torque 20;
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
}
