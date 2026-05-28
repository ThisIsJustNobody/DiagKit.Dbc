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
}
