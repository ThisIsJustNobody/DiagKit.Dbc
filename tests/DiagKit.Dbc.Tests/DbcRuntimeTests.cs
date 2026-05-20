namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcRuntimeTests
{
    [TestMethod]
    public void RuntimeHandles_DoNotExposePublicIndexProperties()
    {
        var messageProperties = typeof(MessageHandle)
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Select(x => x.Name)
            .ToArray();
        var signalProperties = typeof(SignalHandle)
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Select(x => x.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(messageProperties, "Value");
        CollectionAssert.DoesNotContain(signalProperties, "MessageIndex");
        CollectionAssert.DoesNotContain(signalProperties, "SignalIndex");
    }

    [TestMethod]
    public void Runtime_RejectsUnsupportedTransportPayloadMessageHandles()
    {
        var document = DbcLoader.LoadText("""
            VERSION ""
            BU_: ECU TESTER
            BO_ 2364539904 LargePG: 1785 ECU
             SG_ FirstByte : 0|8@1+ (1,0) [0|255] "" TESTER
            """, DbcLoadOptions.Lenient).GetDocumentOrThrow();
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");

        Assert.IsFalse(channel.TryResolveMessage("LargePG", out _));
        var ex = Assert.ThrowsExactly<DbcException>(() => channel.ResolveMessage("LargePG"));
        StringAssert.Contains(ex.Message, "not supported by the CAN/CAN FD single-frame runtime");
    }

    [TestMethod]
    public void Channel_ResolvesHandlesAndSetsPhysicalValue()
    {
        var document = LoadDocument();
        var session = DbcRuntimeSession.Create(document);
        var channel = session.CreateChannel("CAN1");

        var message = channel.ResolveMessage("VehicleStatus");
        var speed = channel.ResolveSignal(message, "VehicleSpeed");

        var result = channel.SetPhysicalValue(speed, 12.34);
        var snapshot = channel.GetSignalSnapshot(speed);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("CAN1", channel.Name);
        Assert.AreEqual(1234UL, snapshot.RawValue);
        Assert.AreEqual(12.34, snapshot.PhysicalValue, 0.000_001);
        Assert.AreEqual(SignalQuality.Valid, snapshot.Quality);
    }

    [TestMethod]
    public void Channel_TryResolveSignalReturnsFalseForMissingSignal()
    {
        var channel = DbcRuntimeSession.Create(LoadDocument()).CreateChannel("CAN1");
        var message = channel.ResolveMessage("VehicleStatus");

        Assert.IsTrue(channel.TryResolveSignal(message, "VehicleSpeed", out var speed));
        Assert.IsTrue(channel.SetPhysicalValue(speed, 1.23).Succeeded);
        Assert.IsFalse(channel.TryResolveSignal(message, "Missing", out _));
    }

    [TestMethod]
    public void Channel_ResolvesDuplicateNamedSignalBySignalMetadataObject()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ CHECKSUM : 0|4@1+ (1,0) [0|15] "" HOST
             SG_ CHECKSUM : 4|4@1+ (1,0) [0|15] "" HOST
            """;

        var document = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient).GetDocumentOrThrow();
        var message = document.ResolveMessage("VehicleStatus");
        var signals = message.FindSignals("CHECKSUM").ToArray();
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");

        Assert.IsFalse(channel.TryResolveSignal(messageHandle, "CHECKSUM", out _));

        var first = channel.ResolveSignal(messageHandle, signals[0]);
        var second = channel.ResolveSignal(messageHandle, signals[1]);

        channel.SetRawValue(first, 0xA);
        channel.SetRawValue(second, 0x5);
        var snapshot = channel.GetMessageSnapshot(messageHandle);

        Assert.AreEqual(0xAUL, signals[0].DecodeRaw(snapshot.Data));
        Assert.AreEqual(0x5UL, signals[1].DecodeRaw(snapshot.Data));
    }

    [TestMethod]
    public void Channel_ProcessReceivedFrameUpdatesSnapshotAndEmitsSamples()
    {
        var document = LoadDocument();
        var message = document.ResolveMessage("VehicleStatus");
        var payload = new byte[8];
        message.TryEncodeSignal("VehicleSpeed", payload, 45.67);

        var session = DbcRuntimeSession.Create(document);
        var channel = session.CreateChannel("CAN1");
        var speed = channel.ResolveSignal(channel.ResolveMessage(message.Identifier), "VehicleSpeed");
        var sink = new RecordingSampleSink();
        var timestamp = new DbcTimestamp(123, DbcTimestampKind.MonotonicTicks);

        var sampleCount = channel.ProcessReceivedFrame(
            new DbcFrameView(message.Identifier, payload, timestamp: timestamp),
            sink);
        var snapshot = channel.GetSignalSnapshot(speed);

        Assert.AreEqual(1, sampleCount);
        Assert.AreEqual(1, sink.Samples.Count);
        Assert.AreEqual(4567UL, sink.Samples[0].RawValue);
        Assert.AreEqual(45.67, sink.Samples[0].PhysicalValue, 0.000_001);
        Assert.AreEqual(timestamp, sink.Samples[0].Timestamp);
        Assert.AreEqual(4567UL, snapshot.RawValue);
        Assert.AreEqual(timestamp, snapshot.Timestamp);
    }

    [TestMethod]
    public void Channel_ProcessReceivedFrameIgnoresUnknownIdentifier()
    {
        var session = DbcRuntimeSession.Create(LoadDocument());
        var channel = session.CreateChannel("CAN1");
        var payload = new byte[8];

        var sampleCount = channel.ProcessReceivedFrame(
            new DbcFrameView(new CanIdentifier(0x123, CanIdFormat.Standard), payload),
            new RecordingSampleSink());

        Assert.AreEqual(0, sampleCount);
    }

    [TestMethod]
    public void Channel_ProcessReceivedFrameObservesAllMessagesByDefault()
    {
        var document = LoadTwoMessageDocument();
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");
        var first = document.ResolveMessage("FirstStatus");
        var second = document.ResolveMessage("SecondStatus");
        var payload = new byte[8];
        var sink = new RecordingSampleSink();

        Assert.AreEqual(1, channel.ProcessReceivedFrame(new DbcFrameView(first.Identifier, payload), sink));
        Assert.AreEqual(1, channel.ProcessReceivedFrame(new DbcFrameView(second.Identifier, payload), sink));
        Assert.AreEqual(2, sink.Samples.Count);
    }

    [TestMethod]
    public void Channel_AddObservingMessageFiltersReceivedFrames()
    {
        var document = LoadTwoMessageDocument();
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");
        var first = document.ResolveMessage("FirstStatus");
        var second = document.ResolveMessage("SecondStatus");
        var payload = new byte[8];
        var sink = new RecordingSampleSink();

        channel.AddObservingMessage(channel.ResolveMessage("FirstStatus"));

        Assert.AreEqual(1, channel.ProcessReceivedFrame(new DbcFrameView(first.Identifier, payload), sink));
        Assert.AreEqual(0, channel.ProcessReceivedFrame(new DbcFrameView(second.Identifier, payload), sink));
        Assert.AreEqual(1, sink.Samples.Count);
        Assert.AreEqual("FirstValue", sink.Samples[0].SignalName);
    }

    [TestMethod]
    public void Channel_AddPublishingMessageMarksMessageAsPublishing()
    {
        var channel = DbcRuntimeSession.Create(LoadDocument()).CreateChannel("CAN1");
        var message = channel.ResolveMessage("VehicleStatus");

        Assert.IsFalse(channel.IsPublishing(message));
        channel.AddPublishingMessage(message, TimeSpan.FromMilliseconds(10));

        Assert.IsTrue(channel.IsPublishing(message));
    }

    [TestMethod]
    public void Channel_GetMessageSnapshotReturnsOwnedPayloadCopy()
    {
        var document = LoadDocument();
        var session = DbcRuntimeSession.Create(document);
        var channel = session.CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("VehicleStatus");
        var speed = channel.ResolveSignal(messageHandle, "VehicleSpeed");

        channel.SetPhysicalValue(speed, 1.23, timestamp: new DbcTimestamp(10, DbcTimestampKind.MonotonicTicks));
        var snapshot = channel.GetMessageSnapshot(messageHandle);

        Assert.AreEqual("VehicleStatus", snapshot.MessageName);
        Assert.AreEqual(new DbcTimestamp(10, DbcTimestampKind.MonotonicTicks), snapshot.Timestamp);
        Assert.AreEqual(SignalQuality.Valid, snapshot.Quality);
        Assert.AreEqual(1.23, document.ResolveMessage("VehicleStatus").DecodeSignal("VehicleSpeed", snapshot.Data), 0.000_001);

        var secondSnapshot = channel.GetMessageSnapshot(messageHandle);
        CollectionAssert.AreEqual(snapshot.Data.ToArray(), secondSnapshot.Data.ToArray());
        Assert.AreEqual(1.23, document.ResolveMessage("VehicleStatus").DecodeSignal("VehicleSpeed", secondSnapshot.Data), 0.000_001);
    }

    [TestMethod]
    public void Channel_RejectsHandlesResolvedFromDifferentDocument()
    {
        var firstChannel = DbcRuntimeSession.Create(LoadDocument()).CreateChannel("CAN1");
        var firstMessage = firstChannel.ResolveMessage("VehicleStatus");
        var firstSignal = firstChannel.ResolveSignal(firstMessage, "VehicleSpeed");
        var secondChannel = DbcRuntimeSession.Create(LoadTwoMessageDocument()).CreateChannel("CAN2");

        Assert.ThrowsExactly<DbcException>(() => secondChannel.GetMessageSnapshot(firstMessage));
        Assert.ThrowsExactly<DbcException>(() => secondChannel.SetRawValue(firstSignal, 1));
    }

    [TestMethod]
    public void SignalHandle_FromAnotherChannelIsRejected()
    {
        var session = DbcRuntimeSession.Create(LoadDocument());
        var can1 = session.CreateChannel("CAN1");
        var can2 = session.CreateChannel("CAN2");
        var signal = can1.ResolveSignal(can1.ResolveMessage("VehicleStatus"), "VehicleSpeed");

        Assert.ThrowsExactly<DbcException>(() => can2.SetRawValue(signal, 1));
    }

    [TestMethod]
    public void MessageHandle_FromAnotherChannelIsRejected()
    {
        var session = DbcRuntimeSession.Create(LoadDocument());
        var can1 = session.CreateChannel("CAN1");
        var can2 = session.CreateChannel("CAN2");
        var message = can1.ResolveMessage("VehicleStatus");

        Assert.ThrowsExactly<DbcException>(() => can2.GetMessageSnapshot(message));
    }

    [TestMethod]
    public void Channel_ProcessReceivedFrameMarksInactiveMultiplexedSignals()
    {
        var document = LoadMultiplexedDocument();
        var message = document.ResolveMessage("MuxStatus");
        var mode = message.ResolveSignal("Mode");
        var speed = message.ResolveSignal("Speed");
        var torque = message.ResolveSignal("Torque");
        var payload = new byte[8];
        mode.EncodeRaw(payload, 2);
        speed.TryEncodePhysical(payload, 12.34);
        torque.TryEncodePhysical(payload, 56);

        var session = DbcRuntimeSession.Create(document);
        var channel = session.CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("MuxStatus");
        var speedHandle = channel.ResolveSignal(messageHandle, "Speed");
        var torqueHandle = channel.ResolveSignal(messageHandle, "Torque");
        var sink = new RecordingSampleSink();

        channel.ProcessReceivedFrame(new DbcFrameView(message.Identifier, payload), sink);

        var speedSample = sink.Samples.Single(x => x.SignalName == "Speed");
        var torqueSample = sink.Samples.Single(x => x.SignalName == "Torque");
        Assert.AreEqual(SignalQuality.InactiveMultiplex, speedSample.Quality);
        Assert.IsTrue(double.IsNaN(speedSample.PhysicalValue));
        Assert.AreEqual(SignalQuality.Valid, torqueSample.Quality);
        Assert.AreEqual(56, torqueSample.PhysicalValue, 0.000_001);
        Assert.AreEqual(SignalQuality.InactiveMultiplex, channel.GetSignalSnapshot(speedHandle).Quality);
        Assert.AreEqual(SignalQuality.Valid, channel.GetSignalSnapshot(torqueHandle).Quality);
    }

    [TestMethod]
    public void Channel_ProcessReceivedFrameUsesExtendedMultiplexingRangesAndPlainSwitchUnion()
    {
        var document = LoadExtendedMultiplexedDocument();
        var message = document.ResolveMessage("MuxStatus");
        var mode = message.ResolveSignal("Mode");
        var speed = message.ResolveSignal("Speed");
        var payload = new byte[8];
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");
        var speedHandle = channel.ResolveSignal(channel.ResolveMessage("MuxStatus"), "Speed");
        var sink = new RecordingSampleSink();

        mode.EncodeRaw(payload, 2);
        speed.TryEncodePhysical(payload, 12.34);
        channel.ProcessReceivedFrame(new DbcFrameView(message.Identifier, payload), sink);

        Assert.AreEqual(SignalQuality.Valid, sink.Samples.Single(x => x.SignalName == "Speed").Quality);
        Assert.AreEqual(12.34, channel.GetSignalSnapshot(speedHandle).PhysicalValue, 0.000_001);

        sink.Samples.Clear();
        Array.Clear(payload);
        mode.EncodeRaw(payload, 4);
        speed.TryEncodePhysical(payload, 45.67);
        channel.ProcessReceivedFrame(new DbcFrameView(message.Identifier, payload), sink);

        var inactiveSample = sink.Samples.Single(x => x.SignalName == "Speed");
        Assert.AreEqual(SignalQuality.InactiveMultiplex, inactiveSample.Quality);
        Assert.IsTrue(double.IsNaN(inactiveSample.PhysicalValue));
        Assert.AreEqual(SignalQuality.InactiveMultiplex, channel.GetSignalSnapshot(speedHandle).Quality);

        sink.Samples.Clear();
        Array.Clear(payload);
        mode.EncodeRaw(payload, 1);
        speed.TryEncodePhysical(payload, 7.89);
        channel.ProcessReceivedFrame(new DbcFrameView(message.Identifier, payload), sink);

        Assert.AreEqual(SignalQuality.Valid, sink.Samples.Single(x => x.SignalName == "Speed").Quality);
        Assert.AreEqual(7.89, channel.GetSignalSnapshot(speedHandle).PhysicalValue, 0.000_001);
    }

    private static DbcDocument LoadDocument()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ VehicleSpeed : 0|16@1+ (0.01,0) [0|250] "km/h" HOST
            """;

        return DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict).GetDocumentOrThrow();
    }

    private static DbcDocument LoadMultiplexedDocument()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 512 MuxStatus: 8 VCU
             SG_ Mode M : 0|4@1+ (1,0) [0|15] "" HOST
             SG_ Speed m1 : 8|16@1+ (0.01,0) [0|250] "km/h" HOST
             SG_ Torque m2 : 24|16@1+ (1,0) [0|1000] "Nm" HOST
            """;

        return DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict).GetDocumentOrThrow();
    }

    private static DbcDocument LoadExtendedMultiplexedDocument()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 768 MuxStatus: 8 VCU
             SG_ Mode M : 0|4@1+ (1,0) [0|15] "" HOST
             SG_ Speed m1 : 8|16@1+ (0.01,0) [0|250] "km/h" HOST
            SG_MUL_VAL_ 768 Speed Mode 2-3;
            SG_MUL_VAL_ 768 Speed Mode 5-7;
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

    private sealed class RecordingSampleSink : ISignalSampleSink
    {
        public List<SignalSample> Samples { get; } = [];

        public void OnSignalSample(in SignalSample sample)
        {
            Samples.Add(sample);
        }
    }
}
