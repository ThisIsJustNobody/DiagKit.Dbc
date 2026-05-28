namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcWriterValidationTests
{
    [TestMethod]
    public void WriteText_VectorLongSymbolAttributeDefinitions_ReturnUnsupportedLongSymbolError()
    {
        var definitions = new Dictionary<string, DbcAttributeDefinition>
        {
            ["SystemNodeLongSymbol"] = new("SystemNodeLongSymbol", DbcAttributeOwnerKind.Node, DbcAttributeValueKind.String),
            ["SystemMessageLongSymbol"] = new("SystemMessageLongSymbol", DbcAttributeOwnerKind.Message, DbcAttributeValueKind.String),
            ["SystemSignalLongSymbol"] = new("SystemSignalLongSymbol", DbcAttributeOwnerKind.Signal, DbcAttributeValueKind.String),
            ["SystemEnvVarLongSymbol"] = new("SystemEnvVarLongSymbol", DbcAttributeOwnerKind.EnvironmentVariable, DbcAttributeValueKind.String),
        };
        var document = new DbcDocument([], [], definitions);

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_LONG_SYMBOL"));
    }

    [TestMethod]
    public void WriteText_VectorLongSymbolAttributeValues_ReturnUnsupportedLongSymbolError()
    {
        var ecu = new DbcNode(
            "ECU",
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["SystemNodeLongSymbol"] = new("SystemNodeLongSymbol", DbcAttributeValueKind.String, "EngineControlUnit", "EngineControlUnit"),
            });
        var signal = new DbcSignal(
            "Mode",
            0,
            8,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            1,
            0,
            0,
            255,
            "",
            [ecu],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["SystemSignalLongSymbol"] = new("SystemSignalLongSymbol", DbcAttributeValueKind.String, "OperatingMode", "OperatingMode"),
            });
        var message = new DbcMessage(
            new DbcRawMessageId(256),
            "Status",
            8,
            ecu,
            [signal],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["SystemMessageLongSymbol"] = new("SystemMessageLongSymbol", DbcAttributeValueKind.String, "StatusMessage", "StatusMessage"),
            });
        var environmentVariable = new DbcEnvironmentVariable(
            "Ignition",
            0,
            0,
            1,
            "",
            0,
            1,
            "DUMMY_NODE_VECTOR0",
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["SystemEnvVarLongSymbol"] = new("SystemEnvVarLongSymbol", DbcAttributeValueKind.String, "IgnitionState", "IgnitionState"),
            });
        var document = new DbcDocument(
            [ecu],
            [message],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["SystemNodeLongSymbol"] = new("SystemNodeLongSymbol", DbcAttributeValueKind.String, "NetworkLevel", "NetworkLevel"),
            },
            environmentVariables: new Dictionary<string, DbcEnvironmentVariable>
            {
                [environmentVariable.Name] = environmentVariable,
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_LONG_SYMBOL"));
    }

    [TestMethod]
    public void WriteText_PayloadLengthGreaterThan64_ReturnsRuntimeUnsupportedWarningAndText()
    {
        var ecu = new DbcNode("ECU");
        var document = new DbcDocument(
            [ecu],
            [
                new DbcMessage(
                    new DbcRawMessageId(1280),
                    "LargePayload",
                    65,
                    ecu,
                    [])
            ]);

        var result = DbcWriter.WriteText(document);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.IsTrue(result.Warnings.Any(x => x.Code == "DBC_WRITE_RUNTIME_UNSUPPORTED_MESSAGE"));
        StringAssert.Contains(result.GetTextOrThrow(), "BO_ 1280 LargePayload: 65 ECU");
    }

    [TestMethod]
    public void WriteText_FrameFlagsWithoutAttributeRepresentation_ReturnUnsupportedMetadataError()
    {
        var ecu = new DbcNode("ECU");
        var cases = new[]
        {
            new DbcMessage(
                new DbcRawMessageId(256),
                "FdWithoutAttribute",
                8,
                ecu,
                [],
                frameFlags: DbcFrameFlags.FlexibleDataRate),
            new DbcMessage(
                new DbcRawMessageId(512),
                "BrsStatus",
                12,
                ecu,
                [],
                frameFlags: DbcFrameFlags.BitRateSwitch),
            new DbcMessage(
                new DbcRawMessageId(768),
                "EsiStatus",
                12,
                ecu,
                [],
                frameFlags: DbcFrameFlags.ErrorStateIndicator),
        };

        foreach (var message in cases)
        {
            var result = DbcWriter.WriteText(new DbcDocument([ecu], [message]));

            Assert.IsFalse(result.Succeeded, message.Name);
            Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_METADATA"), message.Name);
        }
    }

    [TestMethod]
    public void WriteText_LargePayloadFlexibleDataRateWithoutVFrameFormat_ReturnsUnsupportedMetadataError()
    {
        var ecu = new DbcNode("ECU");
        var message = new DbcMessage(
            new DbcRawMessageId(1024),
            "LargeFdPayload",
            100,
            ecu,
            [],
            frameFlags: DbcFrameFlags.FlexibleDataRate);

        var result = DbcWriter.WriteText(new DbcDocument([ecu], [message]));

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_METADATA"));
    }

    [TestMethod]
    public void WriteText_VFrameFormatCanFdWithoutFrameFlag_ReturnsUnsupportedMetadataError()
    {
        var ecu = new DbcNode("ECU");
        var message = new DbcMessage(
            new DbcRawMessageId(1025),
            "ClassicPayloadWithFdAttribute",
            8,
            ecu,
            [],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["VFrameFormat"] = new("VFrameFormat", DbcAttributeValueKind.Enum, "14", "StandardCAN_FD"),
            });
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["VFrameFormat"] = CreateVFrameFormatDefinition(),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_METADATA"));
    }

    [TestMethod]
    public void WriteText_VFrameFormatCanFdWithFrameFlag_SucceedsAndReloadPreservesFlexibleDataRate()
    {
        var ecu = new DbcNode("ECU");
        var message = new DbcMessage(
            new DbcRawMessageId(1026),
            "ExplicitFdPayload",
            8,
            ecu,
            [],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["VFrameFormat"] = new("VFrameFormat", DbcAttributeValueKind.Enum, "14", "StandardCAN_FD"),
            },
            frameFlags: DbcFrameFlags.FlexibleDataRate);
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["VFrameFormat"] = CreateVFrameFormatDefinition(),
            });

        var text = DbcWriter.WriteTextOrThrow(document);
        var reloaded = DbcLoader.LoadTextDocumentOrThrow(text);

        Assert.AreEqual(DbcFrameFlags.FlexibleDataRate, reloaded.ResolveMessage("ExplicitFdPayload").FrameFlags & DbcFrameFlags.FlexibleDataRate);
    }

    [TestMethod]
    public void WriteText_InvalidAttributeDefaultAndValueRawText_ReturnsInvalidAttributeValueError()
    {
        var ecu = new DbcNode("ECU");
        var message = new DbcMessage(
            new DbcRawMessageId(1027),
            "InvalidAttributeRaw",
            8,
            ecu,
            [],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["GenMsgCycleTime"] = new("GenMsgCycleTime", DbcAttributeValueKind.Integer, "not_an_integer", "not_an_integer"),
            });
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["GenMsgCycleTime"] = new(
                    "GenMsgCycleTime",
                    DbcAttributeOwnerKind.Message,
                    DbcAttributeValueKind.Integer,
                    minimum: 0,
                    maximum: 65535,
                    defaultValue: new DbcAttributeValue("GenMsgCycleTime", DbcAttributeValueKind.Integer, "bad_default", "bad_default")),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_VALUE"));
    }

    [TestMethod]
    public void WriteText_NegativeHexAttributeDefaultAndValue_ReturnsInvalidAttributeValueError()
    {
        var ecu = new DbcNode("ECU");
        var message = new DbcMessage(
            new DbcRawMessageId(1028),
            "NegativeHexAttribute",
            8,
            ecu,
            [],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["HexAttribute"] = new("HexAttribute", DbcAttributeValueKind.Hex, "-1", -1),
            });
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["HexAttribute"] = new(
                    "HexAttribute",
                    DbcAttributeOwnerKind.Message,
                    DbcAttributeValueKind.Hex,
                    minimum: 0,
                    maximum: 255,
                    defaultValue: new DbcAttributeValue("HexAttribute", DbcAttributeValueKind.Hex, "-1", -1)),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_VALUE"));
    }

    [TestMethod]
    public void WriteText_InvalidNumericAttributeDefinitionRanges_ReturnsInvalidAttributeDefinitionError()
    {
        var document = new DbcDocument(
            [],
            [],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["MissingRange"] = new("MissingRange", DbcAttributeOwnerKind.Network, DbcAttributeValueKind.Integer),
                ["NonFiniteRange"] = new("NonFiniteRange", DbcAttributeOwnerKind.Network, DbcAttributeValueKind.Float, minimum: double.NaN, maximum: double.PositiveInfinity),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_DEFINITION"));
    }

    [TestMethod]
    public void WriteText_InvalidNumericRelationAttributeDefinitionRanges_ReturnsInvalidAttributeDefinitionError()
    {
        var document = new DbcDocument(
            [],
            [],
            relationAttributeDefinitions: new Dictionary<string, DbcRelationAttributeDefinition>
            {
                ["MissingRange"] = new("MissingRange", "BU_SG_REL_", DbcAttributeValueKind.Integer),
                ["InvalidRange"] = new("InvalidRange", "BU_SG_REL_", DbcAttributeValueKind.Hex, minimum: 10, maximum: 1),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_DEFINITION"));
    }

    [TestMethod]
    public void WriteText_InvalidRelationKind_ReturnsInvalidRelationMetadataError()
    {
        var document = new DbcDocument(
            [],
            [],
            relationAttributeDefinitions: new Dictionary<string, DbcRelationAttributeDefinition>
            {
                ["RelationAttribute"] = new("RelationAttribute", "BU;SG_REL_", DbcAttributeValueKind.Integer, minimum: 0, maximum: 10),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_RELATION_METADATA"));
    }

    [TestMethod]
    public void WriteText_RelationTargetWithSemicolon_ReturnsInvalidRelationMetadataError()
    {
        var document = new DbcDocument(
            [],
            [],
            relationAttributeDefinitions: new Dictionary<string, DbcRelationAttributeDefinition>
            {
                ["RelationAttribute"] = new("RelationAttribute", "BU_SG_REL_", DbcAttributeValueKind.Integer, minimum: 0, maximum: 10),
            },
            relationAttributes:
            [
                new DbcRelationAttributeValue("RelationAttribute", "BU_SG_REL_ ECU; 256 Speed", "5"),
            ]);

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_RELATION_METADATA"));
    }

    [TestMethod]
    public void WriteText_RelationDefaultAndValueRawKindMismatch_ReturnsInvalidAttributeValueError()
    {
        var document = new DbcDocument(
            [],
            [],
            relationAttributeDefinitions: new Dictionary<string, DbcRelationAttributeDefinition>
            {
                ["RelationAttribute"] = new("RelationAttribute", "BU_SG_REL_", DbcAttributeValueKind.Integer, minimum: 0, maximum: 10),
            },
            relationAttributeDefaults: new Dictionary<string, DbcRelationAttributeDefault>
            {
                ["RelationAttribute"] = new("RelationAttribute", "abc"),
            },
            relationAttributes:
            [
                new DbcRelationAttributeValue("RelationAttribute", "BU_SG_REL_ ECU 256 Speed", "abc"),
            ]);

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_VALUE"));
    }

    [TestMethod]
    public void WriteText_InvalidEnvironmentVariableNumbersOrAccessType_ReturnsInvalidEnvironmentVariableError()
    {
        var cases = new[]
        {
            new DbcEnvironmentVariable("BadMin", 0, double.NaN, 1, "", 0, 1, "DUMMY_NODE_VECTOR0"),
            new DbcEnvironmentVariable("BadMax", 0, 0, double.PositiveInfinity, "", 0, 1, "DUMMY_NODE_VECTOR0"),
            new DbcEnvironmentVariable("BadInitial", 0, 0, 1, "", double.NegativeInfinity, 1, "DUMMY_NODE_VECTOR0"),
            new DbcEnvironmentVariable("BadAccessType", 0, 0, 1, "", 0, 1, "ACCESS TYPE"),
            new DbcEnvironmentVariable("BrokenAccessType", 0, 0, 1, "", 0, 1, "DUMMY;BROKEN"),
        };

        foreach (var variable in cases)
        {
            var result = DbcWriter.WriteText(new DbcDocument(
                [],
                [],
                environmentVariables: new Dictionary<string, DbcEnvironmentVariable>
                {
                    [variable.Name] = variable,
                }));

            Assert.IsFalse(result.Succeeded, variable.Name);
            Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ENVIRONMENT_VARIABLE"), variable.Name);
        }
    }

    [TestMethod]
    public void WriteText_EnvironmentVariableSourceNameDiffersFromCanonicalName_ReturnsUnsupportedLongSymbolError()
    {
        var variable = new DbcEnvironmentVariable(
            "Environment_Variable_Long_Name",
            0,
            0,
            1,
            "",
            0,
            1,
            "DUMMY_NODE_VECTOR0",
            sourceName: "EnvShort");
        var document = new DbcDocument(
            [],
            [],
            environmentVariables: new Dictionary<string, DbcEnvironmentVariable>
            {
                [variable.Name] = variable,
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_LONG_SYMBOL"));
    }

    [TestMethod]
    public void WriteText_MappedMetadataValueWithoutMatchingAttribute_ReturnsUnsupportedMetadataError()
    {
        var ecu = new DbcNode("ECU");
        var message = new DbcMessage(
            new DbcRawMessageId(256),
            "CyclicStatus",
            8,
            ecu,
            [],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["GenMsgCycleTime"] = new("GenMsgCycleTime", DbcAttributeValueKind.Integer, "20", 20),
            },
            cycleTimeMs: 10);

        var result = DbcWriter.WriteText(new DbcDocument([ecu], [message]));

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_METADATA"));
    }

    private static DbcAttributeDefinition CreateVFrameFormatDefinition()
    {
        return new DbcAttributeDefinition(
            "VFrameFormat",
            DbcAttributeOwnerKind.Message,
            DbcAttributeValueKind.Enum,
            ["StandardCAN", "ExtendedCAN", "reserved", "J1939PG", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "StandardCAN_FD", "ExtendedCAN_FD"]);
    }
}
