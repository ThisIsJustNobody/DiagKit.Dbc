namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcWriterReloadEquivalenceTests
{
    [TestMethod]
    public void WriteText_LoadText_PreservesCoreMessageAndSignalSemantics()
    {
        var ecu = new DbcNode("ECU");
        var tool = new DbcNode("Tool");
        var original = new DbcDocument(
            [ecu, tool],
            [
                new DbcMessage(
                    new DbcRawMessageId(256),
                    "VehicleStatus",
                    8,
                    ecu,
                    [
                        new DbcSignal("Speed", 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 0.1, 0, 0, 250, "km/h", [tool]),
                        new DbcSignal("Gear", 16, 8, DbcByteOrder.Intel, DbcSignalValueType.Signed, 1, -1, -1, 8, "", [tool]),
                    ]),
            ]);

        var reloaded = DbcLoader.LoadTextDocumentOrThrow(DbcWriter.WriteTextOrThrow(original));

        Assert.AreEqual(2, reloaded.Nodes.Count);
        var message = reloaded.ResolveMessage("VehicleStatus");
        Assert.AreEqual(256u, message.RawId.Value);
        Assert.AreEqual(8, message.DataLength);
        Assert.AreEqual("ECU", message.PrimaryTransmitter.Name);

        var speed = message.ResolveSignal("Speed");
        Assert.AreEqual(0, speed.StartBit);
        Assert.AreEqual(16, speed.BitLength);
        Assert.AreEqual(DbcByteOrder.Intel, speed.ByteOrder);
        Assert.AreEqual(DbcSignalValueType.Unsigned, speed.ValueType);
        Assert.AreEqual(0.1, speed.Factor);
        Assert.AreEqual("km/h", speed.Unit);

        var gear = message.ResolveSignal("Gear");
        Assert.AreEqual(DbcSignalValueType.Signed, gear.ValueType);
        Assert.AreEqual(-1, gear.Offset);
    }

    [TestMethod]
    public void WriteText_LoadText_PreservesRegularMultiplexingSemantics()
    {
        var ecu = new DbcNode("ECU");
        var tool = new DbcNode("Tool");
        var original = new DbcDocument(
            [ecu, tool],
            [
                new DbcMessage(
                    new DbcRawMessageId(512),
                    "MuxStatus",
                    8,
                    ecu,
                    [
                        new DbcSignal("Mode", 0, 4, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 15, "", [tool], DbcMultiplexing.Multiplexor),
                        new DbcSignal("Speed", 8, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 0.5, 1, 0, 250, "km/h", [tool], DbcMultiplexing.Multiplexed(1)),
                    ]),
            ]);

        var reloaded = DbcLoader.LoadTextDocumentOrThrow(DbcWriter.WriteTextOrThrow(original));

        var message = reloaded.ResolveMessage("MuxStatus");
        var mode = message.ResolveSignal("Mode");
        Assert.AreEqual(DbcMultiplexingRole.Multiplexor, mode.Multiplexing.Role);
        Assert.IsNull(mode.Multiplexing.SwitchValue);
        Assert.AreEqual(0, mode.StartBit);
        Assert.AreEqual(4, mode.BitLength);

        var speed = message.ResolveSignal("Speed");
        Assert.AreEqual(DbcMultiplexingRole.Multiplexed, speed.Multiplexing.Role);
        Assert.AreEqual(1, speed.Multiplexing.SwitchValue);
        Assert.AreEqual(8, speed.StartBit);
        Assert.AreEqual(16, speed.BitLength);
        Assert.AreEqual(DbcByteOrder.Intel, speed.ByteOrder);
        Assert.AreEqual(DbcSignalValueType.Unsigned, speed.ValueType);
        Assert.AreEqual(0.5, speed.Factor);
        Assert.AreEqual(1, speed.Offset);
        Assert.AreEqual("km/h", speed.Unit);
    }

    [TestMethod]
    public void WriteText_LoadText_PreservesMotorolaSignalBitRange()
    {
        var ecu = new DbcNode("ECU");
        var tool = new DbcNode("Tool");
        var original = new DbcDocument(
            [ecu, tool],
            [
                new DbcMessage(
                    new DbcRawMessageId(640),
                    "MotorolaStatus",
                    8,
                    ecu,
                    [new DbcSignal("MotoSigned12", 55, 12, DbcByteOrder.Motorola, DbcSignalValueType.Signed, 0.25, -5, -517, 506.75, "", [tool])]),
            ]);

        var reloaded = DbcLoader.LoadTextDocumentOrThrow(DbcWriter.WriteTextOrThrow(original));

        var signal = reloaded.ResolveMessage("MotorolaStatus").ResolveSignal("MotoSigned12");
        Assert.AreEqual(55, signal.StartBit);
        Assert.AreEqual(12, signal.BitLength);
        Assert.AreEqual(DbcByteOrder.Motorola, signal.ByteOrder);
        Assert.AreEqual(DbcSignalValueType.Signed, signal.ValueType);
        Assert.AreEqual(0.25, signal.Factor);
        Assert.AreEqual(-5, signal.Offset);
    }

    [TestMethod]
    public void WriteText_LoadText_PreservesEmptySignalReceivers()
    {
        var ecu = new DbcNode("ECU");
        var original = new DbcDocument(
            [ecu],
            [
                new DbcMessage(
                    new DbcRawMessageId(768),
                    "NoReceiverStatus",
                    8,
                    ecu,
                    [new DbcSignal("Spare", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 255, "", [])]),
            ]);

        var text = DbcWriter.WriteTextOrThrow(original);
        StringAssert.Contains(text, " SG_ Spare : 0|8@1+ (1,0) [0|255] \"\" Vector__XXX");

        var reloaded = DbcLoader.LoadTextDocumentOrThrow(text);

        Assert.AreEqual(1, reloaded.Nodes.Count);
        Assert.IsFalse(reloaded.TryResolveNode("Vector__XXX", out _));
        var signal = reloaded.ResolveMessage("NoReceiverStatus").ResolveSignal("Spare");
        Assert.AreEqual(0, signal.Receivers.Count);
    }

    [TestMethod]
    public void WriteText_LoadText_PreservesEscapedSignalUnit()
    {
        var ecu = new DbcNode("ECU");
        var tool = new DbcNode("Tool");
        const string unit = "V\\rms\"quoted\"";
        var original = new DbcDocument(
            [ecu, tool],
            [
                new DbcMessage(
                    new DbcRawMessageId(1024),
                    "EscapedUnitStatus",
                    8,
                    ecu,
                    [new DbcSignal("Voltage", 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 0.01, 0, 0, 100, unit, [tool])]),
            ]);

        var text = DbcWriter.WriteTextOrThrow(original);
        StringAssert.Contains(text, "\"V\\\\rms\\\"quoted\\\"\"");

        var signal = DbcLoader.LoadTextDocumentOrThrow(text)
            .ResolveMessage("EscapedUnitStatus")
            .ResolveSignal("Voltage");

        Assert.AreEqual(unit, signal.Unit);
    }
}
