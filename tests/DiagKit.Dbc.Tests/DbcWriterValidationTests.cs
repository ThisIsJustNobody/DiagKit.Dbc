namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcWriterValidationTests
{
    [TestMethod]
    public void WriteText_VectorLongSymbolAttributeDefinitions_SucceedAndEmitOnce()
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

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        var text = result.GetTextOrThrow();
        Assert.AreEqual(1, CountOccurrences(text, "BA_DEF_ BU_ \"SystemNodeLongSymbol\" STRING;"));
        Assert.AreEqual(1, CountOccurrences(text, "BA_DEF_ BO_ \"SystemMessageLongSymbol\" STRING;"));
        Assert.AreEqual(1, CountOccurrences(text, "BA_DEF_ SG_ \"SystemSignalLongSymbol\" STRING;"));
        Assert.AreEqual(1, CountOccurrences(text, "BA_DEF_ EV_ \"SystemEnvVarLongSymbol\" STRING;"));
    }

    [TestMethod]
    public void WriteText_VectorLongSymbolAttributeValues_MatchingCanonicalNamesSucceedAndEmitOnce()
    {
        var ecu = new DbcNode(
            "EngineControlUnit",
            sourceName: "ECU",
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["SystemNodeLongSymbol"] = new("SystemNodeLongSymbol", DbcAttributeValueKind.String, "EngineControlUnit", "EngineControlUnit"),
            });
        var signal = new DbcSignal(
            "OperatingMode",
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
            },
            sourceName: "Mode");
        var message = new DbcMessage(
            new DbcRawMessageId(256),
            "StatusMessage",
            8,
            ecu,
            [signal],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["SystemMessageLongSymbol"] = new("SystemMessageLongSymbol", DbcAttributeValueKind.String, "StatusMessage", "StatusMessage"),
            },
            sourceName: "Status");
        var environmentVariable = new DbcEnvironmentVariable(
            "IgnitionState",
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
            },
            sourceName: "Ignition");
        var document = new DbcDocument(
            [ecu],
            [message],
            environmentVariables: new Dictionary<string, DbcEnvironmentVariable>
            {
                [environmentVariable.Name] = environmentVariable,
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        var text = result.GetTextOrThrow();
        Assert.AreEqual(1, CountOccurrences(text, "BA_ \"SystemNodeLongSymbol\" BU_ ECU \"EngineControlUnit\";"));
        Assert.AreEqual(1, CountOccurrences(text, "BA_ \"SystemMessageLongSymbol\" BO_ 256 \"StatusMessage\";"));
        Assert.AreEqual(1, CountOccurrences(text, "BA_ \"SystemSignalLongSymbol\" SG_ 256 Mode \"OperatingMode\";"));
        Assert.AreEqual(1, CountOccurrences(text, "BA_ \"SystemEnvVarLongSymbol\" EV_ Ignition \"IgnitionState\";"));
    }

    [TestMethod]
    public void WriteText_SignalLongSymbolValueWithDottedCanonicalName_SucceedsAndReloadsAliases()
    {
        var ecu = new DbcNode("ECU");
        var signal = new DbcSignal(
            "Powertrain.Vehicle.Speed",
            0,
            16,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            1,
            0,
            0,
            250,
            "km/h",
            [ecu],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["SystemSignalLongSymbol"] = new("SystemSignalLongSymbol", DbcAttributeValueKind.String, "Powertrain.Vehicle.Speed", "Powertrain.Vehicle.Speed"),
            },
            sourceName: "VehSpdShort");
        var document = new DbcDocument(
            [ecu],
            [
                new DbcMessage(
                    new DbcRawMessageId(258),
                    "VehicleStatus",
                    8,
                    ecu,
                    [signal]),
            ]);

        var result = DbcWriter.WriteText(document);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        var text = result.GetTextOrThrow();
        StringAssert.Contains(text, "BA_ \"SystemSignalLongSymbol\" SG_ 258 VehSpdShort \"Powertrain.Vehicle.Speed\";");
        var reloadedMessage = DbcLoader.LoadTextDocumentOrThrow(text).ResolveMessage("VehicleStatus");
        var reloadedSignal = reloadedMessage.ResolveSignal("Powertrain.Vehicle.Speed");
        Assert.AreEqual("Powertrain.Vehicle.Speed", reloadedSignal.Name);
        Assert.IsTrue(reloadedMessage.TryResolveSignal("VehSpdShort", out _));
    }

    [TestMethod]
    public void WriteText_VectorLongSymbolAttributeValues_ConflictingExplicitValueReturnsConflictError()
    {
        var ecu = new DbcNode(
            "ECU",
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["SystemNodeLongSymbol"] = new("SystemNodeLongSymbol", DbcAttributeValueKind.String, "OtherName", "OtherName"),
            });
        var document = new DbcDocument([ecu], []);

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_LONG_SYMBOL_CONFLICT"));
    }

    [TestMethod]
    public void WriteText_VectorLongSymbolAttributeDefinitionWrongOwnerOrKindReturnsConflictError()
    {
        var cases = new[]
        {
            new DbcAttributeDefinition("SystemNodeLongSymbol", DbcAttributeOwnerKind.Message, DbcAttributeValueKind.String),
            new DbcAttributeDefinition("SystemMessageLongSymbol", DbcAttributeOwnerKind.Message, DbcAttributeValueKind.Integer, minimum: 0, maximum: 1),
        };

        foreach (var definition in cases)
        {
            var document = new DbcDocument(
                [],
                [],
                new Dictionary<string, DbcAttributeDefinition>
                {
                    [definition.Name] = definition,
                });

            var result = DbcWriter.WriteText(document);

            Assert.IsFalse(result.Succeeded, definition.Name);
            Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_LONG_SYMBOL_CONFLICT"), definition.Name);
        }
    }

    [TestMethod]
    public void WriteText_VectorLongSymbolNetworkValueReturnsConflictError()
    {
        var document = new DbcDocument(
            [],
            [],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["SystemNodeLongSymbol"] = new("SystemNodeLongSymbol", DbcAttributeValueKind.String, "NetworkLevel", "NetworkLevel"),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_LONG_SYMBOL_CONFLICT"));
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
    public void WriteText_NumericAttributeRawWithLeadingOrTrailingWhitespace_ReturnsInvalidAttributeValueError()
    {
        var ecu = new DbcNode("ECU");
        var message = new DbcMessage(
            new DbcRawMessageId(1030),
            "WhitespaceNumericAttributes",
            8,
            ecu,
            [],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["IntegerAttribute"] = new("IntegerAttribute", DbcAttributeValueKind.Integer, "1 ", 1),
                ["HexAttribute"] = new("HexAttribute", DbcAttributeValueKind.Hex, " 1", 1),
                ["FloatAttribute"] = new("FloatAttribute", DbcAttributeValueKind.Float, "1 ", 1d),
            });
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["IntegerAttribute"] = new("IntegerAttribute", DbcAttributeOwnerKind.Message, DbcAttributeValueKind.Integer, minimum: 0, maximum: 10),
                ["HexAttribute"] = new("HexAttribute", DbcAttributeOwnerKind.Message, DbcAttributeValueKind.Hex, minimum: 0, maximum: 10),
                ["FloatAttribute"] = new("FloatAttribute", DbcAttributeOwnerKind.Message, DbcAttributeValueKind.Float, minimum: 0, maximum: 10),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_VALUE"));
    }

    [TestMethod]
    public void WriteText_IntegerAttributeParsedValueMismatch_ReturnsInvalidAttributeValueError()
    {
        var ecu = new DbcNode("ECU");
        var message = new DbcMessage(
            new DbcRawMessageId(1031),
            "MismatchedIntegerAttribute",
            8,
            ecu,
            [],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["IntegerAttribute"] = new("IntegerAttribute", DbcAttributeValueKind.Integer, "1", 2),
            });
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["IntegerAttribute"] = new("IntegerAttribute", DbcAttributeOwnerKind.Message, DbcAttributeValueKind.Integer, minimum: 0, maximum: 10),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_VALUE"));
    }

    [TestMethod]
    public void WriteText_StringAttributeParsedValueMismatch_ReturnsInvalidAttributeValueError()
    {
        var document = new DbcDocument(
            [new DbcNode("ECU")],
            [],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["Name"] = new("Name", DbcAttributeOwnerKind.Network, DbcAttributeValueKind.String),
            },
            new Dictionary<string, DbcAttributeValue>
            {
                ["Name"] = new("Name", DbcAttributeValueKind.String, "RawName", "ParsedName"),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_VALUE"));
    }

    [TestMethod]
    public void WriteText_EnumNumericRawWithExpectedLabelValue_Succeeds()
    {
        var document = new DbcDocument(
            [new DbcNode("ECU")],
            [],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["Mode"] = new("Mode", DbcAttributeOwnerKind.Network, DbcAttributeValueKind.Enum, ["Off", "On"]),
            },
            new Dictionary<string, DbcAttributeValue>
            {
                ["Mode"] = new("Mode", DbcAttributeValueKind.Enum, "1", "On"),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
    }

    [TestMethod]
    public void WriteText_EnumNumericRawOutsideIndexWithMatchingRawValue_Succeeds()
    {
        var document = new DbcDocument(
            [new DbcNode("ECU")],
            [],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["Mode"] = new("Mode", DbcAttributeOwnerKind.Network, DbcAttributeValueKind.Enum, ["1"]),
            },
            new Dictionary<string, DbcAttributeValue>
            {
                ["Mode"] = new("Mode", DbcAttributeValueKind.Enum, "1", "1"),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
    }

    [TestMethod]
    public void WriteText_EnumNumericRawWithMismatchedValue_ReturnsInvalidAttributeValueError()
    {
        var cases = new[]
        {
            new DbcAttributeValue("Mode", DbcAttributeValueKind.Enum, "1", "1"),
            new DbcAttributeValue("Mode", DbcAttributeValueKind.Enum, "1", "Off"),
        };

        foreach (var attribute in cases)
        {
            var document = new DbcDocument(
                [new DbcNode("ECU")],
                [],
                new Dictionary<string, DbcAttributeDefinition>
                {
                    ["Mode"] = new("Mode", DbcAttributeOwnerKind.Network, DbcAttributeValueKind.Enum, ["Off", "On"]),
                },
                new Dictionary<string, DbcAttributeValue>
                {
                    ["Mode"] = attribute,
                });

            var result = DbcWriter.WriteText(document);

            Assert.IsFalse(result.Succeeded, attribute.Value?.ToString());
            Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_VALUE"), attribute.Value?.ToString());
        }
    }

    [TestMethod]
    public void WriteText_EnumNumericIndexRawWithWhitespace_ReturnsInvalidAttributeValueError()
    {
        var document = new DbcDocument(
            [new DbcNode("ECU")],
            [],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["Mode"] = new("Mode", DbcAttributeOwnerKind.Network, DbcAttributeValueKind.Enum, ["Off", "On"]),
            },
            new Dictionary<string, DbcAttributeValue>
            {
                ["Mode"] = new("Mode", DbcAttributeValueKind.Enum, "1 ", "On"),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_VALUE"));
    }

    [TestMethod]
    public void WriteText_RelationNumericRawWithWhitespace_ReturnsInvalidAttributeValueError()
    {
        var document = new DbcDocument(
            [new DbcNode("ECU")],
            [],
            relationAttributeDefinitions: new Dictionary<string, DbcRelationAttributeDefinition>
            {
                ["RelationAttribute"] = new("RelationAttribute", "BU_SG_REL_", DbcAttributeValueKind.Integer, minimum: 0, maximum: 10),
            },
            relationAttributeDefaults: new Dictionary<string, DbcRelationAttributeDefault>
            {
                ["RelationAttribute"] = new("RelationAttribute", "1 "),
            },
            relationAttributes:
            [
                new DbcRelationAttributeValue("RelationAttribute", "BU_SG_REL_ ECU 256 Speed", "1 "),
            ]);

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
    public void WriteText_StringAttributeRawWithNewline_ReturnsInvalidAttributeValueError()
    {
        var ecu = new DbcNode("ECU");
        var message = new DbcMessage(
            new DbcRawMessageId(1029),
            "UnsafeStringAttribute",
            8,
            ecu,
            [],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["DisplayName"] = new("DisplayName", DbcAttributeValueKind.String, "Line1\nLine2", "Line1\nLine2"),
            });
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["DisplayName"] = new("DisplayName", DbcAttributeOwnerKind.Message, DbcAttributeValueKind.String),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_VALUE"));
    }

    [TestMethod]
    public void WriteText_EnumAttributeDefinitionLabelWithNewline_ReturnsInvalidAttributeDefinitionError()
    {
        var document = new DbcDocument(
            [],
            [],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["Mode"] = new("Mode", DbcAttributeOwnerKind.Network, DbcAttributeValueKind.Enum, ["Normal", "Broken\nLabel"]),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_DEFINITION"));
    }

    [TestMethod]
    public void WriteText_RelationStringRawWithNewline_ReturnsInvalidAttributeValueError()
    {
        var document = new DbcDocument(
            [],
            [],
            relationAttributeDefinitions: new Dictionary<string, DbcRelationAttributeDefinition>
            {
                ["RelationText"] = new("RelationText", "BU_SG_REL_", DbcAttributeValueKind.String),
            },
            relationAttributeDefaults: new Dictionary<string, DbcRelationAttributeDefault>
            {
                ["RelationText"] = new("RelationText", "Line1\nLine2"),
            },
            relationAttributes:
            [
                new DbcRelationAttributeValue("RelationText", "BU_SG_REL_ ECU 256 Speed", "Line1\nLine2"),
            ]);

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_VALUE"));
    }

    [TestMethod]
    public void WriteText_RelationEnumDefinitionLabelWithNewline_ReturnsInvalidAttributeDefinitionError()
    {
        var document = new DbcDocument(
            [],
            [],
            relationAttributeDefinitions: new Dictionary<string, DbcRelationAttributeDefinition>
            {
                ["RelationMode"] = new("RelationMode", "BU_SG_REL_", DbcAttributeValueKind.Enum, ["Normal", "Broken\nLabel"]),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_INVALID_ATTRIBUTE_DEFINITION"));
    }

    [TestMethod]
    public void WriteText_CanDbPlusKnownGoodStrictRejectsKnownUnsupportedMetadata()
    {
        var document = CreateCanDbPlusCompatibilityDocument();
        var options = new DbcWriterOptions
        {
            CompatibilityProfile = DbcWriterCompatibilityProfile.CanDbPlusKnownGood,
        };

        var result = DbcWriter.WriteText(document, options);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(2, result.Errors.Count(x => x.Code == "DBC_WRITE_UNSUPPORTED_CANDB_PLUS_METADATA"));
        Assert.IsTrue(result.Errors.Any(x => x.Message.Contains("Environment variable 'Ignition'", StringComparison.Ordinal)));
        Assert.IsTrue(result.Errors.Any(x => x.Message.Contains("Relation attribute 'GenSigTimeoutTime'", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void WriteText_CanDbPlusKnownGoodLenientOmitsKnownUnsupportedMetadataAndWarns()
    {
        var document = CreateCanDbPlusCompatibilityDocument();
        var options = new DbcWriterOptions
        {
            CompatibilityProfile = DbcWriterCompatibilityProfile.CanDbPlusKnownGood,
            Mode = DbcWriteMode.Lenient,
        };

        var result = DbcWriter.WriteText(document, options);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        Assert.AreEqual(2, result.Warnings.Count(x => x.Code == "DBC_WRITE_UNSUPPORTED_CANDB_PLUS_METADATA"));
        Assert.AreEqual(0, result.Errors.Count);
        var text = result.GetTextOrThrow();
        StringAssert.Contains(text, "EV_ Ignition : 0 [0|1] \"bool\" 0 1 DUMMY_NODE_VECTOR0 HOST;");
        StringAssert.Contains(text, "BA_DEF_REL_ BU_SG_REL_ \"GenSigTimeoutTime\" INT 0 65535;");
        StringAssert.Contains(text, "BA_DEF_DEF_REL_ \"GenSigTimeoutTime\" 0;");
        Assert.IsFalse(text.Contains("BA_ \"EnvKind\" EV_ Ignition", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("BA_REL_ \"GenSigTimeoutTime\"", StringComparison.Ordinal));
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
    public void WriteText_EnvironmentVariableSourceNameDiffersFromCanonicalName_EmitsLongSymbolAndReloadsAliases()
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
            [new DbcNode("ECU")],
            [],
            environmentVariables: new Dictionary<string, DbcEnvironmentVariable>
            {
                [variable.Name] = variable,
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        var text = result.GetTextOrThrow();
        StringAssert.Contains(text, "EV_ EnvShort : 0 [0|1] \"\" 0 1 DUMMY_NODE_VECTOR0;");
        StringAssert.Contains(text, "BA_DEF_ EV_ \"SystemEnvVarLongSymbol\" STRING;");
        StringAssert.Contains(text, "BA_ \"SystemEnvVarLongSymbol\" EV_ EnvShort \"Environment_Variable_Long_Name\";");
        var reloaded = DbcLoader.LoadTextDocumentOrThrow(text);
        Assert.IsTrue(reloaded.TryResolveEnvironmentVariable("Environment_Variable_Long_Name", out _));
        Assert.IsTrue(reloaded.TryResolveEnvironmentVariable("EnvShort", out _));
    }

    private static DbcDocument CreateCanDbPlusCompatibilityDocument()
    {
        var vcu = new DbcNode("VCU");
        var host = new DbcNode("HOST");
        var ignition = new DbcEnvironmentVariable(
            "Ignition",
            0,
            0,
            1,
            "bool",
            0,
            1,
            "DUMMY_NODE_VECTOR0",
            [host],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["EnvKind"] = new("EnvKind", DbcAttributeValueKind.String, "Calibration", "Calibration"),
            });
        return new DbcDocument(
            [vcu, host],
            [
                new DbcMessage(
                    new DbcRawMessageId(256),
                    "VehicleStatus",
                    8,
                    vcu,
                    [new DbcSignal("Speed", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 255, "", [host])]),
            ],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["EnvKind"] = new("EnvKind", DbcAttributeOwnerKind.EnvironmentVariable, DbcAttributeValueKind.String),
            },
            environmentVariables: new Dictionary<string, DbcEnvironmentVariable>
            {
                [ignition.Name] = ignition,
            },
            relationAttributeDefinitions: new Dictionary<string, DbcRelationAttributeDefinition>
            {
                ["GenSigTimeoutTime"] = new("GenSigTimeoutTime", "BU_SG_REL_", DbcAttributeValueKind.Integer, minimum: 0, maximum: 65535),
            },
            relationAttributeDefaults: new Dictionary<string, DbcRelationAttributeDefault>
            {
                ["GenSigTimeoutTime"] = new("GenSigTimeoutTime", "0"),
            },
            relationAttributes:
            [
                new DbcRelationAttributeValue("GenSigTimeoutTime", "BU_SG_REL_ VCU 256 Speed", "100"),
            ]);
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

    [TestMethod]
    public void WriteText_MessageCycleTimeAttributeWithoutSemanticValue_ReturnsUnsupportedMetadataError()
    {
        var ecu = new DbcNode("ECU");
        var message = new DbcMessage(
            new DbcRawMessageId(256),
            "Status",
            8,
            ecu,
            [],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["GenMsgCycleTime"] = new("GenMsgCycleTime", DbcAttributeValueKind.Integer, "10", 10),
            });
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["GenMsgCycleTime"] = new("GenMsgCycleTime", DbcAttributeOwnerKind.Message, DbcAttributeValueKind.Integer, minimum: 0, maximum: 65535),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_METADATA"));
    }

    [TestMethod]
    public void WriteText_SignalStartValueAttributeWithoutSemanticValue_ReturnsUnsupportedMetadataError()
    {
        var ecu = new DbcNode("ECU");
        var signal = new DbcSignal(
            "Speed",
            0,
            16,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            1,
            0,
            0,
            250,
            "km/h",
            [ecu],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["GenSigStartValue"] = new("GenSigStartValue", DbcAttributeValueKind.Integer, "5", 5),
            });
        var message = new DbcMessage(new DbcRawMessageId(256), "Status", 8, ecu, [signal]);
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["GenSigStartValue"] = new("GenSigStartValue", DbcAttributeOwnerKind.Signal, DbcAttributeValueKind.Integer, minimum: 0, maximum: 65535),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_METADATA"));
    }

    [TestMethod]
    public void WriteText_MappedAttributeDefaultsWouldChangeMessageOrSignalSemantics_ReturnsUnsupportedMetadataError()
    {
        var ecu = new DbcNode("ECU");
        var signal = new DbcSignal(
            "Speed",
            0,
            16,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            1,
            0,
            0,
            250,
            "km/h",
            [ecu]);
        var message = new DbcMessage(new DbcRawMessageId(256), "Status", 8, ecu, [signal]);
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["GenMsgSendType"] = new(
                    "GenMsgSendType",
                    DbcAttributeOwnerKind.Message,
                    DbcAttributeValueKind.Enum,
                    ["cyclic", "event"],
                    defaultValue: new DbcAttributeValue("GenMsgSendType", DbcAttributeValueKind.Enum, "cyclic", "cyclic")),
                ["GenSigTimeoutTime"] = new(
                    "GenSigTimeoutTime",
                    DbcAttributeOwnerKind.Signal,
                    DbcAttributeValueKind.Integer,
                    minimum: 0,
                    maximum: 65535,
                    defaultValue: new DbcAttributeValue("GenSigTimeoutTime", DbcAttributeValueKind.Integer, "250", 250)),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_METADATA"));
    }

    [TestMethod]
    public void WriteText_UnparseableExplicitMessageSendTypeWithMatchingDefault_ReturnsUnsupportedMetadataError()
    {
        var ecu = new DbcNode("ECU");
        var message = new DbcMessage(
            new DbcRawMessageId(256),
            "Status",
            8,
            ecu,
            [],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["GenMsgSendType"] = new("GenMsgSendType", DbcAttributeValueKind.Enum, "bogus", "bogus"),
            },
            sendType: DbcSendType.Cyclic);
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["GenMsgSendType"] = new(
                    "GenMsgSendType",
                    DbcAttributeOwnerKind.Message,
                    DbcAttributeValueKind.Enum,
                    ["cyclic", "event"],
                    defaultValue: new DbcAttributeValue("GenMsgSendType", DbcAttributeValueKind.Enum, "cyclic", "cyclic")),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_METADATA"));
    }

    [TestMethod]
    public void WriteText_UnparseableExplicitSignalSendTypeWithMatchingDefault_ReturnsUnsupportedMetadataError()
    {
        var ecu = new DbcNode("ECU");
        var signal = new DbcSignal(
            "Speed",
            0,
            16,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            1,
            0,
            0,
            250,
            "km/h",
            [ecu],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["GenSigSendType"] = new("GenSigSendType", DbcAttributeValueKind.Enum, "bogus", "bogus"),
            },
            sendType: DbcSendType.OnWrite);
        var message = new DbcMessage(new DbcRawMessageId(256), "Status", 8, ecu, [signal]);
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["GenSigSendType"] = new(
                    "GenSigSendType",
                    DbcAttributeOwnerKind.Signal,
                    DbcAttributeValueKind.Enum,
                    ["cyclic", "onWrite"],
                    defaultValue: new DbcAttributeValue("GenSigSendType", DbcAttributeValueKind.Enum, "onWrite", "onWrite")),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_METADATA"));
    }

    [TestMethod]
    public void WriteText_MappedMetadataExplicitAttributeOrDefaultMatchingSemanticValue_Succeeds()
    {
        var ecu = new DbcNode("ECU");
        var signal = new DbcSignal(
            "Speed",
            0,
            16,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            1,
            0,
            0,
            250,
            "km/h",
            [ecu],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["GenSigStartValue"] = new("GenSigStartValue", DbcAttributeValueKind.Integer, "5", 5),
                ["GenSigSendType"] = new("GenSigSendType", DbcAttributeValueKind.Enum, "onChange", "onChange"),
            },
            initialValue: 5,
            sendType: DbcSendType.OnChange,
            timeoutTimeMs: 250);
        var message = new DbcMessage(
            new DbcRawMessageId(256),
            "Status",
            8,
            ecu,
            [signal],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["GenMsgCycleTime"] = new("GenMsgCycleTime", DbcAttributeValueKind.Integer, "10", 10),
                ["GenMsgTimeoutTime"] = new("GenMsgTimeoutTime", DbcAttributeValueKind.Integer, "1000", 1000),
            },
            cycleTimeMs: 10,
            sendType: DbcSendType.Cyclic,
            timeoutTimeMs: 1000);
        var document = new DbcDocument(
            [ecu],
            [message],
            new Dictionary<string, DbcAttributeDefinition>
            {
                ["GenMsgCycleTime"] = new("GenMsgCycleTime", DbcAttributeOwnerKind.Message, DbcAttributeValueKind.Integer, minimum: 0, maximum: 65535),
                ["GenMsgSendType"] = new(
                    "GenMsgSendType",
                    DbcAttributeOwnerKind.Message,
                    DbcAttributeValueKind.Enum,
                    ["cyclic", "event"],
                    defaultValue: new DbcAttributeValue("GenMsgSendType", DbcAttributeValueKind.Enum, "cyclic", "cyclic")),
                ["GenMsgTimeoutTime"] = new("GenMsgTimeoutTime", DbcAttributeOwnerKind.Message, DbcAttributeValueKind.Integer, minimum: 0, maximum: 65535),
                ["GenSigStartValue"] = new("GenSigStartValue", DbcAttributeOwnerKind.Signal, DbcAttributeValueKind.Integer, minimum: 0, maximum: 65535),
                ["GenSigSendType"] = new("GenSigSendType", DbcAttributeOwnerKind.Signal, DbcAttributeValueKind.Enum, ["onChange"]),
                ["GenSigTimeoutTime"] = new(
                    "GenSigTimeoutTime",
                    DbcAttributeOwnerKind.Signal,
                    DbcAttributeValueKind.Integer,
                    minimum: 0,
                    maximum: 65535,
                    defaultValue: new DbcAttributeValue("GenSigTimeoutTime", DbcAttributeValueKind.Integer, "250", 250)),
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
    }

    [TestMethod]
    public void WriteText_ExtraNameAliases_ReturnsUnsupportedMetadataError()
    {
        var ecu = new DbcNode("EngineControlUnit", sourceName: "ECU", nameAliases: ["ExtraNodeAlias"]);
        var receiver = new DbcNode("DisplayNode", sourceName: "Display", nameAliases: ["ExtraReferencedAlias"]);
        var signal = new DbcSignal(
            "VehicleSpeed",
            0,
            16,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            1,
            0,
            0,
            250,
            "km/h",
            [receiver],
            sourceName: "VehSpd",
            nameAliases: ["ExtraSignalAlias"]);
        var message = new DbcMessage(
            new DbcRawMessageId(256),
            "VehicleStatus",
            8,
            ecu,
            [signal],
            sourceName: "VehStatus",
            nameAliases: ["ExtraMessageAlias"]);
        var variable = new DbcEnvironmentVariable(
            "IgnitionState",
            0,
            0,
            1,
            "",
            0,
            1,
            "DUMMY_NODE_VECTOR0",
            sourceName: "Ignition",
            nameAliases: ["ExtraEnvAlias"]);
        var document = new DbcDocument(
            [ecu],
            [message],
            environmentVariables: new Dictionary<string, DbcEnvironmentVariable>
            {
                [variable.Name] = variable,
            });

        var result = DbcWriter.WriteText(document);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WRITE_UNSUPPORTED_METADATA"));
        var messages = string.Join(Environment.NewLine, result.Errors.Select(x => x.Message));
        StringAssert.Contains(messages, "ExtraNodeAlias");
        StringAssert.Contains(messages, "ExtraReferencedAlias");
        StringAssert.Contains(messages, "ExtraMessageAlias");
        StringAssert.Contains(messages, "ExtraSignalAlias");
        StringAssert.Contains(messages, "ExtraEnvAlias");
    }

    private static DbcAttributeDefinition CreateVFrameFormatDefinition()
    {
        return new DbcAttributeDefinition(
            "VFrameFormat",
            DbcAttributeOwnerKind.Message,
            DbcAttributeValueKind.Enum,
            ["StandardCAN", "ExtendedCAN", "reserved", "J1939PG", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "reserved", "StandardCAN_FD", "ExtendedCAN_FD"]);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
