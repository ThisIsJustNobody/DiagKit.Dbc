namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcWriterTests
{
    [TestMethod]
    public void WriteText_EmptyDocument_ReturnsHeaderAndNoErrors()
    {
        var document = new DbcDocument([], []);

        var result = DbcWriter.WriteText(document);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, result.Errors.Count);
        StringAssert.Contains(result.GetTextOrThrow(), "VERSION \"\"");
        StringAssert.Contains(result.GetTextOrThrow(), "NS_ :");
        StringAssert.Contains(result.GetTextOrThrow(), "BS_:");
        StringAssert.Contains(result.GetTextOrThrow(), "BU_:");
    }

    [TestMethod]
    public void WriteTextOrThrow_ThrowsWhenValidationHasError()
    {
        var ecu = new DbcNode("ECU");
        var document = new DbcDocument(
            [ecu],
            [
                new DbcMessage(
                    new DbcRawMessageId(0x100),
                    "Bad Message",
                    8,
                    ecu,
                    [])
            ]);

        var exception = Assert.ThrowsExactly<DbcException>(() => DbcWriter.WriteTextOrThrow(document));

        StringAssert.Contains(exception.Message, "DBC_WRITE_INVALID_IDENTIFIER");
    }

    [TestMethod]
    public void WriteText_DuplicateNodeSourceNames_ReturnsNameCollisionError()
    {
        var document = new DbcDocument(
            [
                new DbcNode("FirstNode", sourceName: "ECU"),
                new DbcNode("SecondNode", sourceName: "ECU"),
            ],
            []);

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_NAME_COLLISION"));
    }

    [TestMethod]
    public void WriteText_UseCanonicalNamesWhenValid_EmitsCanonicalNodeName()
    {
        var document = new DbcDocument(
            [new DbcNode("CanonicalNode", sourceName: "ShortNode")],
            []);
        var options = new DbcWriterOptions
        {
            NameExportPolicy = DbcNameExportPolicy.UseCanonicalNamesWhenValid,
        };

        var text = DbcWriter.WriteTextOrThrow(document, options);

        StringAssert.Contains(text, "BU_: CanonicalNode");
    }

    [TestMethod]
    public void WriteText_LenientDuplicateExportName_ReturnsNameCollisionError()
    {
        var document = new DbcDocument(
            [
                new DbcNode("FirstNode", sourceName: "ECU"),
                new DbcNode("SecondNode", sourceName: "ECU"),
            ],
            []);
        var options = new DbcWriterOptions
        {
            Mode = DbcWriteMode.Lenient,
        };

        var result = DbcWriter.WriteText(document, options);

        Assert.IsFalse(result.Succeeded);
        var diagnostic = result.Errors.Single(x => x.Code == "DBC_WRITE_NAME_COLLISION");
        Assert.AreEqual(DbcDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [TestMethod]
    public void WriteText_PayloadGreaterThan64_ReturnsRuntimeUnsupportedWarningAndText()
    {
        var ecu = new DbcNode("ECU");
        var document = new DbcDocument(
            [ecu],
            [
                new DbcMessage(
                    new DbcRawMessageId(0x100),
                    "LargePayload",
                    65,
                    ecu,
                    [])
            ]);

        var result = DbcWriter.WriteText(document);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, result.Errors.Count);
        var diagnostic = result.Warnings.Single(x => x.Code == "DBC_WRITE_RUNTIME_UNSUPPORTED_MESSAGE");
        Assert.AreEqual(DbcDiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(result.GetTextOrThrow(), "BU_: ECU");
    }

    [TestMethod]
    public void WriteText_MessageAndSignals_EmitsNormalizedBoAndSgLines()
    {
        var ecu = new DbcNode("ECU");
        var tool = new DbcNode("Tool");
        var document = new DbcDocument(
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

        var text = DbcWriter.WriteTextOrThrow(document);

        StringAssert.Contains(text, "BU_: ECU Tool");
        StringAssert.Contains(text, "BO_ 256 VehicleStatus: 8 ECU");
        StringAssert.Contains(text, " SG_ Speed : 0|16@1+ (0.10000000000000001,0) [0|250] \"km/h\" Tool");
        StringAssert.Contains(text, " SG_ Gear : 16|8@1- (1,-1) [-1|8] \"\" Tool");
        StringAssert.Contains(text, " SG_ Status : 24|8@1+ (1,0) [0|15] \"\" ECU,Tool");
    }

    [TestMethod]
    public void WriteText_FloatAndDoubleSignals_ReturnsUnsupportedSignalValueTypeError()
    {
        var ecu = new DbcNode("ECU");
        var document = new DbcDocument(
            [ecu],
            [
                new DbcMessage(
                    new DbcRawMessageId(256),
                    "VehicleStatus",
                    8,
                    ecu,
                    [
                        new DbcSignal("Temperature", 0, 32, DbcByteOrder.Intel, DbcSignalValueType.Float, 1, 0, 0, 100, "degC", [ecu]),
                        new DbcSignal("Energy", 32, 32, DbcByteOrder.Intel, DbcSignalValueType.Double, 1, 0, 0, 100, "kWh", [ecu]),
                    ]),
            ]);

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(2, result.Errors.Count(x => x.Code == "DBC_WRITE_UNSUPPORTED_SIGNAL_VALUE_TYPE"));
    }

    [TestMethod]
    public void WriteText_NonFiniteSignalNumbers_ReturnsNonFiniteNumberError()
    {
        (string SignalName, double Factor, double Offset, double Minimum, double Maximum)[] cases =
        {
            ("BadFactor", double.NaN, 0, 0, 100),
            ("BadOffset", 1, double.PositiveInfinity, 0, 100),
            ("BadMinimum", 1, 0, double.NegativeInfinity, 100),
            ("BadMaximum", 1, 0, 0, double.NaN),
        };

        foreach (var (signalName, factor, offset, minimum, maximum) in cases)
        {
            var ecu = new DbcNode("ECU");
            var document = new DbcDocument(
                [ecu],
                [
                    new DbcMessage(
                        new DbcRawMessageId(256),
                        "VehicleStatus",
                        8,
                        ecu,
                        [new DbcSignal(signalName, 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, factor, offset, minimum, maximum, "", [ecu])]),
                ]);

            var result = DbcWriter.WriteText(document);

            Assert.IsFalse(result.Succeeded, signalName);
            Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_NON_FINITE_SIGNAL_NUMBER"), signalName);
        }
    }

    [TestMethod]
    public void WriteText_ReceiverExportNameUsesVectorSentinel_ReturnsReservedReceiverNameError()
    {
        var ecu = new DbcNode("ECU");
        var reservedReceiver = new DbcNode("ReservedReceiver", sourceName: "Vector__XXX");
        var document = new DbcDocument(
            [ecu, reservedReceiver],
            [
                new DbcMessage(
                    new DbcRawMessageId(256),
                    "VehicleStatus",
                    8,
                    ecu,
                    [new DbcSignal("Speed", 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 250, "km/h", [reservedReceiver])]),
            ]);

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Text);
        var diagnostic = result.Errors.Single(x => x.Code == "DBC_WRITE_RESERVED_RECEIVER_NAME");
        Assert.AreEqual(DbcDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [TestMethod]
    public void WriteText_DefaultPolicyWithLongSymbolExport_ReturnsUnsupportedLongSymbolError()
    {
        var nodeLongSymbol = new DbcNode("CanonicalEcu", sourceName: "ECU");
        var nodeCase = new DbcDocument([nodeLongSymbol], []);

        var messageEcu = new DbcNode("ECU");
        var messageCase = new DbcDocument(
            [messageEcu],
            [
                new DbcMessage(
                    new DbcRawMessageId(256),
                    "CanonicalStatus",
                    8,
                    messageEcu,
                    [],
                    sourceName: "Status"),
            ]);

        var signalEcu = new DbcNode("ECU");
        var signalCase = new DbcDocument(
            [signalEcu],
            [
                new DbcMessage(
                    new DbcRawMessageId(256),
                    "VehicleStatus",
                    8,
                    signalEcu,
                    [new DbcSignal("CanonicalSpeed", 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 250, "km/h", [signalEcu], sourceName: "Speed")]),
            ]);

        var referencedReceiver = new DbcNode("CanonicalTool", sourceName: "Tool");
        var referencedNodeCase = new DbcDocument(
            [signalEcu],
            [
                new DbcMessage(
                    new DbcRawMessageId(512),
                    "ReceiverStatus",
                    8,
                    signalEcu,
                    [new DbcSignal("Speed", 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 250, "km/h", [referencedReceiver])]),
            ]);

        foreach (var document in new[] { nodeCase, messageCase, signalCase, referencedNodeCase })
        {
            var result = DbcWriter.WriteText(document);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Text);
            var diagnostic = result.Errors.Single(x => x.Code == "DBC_WRITE_UNSUPPORTED_LONG_SYMBOL");
            Assert.AreEqual(DbcDiagnosticSeverity.Error, diagnostic.Severity);
        }
    }

    [TestMethod]
    public void WriteText_AdditionalTransmitters_ReturnsUnsupportedAdditionalTransmittersError()
    {
        var ecu = new DbcNode("ECU");
        var tool = new DbcNode("Tool");
        var additionalTransmittersDocument = new DbcDocument(
            [ecu, tool],
            [
                new DbcMessage(
                    new DbcRawMessageId(256),
                    "VehicleStatus",
                    8,
                    ecu,
                    [],
                    transmitters: [ecu, tool]),
            ]);

        var mismatchedTransmitterDocument = new DbcDocument(
            [ecu, tool],
            [
                new DbcMessage(
                    new DbcRawMessageId(512),
                    "ToolStatus",
                    8,
                    ecu,
                    [],
                    transmitters: [tool]),
            ]);
        var sameNamePrimary = new DbcNode("ECU");
        var sameNameDifferentInstanceTransmitter = new DbcNode("ECU", comment: "not the primary transmitter object");
        var sameNameDifferentInstanceDocument = new DbcDocument(
            [sameNamePrimary],
            [
                new DbcMessage(
                    new DbcRawMessageId(768),
                    "SameNameStatus",
                    8,
                    sameNamePrimary,
                    [],
                    transmitters: [sameNameDifferentInstanceTransmitter]),
            ]);

        foreach (var document in new[] { additionalTransmittersDocument, mismatchedTransmitterDocument, sameNameDifferentInstanceDocument })
        {
            var result = DbcWriter.WriteText(document);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Text);
            var diagnostic = result.Errors.Single(x => x.Code == "DBC_WRITE_UNSUPPORTED_ADDITIONAL_TRANSMITTERS");
            Assert.AreEqual(DbcDiagnosticSeverity.Error, diagnostic.Severity);
        }
    }

    [TestMethod]
    public void WriteText_UnsupportedMetadata_ReturnsUnsupportedMetadataError()
    {
        var ecu = new DbcNode("ECU");
        var attribute = new DbcAttributeValue("Metadata", DbcAttributeValueKind.String, "\"x\"", "x");
        var environmentVariable = new DbcEnvironmentVariable("EnvStatus", 0, 0, 1, "", 0, 0, "DUMMY_NODE_VECTOR0", [ecu]);
        var metadataCases = new[]
        {
            new DbcDocument([ecu], [], comment: "network comment"),
            new DbcDocument([ecu], [], environmentVariables: new Dictionary<string, DbcEnvironmentVariable>
            {
                [environmentVariable.Name] = environmentVariable,
            }),
            new DbcDocument(
                [new DbcNode("AttributedNode", attributes: new Dictionary<string, DbcAttributeValue>
                {
                    [attribute.Name] = attribute,
                })],
                []),
            new DbcDocument(
                [ecu],
                [
                    new DbcMessage(
                        new DbcRawMessageId(256),
                        "CyclicStatus",
                        8,
                        ecu,
                        [],
                        cycleTimeMs: 10),
                ]),
            new DbcDocument(
                [ecu],
                [
                    new DbcMessage(
                        new DbcRawMessageId(512),
                        "ValueStatus",
                        8,
                        ecu,
                        [
                            new DbcSignal(
                                "Mode",
                                0,
                                8,
                                DbcByteOrder.Intel,
                                DbcSignalValueType.Unsigned,
                                1,
                                0,
                                0,
                                3,
                                "",
                                [ecu],
                                valueDescriptions: new Dictionary<long, string>
                                {
                                    [1] = "On",
                                }),
                        ]),
                ]),
        };

        foreach (var document in metadataCases)
        {
            var result = DbcWriter.WriteText(document);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Text);
            var diagnostic = result.Errors.Single(x => x.Code == "DBC_WRITE_UNSUPPORTED_METADATA");
            Assert.AreEqual(DbcDiagnosticSeverity.Error, diagnostic.Severity);
        }
    }

    [TestMethod]
    public void WriteText_MalformedMultiplexedSignal_ReturnsUnsupportedMultiplexingError()
    {
        (string CaseName, DbcMultiplexing Multiplexing)[] cases =
        {
            ("MissingSwitchValue", new DbcMultiplexing(DbcMultiplexingRole.Multiplexed, null)),
            ("NegativeSwitchValue", DbcMultiplexing.Multiplexed(-1)),
            ("NoneWithSwitchValue", new DbcMultiplexing(DbcMultiplexingRole.None, 1)),
            ("MultiplexorWithSwitchValue", new DbcMultiplexing(DbcMultiplexingRole.Multiplexor, 1)),
        };

        foreach (var (caseName, multiplexing) in cases)
        {
            var ecu = new DbcNode("ECU");
            var document = new DbcDocument(
                [ecu],
                [
                    new DbcMessage(
                        new DbcRawMessageId(256),
                        "MuxStatus",
                        8,
                        ecu,
                        [new DbcSignal("Speed", 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 250, "km/h", [ecu], multiplexing)]),
                ]);

            var result = DbcWriter.WriteText(document);

            Assert.IsFalse(result.Succeeded, caseName);
            Assert.IsNull(result.Text, caseName);
            var diagnostic = result.Errors.Single(x => x.Code == "DBC_WRITE_UNSUPPORTED_MULTIPLEXING");
            Assert.AreEqual(DbcDiagnosticSeverity.Error, diagnostic.Severity, caseName);
        }
    }

    [TestMethod]
    public void WriteText_InvalidSignalBitRanges_ReturnsInvalidSignalBitRangeError()
    {
        (string SignalName, int DataLength, int StartBit, int BitLength, DbcByteOrder ByteOrder)[] cases =
        {
            ("NegativeStart", 8, -1, 8, DbcByteOrder.Intel),
            ("ZeroLength", 8, 0, 0, DbcByteOrder.Intel),
            ("ExceedsMessagePayload", 8, 64, 1, DbcByteOrder.Intel),
            ("SignalTooWide", 8, 0, 65, DbcByteOrder.Intel),
            ("MotorolaExceedsMessagePayload", 1, 3, 12, DbcByteOrder.Motorola),
        };

        foreach (var (signalName, dataLength, startBit, bitLength, byteOrder) in cases)
        {
            var ecu = new DbcNode("ECU");
            var document = new DbcDocument(
                [ecu],
                [
                    new DbcMessage(
                        new DbcRawMessageId(256),
                        "VehicleStatus",
                        dataLength,
                        ecu,
                        [new DbcSignal(signalName, startBit, bitLength, byteOrder, DbcSignalValueType.Unsigned, 1, 0, 0, 250, "", [ecu])]),
                ]);

            var result = DbcWriter.WriteText(document);

            Assert.IsFalse(result.Succeeded, signalName);
            Assert.IsNull(result.Text, signalName);
            Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_SIGNAL_BIT_RANGE"), signalName);
        }
    }

    [TestMethod]
    public void WriteText_MetadataOnlyLargePayloadSignalWithinMessageRange_SucceedsWithRuntimeWarning()
    {
        var ecu = new DbcNode("ECU");
        var document = new DbcDocument(
            [ecu],
            [
                new DbcMessage(
                    new DbcRawMessageId(1280),
                    "LargePayload",
                    100,
                    ecu,
                    [new DbcSignal("DiagnosticBlock", 512, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 255, "", [ecu])]),
            ]);

        var result = DbcWriter.WriteText(document);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.IsTrue(result.Warnings.Any(x => x.Code == "DBC_WRITE_RUNTIME_UNSUPPORTED_MESSAGE"));
        var text = result.GetTextOrThrow();
        StringAssert.Contains(text, "BO_ 1280 LargePayload: 100 ECU");
        StringAssert.Contains(text, " SG_ DiagnosticBlock : 512|8@1+ (1,0) [0|255] \"\" ECU");
    }

    [TestMethod]
    public void WriteText_UseCanonicalNamesWhenValid_EmitsCanonicalMessageSignalAndNodeReferences()
    {
        var ecu = new DbcNode("CanonicalEcu", sourceName: "ECU");
        var tool = new DbcNode("CanonicalTool", sourceName: "Tool");
        var document = new DbcDocument(
            [ecu, tool],
            [
                new DbcMessage(
                    new DbcRawMessageId(256),
                    "CanonicalStatus",
                    8,
                    ecu,
                    [new DbcSignal("CanonicalSpeed", 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 250, "km/h", [tool], sourceName: "SpeedShort")],
                    sourceName: "StatusShort"),
            ]);
        var options = new DbcWriterOptions
        {
            NameExportPolicy = DbcNameExportPolicy.UseCanonicalNamesWhenValid,
        };

        var text = DbcWriter.WriteTextOrThrow(document, options);

        StringAssert.Contains(text, "BU_: CanonicalEcu CanonicalTool");
        StringAssert.Contains(text, "BO_ 256 CanonicalStatus: 8 CanonicalEcu");
        StringAssert.Contains(text, " SG_ CanonicalSpeed : 0|16@1+ (1,0) [0|250] \"km/h\" CanonicalTool");

        var reloaded = DbcLoader.LoadTextDocumentOrThrow(text);
        Assert.IsTrue(reloaded.TryResolveNode("CanonicalEcu", out _));
        var message = reloaded.ResolveMessage("CanonicalStatus");
        Assert.AreEqual("CanonicalStatus", message.Name);
        Assert.AreEqual("CanonicalSpeed", message.ResolveSignal("CanonicalSpeed").Name);
    }

    [TestMethod]
    public void WriteText_StableSortMode_OrdersMessagesByRawIdThenExportName()
    {
        var ecu = new DbcNode("ECU");
        var document = new DbcDocument(
            [ecu],
            [
                new DbcMessage(new DbcRawMessageId(300), "AHighId", 8, ecu, []),
                new DbcMessage(new DbcRawMessageId(100), "ZLowId", 8, ecu, []),
                new DbcMessage(new DbcRawMessageId(200), "MMidId", 8, ecu, []),
            ]);
        var options = new DbcWriterOptions
        {
            SortMode = DbcWriterSortMode.Stable,
        };

        var text = DbcWriter.WriteTextOrThrow(document, options);

        Assert.IsTrue(
            text.IndexOf("BO_ 100 ZLowId: 8 ECU", StringComparison.Ordinal) <
            text.IndexOf("BO_ 200 MMidId: 8 ECU", StringComparison.Ordinal));
        Assert.IsTrue(
            text.IndexOf("BO_ 200 MMidId: 8 ECU", StringComparison.Ordinal) <
            text.IndexOf("BO_ 300 AHighId: 8 ECU", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WriteText_ExtendedMultiplexing_ReturnsUnsupportedMultiplexingError()
    {
        var ecu = new DbcNode("ECU");
        var document = new DbcDocument(
            [ecu],
            [
                new DbcMessage(
                    new DbcRawMessageId(256),
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
                            DbcMultiplexing.Multiplexed("Mode", [new DbcMultiplexorRange(1, 3)])),
                    ]),
            ]);

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Text);
        var diagnostic = result.Errors.Single(x => x.Code == "DBC_WRITE_UNSUPPORTED_MULTIPLEXING");
        Assert.AreEqual(DbcDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [TestMethod]
    public void WriteText_MultiplexedSignalWithExtendedRanges_ReturnsUnsupportedMultiplexingError()
    {
        var ecu = new DbcNode("ECU");
        var multiplexing = DbcMultiplexing.Multiplexed(1)
            .WithExtendedRanges("Mode", [new DbcMultiplexorRange(1, 3)]);
        var document = new DbcDocument(
            [ecu],
            [
                new DbcMessage(
                    new DbcRawMessageId(256),
                    "MuxStatus",
                    8,
                    ecu,
                    [
                        new DbcSignal("Mode", 0, 4, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 15, "", [ecu], DbcMultiplexing.Multiplexor),
                        new DbcSignal("Speed", 8, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 250, "km/h", [ecu], multiplexing),
                    ]),
            ]);

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Text);
        var diagnostic = result.Errors.Single(x => x.Code == "DBC_WRITE_UNSUPPORTED_MULTIPLEXING");
        Assert.AreEqual(DbcDiagnosticSeverity.Error, diagnostic.Severity);
    }
}
