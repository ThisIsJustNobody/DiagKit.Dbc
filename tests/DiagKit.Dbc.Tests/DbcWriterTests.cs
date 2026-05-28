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
}
