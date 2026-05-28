namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcWriterValidationTests
{
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
}
