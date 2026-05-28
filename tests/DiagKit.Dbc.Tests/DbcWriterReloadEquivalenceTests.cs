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
}
