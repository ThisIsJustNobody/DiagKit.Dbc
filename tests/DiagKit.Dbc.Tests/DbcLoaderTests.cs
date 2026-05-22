namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcLoaderTests
{
    [TestMethod]
    public void LoadResult_SucceededReflectsDocumentAndErrorDiagnostics()
    {
        var document = new DbcDocument([], []);
        var warnings = Enumerable.Range(0, 10_000)
            .Select(i => new DbcDiagnostic(DbcDiagnosticSeverity.Warning, $"WARN_{i}", "warning"))
            .ToArray();
        var warningResult = new DbcLoadResult(document, warnings);
        var errorResult = new DbcLoadResult(
            document,
            [.. warnings, new DbcDiagnostic(DbcDiagnosticSeverity.Error, "ERR", "error")]);
        var missingDocumentResult = new DbcLoadResult(null, warnings);

        Assert.IsTrue(warningResult.Succeeded);
        Assert.IsFalse(errorResult.Succeeded);
        Assert.IsFalse(missingDocumentResult.Succeeded);
    }

    [TestMethod]
    public void LoadText_LoadsNodesMessagesSignalsAttributesAndValueTables()
    {
        const string dbcText = """
            VERSION ""
            NS_ :
            BS_:
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ VehicleSpeed : 0|16@1+ (0.01,0) [0|250] "km/h" HOST
             SG_ Gear : 16|8@1+ (1,0) [0|15] "" HOST

            BA_DEF_ BO_  "GenMsgCycleTime" INT 0 100000;
            BA_ "GenMsgCycleTime" BO_ 256 10;
            CM_ BO_ 256 "status message";
            CM_ SG_ 256 VehicleSpeed "vehicle speed";
            VAL_ 256 Gear 0 "P" 1 "D";
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var document = result.GetDocumentOrThrow();
        var message = document.ResolveMessage(new CanIdentifier(0x100, CanIdFormat.Standard));
        var speed = document.ResolveSignal("VehicleStatus", "VehicleSpeed");
        var gear = document.ResolveSignal("VehicleStatus", "Gear");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("VehicleStatus", message.Name);
        Assert.AreEqual("VCU", message.PrimaryTransmitter.Name);
        Assert.AreEqual(8, message.DataLength);
        Assert.AreEqual(10, message.CycleTimeMs);
        Assert.AreEqual("status message", message.Comment);
        Assert.AreEqual("vehicle speed", speed.Comment);
        Assert.AreEqual(DbcByteOrder.Intel, speed.ByteOrder);
        Assert.AreEqual(DbcSignalValueType.Unsigned, speed.ValueType);
        Assert.AreEqual(0.01, speed.Factor);
        Assert.AreEqual("HOST", speed.Receivers.Single().Name);
        Assert.AreEqual("D", gear.ValueDescriptions[1]);
    }

    [TestMethod]
    public void LoadText_AppliesVectorLongSymbolsAsCanonicalNamesAndAliases()
    {
        const string dbcText = """
            VERSION ""
            BU_: MCU HOST

            BO_ 256 Msg_Short: 8 MCU
             SG_ Mode M : 0|8@1+ (1,0) [0|255] "" HOST
             SG_ Left_Demand_Limit_High_Positive_ m1 : 8|32@1+ (1,0) [0|1000] "rpm" HOST
            CM_ SG_ 256 Left_Demand_Limit_High_Positive_ "long signal comment";
            VAL_ 256 Left_Demand_Limit_High_Positive_ 1 "One";
            SIG_VALTYPE_ 256 Left_Demand_Limit_High_Positive_ : 1;
            SG_MUL_VAL_ 256 Left_Demand_Limit_High_Positive_ Mode 1-2;

            EV_ EnvShort : 0 [0|1] "bool" 0 1 DUMMY_NODE_VECTOR0 HOST;

            BA_DEF_ BU_ "SystemNodeLongSymbol" STRING ;
            BA_DEF_ BO_ "SystemMessageLongSymbol" STRING ;
            BA_DEF_ SG_ "SystemSignalLongSymbol" STRING ;
            BA_DEF_ EV_ "SystemEnvVarLongSymbol" STRING ;
            BA_DEF_ EV_ "EnvKind" STRING ;
            BA_ "SystemNodeLongSymbol" BU_ MCU "Motor_Control_Unit";
            BA_ "SystemNodeLongSymbol" BU_ HOST "Host_Controller";
            BA_ "SystemMessageLongSymbol" BO_ 256 "Vehicle_Status_Command_Message";
            BA_ "SystemSignalLongSymbol" SG_ 256 Mode "Operating_Mode_Value";
            BA_ "SystemSignalLongSymbol" SG_ 256 Left_Demand_Limit_High_Positive_ "Left_Demand_Limit_High_Positive_Value";
            BA_ "SystemEnvVarLongSymbol" EV_ EnvShort "Environment_Variable_Long_Name";
            BA_ "EnvKind" EV_ EnvShort "Calibration";
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var document = result.GetDocumentOrThrow();

        var node = document.ResolveNode("Motor_Control_Unit");
        Assert.AreEqual("Motor_Control_Unit", node.Name);
        Assert.AreEqual("MCU", node.SourceName);
        CollectionAssert.Contains(node.NameAliases.ToArray(), "MCU");
        Assert.AreSame(node, document.ResolveNode("MCU"));
        Assert.AreEqual(1, document.FindNodes("MCU").Count);
        var hostNode = document.ResolveNode("Host_Controller");
        Assert.AreSame(hostNode, document.ResolveNode("HOST"));

        var message = document.ResolveMessage("Vehicle_Status_Command_Message");
        Assert.AreEqual("Vehicle_Status_Command_Message", message.Name);
        Assert.AreEqual("Msg_Short", message.SourceName);
        CollectionAssert.Contains(message.NameAliases.ToArray(), "Msg_Short");
        Assert.AreSame(message, document.ResolveMessage("Msg_Short"));
        Assert.AreEqual("Motor_Control_Unit", message.PrimaryTransmitter.Name);
        Assert.AreEqual(1, message.Transmitters.Count);

        var mode = message.ResolveSignal("Operating_Mode_Value");
        Assert.AreEqual("Mode", mode.SourceName);
        CollectionAssert.Contains(mode.NameAliases.ToArray(), "Mode");
        Assert.AreSame(mode, message.ResolveSignal("Mode"));

        var signal = message.ResolveSignal("Left_Demand_Limit_High_Positive_Value");
        Assert.AreEqual("Left_Demand_Limit_High_Positive_Value", signal.Name);
        Assert.AreEqual("Left_Demand_Limit_High_Positive_", signal.SourceName);
        CollectionAssert.Contains(signal.NameAliases.ToArray(), "Left_Demand_Limit_High_Positive_");
        Assert.AreSame(signal, message.ResolveSignal("Left_Demand_Limit_High_Positive_"));
        Assert.AreEqual("long signal comment", signal.Comment);
        Assert.AreEqual(DbcSignalValueType.Float, signal.ValueType);
        Assert.AreEqual("One", signal.ValueDescriptions[1]);
        Assert.AreEqual("Host_Controller", signal.Receivers.Single().Name);

        Assert.IsTrue(document.EnvironmentVariables.ContainsKey("Environment_Variable_Long_Name"));
        var env = document.ResolveEnvironmentVariable("Environment_Variable_Long_Name");
        Assert.AreSame(env, document.ResolveEnvironmentVariable("EnvShort"));
        Assert.IsTrue(document.TryResolveEnvironmentVariable("EnvShort", out var envByAlias));
        Assert.AreSame(env, envByAlias);
        Assert.AreSame(env, document.FindEnvironmentVariables("EnvShort").Single());
        Assert.AreEqual("Environment_Variable_Long_Name", env.Name);
        Assert.AreEqual("EnvShort", env.SourceName);
        CollectionAssert.Contains(env.NameAliases.ToArray(), "EnvShort");
        Assert.AreEqual("Calibration", env.Attributes["EnvKind"].Value);

        var payload = new byte[8];
        Assert.IsTrue(message.TryEncodeSignal("Mode", payload, 3).Succeeded);
        var samples = new SignalSample[message.Signals.Count];
        message.Decode(payload, samples);
        Assert.AreEqual(
            SignalQuality.InactiveMultiplex,
            samples.Single(x => x.SignalName == "Left_Demand_Limit_High_Positive_Value").Quality);

        var simple = DbcSimpleRuntime.LoadText(dbcText, DbcLoadOptions.Strict);
        var snapshot = simple.GetSignalViewSnapshot("Msg_Short.Left_Demand_Limit_High_Positive_");
        Assert.AreEqual("Vehicle_Status_Command_Message", snapshot.MessageName);
        Assert.AreEqual("Left_Demand_Limit_High_Positive_Value", snapshot.SignalName);
        Assert.AreEqual(2, simple.GetSignalViewSnapshotsTransmittedBy("MCU").Count);
        Assert.AreEqual(2, simple.GetSignalViewSnapshotsReceivedBy("HOST").Count);

        var longMessageHandle = simple.RuntimeChannel.ResolveMessage("Vehicle_Status_Command_Message");
        var shortMessageHandle = simple.RuntimeChannel.ResolveMessage("Msg_Short");
        Assert.AreEqual(longMessageHandle, shortMessageHandle);
        Assert.AreEqual(
            1,
            simple.RuntimeChannel.RegisterPublishingMessagesTransmittedBy("MCU", TimeSpan.FromMilliseconds(10)).Entries.Count(x => x.Status == DbcPublishingRegistrationStatus.Registered));
        Assert.AreEqual(
            simple.RuntimeChannel.ResolveSignal(longMessageHandle, "Left_Demand_Limit_High_Positive_Value"),
            simple.RuntimeChannel.ResolveSignal(shortMessageHandle, "Left_Demand_Limit_High_Positive_"));
    }

    [TestMethod]
    public void LoadText_LongSymbolNameConflictsAreDiagnosedAndLookupFailsClosed()
    {
        const string dbcText = """
            VERSION ""
            BU_: ECU HOST

            EV_ FirstEnv : 0 [0|1] "bool" 0 1 DUMMY_NODE_VECTOR0 HOST;
            EV_ SecondEnv : 0 [0|1] "bool" 0 2 DUMMY_NODE_VECTOR0 HOST;
            BO_ 256 FirstStatus: 8 ECU
             SG_ FirstSignal : 0|8@1+ (1,0) [0|255] "" HOST
             SG_ FirstSignalB : 8|8@1+ (1,0) [0|255] "" HOST
            BO_ 257 SecondStatus: 8 ECU
             SG_ SecondSignal : 0|8@1+ (1,0) [0|255] "" HOST
            BA_DEF_ BO_ "SystemMessageLongSymbol" STRING ;
            BA_DEF_ SG_ "SystemSignalLongSymbol" STRING ;
            BA_DEF_ EV_ "SystemEnvVarLongSymbol" STRING ;
            BA_ "SystemMessageLongSymbol" BO_ 256 "Shared_Status";
            BA_ "SystemMessageLongSymbol" BO_ 257 "Shared_Status";
            BA_ "SystemSignalLongSymbol" SG_ 256 FirstSignal "Shared_Signal";
            BA_ "SystemSignalLongSymbol" SG_ 256 FirstSignalB "Shared_Signal";
            BA_ "SystemSignalLongSymbol" SG_ 257 SecondSignal "Shared_Signal";
            BA_ "SystemEnvVarLongSymbol" EV_ FirstEnv "Shared_Environment";
            BA_ "SystemEnvVarLongSymbol" EV_ SecondEnv "Shared_Environment";
            """;

        var lenient = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);
        Assert.IsTrue(lenient.Succeeded, string.Join(Environment.NewLine, lenient.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        Assert.IsTrue(lenient.Diagnostics.Any(x => x.Severity == DbcDiagnosticSeverity.Warning && x.Code == "DBC_NAME_ALIAS_AMBIGUOUS"));

        var document = lenient.GetDocumentOrThrow();
        Assert.AreEqual(2, document.FindMessages("Shared_Status").Count);
        Assert.IsFalse(document.TryResolveMessage("Shared_Status", out _));
        Assert.ThrowsExactly<DbcException>(() => document.ResolveMessage("Shared_Status"));
        Assert.AreEqual(2, document.FindEnvironmentVariables("Shared_Environment").Count);
        Assert.IsFalse(document.TryResolveEnvironmentVariable("Shared_Environment", out _));
        Assert.ThrowsExactly<DbcException>(() => document.ResolveEnvironmentVariable("Shared_Environment"));
        Assert.ThrowsExactly<DbcException>(() => document.ResolveEnvironmentVariable("MissingEnv"));
        Assert.AreSame(document.ResolveMessage("FirstStatus"), document.FindMessages("FirstStatus").Single());
        var first = document.ResolveMessage("FirstStatus");
        Assert.AreEqual(2, first.FindSignals("Shared_Signal").Count);
        Assert.IsFalse(first.TryResolveSignal("Shared_Signal", out _));
        Assert.ThrowsExactly<DbcException>(() => first.ResolveSignal("Shared_Signal"));

        var strict = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        Assert.IsFalse(strict.Succeeded);
        Assert.IsNull(strict.Document);
        Assert.IsTrue(strict.Diagnostics.Any(x => x.Severity == DbcDiagnosticSeverity.Error && x.Code == "DBC_NAME_ALIAS_AMBIGUOUS"));
    }

    [TestMethod]
    public void LoadText_MapsSignalValueTypeToFloat()
    {
        const string dbcText = """
            VERSION ""
            NS_ :
            BS_:
            BU_: ECU HOST

            BO_ 300 FloatMessage: 8 ECU
             SG_ Temperature : 0|32@1+ (1,0) [0|0] "degC" HOST
            SIG_VALTYPE_ 300 Temperature : 1;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var signal = result.GetDocumentOrThrow().ResolveSignal("FloatMessage", "Temperature");

        Assert.AreEqual(DbcSignalValueType.Float, signal.ValueType);
    }

    [TestMethod]
    public void LoadText_LoadsNamedValueTableReference()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            VAL_TABLE_ GearTable 0 "P" 1 "D" 2 "R";

            BO_ 256 VehicleStatus: 8 VCU
             SG_ Gear : 0|8@1+ (1,0) [0|15] "" HOST
            VAL_ 256 Gear GearTable;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var gear = result.GetDocumentOrThrow().ResolveSignal("VehicleStatus", "Gear");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("R", gear.ValueDescriptions[2]);
    }

    [TestMethod]
    public void LoadText_StrictFailsForMissingNamedValueTable()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ Gear : 0|8@1+ (1,0) [0|15] "" HOST
            VAL_ 256 Gear MissingTable;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Document);
        Assert.AreEqual("DBC_VALUE_TABLE_MISSING", result.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void LoadText_LenientWarnsForMissingNamedValueTable()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ Gear : 0|8@1+ (1,0) [0|15] "" HOST
            VAL_ 256 Gear MissingTable;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual(DbcDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.AreEqual("DBC_VALUE_TABLE_MISSING", diagnostic.Code);
    }

    [TestMethod]
    public void LoadText_LenientReportsNumericOverflowWithoutThrowing()
    {
        const string dbcText = """
            VERSION ""
            BU_: ECU HOST

            BO_ 999999999999999999999999999999999999999 OverflowMessage: 8 ECU
             SG_ Speed : 0|8@1+ (1,0) [0|255] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_NUMERIC_PARSE"));
    }

    [TestMethod]
    public void GetDocumentOrThrow_ThrowsWhenDiagnosticsContainErrors()
    {
        const string dbcText = """
            VERSION ""
            BU_: ECU HOST

            BO_ 999999999999999999999999999999999999999 Status: 8 ECU
             SG_ State : 0|8@1+ (1,0) [0|255] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(x => x.Severity == DbcDiagnosticSeverity.Error && x.Code == "DBC_NUMERIC_PARSE"));
        Assert.ThrowsExactly<DbcException>(() => result.GetDocumentOrThrow());
    }

    [TestMethod]
    public void LoadDocumentConvenienceApis_ReturnDocumentsAndFormattedErrors()
    {
        const string validDbc = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;
        const string invalidDbc = """
            VERSION ""
            BU_: ECU HOST

            BO_ 999999999999999999999999999999999999999 Status: 8 ECU
             SG_ State : 0|8@1+ (1,0) [0|255] "" HOST
            """;
        var validPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dbc");
        var invalidPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dbc");
        File.WriteAllText(validPath, validDbc);
        File.WriteAllText(invalidPath, invalidDbc);

        try
        {
            var document = DbcLoader.LoadDocument(validPath);
            var documentOrThrow = DbcLoader.LoadDocumentOrThrow(validPath);
            var textDocument = DbcLoader.LoadTextDocument(validDbc);
            var textDocumentOrThrow = DbcLoader.LoadTextDocumentOrThrow(validDbc);
            var exception = Assert.ThrowsExactly<DbcException>(() => DbcLoader.LoadDocumentOrThrow(invalidPath));

            Assert.AreEqual("VehicleStatus", document.ResolveMessage("VehicleStatus").Name);
            Assert.AreEqual("VehicleStatus", documentOrThrow.ResolveMessage("VehicleStatus").Name);
            Assert.AreEqual("VehicleStatus", textDocument.ResolveMessage("VehicleStatus").Name);
            Assert.AreEqual("VehicleStatus", textDocumentOrThrow.ResolveMessage("VehicleStatus").Name);
            StringAssert.Contains(exception.Message, "DBC diagnostics");
            StringAssert.Contains(exception.Message, "DBC_NUMERIC_PARSE");
        }
        finally
        {
            File.Delete(validPath);
            File.Delete(invalidPath);
        }
    }

    [TestMethod]
    public async Task LoadDocumentAsyncConvenienceApis_ReturnDocuments()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dbc");
        await File.WriteAllTextAsync(path, dbcText);

        try
        {
            var document = await DbcLoader.LoadDocumentAsync(path, DbcLoadOptions.Strict, CancellationToken.None);
            var documentOrThrow = await DbcLoader.LoadDocumentOrThrowAsync(path, DbcLoadOptions.Strict, CancellationToken.None);

            Assert.AreEqual("VehicleStatus", document.ResolveMessage("VehicleStatus").Name);
            Assert.AreEqual("VehicleStatus", documentOrThrow.ResolveMessage("VehicleStatus").Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadText_LoadsNetworkAttributeAndDefaultValue()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BA_DEF_ "ProjectName" STRING;
            BA_DEF_DEF_ "ProjectName" "DefaultProject";
            BA_ "ProjectName" "ActualProject";

            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var document = result.GetDocumentOrThrow();

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(DbcAttributeOwnerKind.Network, document.AttributeDefinitions["ProjectName"].OwnerKind);
        Assert.AreEqual("DefaultProject", document.AttributeDefinitions["ProjectName"].DefaultValue?.Value);
        Assert.AreEqual("ActualProject", document.Attributes["ProjectName"].Value);
    }

    [TestMethod]
    public void LoadText_PreservesSourceLinesForMessagesSignalsAndAttributes()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000;
            BA_ "GenMsgCycleTime" BO_ 256 10;
            """;

        var document = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict).GetDocumentOrThrow();
        var message = document.ResolveMessage("VehicleStatus");
        var signal = message.ResolveSignal("Speed");

        Assert.AreEqual(4, message.SourceLine);
        Assert.AreEqual(5, signal.SourceLine);
        Assert.AreEqual(6, document.AttributeDefinitions["GenMsgCycleTime"].SourceLine);
        Assert.AreEqual(7, message.Attributes["GenMsgCycleTime"].SourceLine);
    }

    [TestMethod]
    public void LoadText_LoadsMultipleSemicolonStatementsFromSameLine()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000; BA_ "GenMsgCycleTime" BO_ 256 10;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var message = result.GetDocumentOrThrow().ResolveMessage("VehicleStatus");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, result.Diagnostics.Count);
        Assert.AreEqual(10, message.CycleTimeMs);
        Assert.AreEqual(6, result.GetDocumentOrThrow().AttributeDefinitions["GenMsgCycleTime"].SourceLine);
        Assert.AreEqual(6, message.Attributes["GenMsgCycleTime"].SourceLine);
    }

    [TestMethod]
    public void LoadText_MapsVFrameFormatAttributeToCanFdFlags()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BA_DEF_ BO_ "VFrameFormat" ENUM "StandardCAN","ExtendedCAN","reserved","J1939PG","reserved","reserved","reserved","reserved","reserved","reserved","reserved","reserved","reserved","reserved","StandardCAN_FD","ExtendedCAN_FD";

            BO_ 256 FdStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            BA_ "VFrameFormat" BO_ 256 14;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var message = result.GetDocumentOrThrow().ResolveMessage("FdStatus");

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(message.IsCanFd);
        Assert.IsTrue(message.FrameFlags.HasFlag(DbcFrameFlags.FlexibleDataRate));
        Assert.AreEqual("14", message.Attributes["VFrameFormat"].RawValue);
        Assert.AreEqual("StandardCAN_FD", message.Attributes["VFrameFormat"].Value);
    }

    [TestMethod]
    public void LoadText_MapsMessageSendTypeAndTimeoutSemantics()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BA_DEF_ BO_ "GenMsgSendType" ENUM "cyclic","reserved","cyclicIfActive","cyclicAndEvent","noMsgSendType";
            BA_DEF_ BO_ "GenMsgTimeoutTime" INT 0 100000;
            BA_DEF_DEF_ "GenMsgSendType" "cyclic";
            BA_DEF_DEF_ "GenMsgTimeoutTime" 500;

            BO_ 256 PeriodicStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            BO_ 257 ActiveStatus: 8 VCU
             SG_ Mode : 0|8@1+ (1,0) [0|255] "" HOST
            BO_ 258 ReservedStatus: 8 VCU
             SG_ ReservedMode : 0|8@1+ (1,0) [0|255] "" HOST
            BA_ "GenMsgSendType" BO_ 257 2;
            BA_ "GenMsgTimeoutTime" BO_ 257 1250;
            BA_ "GenMsgSendType" BO_ 258 1;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var document = result.GetDocumentOrThrow();
        var periodic = document.ResolveMessage("PeriodicStatus");
        var active = document.ResolveMessage("ActiveStatus");
        var reserved = document.ResolveMessage("ReservedStatus");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(DbcSendType.Cyclic, periodic.SendType);
        Assert.AreEqual(500, periodic.TimeoutTimeMs);
        Assert.AreEqual(DbcSendType.CyclicIfActive, active.SendType);
        Assert.AreEqual(1250, active.TimeoutTimeMs);
        Assert.AreEqual(DbcSendType.Unknown, reserved.SendType);
        Assert.AreEqual("2", active.Attributes["GenMsgSendType"].RawValue);
        Assert.AreEqual("cyclicIfActive", active.Attributes["GenMsgSendType"].Value);
    }

    [TestMethod]
    public void LoadText_MapsSignalSendTypeAndTimeoutSemantics()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BA_DEF_ SG_ "GenSigSendType" ENUM "cyclic","onWrite","onChange","onChangeWithRepetition","noSigSendType";
            BA_DEF_ SG_ "GenSigTimeoutTime" INT 0 100000;
            BA_DEF_DEF_ "GenSigSendType" "noSigSendType";
            BA_DEF_DEF_ "GenSigTimeoutTime" 250;

            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
             SG_ Alive : 16|8@1+ (1,0) [0|255] "" HOST
            BA_ "GenSigSendType" SG_ 256 Speed 3;
            BA_ "GenSigTimeoutTime" SG_ 256 Speed 1000;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var message = result.GetDocumentOrThrow().ResolveMessage("VehicleStatus");
        var speed = message.ResolveSignal("Speed");
        var alive = message.ResolveSignal("Alive");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(DbcSendType.OnChangeWithRepetition, speed.SendType);
        Assert.AreEqual(1000, speed.TimeoutTimeMs);
        Assert.AreEqual(DbcSendType.None, alive.SendType);
        Assert.AreEqual(250, alive.TimeoutTimeMs);
        Assert.AreEqual("3", speed.Attributes["GenSigSendType"].RawValue);
        Assert.AreEqual("onChangeWithRepetition", speed.Attributes["GenSigSendType"].Value);
    }

    [TestMethod]
    public void LoadText_LoadsRegularMultiplexing()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 MuxStatus: 8 VCU
             SG_ Mode M : 0|4@1+ (1,0) [0|15] "" HOST
             SG_ Speed m1 : 8|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var message = result.GetDocumentOrThrow().ResolveMessage("MuxStatus");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(DbcMultiplexingRole.Multiplexor, message.ResolveSignal("Mode").Multiplexing.Role);
        Assert.AreEqual(DbcMultiplexingRole.Multiplexed, message.ResolveSignal("Speed").Multiplexing.Role);
        Assert.AreEqual(1, message.ResolveSignal("Speed").Multiplexing.SwitchValue);
    }

    [TestMethod]
    public void LoadText_DoesNotTreatNamespaceKeywordsAsMessagesOrSignals()
    {
        const string dbcText = """
            VERSION ""

            NS_ :
                BO_TX_BU_
                SG_MUL_VAL_
                BA_DEF_
                BA_
                VAL_
                SIG_VALTYPE_

            BS_:
            BU_: HOST ECU

            BO_ 256 Status: 8 ECU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, result.Diagnostics.Count);
        Assert.AreEqual(1, result.GetDocumentOrThrow().Messages.Count);
    }

    [TestMethod]
    public void LoadText_ReportsBareNamespaceEntriesOutsideNamespaceList()
    {
        const string dbcText = """
            VERSION ""
            BA_DEF_
            BA_
            VAL_
            SIG_VALTYPE_
            BO_TX_BU_
            SG_MUL_VAL_
            BU_: HOST ECU
            BO_ 256 Status: 8 ECU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_ATTRIBUTE_DEFINITION_SYNTAX"));
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_ATTRIBUTE_VALUE_SYNTAX"));
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_VALUE_DESCRIPTION_SYNTAX"));
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_SIGNAL_VALUE_TYPE_SYNTAX"));
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_NAMESPACE_ENTRY_OUTSIDE_NAMESPACE"));
        Assert.AreEqual(1, result.Document.Messages.Count);
    }

    [TestMethod]
    public void LoadText_DoesNotEnterNamespaceListForMalformedNamespaceHeaders()
    {
        const string dbcText = """
            VERSION ""
            NS_DESC_:
            BA_DEF_
            NS_BROKEN:
            VAL_
            NS_ invalid:
            SIG_VALTYPE_
            BU_: HOST ECU
            BO_ 256 Status: 8 ECU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        Assert.AreEqual(3, result.Diagnostics.Count(x => x.Code == "DBC_NAMESPACE_SYNTAX"));
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_ATTRIBUTE_DEFINITION_SYNTAX"));
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_VALUE_DESCRIPTION_SYNTAX"));
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_SIGNAL_VALUE_TYPE_SYNTAX"));
        Assert.AreEqual(1, result.Document.Messages.Count);
    }

    [TestMethod]
    public void LoadText_LoadsExtendedMultiplexingRangeActivation()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 MuxStatus: 8 VCU
             SG_ Mode M : 0|4@1+ (1,0) [0|15] "" HOST
             SG_ Speed m1 : 8|16@1+ (1,0) [0|65535] "" HOST
            SG_MUL_VAL_ 256 Speed Mode 1-3;
            SG_MUL_VAL_ 256 Speed Mode 5-7;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var speed = result.GetDocumentOrThrow().ResolveMessage("MuxStatus").ResolveSignal("Speed");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, result.Diagnostics.Count);
        Assert.AreEqual(DbcMultiplexingRole.Multiplexed, speed.Multiplexing.Role);
        Assert.AreEqual(1, speed.Multiplexing.SwitchValue);
        Assert.AreEqual("Mode", speed.Multiplexing.MultiplexorSignalName);
        CollectionAssert.AreEqual(
            new[] { new DbcMultiplexorRange(1, 3), new DbcMultiplexorRange(5, 7) },
            speed.Multiplexing.SwitchRanges.ToArray());
    }

    [TestMethod]
    public void LoadText_MergesExtendedMultiplexingRangesForSameSignal()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 MuxStatus: 8 VCU
             SG_ Mode M : 0|4@1+ (1,0) [0|15] "" HOST
             SG_ Speed : 8|16@1+ (1,0) [0|65535] "" HOST
            SG_MUL_VAL_ 256 Speed Mode 5-7;
            SG_MUL_VAL_ 256 Speed Mode 2-3;
            SG_MUL_VAL_ 256 Speed Mode 3-5;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var speed = result.GetDocumentOrThrow().ResolveMessage("MuxStatus").ResolveSignal("Speed");

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(
            new[] { new DbcMultiplexorRange(2, 7) },
            speed.Multiplexing.SwitchRanges.ToArray());
    }

    [TestMethod]
    public void LoadText_ReportsExtendedMultiplexingMissingReferencesByMode()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 MuxStatus: 8 VCU
             SG_ Mode M : 0|4@1+ (1,0) [0|15] "" HOST
             SG_ Speed : 8|16@1+ (1,0) [0|65535] "" HOST
            SG_MUL_VAL_ 256 Missing Mode 1-3;
            SG_MUL_VAL_ 256 Speed MissingMode 1-3;
            """;

        var strict = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var lenient = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsFalse(strict.Succeeded);
        Assert.IsNull(strict.Document);
        Assert.AreEqual(2, strict.Diagnostics.Count(x => x.Code == "DBC_EXTENDED_MULTIPLEXING_REFERENCE_MISSING"));
        Assert.IsTrue(strict.Diagnostics.Where(x => x.Code == "DBC_EXTENDED_MULTIPLEXING_REFERENCE_MISSING").All(x => x.Severity == DbcDiagnosticSeverity.Error));
        Assert.IsTrue(lenient.Succeeded);
        Assert.AreEqual(2, lenient.Diagnostics.Count(x => x.Code == "DBC_EXTENDED_MULTIPLEXING_REFERENCE_MISSING"));
        Assert.IsTrue(lenient.Diagnostics.Where(x => x.Code == "DBC_EXTENDED_MULTIPLEXING_REFERENCE_MISSING").All(x => x.Severity == DbcDiagnosticSeverity.Warning));
        Assert.AreEqual(0, lenient.GetDocumentOrThrow().ResolveMessage("MuxStatus").ResolveSignal("Speed").Multiplexing.SwitchRanges.Count);
    }

    [TestMethod]
    public void LoadText_WarnsAndSkipsNestedExtendedMultiplexing()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 MuxStatus: 8 VCU
             SG_ PrimaryMode M : 0|4@1+ (1,0) [0|15] "" HOST
             SG_ SecondaryMode m1M : 4|4@1+ (1,0) [0|15] "" HOST
             SG_ Speed : 8|16@1+ (1,0) [0|65535] "" HOST
            SG_MUL_VAL_ 256 Speed SecondaryMode 1-3;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var speed = result.GetDocumentOrThrow().ResolveMessage("MuxStatus").ResolveSignal("Speed");

        Assert.IsTrue(result.Succeeded);
        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual(DbcDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.AreEqual("DBC_EXTENDED_MULTIPLEXING_UNSUPPORTED", diagnostic.Code);
        Assert.AreEqual(0, speed.Multiplexing.SwitchRanges.Count);
    }

    [TestMethod]
    public void LoadText_LoadsDocumentLegacyMessageAndMultilineSignalComments()
    {
        const string dbcText = """
            VERSION ""
            NS_ :
            BS_:
            BU_: ECU HOST

            BO_ 256 Status: 8 ECU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST

            CM_ "network comment";
            CM_ 256 "legacy message comment";
            CM_ SG_ 256 Speed "line 1
            line 2";
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var document = result.GetDocumentOrThrow();
        var message = document.ResolveMessage("Status");
        var signal = message.ResolveSignal("Speed");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, result.Diagnostics.Count);
        Assert.AreEqual("network comment", document.Comment);
        Assert.AreEqual("legacy message comment", message.Comment);
        StringAssert.Contains(signal.Comment, "line 1");
        StringAssert.Contains(signal.Comment, "line 2");
    }

    [TestMethod]
    public void LoadText_LoadsStatementAfterMultilineQuotedStatement()
    {
        const string dbcText = """
            VERSION ""
            BU_: ECU HOST

            BO_ 256 Status: 8 ECU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000;
            CM_ SG_ 256 Speed "line 1
            line 2"; BA_ "GenMsgCycleTime" BO_ 256 10;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var document = result.GetDocumentOrThrow();
        var message = document.ResolveMessage("Status");
        var signal = message.ResolveSignal("Speed");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, result.Diagnostics.Count);
        StringAssert.Contains(signal.Comment, "line 1");
        StringAssert.Contains(signal.Comment, "line 2");
        Assert.AreEqual(10, message.CycleTimeMs);
        Assert.AreEqual(8, message.Attributes["GenMsgCycleTime"].SourceLine);
    }

    [TestMethod]
    public void LoadText_ParsesEscapedQuotedTextWithoutBreakingStatements()
    {
        const string dbcText = """
            VERSION ""
            BU_: ECU HOST

            BA_DEF_ "ProjectName" STRING;
            BA_ "ProjectName" "Alpha \"Beta\"";
            BO_ 256 Status: 8 ECU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "km\"/h" HOST
            CM_ SG_ 256 Speed "line \"quoted\"; still comment";
            VAL_ 256 Speed 1 "one \"quoted\"; value";
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);
        var document = result.GetDocumentOrThrow();
        var signal = document.ResolveMessage("Status").ResolveSignal("Speed");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, result.Diagnostics.Count);
        Assert.AreEqual("Alpha \"Beta\"", document.Attributes["ProjectName"].Value);
        Assert.AreEqual("km\"/h", signal.Unit);
        Assert.AreEqual("line \"quoted\"; still comment", signal.Comment);
        Assert.AreEqual("one \"quoted\"; value", signal.ValueDescriptions[1]);
    }

    [TestMethod]
    public void LoadText_StrictFailsForInvalidMessageSyntax()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ invalid
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Document);
        Assert.AreEqual("DBC_MESSAGE_SYNTAX", result.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void LoadText_LenientWarnsForInvalidMessageSyntaxAndKeepsValidMessages()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ invalid
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual(DbcDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.AreEqual("DBC_MESSAGE_SYNTAX", diagnostic.Code);
        Assert.AreEqual(3, diagnostic.LineNumber);
        Assert.AreEqual(1, result.Document.Messages.Count);
        Assert.AreEqual("VehicleStatus", result.Document.ResolveMessage("VehicleStatus").Name);
    }

    [TestMethod]
    public void LoadText_LenientDoesNotAttachSignalsAfterInvalidMessageToPreviousMessage()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 ExistingStatus: 8 VCU
             SG_ ExistingSpeed : 0|16@1+ (1,0) [0|65535] "" HOST
            BO_ invalid
             SG_ OrphanSpeed : 16|16@1+ (1,0) [0|65535] "" HOST
            BO_ 257 FollowingStatus: 8 VCU
             SG_ FollowingSpeed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        CollectionAssert.AreEqual(
            new[] { "DBC_MESSAGE_SYNTAX", "DBC_SIGNAL_WITHOUT_MESSAGE" },
            result.Diagnostics.Select(x => x.Code).ToArray());
        var existing = result.Document.ResolveMessage("ExistingStatus");
        var following = result.Document.ResolveMessage("FollowingStatus");
        Assert.AreEqual(1, existing.Signals.Count);
        Assert.AreEqual("ExistingSpeed", existing.Signals.Single().Name);
        Assert.AreEqual(1, following.Signals.Count);
        Assert.AreEqual("FollowingSpeed", following.Signals.Single().Name);
    }

    [TestMethod]
    public void LoadText_LenientPreservesTransportPayloadMessageAsRuntimeUnsupported()
    {
        const string dbcText = """
            VERSION ""
            BU_: ECU TESTER
            BA_DEF_ "ProtocolType" STRING;
            BA_ "ProtocolType" "J1939";
            BO_ 2364539904 LargePG: 1785 ECU
             SG_ FirstByte : 0|8@1+ (1,0) [0|255] "" TESTER
             SG_ BeyondCanFd : 512|16@1+ (1,0) [0|65535] "" TESTER
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Severity} {x.Code}: {x.Message}")));
        Assert.IsTrue(result.Diagnostics.Any(x => x.Severity == DbcDiagnosticSeverity.Warning && x.Code == "DBC_MESSAGE_RUNTIME_UNSUPPORTED"));
        var message = result.GetDocumentOrThrow().ResolveMessage("LargePG");
        Assert.AreEqual(1785, message.DataLength);
        Assert.IsFalse(message.SupportsSingleFrameRuntime);
        Assert.AreEqual(2, message.Signals.Count);
        Assert.AreEqual(512, message.ResolveSignal("BeyondCanFd").StartBit);
    }

    [TestMethod]
    public void LoadText_LenientWarnsForInvalidSignalSyntaxAndKeepsValidSignals()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ invalid
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual(DbcDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.AreEqual("DBC_SIGNAL_SYNTAX", diagnostic.Code);
        Assert.AreEqual(4, diagnostic.LineNumber);
        var message = result.Document.ResolveMessage("VehicleStatus");
        Assert.AreEqual(1, message.Signals.Count);
        Assert.AreEqual("Speed", message.ResolveSignal("Speed").Name);
    }

    [TestMethod]
    public void LoadText_LenientWarnsForSignalWithoutMessageAndKeepsFollowingMessages()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            SG_ Orphan : 0|8@1+ (1,0) [0|1] "" HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual(DbcDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.AreEqual("DBC_SIGNAL_WITHOUT_MESSAGE", diagnostic.Code);
        Assert.AreEqual(3, diagnostic.LineNumber);
        var message = result.Document.ResolveMessage("VehicleStatus");
        Assert.AreEqual(1, message.Signals.Count);
        Assert.AreEqual("Speed", message.ResolveSignal("Speed").Name);
    }

    [TestMethod]
    public void LoadText_ReportsSignalWithoutMessage()
    {
        const string dbcText = """
            VERSION ""
            BU_: HOST
            SG_ Orphan : 0|8@1+ (1,0) [0|1] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Document);
        Assert.AreEqual("DBC_SIGNAL_WITHOUT_MESSAGE", result.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void LoadText_ReportsDuplicateMessageName()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            BO_ 257 VehicleStatus: 8 VCU
             SG_ Speed2 : 0|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_DUPLICATE_MESSAGE_NAME"));
    }

    [TestMethod]
    public void LoadText_ReportsDuplicateNormalizedCanIdentifier()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 2048 ExtendedStatusA: 8 VCU
             SG_ SpeedA : 0|16@1+ (1,0) [0|65535] "" HOST
            BO_ 2147485696 ExtendedStatusB: 8 VCU
             SG_ SpeedB : 0|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Document);
        var diagnostic = result.Diagnostics.Single(x => x.Code == "DBC_DUPLICATE_CAN_IDENTIFIER");
        Assert.AreEqual(DbcDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual(6, diagnostic.LineNumber);
        StringAssert.Contains(diagnostic.Message, "0x800x");
        StringAssert.Contains(diagnostic.Message, "2048");
        StringAssert.Contains(diagnostic.Message, "2147485696");
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_SIGNAL_WITHOUT_MESSAGE"));
    }

    [TestMethod]
    public void LoadText_ReportsDuplicateSignalName()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
             SG_ Speed : 16|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_DUPLICATE_SIGNAL_NAME"));
    }

    [TestMethod]
    public void LoadText_LenientWarnsForUnknownAttributeDefinition()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            BA_ "UnknownAttribute" BO_ 256 10;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual(DbcDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.AreEqual("DBC_ATTRIBUTE_DEFINITION_MISSING", diagnostic.Code);
    }

    [TestMethod]
    public void LoadText_StrictFailsForUnknownAttributeDefinition()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            BA_ "UnknownAttribute" BO_ 256 10;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Document);
        Assert.AreEqual("DBC_ATTRIBUTE_DEFINITION_MISSING", result.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void LoadText_LenientSkipsDuplicateNormalizedCanIdentifierSignals()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST

            BO_ 2048 ExtendedStatusA: 8 VCU
             SG_ SpeedA : 0|16@1+ (1,0) [0|65535] "" HOST
            BO_ 2147485696 ExtendedStatusB: 8 VCU
             SG_ SpeedB : 16|16@1+ (1,0) [0|65535] "" HOST
            BO_ 257 FollowingStatus: 8 VCU
             SG_ Alive : 0|8@1+ (1,0) [0|255] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsNotNull(result.Document);
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_DUPLICATE_CAN_IDENTIFIER"));
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_SIGNAL_WITHOUT_MESSAGE"));
        Assert.AreEqual(2, result.Document.Messages.Count);

        var first = result.Document.ResolveMessage("ExtendedStatusA");
        Assert.AreEqual(1, first.Signals.Count);
        Assert.AreEqual("SpeedA", first.Signals[0].Name);
        Assert.IsFalse(result.Document.TryResolveMessage("ExtendedStatusB", out _));
        Assert.AreEqual("Alive", result.Document.ResolveMessage("FollowingStatus").Signals.Single().Name);
    }

    [TestMethod]
    public void LoadText_LenientReportsTooLongStatementAndRecoversAtNextBoundary()
    {
        var dbcText = $"""
            VERSION ""
            CM_ "{new string('x', 96)}
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;
        var options = new DbcLoadOptions(DbcLoadMode.Lenient)
        {
            MaxStatementLength = 64,
        };

        var result = DbcLoader.LoadText(dbcText, options);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_STATEMENT_TOO_LONG"));
        Assert.IsTrue(result.Document.TryResolveNode("VCU", out _));
        Assert.AreEqual("Speed", result.Document.ResolveMessage("VehicleStatus").Signals.Single().Name);
    }

    [TestMethod]
    public void LoadText_LenientReportsMalformedKnownSignalKeyword()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
            SG_Bad : 0|8@1+ (1,0) [0|1] "" HOST
            SG_ 1Invalid : 8|8@1+ (1,0) [0|1] "" HOST
             SG_ Good : 16|8@1+ (1,0) [0|1] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        Assert.AreEqual(2, result.Diagnostics.Count(x => x.Code == "DBC_SIGNAL_SYNTAX"));
        Assert.AreEqual(1, result.Document.ResolveMessage("VehicleStatus").Signals.Count);
    }

    [TestMethod]
    public void LoadText_LenientReportsMalformedKnownAttributeKeyword()
    {
        const string dbcText = """
            VERSION ""
            BA_ "BrokenAttribute" BO_;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        Assert.AreEqual("DBC_ATTRIBUTE_VALUE_SYNTAX", result.Diagnostics.Single().Code);
    }

    [TestMethod]
    public void LoadText_LenientKeepsDocumentUsableWhenMessageNameIsDuplicated()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            BO_ 257 VehicleStatus: 8 VCU
             SG_ Current : 0|16@1+ (1,0) [0|65535] "" HOST
            BO_ 258 FollowingStatus: 8 VCU
             SG_ Alive : 0|8@1+ (1,0) [0|255] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_DUPLICATE_MESSAGE_NAME"));
        Assert.IsFalse(result.Diagnostics.Any(x => x.Code == "DBC_BUILD_FAILED"));
        Assert.AreEqual(2, result.Document.Messages.Count);
        Assert.AreEqual("Speed", result.GetDocumentOrThrow().ResolveMessage("VehicleStatus").Signals.Single().Name);
        Assert.AreEqual("Alive", result.GetDocumentOrThrow().ResolveMessage("FollowingStatus").Signals.Single().Name);
    }

    [TestMethod]
    public void LoadText_LenientReportsDuplicateAttributeDefinitionAndKeepsFirstDefinition()
    {
        const string dbcText = """
            VERSION ""
            BA_DEF_ "ProjectName" STRING;
            BA_DEF_ "ProjectName" INT 0 100;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        Assert.AreEqual("DBC_DUPLICATE_ATTRIBUTE_DEFINITION", result.Diagnostics.Single().Code);
        Assert.AreEqual(DbcAttributeValueKind.String, result.Document.AttributeDefinitions["ProjectName"].ValueKind);
    }

    [TestMethod]
    public void LoadText_LenientReportsEmptyNodeList()
    {
        const string dbcText = """
            VERSION ""
            BU_:
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_NODE_LIST_EMPTY"));
    }

    [TestMethod]
    public void LoadText_LenientRejectsSignalNamesStartingWithDigits()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ 1Speed : 0|16@1+ (1,0) [0|65535] "" HOST
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Document);
        Assert.AreEqual("DBC_SIGNAL_SYNTAX", result.Diagnostics.Single().Code);
        Assert.AreEqual("Speed", result.Document.ResolveMessage("VehicleStatus").Signals.Single().Name);
    }

    [TestMethod]
    public void LoadText_AppliesForwardAttributeDefaultsAndValuesAfterDefinition()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            BA_DEF_DEF_ "GenMsgCycleTime" 20;
            BA_ "GenMsgCycleTime" BO_ 256 10;
            BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        var document = result.GetDocumentOrThrow();
        Assert.AreEqual(20, document.AttributeDefinitions["GenMsgCycleTime"].DefaultValue?.Value);
        Assert.AreEqual(10, document.ResolveMessage("VehicleStatus").CycleTimeMs);
        Assert.AreEqual(10, document.ResolveMessage("VehicleStatus").Attributes["GenMsgCycleTime"].Value);
    }

    [TestMethod]
    public void LoadText_ParsesVectorAttributeNamesAndWideIntegerValues()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|32@1+ (1,0) [0|4294967295] "" HOST
            BA_DEF_ SG_ "NWM-WakeupAllowed" ENUM "No","Yes";
            BA_DEF_ SG_ "GenSigStartValue" INT -9223372036854775808 9223372036854775807;
            BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000;
            BA_ "NWM-WakeupAllowed" SG_ 256 Speed "Yes";
            BA_ "GenSigStartValue" SG_ 256 Speed 4294967171;
            BA_ "GenMsgCycleTime" BO_ 256 50.000;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        var document = result.GetDocumentOrThrow();
        var signal = document.ResolveSignal("VehicleStatus", "Speed");
        Assert.AreEqual("Yes", signal.Attributes["NWM-WakeupAllowed"].Value);
        Assert.AreEqual(4294967171L, signal.Attributes["GenSigStartValue"].Value);
        Assert.AreEqual(4294967171d, signal.InitialValue);
        Assert.AreEqual(50, document.ResolveMessage("VehicleStatus").CycleTimeMs);
    }

    [TestMethod]
    public void LoadText_ParsesFractionalGenSigStartValueFromHexAttribute()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ FilterState_Min_Y : 0|16@1+ (0.5,0) [0|2047.5] "" HOST
            BA_DEF_ SG_ "GenSigStartValue" HEX 0 4294967295;
            BA_ "GenSigStartValue" SG_ 256 FilterState_Min_Y 2047.5;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        var signal = result.GetDocumentOrThrow().ResolveSignal("VehicleStatus", "FilterState_Min_Y");
        Assert.AreEqual(2047.5d, signal.Attributes["GenSigStartValue"].Value);
        Assert.AreEqual(2047.5d, signal.InitialValue);
    }

    [TestMethod]
    public void LoadText_LenientReportsNonIntegralIntegerAttributeValue()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|8@1+ (1,0) [0|255] "" HOST
            BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000;
            BA_ "GenMsgCycleTime" BO_ 256 1.5E+1;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_NUMERIC_PARSE"));
        Assert.IsNull(result.Document);
    }

    [TestMethod]
    public void LoadText_ParsesMultilineValueTables()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            VAL_TABLE_ Modes 1 "Line1
            Line2" 0 "Off" ;
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Mode : 0|1@1+ (1,0) [0|1] "" HOST
            VAL_ 256 Mode Modes;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        var signal = result.GetDocumentOrThrow().ResolveSignal("VehicleStatus", "Mode");
        Assert.IsTrue(signal.TryGetValueDescription(1, out var description));
        Assert.AreEqual($"Line1{Environment.NewLine}Line2", description.Replace("\n", Environment.NewLine));
    }

    [TestMethod]
    public void LoadText_ParsesAdditionalMessageTransmitters()
    {
        const string dbcText = """
            VERSION ""
            BU_: Primary Backup HOST
            BO_ 256 VehicleStatus: 8 Primary
             SG_ Speed : 0|8@1+ (1,0) [0|255] "" HOST
            BO_TX_BU_ 256 : Backup,Primary;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        CollectionAssert.AreEqual(
            new[] { "Primary", "Backup" },
            result.GetDocumentOrThrow().ResolveMessage("VehicleStatus").Transmitters.Select(x => x.Name).ToArray());
    }

    [TestMethod]
    public void LoadText_ParsesEnvironmentVariablesAsMetadata()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            EV_ Ignition: 0 [0|1] "bool" 0 1 DUMMY_NODE_VECTOR0 HOST,VCU;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        var variable = result.GetDocumentOrThrow().EnvironmentVariables["Ignition"];
        Assert.AreEqual("Ignition", variable.Name);
        Assert.AreEqual(0, variable.ValueType);
        Assert.AreEqual(0d, variable.Minimum);
        Assert.AreEqual(1d, variable.Maximum);
        Assert.AreEqual("bool", variable.Unit);
        Assert.AreEqual(1, variable.Identifier);
        CollectionAssert.AreEqual(new[] { "HOST", "VCU" }, variable.AccessNodes.Select(x => x.Name).ToArray());
    }

    [TestMethod]
    public void LoadText_LenientKeepsDuplicateSignalNamesAndNameLookupIsAmbiguous()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ CHECKSUM : 0|4@1+ (1,0) [0|15] "" HOST
             SG_ CHECKSUM : 4|4@1+ (1,0) [0|15] "" HOST
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        Assert.IsTrue(result.Diagnostics.Any(x => x.Severity == DbcDiagnosticSeverity.Warning && x.Code == "DBC_DUPLICATE_SIGNAL_NAME"));

        var message = result.GetDocumentOrThrow().ResolveMessage("VehicleStatus");
        Assert.AreEqual(2, message.Signals.Count(x => x.Name == "CHECKSUM"));
        Assert.AreEqual(2, message.FindSignals("CHECKSUM").Count);
        Assert.IsFalse(message.TryResolveSignal("CHECKSUM", out _));
        Assert.ThrowsExactly<DbcException>(() => message.ResolveSignal("CHECKSUM"));
    }

    [TestMethod]
    public void LoadText_DoesNotApplyAmbiguousSignalMetadataToDuplicateNames()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ CHECKSUM : 0|4@1+ (1,0) [0|15] "" HOST
             SG_ CHECKSUM : 4|4@1+ (1,0) [0|15] "" HOST
            CM_ SG_ 256 CHECKSUM "ambiguous";
            VAL_ 256 CHECKSUM 1 "One" 0 "Zero";
            SIG_VALTYPE_ 256 CHECKSUM : 1;
            BA_DEF_ SG_ "GenSigStartValue" INT 0 15;
            BA_ "GenSigStartValue" SG_ 256 CHECKSUM 1;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_SIGNAL_METADATA_AMBIGUOUS"));
        foreach (var signal in result.GetDocumentOrThrow().ResolveMessage("VehicleStatus").FindSignals("CHECKSUM"))
        {
            Assert.IsNull(signal.Comment);
            Assert.AreEqual(DbcSignalValueType.Unsigned, signal.ValueType);
            Assert.IsNull(signal.InitialValue);
            Assert.AreEqual(0, signal.Attributes.Count);
            Assert.IsFalse(signal.TryGetValueDescription(1, out _));
        }
    }

    [TestMethod]
    public void LoadText_DuplicateValueTableCompatibilityRequiresEquivalentContent()
    {
        const string equivalent = """
            VERSION ""
            VAL_TABLE_ Switch 1 "On" 0 "Off" ;
            VAL_TABLE_ Switch 1 "On" 0 "Off" ;
            """;
        const string conflicting = """
            VERSION ""
            VAL_TABLE_ Switch 1 "On" 0 "Off" ;
            VAL_TABLE_ Switch 1 "Enabled" 0 "Off" ;
            """;

        var equivalentResult = DbcLoader.LoadText(equivalent, DbcLoadOptions.Lenient);
        var conflictingResult = DbcLoader.LoadText(conflicting, DbcLoadOptions.Lenient);
        var strictResult = DbcLoader.LoadText(equivalent, DbcLoadOptions.Strict);

        Assert.IsTrue(equivalentResult.Succeeded, string.Join(Environment.NewLine, equivalentResult.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        Assert.IsTrue(equivalentResult.Diagnostics.Any(x => x.Code == "DBC_DUPLICATE_VALUE_TABLE"));
        Assert.IsTrue(conflictingResult.Succeeded, string.Join(Environment.NewLine, conflictingResult.Diagnostics.Select(x => $"{x.Severity} {x.Code}: {x.Message}")));
        Assert.IsTrue(conflictingResult.Diagnostics.Any(x => x.Severity == DbcDiagnosticSeverity.Warning && x.Code == "DBC_DUPLICATE_VALUE_TABLE"));
        Assert.IsFalse(strictResult.Succeeded);
        Assert.IsTrue(strictResult.Diagnostics.Any(x => x.Severity == DbcDiagnosticSeverity.Error && x.Code == "DBC_DUPLICATE_VALUE_TABLE"));
    }

    [TestMethod]
    public void LoadText_PreservesRelationAttributesAsUnappliedMetadata()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST
            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|8@1+ (1,0) [0|255] "" HOST
            BA_DEF_REL_ BU_SG_REL_ "GenSigTimeoutTime" INT 0 65535;
            BA_DEF_DEF_REL_ "GenSigTimeoutTime" 0;
            BA_REL_ "GenSigTimeoutTime" BU_SG_REL_ VCU 256 Speed 100;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));
        Assert.IsTrue(result.Diagnostics.Any(x => x.Code == "DBC_RELATION_ATTRIBUTE_UNAPPLIED"));
        var document = result.GetDocumentOrThrow();
        Assert.AreEqual("GenSigTimeoutTime", document.RelationAttributeDefinitions["GenSigTimeoutTime"].Name);
        Assert.AreEqual("0", document.RelationAttributeDefaults["GenSigTimeoutTime"].RawValue);
        Assert.AreEqual(1, document.RelationAttributes.Count);
        Assert.AreEqual("BU_SG_REL_ VCU 256 Speed", document.RelationAttributes[0].Target);
    }
}
