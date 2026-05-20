namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcMessageCodecTests
{
    [TestMethod]
    public void Message_EncodesAndDecodesUnsignedAndSignedSignals()
    {
        var message = CreateVehicleStatusMessage();
        Span<byte> data = stackalloc byte[8];

        var speedResult = message.TryEncodeSignal("VehicleSpeed", data, 123.45);
        var currentResult = message.TryEncodeSignal("BatteryCurrent", data, -12);

        Assert.IsTrue(speedResult.Succeeded);
        Assert.IsTrue(currentResult.Succeeded);
        Assert.AreEqual(123.45, message.DecodeSignal("VehicleSpeed", data), 0.0001);
        Assert.AreEqual(-12, message.DecodeSignal("BatteryCurrent", data), 0.0001);
        Assert.AreEqual(0x39, data[0]);
        Assert.AreEqual(0x30, data[1]);
        Assert.AreEqual(0xF4, data[2]);
    }

    [TestMethod]
    public void Message_DecodesAllSignalsIntoReusableSampleBuffer()
    {
        var message = CreateVehicleStatusMessage();
        Span<byte> data = stackalloc byte[8];
        var samples = new SignalSample[message.Signals.Count];

        message.TryEncodeSignal("VehicleSpeed", data, 1.23);
        message.TryEncodeSignal("BatteryCurrent", data, -1);
        var count = message.Decode(data, samples, new DbcTimestamp(123, DbcTimestampKind.MonotonicTicks));

        Assert.AreEqual(2, count);
        Assert.AreEqual("VehicleSpeed", samples[0].SignalName);
        Assert.AreEqual(1.23, samples[0].PhysicalValue, 0.0001);
        Assert.AreEqual(123, samples[0].Timestamp.Ticks);
        Assert.AreEqual("BatteryCurrent", samples[1].SignalName);
        Assert.AreEqual(-1, samples[1].PhysicalValue, 0.0001);
    }

    [TestMethod]
    public void Message_DecodeMarksInactiveMultiplexedSignals()
    {
        var message = CreateMultiplexedMessage();
        var mode = message.ResolveSignal("Mode");
        var speed = message.ResolveSignal("Speed");
        var torque = message.ResolveSignal("Torque");
        Span<byte> data = stackalloc byte[8];
        var samples = new SignalSample[message.Signals.Count];

        mode.EncodeRaw(data, 2);
        speed.TryEncodePhysical(data, 12.34);
        torque.TryEncodePhysical(data, 56);

        var count = message.Decode(data, samples);

        Assert.AreEqual(3, count);
        Assert.AreEqual(SignalQuality.Valid, samples.Single(x => x.SignalName == "Mode").Quality);
        Assert.AreEqual(SignalQuality.InactiveMultiplex, samples.Single(x => x.SignalName == "Speed").Quality);
        Assert.IsTrue(double.IsNaN(samples.Single(x => x.SignalName == "Speed").PhysicalValue));
        Assert.AreEqual(SignalQuality.Valid, samples.Single(x => x.SignalName == "Torque").Quality);
        Assert.AreEqual(56, samples.Single(x => x.SignalName == "Torque").PhysicalValue, 0.000_001);
    }

    [TestMethod]
    public void EncodeSignal_ReturnsFailureByDefaultAndClampsWhenRequested()
    {
        var message = CreateVehicleStatusMessage();
        Span<byte> data = stackalloc byte[8];

        var strictResult = message.TryEncodeSignal("BatteryCurrent", data, 200);
        var clampResult = message.TryEncodeSignal("BatteryCurrent", data, 200, SignalWritePolicy.ClampToRawRange);

        Assert.AreEqual(SignalWriteStatus.OutOfPhysicalRange, strictResult.Status);
        Assert.IsTrue(clampResult.Succeeded);
        Assert.AreEqual(127, message.DecodeSignal("BatteryCurrent", data), 0.0001);
    }

    private static DbcMessage CreateVehicleStatusMessage()
    {
        var vcu = new DbcNode("VCU");
        var host = new DbcNode("HOST");
        var speed = new DbcSignal(
            "VehicleSpeed",
            0,
            16,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            0.01,
            0,
            0,
            250,
            "km/h",
            [host]);
        var current = new DbcSignal(
            "BatteryCurrent",
            16,
            8,
            DbcByteOrder.Intel,
            DbcSignalValueType.Signed,
            1,
            0,
            -128,
            127,
            "A",
            [host]);

        return new DbcMessage(
            new DbcRawMessageId(0x100),
            "VehicleStatus",
            8,
            vcu,
            [speed, current]);
    }

    private static DbcMessage CreateMultiplexedMessage()
    {
        var vcu = new DbcNode("VCU");
        var host = new DbcNode("HOST");
        var mode = new DbcSignal("Mode", 0, 4, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 15, "", [host], DbcMultiplexing.Multiplexor);
        var speed = new DbcSignal("Speed", 8, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 0.01, 0, 0, 250, "km/h", [host], DbcMultiplexing.Multiplexed(1));
        var torque = new DbcSignal("Torque", 24, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 1000, "Nm", [host], DbcMultiplexing.Multiplexed(2));

        return new DbcMessage(new DbcRawMessageId(0x200), "MuxStatus", 8, vcu, [mode, speed, torque]);
    }
}
