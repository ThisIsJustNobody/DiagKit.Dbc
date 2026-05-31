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

        var text = DbcWriter.WriteTextOrThrow(builder.Build());
        var reloaded = DbcLoader.LoadTextDocumentOrThrow(text);

        var message = reloaded.ResolveMessage("Status");
        Assert.AreEqual("edited", message.Comment);
        var signal = message.ResolveSignal("Speed");
        Assert.AreEqual(0, signal.StartBit);
        Assert.AreEqual(16, signal.BitLength);
        Assert.AreEqual(0.1, signal.Factor);
        Assert.AreEqual("km/h", signal.Unit);
        Assert.AreEqual("Tool", signal.Receivers.Single().Name);
    }

    [TestMethod]
    public void FromDocument_GetSignal_EditsExistingSignal()
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
                    [new DbcSignal("Speed", 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 0.1, 0, 0, 250, "km/h", [tool])]),
            ]);

        var builder = original.ToBuilder();
        builder.GetMessage("Status")
            .GetSignal("Speed")
            .WithScaling(0.125, -10)
            .WithRange(-10, 500)
            .WithUnit("rpm")
            .WithComment("edited signal");

        var reloaded = DbcLoader.LoadTextDocumentOrThrow(DbcWriter.WriteTextOrThrow(builder.Build()));
        var signal = reloaded.ResolveSignal("Status", "Speed");

        Assert.AreEqual(0.125, signal.Factor);
        Assert.AreEqual(-10, signal.Offset);
        Assert.AreEqual(-10, signal.Minimum);
        Assert.AreEqual(500, signal.Maximum);
        Assert.AreEqual("rpm", signal.Unit);
        Assert.AreEqual("edited signal", signal.Comment);
    }

    [TestMethod]
    public void FromDocument_SourceNameNodeReferences_BuildsWritableDocument()
    {
        var ecu = new DbcNode("LongEngineController", sourceName: "ECU");
        var tool = new DbcNode("LongDiagnosticTool", sourceName: "Tool");
        var original = new DbcDocument([ecu, tool], []);
        var builder = DbcDocumentBuilder.FromDocument(original);

        builder
            .AddMessage(new DbcRawMessageId(512), "Command", 8, "ECU")
            .AddSignal("Request", 0, 8)
            .WithReceiver("Tool");

        var text = DbcWriter.WriteTextOrThrow(builder.Build());
        var reloaded = DbcLoader.LoadTextDocumentOrThrow(text);

        var message = reloaded.ResolveMessage("Command");
        Assert.AreEqual("LongEngineController", message.PrimaryTransmitter.Name);
        Assert.AreEqual("LongDiagnosticTool", message.ResolveSignal("Request").Receivers.Single().Name);
    }

    [TestMethod]
    public void FromDocument_AliasCanonicalCollision_PreservesDistinctNodes()
    {
        var aliasedNode = new DbcNode("Controller", sourceName: "Tool");
        var canonicalCollision = new DbcNode("Tool", comment: "distinct node");
        var original = new DbcDocument([aliasedNode, canonicalCollision], []);

        var document = DbcDocumentBuilder.FromDocument(original).Build();

        Assert.AreEqual(2, document.Nodes.Count);
        Assert.IsTrue(document.Nodes.Any(x => x.Name == "Controller"));
        Assert.IsTrue(document.Nodes.Any(x => x.Name == "Tool" && x.Comment == "distinct node"));
        Assert.IsFalse(DbcWriter.WriteText(document).Succeeded);
    }

    [TestMethod]
    public void FromDocument_AmbiguousSourceNameReference_Throws()
    {
        var aliasedNode = new DbcNode("Controller", sourceName: "ECU");
        var canonicalCollision = new DbcNode("ECU");
        var builder = DbcDocumentBuilder.FromDocument(new DbcDocument([aliasedNode, canonicalCollision], []));

        builder.AddMessage(new DbcRawMessageId(768), "Ambiguous", 8, "ECU");

        Assert.ThrowsExactly<DbcException>(() => builder.Build());
    }
}
