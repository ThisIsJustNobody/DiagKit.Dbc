namespace DiagKit.Dbc.Workbook.Tests;

[TestClass]
public sealed class DbcWorkbookImporterTests
{
    [TestMethod]
    public void ImportWorkbook_EditedSignalCells_UpdateDocument()
    {
        var document = WorkbookTestDocuments.CreateEditableDocument();
        var bytes = DbcWorkbookExporter.ExportDocument(document).GetWorkbookBytesOrThrow();
        var editedBytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Signals",
            new Dictionary<string, string>
            {
                ["G2"] = "0.125",
                ["H2"] = "-10",
                ["I2"] = "-10",
                ["J2"] = "500",
                ["K2"] = "rpm",
                ["S2"] = "edited speed",
            });

        var result = DbcWorkbookImporter.ImportWorkbook(editedBytes);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var signal = result.GetDocumentOrThrow().ResolveSignal("VehicleStatus", "Speed");
        Assert.AreEqual(0.125, signal.Factor);
        Assert.AreEqual(-10, signal.Offset);
        Assert.AreEqual(-10, signal.Minimum);
        Assert.AreEqual(500, signal.Maximum);
        Assert.AreEqual("rpm", signal.Unit);
        Assert.AreEqual("edited speed", signal.Comment);
    }

    [TestMethod]
    public void ImportWorkbook_EditedMessageCells_UpdateDocument()
    {
        var document = WorkbookTestDocuments.CreateEditableDocument();
        var bytes = DbcWorkbookExporter.ExportDocument(document).GetWorkbookBytesOrThrow();
        var editedBytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Messages",
            new Dictionary<string, string>
            {
                ["A2"] = "RenamedStatus",
                ["B2"] = "512",
                ["H2"] = "Event",
                ["J2"] = "edited message",
            });
        editedBytes = WorkbookTestXlsx.WithCells(
            editedBytes,
            "Signals",
            new Dictionary<string, string> { ["A2"] = "RenamedStatus" });
        editedBytes = WorkbookTestXlsx.WithCells(
            editedBytes,
            "ValueDescriptions",
            new Dictionary<string, string>
            {
                ["A2"] = "RenamedStatus",
                ["A3"] = "RenamedStatus",
            });

        var result = DbcWorkbookImporter.ImportWorkbook(editedBytes);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var message = result.GetDocumentOrThrow().ResolveMessage("RenamedStatus");
        Assert.AreEqual((uint)512, message.Identifier.Value);
        Assert.AreEqual(DbcSendType.Event, message.SendType);
        Assert.AreEqual("edited message", message.Comment);
    }

    [TestMethod]
    public void ImportWorkbook_EditedValueDescription_UpdateDocument()
    {
        var document = WorkbookTestDocuments.CreateEditableDocument();
        var bytes = DbcWorkbookExporter.ExportDocument(document).GetWorkbookBytesOrThrow();
        var editedBytes = WorkbookTestXlsx.WithCells(
            bytes,
            "ValueDescriptions",
            new Dictionary<string, string> { ["D2"] = "Idle" });

        var result = DbcWorkbookImporter.ImportWorkbook(editedBytes);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var signal = result.GetDocumentOrThrow().ResolveSignal("VehicleStatus", "Speed");
        Assert.AreEqual("Idle", signal.ValueDescriptions[0]);
    }

    [TestMethod]
    public void ImportWorkbook_ReservedEmptyNodeAndManagedAttributes_RemainWritable()
    {
        const string dbcText = """
            VERSION ""
            BU_: ECU

            BO_ 256 EmptyTx: 8 Vector__XXX
             SG_ Flag : 0|1@1+ (1,0) [0|1] "" Vector__XXX

            BA_DEF_ BO_  "GenMsgSendType" ENUM "NoMsgSendType","Cyclic","Event";
            BA_DEF_DEF_ "GenMsgSendType" "Cyclic";
            BA_DEF_ SG_  "GenSigSendType" ENUM "NoSigSendType","Cyclic","Event";
            BA_DEF_DEF_ "GenSigSendType" "NoSigSendType";
            BA_DEF_ BO_  "VFrameFormat" ENUM "StandardCAN","ExtendedCAN","reserved","J1939PG","reserved","reserved","reserved","reserved","reserved","reserved","reserved","reserved","reserved","reserved","StandardCAN_FD","ExtendedCAN_FD";
            BA_ "VFrameFormat" BO_ 256 1;
            """;
        var document = DbcLoader.LoadTextDocumentOrThrow(dbcText, DbcLoadOptions.Lenient);
        var bytes = DbcWorkbookExporter.ExportDocument(document).GetWorkbookBytesOrThrow();

        var result = DbcWorkbookImporter.ImportWorkbook(bytes);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var text = DbcWriter.WriteTextOrThrow(result.GetDocumentOrThrow());
        StringAssert.Contains(text, "Vector__XXX");
    }

    [TestMethod]
    public void ImportWorkbook_ControlCharactersInEditableText_AreSanitized()
    {
        var ecu = new DbcNode("ECU");
        var host = new DbcNode("HOST");
        var signal = new DbcSignal(
            "Flag",
            0,
            1,
            DbcByteOrder.Intel,
            DbcSignalValueType.Unsigned,
            1,
            0,
            0,
            1,
            "",
            [host],
            valueDescriptions: new Dictionary<long, string> { [0] = "Off\r\nValue" },
            comment: "flag\r\ncomment");
        var document = new DbcDocument(
            [ecu, host],
            [new DbcMessage(new DbcRawMessageId(0x100), "Status", 8, ecu, [signal], comment: "message\rcomment")]);
        var bytes = DbcWorkbookExporter.ExportDocument(document).GetWorkbookBytesOrThrow();

        var result = DbcWorkbookImporter.ImportWorkbook(bytes);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var imported = result.GetDocumentOrThrow();
        Assert.IsFalse(imported.ResolveMessage("Status").Comment!.Any(char.IsControl));
        Assert.IsFalse(imported.ResolveSignal("Status", "Flag").Comment!.Any(char.IsControl));
        Assert.IsFalse(imported.ResolveSignal("Status", "Flag").ValueDescriptions[0].Any(char.IsControl));
    }

    [TestMethod]
    public void ImportWorkbook_InvalidSignalNumber_ReturnsCellDiagnostic()
    {
        var document = WorkbookTestDocuments.CreateEditableDocument();
        var bytes = DbcWorkbookExporter.ExportDocument(document).GetWorkbookBytesOrThrow();
        var editedBytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Signals",
            new Dictionary<string, string> { ["G2"] = "not-a-number" });

        var result = DbcWorkbookImporter.ImportWorkbook(editedBytes);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WORKBOOK_INVALID_CELL"));
        StringAssert.Contains(result.Errors[0].Message, "Signals!G2");
    }

    [TestMethod]
    public void ImportWorkbook_InvalidEnumCells_ReturnCellDiagnostics()
    {
        var document = WorkbookTestDocuments.CreateEditableDocument();
        var bytes = DbcWorkbookExporter.ExportDocument(document).GetWorkbookBytesOrThrow();
        var editedBytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Messages",
            new Dictionary<string, string> { ["H2"] = "Cyclik" });
        editedBytes = WorkbookTestXlsx.WithCells(
            editedBytes,
            "Signals",
            new Dictionary<string, string>
            {
                ["E2"] = "Motorolla",
                ["F2"] = "Signedd",
                ["Q2"] = "Evnt",
            });

        var result = DbcWorkbookImporter.ImportWorkbook(editedBytes);

        Assert.IsFalse(result.Succeeded);
        var diagnostics = DbcDiagnosticFormatter.Format(result.Diagnostics);
        StringAssert.Contains(diagnostics, "Messages!H2");
        StringAssert.Contains(diagnostics, "Signals!E2");
        StringAssert.Contains(diagnostics, "Signals!F2");
        StringAssert.Contains(diagnostics, "Signals!Q2");
    }

    [TestMethod]
    public void ImportWorkbook_AutoCreatesReferencedNodes()
    {
        var document = WorkbookTestDocuments.CreateEditableDocument();
        var bytes = DbcWorkbookExporter.ExportDocument(document).GetWorkbookBytesOrThrow();
        var editedBytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Nodes",
            new Dictionary<string, string>
            {
                ["A3"] = string.Empty,
                ["B3"] = string.Empty,
            });

        var result = DbcWorkbookImporter.ImportWorkbook(editedBytes);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var imported = result.GetDocumentOrThrow();
        Assert.IsNotNull(imported.ResolveNode("ECU"));
        Assert.IsNotNull(imported.ResolveNode("Tester"));
    }

    [TestMethod]
    public void ImportWorkbook_TemplateRows_CreateStandaloneDocument()
    {
        var bytes = DbcWorkbookExporter.ExportTemplate().GetWorkbookBytesOrThrow();
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Messages",
            new Dictionary<string, string>
            {
                ["A2"] = "StandaloneStatus",
                ["B2"] = "0x123",
                ["C2"] = "Standard",
                ["D2"] = "8",
                ["E2"] = "FALSE",
                ["F2"] = "ECU",
                ["G2"] = "100",
                ["H2"] = "Cyclic",
                ["I2"] = "500",
                ["J2"] = "created from template",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Signals",
            new Dictionary<string, string>
            {
                ["A2"] = "StandaloneStatus",
                ["B2"] = "Speed",
                ["C2"] = "0",
                ["D2"] = "16",
                ["E2"] = "Intel",
                ["F2"] = "Unsigned",
                ["G2"] = "0.1",
                ["H2"] = "0",
                ["I2"] = "0",
                ["J2"] = "250",
                ["K2"] = "km/h",
                ["L2"] = "Display",
                ["S2"] = "speed signal",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "ValueDescriptions",
            new Dictionary<string, string>
            {
                ["A2"] = "StandaloneStatus",
                ["B2"] = "Speed",
                ["C2"] = "0",
                ["D2"] = "Stopped",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Nodes",
            new Dictionary<string, string>
            {
                ["A2"] = "ECU",
                ["B2"] = "main ECU",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "AttributeDefinitions",
            new Dictionary<string, string>
            {
                ["A2"] = "Message",
                ["B2"] = "CustomMessagePriority",
                ["C2"] = "Hex",
                ["D2"] = "0",
                ["E2"] = "100",
                ["G2"] = "10",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Attributes",
            new Dictionary<string, string>
            {
                ["A2"] = "Message",
                ["B2"] = "StandaloneStatus",
                ["F2"] = "CustomMessagePriority",
                ["G2"] = "42",
            });

        var result = DbcWorkbookImporter.ImportWorkbook(bytes);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var imported = result.GetDocumentOrThrow();
        var message = imported.ResolveMessage("StandaloneStatus");
        Assert.AreEqual((uint)0x123, message.Identifier.Value);
        Assert.AreEqual(100, message.CycleTimeMs);
        Assert.AreEqual(DbcSendType.Cyclic, message.SendType);
        Assert.AreEqual(500, message.TimeoutTimeMs);
        Assert.AreEqual("created from template", message.Comment);
        Assert.AreEqual("main ECU", imported.ResolveNode("ECU").Comment);
        Assert.IsNotNull(imported.ResolveNode("Display"));
        Assert.AreEqual("Stopped", imported.ResolveSignal("StandaloneStatus", "Speed").ValueDescriptions[0]);
        Assert.AreEqual("42", message.Attributes["CustomMessagePriority"].RawValue);
    }

    [TestMethod]
    public void ImportWorkbook_MinimalMessagesAndSignalsOnly_CreatesDocument()
    {
        var bytes = DbcWorkbookExporter.ExportTemplate().GetWorkbookBytesOrThrow();
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Messages",
            new Dictionary<string, string>
            {
                ["A2"] = "MinimalStatus",
                ["B2"] = "0x321",
                ["C2"] = "Standard",
                ["D2"] = "8",
                ["E2"] = "FALSE",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Signals",
            new Dictionary<string, string>
            {
                ["A2"] = "MinimalStatus",
                ["B2"] = "Flag",
                ["C2"] = "0",
                ["D2"] = "1",
                ["E2"] = "Intel",
                ["F2"] = "Unsigned",
                ["G2"] = "1",
                ["H2"] = "0",
                ["I2"] = "0",
                ["J2"] = "1",
            });
        bytes = WorkbookTestXlsx.WithOnlySheets(bytes, "Messages", "Signals");

        var result = DbcWorkbookImporter.ImportWorkbook(bytes);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var imported = result.GetDocumentOrThrow();
        Assert.AreEqual((uint)0x321, imported.ResolveMessage("MinimalStatus").Identifier.Value);
        Assert.AreEqual(1, imported.ResolveSignal("MinimalStatus", "Flag").BitLength);
    }

    [TestMethod]
    public void ImportWorkbook_DbcSemanticTables_CreateAdvancedDocument()
    {
        var bytes = DbcWorkbookExporter.ExportTemplate().GetWorkbookBytesOrThrow();
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Network",
            new Dictionary<string, string> { ["A2"] = "network comment" });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Nodes",
            new Dictionary<string, string>
            {
                ["A2"] = "ECU",
                ["B2"] = "main ECU",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Messages",
            new Dictionary<string, string>
            {
                ["A2"] = "StandaloneStatus",
                ["B2"] = "0x123",
                ["C2"] = "Standard",
                ["D2"] = "8",
                ["E2"] = "FALSE",
                ["F2"] = "ECU",
                ["G2"] = "100",
                ["H2"] = "Cyclic",
                ["I2"] = "500",
                ["J2"] = "created from template",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Signals",
            new Dictionary<string, string>
            {
                ["A2"] = "StandaloneStatus",
                ["B2"] = "Mode",
                ["C2"] = "0",
                ["D2"] = "4",
                ["E2"] = "Intel",
                ["F2"] = "Unsigned",
                ["G2"] = "1",
                ["H2"] = "0",
                ["I2"] = "0",
                ["J2"] = "15",
                ["L2"] = "Display",
                ["M2"] = "Multiplexor",
                ["S2"] = "mode signal",
                ["A3"] = "StandaloneStatus",
                ["B3"] = "Speed",
                ["C3"] = "8",
                ["D3"] = "16",
                ["E3"] = "Intel",
                ["F3"] = "Unsigned",
                ["G3"] = "0.1",
                ["H3"] = "0",
                ["I3"] = "0",
                ["J3"] = "250",
                ["K3"] = "km/h",
                ["L3"] = "Display",
                ["M3"] = "Multiplexed",
                ["N3"] = "2",
                ["O3"] = "Mode",
                ["P3"] = "1",
                ["Q3"] = "Event",
                ["R3"] = "250",
                ["S3"] = "speed signal",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "MultiplexRanges",
            new Dictionary<string, string>
            {
                ["A2"] = "StandaloneStatus",
                ["B2"] = "Speed",
                ["C2"] = "Mode",
                ["D2"] = "4",
                ["E2"] = "6",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "ValueDescriptions",
            new Dictionary<string, string>
            {
                ["A2"] = "StandaloneStatus",
                ["B2"] = "Mode",
                ["C2"] = "0",
                ["D2"] = "Off",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "EnvironmentVariables",
            new Dictionary<string, string>
            {
                ["A2"] = "Ignition",
                ["B2"] = "0",
                ["C2"] = "0",
                ["D2"] = "1",
                ["E2"] = "bool",
                ["F2"] = "0",
                ["G2"] = "1",
                ["H2"] = "DUMMY_NODE_VECTOR0",
                ["I2"] = "ECU",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "AttributeDefinitions",
            new Dictionary<string, string>
            {
                ["A2"] = "EnvironmentVariable",
                ["B2"] = "EnvKind",
                ["C2"] = "String",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Attributes",
            new Dictionary<string, string>
            {
                ["A2"] = "EnvironmentVariable",
                ["E2"] = "Ignition",
                ["F2"] = "EnvKind",
                ["G2"] = "Calibration",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "RelationAttributeDefinitions",
            new Dictionary<string, string>
            {
                ["A2"] = "BU_SG_REL_",
                ["B2"] = "GenSigTimeoutTime",
                ["C2"] = "Integer",
                ["D2"] = "0",
                ["E2"] = "1000",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "RelationAttributeDefaults",
            new Dictionary<string, string>
            {
                ["A2"] = "GenSigTimeoutTime",
                ["B2"] = "0",
            });
        bytes = WorkbookTestXlsx.WithCells(
            bytes,
            "RelationAttributes",
            new Dictionary<string, string>
            {
                ["A2"] = "GenSigTimeoutTime",
                ["B2"] = "BU_SG_REL_ ECU 291 Speed",
                ["C2"] = "100",
            });

        var result = DbcWorkbookImporter.ImportWorkbook(bytes);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var imported = result.GetDocumentOrThrow();
        Assert.AreEqual("network comment", imported.Comment);
        Assert.AreEqual("main ECU", imported.ResolveNode("ECU").Comment);
        var speed = imported.ResolveSignal("StandaloneStatus", "Speed");
        Assert.AreEqual(DbcMultiplexingRole.Multiplexed, speed.Multiplexing.Role);
        Assert.AreEqual(2, speed.Multiplexing.SwitchValue);
        Assert.AreEqual("Mode", speed.Multiplexing.MultiplexorSignalName);
        CollectionAssert.AreEqual(new[] { new DbcMultiplexorRange(4, 6) }, speed.Multiplexing.SwitchRanges.ToArray());
        Assert.AreEqual(1d, speed.InitialValue);
        Assert.AreEqual(DbcSendType.Event, speed.SendType);
        Assert.AreEqual(250, speed.TimeoutTimeMs);
        Assert.AreEqual("Off", imported.ResolveSignal("StandaloneStatus", "Mode").ValueDescriptions[0]);
        var ignition = imported.ResolveEnvironmentVariable("Ignition");
        Assert.AreEqual("bool", ignition.Unit);
        Assert.AreEqual("Calibration", ignition.Attributes["EnvKind"].Value);
        Assert.AreEqual("BU_SG_REL_", imported.RelationAttributeDefinitions["GenSigTimeoutTime"].RelationKind);
        Assert.AreEqual("0", imported.RelationAttributeDefaults["GenSigTimeoutTime"].RawValue);
        Assert.AreEqual("BU_SG_REL_ ECU 291 Speed", imported.RelationAttributes.Single().Target);
    }

    [TestMethod]
    public void ImportWorkbook_MultiplexRangesColumn_CanSupplyMultiplexorSignalName()
    {
        var document = WorkbookTestDocuments.CreateAdvancedDocument();
        var bytes = DbcWorkbookExporter.ExportDocument(document).GetWorkbookBytesOrThrow();
        var editedBytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Signals",
            new Dictionary<string, string> { ["O3"] = string.Empty });

        var result = DbcWorkbookImporter.ImportWorkbook(editedBytes);

        Assert.IsTrue(result.Succeeded, DbcDiagnosticFormatter.Format(result.Diagnostics));
        var speed = result.GetDocumentOrThrow().ResolveSignal("MuxStatus", "Speed");
        Assert.AreEqual("Mode", speed.Multiplexing.MultiplexorSignalName);
        CollectionAssert.AreEqual(new[] { new DbcMultiplexorRange(4, 6) }, speed.Multiplexing.SwitchRanges.ToArray());
    }

    [TestMethod]
    public void ImportWorkbook_MismatchedMultiplexRangeColumn_ReturnsCellDiagnostic()
    {
        var document = WorkbookTestDocuments.CreateAdvancedDocument();
        var bytes = DbcWorkbookExporter.ExportDocument(document).GetWorkbookBytesOrThrow();
        var editedBytes = WorkbookTestXlsx.WithCells(
            bytes,
            "MultiplexRanges",
            new Dictionary<string, string> { ["C2"] = "OtherMode" });

        var result = DbcWorkbookImporter.ImportWorkbook(editedBytes);

        Assert.IsFalse(result.Succeeded);
        var diagnostics = DbcDiagnosticFormatter.Format(result.Diagnostics);
        StringAssert.Contains(diagnostics, "MultiplexRanges!C2");
    }

    [TestMethod]
    public void DbcWorkbookImportOptions_DoesNotExposeUnimplementedMode()
    {
        Assert.IsNull(typeof(DbcWorkbookImportOptions).GetProperty("Mode"));
        Assert.IsNull(Type.GetType("DiagKit.Dbc.Workbook.DbcWorkbookImportMode, DiagKit.Dbc.Workbook"));
    }

    [TestMethod]
    public void ImportWorkbook_LegacyLibraryMetadataSheets_ReturnsUsageDiagnostic()
    {
        var bytes = DbcWorkbookExporter.ExportTemplate().GetWorkbookBytesOrThrow();
        if (!WorkbookTestXlsx.Open(bytes).SheetNames.Contains("_Manifest", StringComparer.Ordinal))
        {
            bytes = WorkbookTestXlsx.WithDuplicateSheet(bytes, "Messages", "_Manifest");
        }

        var result = DbcWorkbookImporter.ImportWorkbook(bytes);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WORKBOOK_LIBRARY_METADATA_SHEET"));
    }

    [TestMethod]
    public void ImportWorkbook_DuplicateMessageName_ReturnsDiagnostic()
    {
        var document = WorkbookTestDocuments.CreateTwoMessageDocument();
        var bytes = DbcWorkbookExporter.ExportDocument(document).GetWorkbookBytesOrThrow();
        var editedBytes = WorkbookTestXlsx.WithCells(bytes, "Messages", new Dictionary<string, string> { ["A3"] = "VehicleStatus" });

        var result = DbcWorkbookImporter.ImportWorkbook(editedBytes);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WORKBOOK_DUPLICATE_MESSAGE_NAME"));
    }

    [TestMethod]
    public void ImportWorkbook_UnknownAttributeDefinition_ReturnsCellDiagnostic()
    {
        var document = WorkbookTestDocuments.CreateEditableDocument();
        var bytes = DbcWorkbookExporter.ExportDocument(document).GetWorkbookBytesOrThrow();
        var editedBytes = WorkbookTestXlsx.WithCells(
            bytes,
            "Attributes",
            new Dictionary<string, string>
            {
                ["A2"] = "Message",
                ["B2"] = "VehicleStatus",
                ["F2"] = "MissingAttribute",
                ["G2"] = "1",
            });

        var result = DbcWorkbookImporter.ImportWorkbook(editedBytes);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "DBC_WORKBOOK_UNKNOWN_ATTRIBUTE_DEFINITION"));
        StringAssert.Contains(result.Errors[0].Message, "Attributes!F2");
    }
}
