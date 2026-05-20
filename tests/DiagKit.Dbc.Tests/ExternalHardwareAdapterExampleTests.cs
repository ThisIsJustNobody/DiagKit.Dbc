namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class ExternalHardwareAdapterExampleTests
{
    [TestMethod]
    public void ReceiveLoop_CanProjectExternalFramesIntoSignalSamples()
    {
        var document = LoadVehicleDocument();
        var message = document.ResolveMessage("VehicleStatus");
        var payload = new byte[8];
        message.TryEncodeSignal("VehicleSpeed", payload, 36.5);

        var hardwareFrame = new HardwareFrame(
            ChannelIndex: 0,
            ArbitrationId: message.Identifier.Value,
            IsExtendedId: message.Identifier.IsExtended,
            IsFlexibleDataRate: false,
            IsBitRateSwitch: false,
            Dlc: DbcDlc.FromDataLength(payload.Length),
            TimestampTicks: TimeSpan.FromMilliseconds(123).Ticks,
            Payload: payload);

        var session = DbcRuntimeSession.Create(document);
        var channel = session.CreateChannel("CAN1");
        var speed = channel.ResolveSignal(channel.ResolveMessage("VehicleStatus"), "VehicleSpeed");
        var samples = new RecordingSampleSink();

        var sampleCount = channel.ProcessReceivedFrame(ToDbcFrameView(hardwareFrame), samples);
        var snapshot = channel.GetSignalSnapshot(speed);

        Assert.AreEqual(1, sampleCount);
        Assert.AreEqual(1, samples.Samples.Count);
        Assert.AreEqual("VehicleStatus", samples.Samples[0].MessageName);
        Assert.AreEqual("VehicleSpeed", samples.Samples[0].SignalName);
        Assert.AreEqual(36.5, samples.Samples[0].PhysicalValue, 0.000_001);
        Assert.AreEqual(new DbcTimestamp(hardwareFrame.TimestampTicks, DbcTimestampKind.MonotonicTicks), samples.Samples[0].Timestamp);
        Assert.AreEqual(SignalQuality.Valid, snapshot.Quality);
        Assert.AreEqual(36.5, snapshot.PhysicalValue, 0.000_001);
    }

    [TestMethod]
    public void TransmitLoop_CanConvertPolledFramesIntoExternalHardwareFrames()
    {
        var document = LoadCanFdCommandDocument();
        var message = document.ResolveMessage("CommandStatus");
        var session = DbcRuntimeSession.Create(document);
        var channel = session.CreateChannel("CAN1");
        var messageHandle = channel.ResolveMessage("CommandStatus");
        var targetSpeed = channel.ResolveSignal(messageHandle, "TargetSpeed");
        var txSink = new HardwareTransmitSink(channelIndex: 0);
        var now = new DbcTimestamp(TimeSpan.FromMilliseconds(10).Ticks, DbcTimestampKind.MonotonicTicks);

        channel.SetPhysicalValue(targetSpeed, 12.34, timestamp: now);
        channel.AddPublishingMessage(messageHandle);

        var frameCount = channel.PollDueFrames(now, txSink);

        Assert.AreEqual(1, frameCount);
        Assert.AreEqual(1, txSink.Frames.Count);

        var hardwareFrame = txSink.Frames[0];
        Assert.AreEqual(0, hardwareFrame.ChannelIndex);
        Assert.AreEqual(message.Identifier.Value, hardwareFrame.ArbitrationId);
        Assert.AreEqual(message.Identifier.IsExtended, hardwareFrame.IsExtendedId);
        Assert.IsTrue(hardwareFrame.IsFlexibleDataRate);
        Assert.AreEqual(DbcDlc.FromDataLength(message.DataLength), hardwareFrame.Dlc);
        Assert.AreEqual(message.DataLength, hardwareFrame.Payload.Length);
        Assert.AreEqual(now.Ticks, hardwareFrame.TimestampTicks);
        Assert.AreEqual(12.34, message.DecodeSignal("TargetSpeed", hardwareFrame.Payload), 0.000_001);
    }

    private static DbcFrameView ToDbcFrameView(HardwareFrame frame)
    {
        var dataLength = DbcDlc.ToDataLength(frame.Dlc);
        var identifier = new CanIdentifier(
            frame.ArbitrationId,
            frame.IsExtendedId ? CanIdFormat.Extended : CanIdFormat.Standard);
        var flags = DbcFrameFlags.None;
        if (frame.IsFlexibleDataRate)
        {
            flags |= DbcFrameFlags.FlexibleDataRate;
        }

        if (frame.IsBitRateSwitch)
        {
            flags |= DbcFrameFlags.BitRateSwitch;
        }

        return new DbcFrameView(
            identifier,
            frame.Payload.AsSpan(0, dataLength),
            flags,
            new DbcTimestamp(frame.TimestampTicks, DbcTimestampKind.MonotonicTicks));
    }

    private static DbcDocument LoadVehicleDocument()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ VehicleSpeed : 0|16@1+ (0.01,0) [0|250] "km/h" HOST
            """;

        return DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict).GetDocumentOrThrow();
    }

    private static DbcDocument LoadCanFdCommandDocument()
    {
        const string dbcText = """
            VERSION ""
            BU_: HOST VCU

            BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000;
            BA_DEF_ BO_ "VFrameFormat" ENUM "StandardCAN","ExtendedCAN","reserved","J1939PG","reserved","reserved","reserved","reserved","reserved","reserved","reserved","reserved","reserved","reserved","StandardCAN_FD","ExtendedCAN_FD";

            BO_ 512 CommandStatus: 12 HOST
             SG_ TargetSpeed : 0|16@1+ (0.01,0) [0|250] "km/h" VCU
             SG_ Enable : 16|1@1+ (1,0) [0|1] "" VCU
            BA_ "GenMsgCycleTime" BO_ 512 10;
            BA_ "VFrameFormat" BO_ 512 14;
            """;

        return DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict).GetDocumentOrThrow();
    }

    private sealed class HardwareTransmitSink(int channelIndex) : IDbcFrameSink
    {
        public List<HardwareFrame> Frames { get; } = [];

        public void OnFrame(CanIdentifier identifier, ReadOnlySpan<byte> data, DbcFrameFlags flags, DbcTimestamp timestamp)
        {
            Frames.Add(new HardwareFrame(
                channelIndex,
                identifier.Value,
                identifier.IsExtended,
                flags.HasFlag(DbcFrameFlags.FlexibleDataRate),
                flags.HasFlag(DbcFrameFlags.BitRateSwitch),
                DbcDlc.FromDataLength(data.Length),
                timestamp.Ticks,
                data.ToArray()));
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

    private readonly record struct HardwareFrame(
        int ChannelIndex,
        uint ArbitrationId,
        bool IsExtendedId,
        bool IsFlexibleDataRate,
        bool IsBitRateSwitch,
        byte Dlc,
        long TimestampTicks,
        byte[] Payload);
}
