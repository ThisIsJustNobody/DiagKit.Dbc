namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcWriterReloadEquivalenceTests
{
    [TestMethod]
    public void WriteText_CommentsAndValueDescriptions_ReloadsEquivalentText()
    {
        var ecu = new DbcNode("CanonicalEcu", "node comment \\ \"quoted\"", sourceName: "ECU");
        var tool = new DbcNode("CanonicalTool", sourceName: "Tool");
        var original = new DbcDocument(
            [ecu, tool],
            [
                new DbcMessage(
                    new DbcRawMessageId(256),
                    "CanonicalStatus",
                    8,
                    ecu,
                    [
                        new DbcSignal(
                            "CanonicalMode",
                            0,
                            8,
                            DbcByteOrder.Intel,
                            DbcSignalValueType.Unsigned,
                            1,
                            0,
                            0,
                            3,
                            "",
                            [tool],
                            valueDescriptions: new Dictionary<long, string>
                            {
                                [2] = "Drive \\ \"quoted\"",
                                [0] = "Park",
                                [1] = "Reverse",
                            },
                            comment: "signal comment \\ \"quoted\"",
                            sourceName: "ModeShort"),
                    ],
                    comment: "message comment \\ \"quoted\"",
                    sourceName: "StatusShort"),
            ],
            comment: "document comment \\ \"quoted\"");
        var options = new DbcWriterOptions
        {
            NameExportPolicy = DbcNameExportPolicy.UseCanonicalNamesWhenValid,
        };

        var text = DbcWriter.WriteTextOrThrow(original, options);

        StringAssert.Contains(text, "CM_ \"document comment \\\\ \\\"quoted\\\"\";");
        StringAssert.Contains(text, "CM_ BU_ CanonicalEcu \"node comment \\\\ \\\"quoted\\\"\";");
        StringAssert.Contains(text, "CM_ BO_ 256 \"message comment \\\\ \\\"quoted\\\"\";");
        StringAssert.Contains(text, "CM_ SG_ 256 CanonicalMode \"signal comment \\\\ \\\"quoted\\\"\";");
        StringAssert.Contains(text, "VAL_ 256 CanonicalMode 0 \"Park\" 1 \"Reverse\" 2 \"Drive \\\\ \\\"quoted\\\"\";");

        var reloaded = DbcLoader.LoadTextDocumentOrThrow(text);

        Assert.AreEqual("document comment \\ \"quoted\"", reloaded.Comment);
        Assert.AreEqual("node comment \\ \"quoted\"", reloaded.ResolveNode("CanonicalEcu").Comment);
        var message = reloaded.ResolveMessage("CanonicalStatus");
        Assert.AreEqual("message comment \\ \"quoted\"", message.Comment);
        var signal = message.ResolveSignal("CanonicalMode");
        Assert.AreEqual("signal comment \\ \"quoted\"", signal.Comment);
        Assert.AreEqual("Park", signal.ValueDescriptions[0]);
        Assert.AreEqual("Reverse", signal.ValueDescriptions[1]);
        Assert.AreEqual("Drive \\ \"quoted\"", signal.ValueDescriptions[2]);
    }

    [TestMethod]
    public void WriteText_ReferencedOnlyNodeComments_ReloadsEquivalentComments()
    {
        var listedNode = new DbcNode("ListedNode");
        var ecu = new DbcNode("ECU", "primary transmitter comment");
        var tool = new DbcNode("Tool", "receiver comment");
        var original = new DbcDocument(
            [listedNode],
            [
                new DbcMessage(
                    new DbcRawMessageId(257),
                    "ReferencedOnlyStatus",
                    8,
                    ecu,
                    [new DbcSignal("Speed", 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 250, "km/h", [tool])]),
            ]);

        var text = DbcWriter.WriteTextOrThrow(original);

        StringAssert.Contains(text, "CM_ BU_ ECU \"primary transmitter comment\";");
        StringAssert.Contains(text, "CM_ BU_ Tool \"receiver comment\";");
        var reloaded = DbcLoader.LoadTextDocumentOrThrow(text);
        Assert.AreEqual("primary transmitter comment", reloaded.ResolveNode("ECU").Comment);
        Assert.AreEqual("receiver comment", reloaded.ResolveNode("Tool").Comment);
    }

    [TestMethod]
    public void WriteText_FloatAndDoubleSignals_EmitSigValTypeAndReloadEquivalentTypes()
    {
        var ecu = new DbcNode("ECU");
        var original = new DbcDocument(
            [ecu],
            [
                new DbcMessage(
                    new DbcRawMessageId(300),
                    "FloatStatus",
                    8,
                    ecu,
                    [
                        new DbcSignal("Temperature", 0, 32, DbcByteOrder.Intel, DbcSignalValueType.Float, 1, 0, -40, 215, "degC", [ecu]),
                        new DbcSignal("Energy", 32, 32, DbcByteOrder.Intel, DbcSignalValueType.Double, 1, 0, 0, 1000, "kWh", [ecu]),
                    ]),
            ]);

        var text = DbcWriter.WriteTextOrThrow(original);

        StringAssert.Contains(text, "SIG_VALTYPE_ 300 Temperature : 1;");
        StringAssert.Contains(text, "SIG_VALTYPE_ 300 Energy : 2;");
        var message = DbcLoader.LoadTextDocumentOrThrow(text).ResolveMessage("FloatStatus");
        Assert.AreEqual(DbcSignalValueType.Float, message.ResolveSignal("Temperature").ValueType);
        Assert.AreEqual(DbcSignalValueType.Double, message.ResolveSignal("Energy").ValueType);
    }

    [TestMethod]
    public void WriteText_ExtendedMultiplexing_EmitsSgMulValAndReloadsRanges()
    {
        var ecu = new DbcNode("ECU");
        var original = new DbcDocument(
            [ecu],
            [
                new DbcMessage(
                    new DbcRawMessageId(400),
                    "MuxStatus",
                    8,
                    ecu,
                    [
                        new DbcSignal("Mode", 0, 4, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 15, "", [ecu], DbcMultiplexing.Multiplexor),
                        new DbcSignal(
                            "Speed",
                            8,
                            16,
                            DbcByteOrder.Intel,
                            DbcSignalValueType.Unsigned,
                            1,
                            0,
                            0,
                            250,
                            "km/h",
                            [ecu],
                            DbcMultiplexing.Multiplexed("Mode", [new DbcMultiplexorRange(1, 3), new DbcMultiplexorRange(5, 7)])),
                    ]),
            ]);

        var text = DbcWriter.WriteTextOrThrow(original);

        StringAssert.Contains(text, "SG_MUL_VAL_ 400 Speed Mode 1-3, 5-7;");
        var signal = DbcLoader.LoadTextDocumentOrThrow(text)
            .ResolveMessage("MuxStatus")
            .ResolveSignal("Speed");
        Assert.AreEqual(DbcMultiplexingRole.Multiplexed, signal.Multiplexing.Role);
        Assert.IsNull(signal.Multiplexing.SwitchValue);
        Assert.AreEqual("Mode", signal.Multiplexing.MultiplexorSignalName);
        CollectionAssert.AreEqual(
            new[] { new DbcMultiplexorRange(1, 3), new DbcMultiplexorRange(5, 7) },
            signal.Multiplexing.SwitchRanges.ToArray());
    }

    [TestMethod]
    public void WriteText_BasicMultiplexingWithExtendedRanges_EmitsTokenAndSgMulVal()
    {
        var ecu = new DbcNode("ECU");
        var original = new DbcDocument(
            [ecu],
            [
                new DbcMessage(
                    new DbcRawMessageId(401),
                    "MixedMuxStatus",
                    8,
                    ecu,
                    [
                        new DbcSignal("Mode", 0, 4, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 15, "", [ecu], DbcMultiplexing.Multiplexor),
                        new DbcSignal(
                            "Speed",
                            8,
                            16,
                            DbcByteOrder.Intel,
                            DbcSignalValueType.Unsigned,
                            1,
                            0,
                            0,
                            250,
                            "km/h",
                            [ecu],
                            DbcMultiplexing.Multiplexed(2).WithExtendedRanges("Mode", [new DbcMultiplexorRange(4, 6)])),
                    ]),
            ]);

        var text = DbcWriter.WriteTextOrThrow(original);

        StringAssert.Contains(text, " SG_ Speed m2 : 8|16@1+ (1,0) [0|250] \"km/h\" ECU");
        StringAssert.Contains(text, "SG_MUL_VAL_ 401 Speed Mode 4-6;");
        var signal = DbcLoader.LoadTextDocumentOrThrow(text)
            .ResolveMessage("MixedMuxStatus")
            .ResolveSignal("Speed");
        Assert.AreEqual(2, signal.Multiplexing.SwitchValue);
        Assert.AreEqual("Mode", signal.Multiplexing.MultiplexorSignalName);
        CollectionAssert.AreEqual(
            new[] { new DbcMultiplexorRange(4, 6) },
            signal.Multiplexing.SwitchRanges.ToArray());
    }

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
                        new DbcSignal("Status", 24, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 15, "", [ecu, tool]),
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

        var status = message.ResolveSignal("Status");
        Assert.AreEqual(2, status.Receivers.Count);
        Assert.AreEqual("ECU", status.Receivers[0].Name);
        Assert.AreEqual("Tool", status.Receivers[1].Name);
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
