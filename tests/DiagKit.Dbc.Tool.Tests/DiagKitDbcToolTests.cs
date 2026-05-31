namespace DiagKit.Dbc.Tool.Tests;

[TestClass]
public sealed class DiagKitDbcToolTests
{
    [TestMethod]
    public void WorkbookExportImportValidate_CreateExpectedFilesAndExitZero()
    {
        var directory = Path.Combine(Path.GetTempPath(), "diagkit-dbc-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var inputDbc = Path.Combine(directory, "input.dbc");
            var workbookPath = Path.Combine(directory, "edit.xlsx");
            var outputDbc = Path.Combine(directory, "output.dbc");
            DbcWriter.WriteFileOrThrow(inputDbc, CreateDocument());

            var templatePath = Path.Combine(directory, "blank.xlsx");
            var templateExit = Program.Main(["workbook", "template", "-o", templatePath]);
            var exportExit = Program.Main(["workbook", "export", inputDbc, "-o", workbookPath]);
            var validateExit = Program.Main(["workbook", "validate", workbookPath]);
            var importExit = Program.Main(["workbook", "import", workbookPath, "-o", outputDbc]);
            var legacyImportExit = Program.Main(["workbook", "import", inputDbc, workbookPath, "-o", Path.Combine(directory, "legacy.dbc")]);

            Assert.AreEqual(0, templateExit);
            Assert.AreEqual(0, exportExit);
            Assert.AreEqual(0, validateExit);
            Assert.AreEqual(0, importExit);
            Assert.AreEqual(2, legacyImportExit);
            Assert.IsTrue(File.Exists(templatePath));
            Assert.IsTrue(File.Exists(workbookPath));
            Assert.IsTrue(File.Exists(outputDbc));
            Assert.IsTrue(DbcLoader.LoadFile(outputDbc).Succeeded);
            var workbookText = ReadAllText(workbookPath);
            Assert.IsFalse(workbookText.Contains("_Readme", StringComparison.Ordinal));
            Assert.IsFalse(workbookText.Contains("_Manifest", StringComparison.Ordinal));
            Assert.IsFalse(workbookText.Contains("schema_version", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DbcDocument CreateDocument()
    {
        var ecu = new DbcNode("ECU");
        var tester = new DbcNode("Tester");
        return new DbcDocument(
            [ecu, tester],
            [
                new DbcMessage(
                    new DbcRawMessageId(0x100),
                    "Status",
                    8,
                    ecu,
                    [new DbcSignal("Speed", 0, 16, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 0.1, 0, 0, 250, "km/h", [tester])]),
            ]);
    }

    private static string ReadAllText(string path)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        return string.Join(
            Environment.NewLine,
            archive.Entries.Select(entry =>
            {
                using var reader = new StreamReader(entry.Open());
                return entry.FullName + Environment.NewLine + reader.ReadToEnd();
            }));
    }
}
