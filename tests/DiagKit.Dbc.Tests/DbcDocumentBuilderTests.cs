namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcDocumentBuilderTests
{
    [TestMethod]
    public void Builder_CreateMessageAndSignal_BuildsWritableDocument()
    {
        var builder = DbcDocumentBuilder.Create();
        var ecu = builder.AddNode("ECU");
        var tool = builder.AddNode("Tool");

        builder
            .AddMessage(new DbcRawMessageId(256), "Status", 8, ecu.Name)
            .WithComment("status message")
            .AddSignal("Speed", 0, 16)
            .WithScaling(0.1, 0)
            .WithRange(0, 250)
            .WithUnit("km/h")
            .WithReceiver(tool.Name)
            .WithValueDescription(0, "Stopped")
            .WithValueDescription(1, "Moving");

        var text = DbcWriter.WriteTextOrThrow(builder.Build());
        var reloaded = DbcLoader.LoadTextDocumentOrThrow(text);

        var message = reloaded.ResolveMessage("Status");
        Assert.AreEqual("status message", message.Comment);
        var signal = message.ResolveSignal("Speed");
        Assert.AreEqual("km/h", signal.Unit);
        Assert.AreEqual("Stopped", signal.ValueDescriptions[0]);
        Assert.AreEqual("Moving", signal.ValueDescriptions[1]);
    }

    [TestMethod]
    public void FromDocument_EditMessageComment_PreservesExistingSignals()
    {
        var ecu = new DbcNode("ECU");
        var tool = new DbcNode("Tool");
        var original = new DbcDocument(
            [ecu, tool],
            [
                new DbcMessage(
                    new DbcRawMessageId(256),
                    "Status",
                    8,
                    ecu,
                    [new DbcSignal("Speed", 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 0.1, 0, 0, 250, "km/h", [tool])],
                    comment: "original"),
            ]);

        var builder = DbcDocumentBuilder.FromDocument(original);
        builder.GetMessage("Status").WithComment("edited");

        var document = builder.Build();

        var message = document.ResolveMessage("Status");
        Assert.AreEqual("edited", message.Comment);
        var signal = message.ResolveSignal("Speed");
        Assert.AreEqual(0, signal.StartBit);
        Assert.AreEqual(16, signal.BitLength);
        Assert.AreEqual(0.1, signal.Factor);
        Assert.AreEqual("km/h", signal.Unit);
        Assert.AreEqual("Tool", signal.Receivers.Single().Name);
    }
}
