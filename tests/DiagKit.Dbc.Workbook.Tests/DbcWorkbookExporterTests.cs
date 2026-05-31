namespace DiagKit.Dbc.Workbook.Tests;

[TestClass]
public sealed class DbcWorkbookExporterTests
{
    private static readonly string[] ExpectedSheetNames =
    [
        "Network",
        "Nodes",
        "Messages",
        "Signals",
        "ValueDescriptions",
        "MultiplexRanges",
        "EnvironmentVariables",
        "AttributeDefinitions",
        "Attributes",
        "RelationAttributeDefinitions",
        "RelationAttributeDefaults",
        "RelationAttributes",
    ];

    private static readonly string[] ForbiddenWorkbookTexts =
    [
        "_Readme",
        "_Manifest",
        "DbcWorkbookStandaloneV1",
        "schema_version",
        "profile",
        "generator",
        "message_key",
        "signal_key",
        "node_key",
        "raw_id",
        "source_path",
        "source_sha256",
    ];

    [TestMethod]
    public void ExportDocument_CreatesOnlyDbcSemanticSheets()
    {
        var document = WorkbookTestDocuments.CreateEditableDocument();

        var result = DbcWorkbookExporter.ExportDocument(document);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var bytes = result.GetWorkbookBytesOrThrow();
        var workbook = WorkbookTestXlsx.Open(bytes);

        CollectionAssert.AreEqual(ExpectedSheetNames, workbook.SheetNames.ToArray());
        AssertWorkbookDoesNotContainLibraryMetadata(bytes);
        foreach (var sheetName in workbook.SheetNames)
        {
            Assert.AreEqual(0, workbook.GetSheet(sheetName).HiddenColumns.Count, sheetName);
        }

        var messages = workbook.GetSheet("Messages");
        Assert.AreEqual("message_name", messages.GetCell("A1"));
        Assert.AreEqual("can_id", messages.GetCell("B1"));
        Assert.AreEqual("id_format", messages.GetCell("C1"));
        Assert.AreEqual("VehicleStatus", messages.GetCell("A2"));
        Assert.AreEqual("8", messages.GetCell("D2"));

        var signals = workbook.GetSheet("Signals");
        Assert.AreEqual("message_name", signals.GetCell("A1"));
        Assert.AreEqual("signal_name", signals.GetCell("B1"));
        Assert.AreEqual("Speed", signals.GetCell("B2"));
        Assert.AreEqual("Intel", signals.GetCell("E2"));
        Assert.AreEqual("0.10000000000000001", signals.GetCell("G2"));
        Assert.AreEqual("km/h", signals.GetCell("K2"));
        Assert.AreEqual("Tester", signals.GetCell("L2"));
        Assert.AreEqual("multiplex_role", signals.GetCell("M1"));
        Assert.AreEqual("multiplex_switch_value", signals.GetCell("N1"));
        Assert.AreEqual("multiplexor_signal_name", signals.GetCell("O1"));
        Assert.AreEqual("initial_value", signals.GetCell("P1"));
        Assert.AreEqual("send_type", signals.GetCell("Q1"));
        Assert.AreEqual("timeout_ms", signals.GetCell("R1"));
        Assert.AreEqual("comment", signals.GetCell("S1"));
        Assert.AreEqual("vehicle speed", signals.GetCell("S2"));

        var descriptions = workbook.GetSheet("ValueDescriptions");
        Assert.AreEqual("raw_value", descriptions.GetCell("C1"));
        Assert.AreEqual("Stopped", descriptions.GetCell("D2"));
    }

    [TestMethod]
    public void ExportTemplate_CreatesStandaloneHeadersWithoutDataRows()
    {
        var result = DbcWorkbookExporter.ExportTemplate();

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var bytes = result.GetWorkbookBytesOrThrow();
        var workbook = WorkbookTestXlsx.Open(bytes);

        CollectionAssert.AreEqual(ExpectedSheetNames, workbook.SheetNames.ToArray());
        AssertWorkbookDoesNotContainLibraryMetadata(bytes);
        Assert.AreEqual("comment", workbook.GetSheet("Network").GetCell("A1"));
        Assert.AreEqual("message_name", workbook.GetSheet("Messages").GetCell("A1"));
        Assert.AreEqual(string.Empty, workbook.GetSheet("Messages").GetCell("A2"));
        Assert.AreEqual("owner_type", workbook.GetSheet("AttributeDefinitions").GetCell("A1"));
        Assert.AreEqual("environment_variable_name", workbook.GetSheet("Attributes").GetCell("E1"));
        Assert.AreEqual("attribute_name", workbook.GetSheet("Attributes").GetCell("F1"));
        Assert.AreEqual("relation_kind", workbook.GetSheet("RelationAttributeDefinitions").GetCell("A1"));
        Assert.AreEqual("target", workbook.GetSheet("RelationAttributes").GetCell("B1"));
    }

    [TestMethod]
    public void ExportDocument_UsesDeterministicZipEntryTimestamps()
    {
        var result = DbcWorkbookExporter.ExportDocument(WorkbookTestDocuments.CreateEditableDocument());

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        using var archive = new System.IO.Compression.ZipArchive(
            new MemoryStream(result.GetWorkbookBytesOrThrow()),
            System.IO.Compression.ZipArchiveMode.Read);
        var expectedTimestamp = new DateTime(1980, 1, 1, 0, 0, 0);
        foreach (var entry in archive.Entries)
        {
            Assert.AreEqual(expectedTimestamp, entry.LastWriteTime.DateTime, entry.FullName);
        }
    }

    [TestMethod]
    public void ExportDocument_ProjectsAdvancedDbcSemanticsToTables()
    {
        var document = WorkbookTestDocuments.CreateAdvancedDocument();

        var result = DbcWorkbookExporter.ExportDocument(document);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var workbook = WorkbookTestXlsx.Open(result.GetWorkbookBytesOrThrow());

        Assert.AreEqual("network comment", workbook.GetSheet("Network").GetCell("A2"));
        Assert.AreEqual("Multiplexor", workbook.GetSheet("Signals").GetCell("M2"));
        Assert.AreEqual("Multiplexed", workbook.GetSheet("Signals").GetCell("M3"));
        Assert.AreEqual("2", workbook.GetSheet("Signals").GetCell("N3"));
        Assert.AreEqual("Mode", workbook.GetSheet("Signals").GetCell("O3"));
        Assert.AreEqual("1", workbook.GetSheet("Signals").GetCell("P3"));
        Assert.AreEqual("Event", workbook.GetSheet("Signals").GetCell("Q3"));
        Assert.AreEqual("250", workbook.GetSheet("Signals").GetCell("R3"));
        Assert.AreEqual("MuxStatus", workbook.GetSheet("MultiplexRanges").GetCell("A2"));
        Assert.AreEqual("Speed", workbook.GetSheet("MultiplexRanges").GetCell("B2"));
        Assert.AreEqual("Mode", workbook.GetSheet("MultiplexRanges").GetCell("C2"));
        Assert.AreEqual("4", workbook.GetSheet("MultiplexRanges").GetCell("D2"));
        Assert.AreEqual("6", workbook.GetSheet("MultiplexRanges").GetCell("E2"));
        Assert.AreEqual("Ignition", workbook.GetSheet("EnvironmentVariables").GetCell("A2"));
        Assert.AreEqual("EnvKind", workbook.GetSheet("Attributes").GetCell("F2"));
        Assert.AreEqual("BU_SG_REL_", workbook.GetSheet("RelationAttributeDefinitions").GetCell("A2"));
        Assert.AreEqual("BU_SG_REL_ ECU 512 Speed", workbook.GetSheet("RelationAttributes").GetCell("B2"));
    }

    [TestMethod]
    public void ExportDocument_OmitsVectorIndependentPseudoMessageAndManagedAttributes()
    {
        var ecu = new DbcNode("ECU");
        var tester = new DbcNode("Tester");
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
            "",
            [tester],
            attributes: new Dictionary<string, DbcAttributeValue>
            {
                ["GenSigStartValue"] = new("GenSigStartValue", DbcAttributeValueKind.Integer, "5", 5),
            });
        var pseudoSignal = new DbcSignal("Independent", 0, 8, DbcByteOrder.Intel, DbcSignalValueType.Unsigned, 1, 0, 0, 255, "", []);
        var document = new DbcDocument(
            [ecu, tester],
            [
                new DbcMessage(new DbcRawMessageId(0x100), "Status", 8, ecu, [signal]),
                new DbcMessage(new DbcRawMessageId(0), "VECTOR__INDEPENDENT_SIG_MSG", 8, ecu, [pseudoSignal]),
            ],
            attributeDefinitions: new Dictionary<string, DbcAttributeDefinition>
            {
                ["GenSigStartValue"] = new("GenSigStartValue", DbcAttributeOwnerKind.Signal, DbcAttributeValueKind.Integer, minimum: 0, maximum: 255),
            });

        var result = DbcWorkbookExporter.ExportDocument(document);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var workbook = WorkbookTestXlsx.Open(result.GetWorkbookBytesOrThrow());
        Assert.AreEqual("Status", workbook.GetSheet("Messages").GetCell("A2"));
        Assert.AreEqual(string.Empty, workbook.GetSheet("Messages").GetCell("A3"));
        Assert.AreEqual(string.Empty, workbook.GetSheet("AttributeDefinitions").GetCell("B2"));
        Assert.AreEqual(string.Empty, workbook.GetSheet("Attributes").GetCell("F2"));
    }

    private static void AssertWorkbookDoesNotContainLibraryMetadata(byte[] bytes)
    {
        var text = WorkbookTestXlsx.ReadAllText(bytes);
        foreach (var forbidden in ForbiddenWorkbookTexts)
        {
            Assert.IsFalse(text.Contains(forbidden, StringComparison.Ordinal), forbidden);
        }
    }
}
