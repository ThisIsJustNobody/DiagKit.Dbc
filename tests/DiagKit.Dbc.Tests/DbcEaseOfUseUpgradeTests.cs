namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcEaseOfUseUpgradeTests
{
    [TestMethod]
    public void SignalPath_ParsesAndRejectsInvalidForms()
    {
        var path = SignalPath.Parse("VehicleStatus.VehicleSpeed");

        Assert.AreEqual("VehicleStatus", path.MessageName);
        Assert.AreEqual("VehicleSpeed", path.SignalName);
        Assert.AreEqual("VehicleStatus.VehicleSpeed", path.ToString());
        Assert.IsTrue(SignalPath.TryParse("VehicleStatus.VehicleSpeed", out var parsed));
        Assert.AreEqual(path, parsed);

        Assert.IsFalse(SignalPath.TryParse(null, out _));
        Assert.IsFalse(SignalPath.TryParse("VehicleStatus", out _));
        Assert.IsFalse(SignalPath.TryParse(".VehicleSpeed", out _));
        Assert.IsFalse(SignalPath.TryParse("VehicleStatus.", out _));
        Assert.IsFalse(SignalPath.TryParse("VehicleStatus. ", out _));
        Assert.IsFalse(SignalPath.TryParse(" .VehicleSpeed", out _));
        Assert.IsFalse(SignalPath.TryParse("A.B.C", out _));
        Assert.ThrowsExactly<FormatException>(() => SignalPath.Parse("VehicleStatus"));
    }

    [TestMethod]
    public void SignalPath_ResolvesAcrossDocumentChannelAndSimpleLayer()
    {
        var document = LoadVehicleDocument();
        var path = SignalPath.Parse("VehicleStatus.VehicleSpeed");
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");
        var simple = DbcSimpleChannel.Create(document);

        Assert.IsTrue(document.TryResolveSignal(path, out var signal));
        Assert.AreEqual("VehicleSpeed", signal.Name);

        Assert.IsTrue(channel.TryResolveSignal(path, out var signalHandle));
        Assert.AreEqual(SignalQuality.NoData, channel.GetSignalSnapshot(signalHandle).Quality);

        var write = simple.SetPhysicalValue(path, 12.34, timestamp: Ms(1));
        Assert.IsTrue(write.Succeeded);
        Assert.AreEqual(12.34, simple.GetPhysicalValue(path), 0.000_001);
        Assert.IsTrue(simple.TryGetPhysicalValue(path, out var value));
        Assert.AreEqual(12.34, value, 0.000_001);
    }

    [TestMethod]
    public void SimpleRuntime_LoadTextRetainsWarningsAndProcessFrameUpdatesState()
    {
        var runtime = DbcSimpleRuntime.LoadText(
            """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 Track: 8 VCU
             SG_ CHECKSUM : 0|8@1+ (1,0) [0|255] "" HOST
             SG_ CHECKSUM : 8|8@1+ (1,0) [0|255] "" HOST
            """);

        Assert.IsTrue(runtime.LoadResult.HasWarnings);
        Assert.AreEqual(1, runtime.Document.Messages.Count);

        runtime = DbcSimpleRuntime.LoadText(VehicleDocumentText);
        runtime.SetPhysicalValue("VehicleStatus.VehicleSpeed", 12.34, timestamp: Ms(1));
        var frame = runtime.BuildFrame("VehicleStatus", Ms(2));
        var values = runtime.ProcessFrame(frame.Identifier, frame.Data, timestamp: Ms(3));

        Assert.AreEqual("VehicleStatus", values.MessageName);
        Assert.AreEqual(12.34, values.GetPhysicalValue("VehicleSpeed"), 0.000_001);
        Assert.AreEqual(12.34, runtime.GetPhysicalValue("VehicleStatus.VehicleSpeed"), 0.000_001);
    }

    [TestMethod]
    public void SimpleRuntime_LoadFileThrowsWhenLenientLoadHasErrors()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dbc");
        File.WriteAllText(
            path,
            """
            VERSION ""
            BU_: VCU
            BO_ 256 First: 8 VCU
            BO_ 256 Duplicate: 8 VCU
            """);

        try
        {
            var exception = Assert.ThrowsExactly<DbcException>(() => DbcSimpleRuntime.LoadFile(path));
            StringAssert.Contains(exception.Message, "DBC diagnostics");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void PublishingRegistrationReport_ExplainsStrictCycleTimeAndDuplicateOutcomes()
    {
        var document = LoadPublishingDocument();
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");

        var strictReport = channel.RegisterCyclicPublishingMessagesFromDbc(firstDueTime: Ms(5));

        Assert.AreEqual(2, strictReport.RegisteredCount);
        Assert.AreEqual(6, strictReport.SkippedCount);
        AssertStatus(strictReport, "CyclicStatus", DbcPublishingRegistrationStatus.Registered);
        AssertStatus(strictReport, "MixedStatus", DbcPublishingRegistrationStatus.Registered);
        AssertStatus(strictReport, "EventStatus", DbcPublishingRegistrationStatus.SkippedSendType);
        AssertStatus(strictReport, "ActiveStatus", DbcPublishingRegistrationStatus.SkippedSendType);
        AssertStatus(strictReport, "NoCycleStatus", DbcPublishingRegistrationStatus.SkippedNoCycleTime);
        AssertStatus(strictReport, "LargeStatus", DbcPublishingRegistrationStatus.SkippedRuntimeUnsupported);
        AssertStatus(strictReport, "GatewayStatus", DbcPublishingRegistrationStatus.SkippedSendType);

        var secondReport = channel.RegisterCycleTimePublishingMessagesFromDbc(firstDueTime: Ms(5));

        AssertStatus(secondReport, "CyclicStatus", DbcPublishingRegistrationStatus.AlreadyRegistered);
        AssertStatus(secondReport, "MixedStatus", DbcPublishingRegistrationStatus.AlreadyRegistered);
        AssertStatus(secondReport, "EventStatus", DbcPublishingRegistrationStatus.Registered);
        AssertStatus(secondReport, "ActiveStatus", DbcPublishingRegistrationStatus.Registered);
    }

    [TestMethod]
    public void PublishingRegistrationReport_FiltersMessagesByTransmittingNode()
    {
        var document = LoadPublishingDocument();
        var channel = DbcRuntimeSession.Create(document).CreateChannel("CAN1");

        var report = channel.RegisterPublishingMessagesTransmittedBy("Gateway", firstDueTime: Ms(5));

        Assert.AreEqual(1, report.RegisteredCount);
        AssertStatus(report, "GatewayStatus", DbcPublishingRegistrationStatus.Registered);
        AssertStatus(report, "CyclicStatus", DbcPublishingRegistrationStatus.SkippedNodeMismatch);
        AssertStatus(report, "SensorStatus", DbcPublishingRegistrationStatus.SkippedNodeMismatch);
    }

    [TestMethod]
    public void SignalViewSnapshot_CombinesCurrentValueWithSignalMetadata()
    {
        var runtime = DbcSimpleRuntime.LoadText(
            """
            VERSION ""
            BU_: VCU HOST

            BO_ 300 GearStatus: 8 VCU
             SG_ Gear : 0|8@1+ (1,0) [0|15] "state" HOST
            VAL_ 300 Gear 1 "Drive" 2 "Reverse";
            """);

        runtime.SetPhysicalValue(SignalPath.Parse("GearStatus.Gear"), 1, timestamp: Ms(10));

        var snapshot = runtime.GetSignalViewSnapshot("GearStatus.Gear", Ms(11));

        Assert.AreEqual("GearStatus", snapshot.MessageName);
        Assert.AreEqual("Gear", snapshot.SignalName);
        Assert.AreEqual(1UL, snapshot.RawValue);
        Assert.AreEqual(1d, snapshot.PhysicalValue);
        Assert.AreEqual(SignalQuality.Valid, snapshot.Quality);
        Assert.AreEqual("state", snapshot.Unit);
        Assert.AreEqual(0d, snapshot.Minimum);
        Assert.AreEqual(15d, snapshot.Maximum);
        Assert.AreEqual("Drive", snapshot.ValueDescriptions[1]);
    }

    [TestMethod]
    public void SignalViewSnapshots_EnumerateAllSignalsForUiBindingAndFiltering()
    {
        var runtime = DbcSimpleRuntime.LoadText(
            """
            VERSION ""
            BU_: VCU HOST Gateway LOGGER TP

            BO_ 256 VehicleStatus: 8 VCU
             SG_ VehicleSpeed : 0|16@1+ (0.01,0) [0|250] "km/h" HOST
             SG_ Gear : 16|8@1+ (1,0) [0|15] "state" HOST
            BO_ 300 GatewayStatus: 8 Vector__XXX
             SG_ GatewayValue : 0|8@1+ (1,0) [0|255] "count" LOGGER
            BO_ 2364539904 LargePG: 1785 VCU
             SG_ FirstByte : 0|8@1+ (1,0) [0|255] "byte" TP

            BO_TX_BU_ 300 : Gateway;
            VAL_ 256 Gear 1 "Drive" 2 "Reverse";
            """);

        runtime.SetPhysicalValue("VehicleStatus.VehicleSpeed", 42.5, timestamp: Ms(10));
        runtime.SetPhysicalValue("VehicleStatus.Gear", 2, timestamp: Ms(10));

        var all = runtime.GetSignalViewSnapshots(Ms(11));
        var vehicle = runtime.GetSignalViewSnapshotsForMessage("VehicleStatus", Ms(11));
        var gateway = runtime.GetSignalViewSnapshotsTransmittedBy("Gateway", Ms(11));
        var hostReceived = runtime.Channel.GetSignalViewSnapshotsReceivedBy("HOST", Ms(11));
        var unsupported = all.Single(x => x.MessageName == "LargePG" && x.SignalName == "FirstByte");

        CollectionAssert.AreEqual(
            new[]
            {
                "VehicleStatus.VehicleSpeed",
                "VehicleStatus.Gear",
                "GatewayStatus.GatewayValue",
                "LargePG.FirstByte",
            },
            all.Select(x => $"{x.MessageName}.{x.SignalName}").ToArray());
        CollectionAssert.AreEqual(
            new[] { "VehicleSpeed", "Gear" },
            vehicle.Select(x => x.SignalName).ToArray());
        CollectionAssert.AreEqual(
            new[] { "GatewayStatus.GatewayValue" },
            gateway.Select(x => $"{x.MessageName}.{x.SignalName}").ToArray());
        CollectionAssert.AreEqual(
            new[] { "VehicleSpeed", "Gear" },
            hostReceived.Select(x => x.SignalName).ToArray());
        Assert.AreEqual(42.5, vehicle.Single(x => x.SignalName == "VehicleSpeed").PhysicalValue, 0.000_001);
        Assert.AreEqual("Reverse", vehicle.Single(x => x.SignalName == "Gear").ValueDescription);
        Assert.AreEqual(SignalQuality.NoData, unsupported.Quality);
        Assert.AreEqual(double.NaN, unsupported.PhysicalValue);
        Assert.AreEqual("byte", unsupported.Unit);
        Assert.AreEqual(255d, unsupported.Maximum);
    }

    [TestMethod]
    public void LookupExceptions_IncludeActionableMessageSignalAndPathContext()
    {
        var document = DbcLoader.LoadText(
            """
            VERSION ""
            BU_: VCU HOST

            BO_ 256 VehicleStatus: 8 VCU
             SG_ Speed : 0|16@1+ (1,0) [0|65535] "" HOST
            BO_ 257 Track: 8 VCU
             SG_ CHECKSUM : 0|8@1+ (1,0) [0|255] "" HOST
             SG_ CHECKSUM : 8|8@1+ (1,0) [0|255] "" HOST
            """,
            DbcLoadOptions.Lenient).GetDocumentOrThrow();
        var simple = DbcSimpleChannel.Create(document);

        var missingMessage = Assert.ThrowsExactly<DbcException>(() => document.ResolveMessage("vehiclestatus"));
        var missingSignal = Assert.ThrowsExactly<DbcException>(() => document.ResolveSignal("VehicleStatus", "Missing"));
        var ambiguousSignal = Assert.ThrowsExactly<DbcException>(() => simple.SetPhysicalValue("Track.CHECKSUM", 1));
        var invalidPath = Assert.ThrowsExactly<FormatException>(() => simple.SetPhysicalValue("VehicleStatus", 1));

        StringAssert.Contains(missingMessage.Message, "case-sensitive");
        StringAssert.Contains(missingMessage.Message, "Document.Messages");
        StringAssert.Contains(missingSignal.Message, "VehicleStatus.Missing");
        StringAssert.Contains(ambiguousSignal.Message, "FindSignals(...)");
        StringAssert.Contains(ambiguousSignal.Message, "object-based");
        StringAssert.Contains(invalidPath.Message, "expected 'Message.Signal'");
    }

    [TestMethod]
    public void DiagnosticFormatter_SummarizesAndFormatsGroupsBySeverityAndCode()
    {
        var diagnostics = new[]
        {
            new DbcDiagnostic(DbcDiagnosticSeverity.Warning, "DBC_WARN", "recoverable", 12),
            new DbcDiagnostic(DbcDiagnosticSeverity.Error, "DBC_ERROR", "broken 1", 7),
            new DbcDiagnostic(DbcDiagnosticSeverity.Error, "DBC_ERROR", "broken 2", 9),
            new DbcDiagnostic(DbcDiagnosticSeverity.Info, "DBC_INFO", "note", 3),
        };

        var summary = DbcDiagnosticFormatter.Summarize(diagnostics);
        var formatted = DbcDiagnosticFormatter.FormatGrouped(diagnostics);

        Assert.IsTrue(summary.HasErrors);
        Assert.AreEqual(2, summary.ErrorCount);
        Assert.AreEqual(1, summary.WarningCount);
        Assert.AreEqual(1, summary.InfoCount);
        Assert.AreEqual(3, summary.Groups.Count);
        Assert.AreEqual(2, summary.Groups.Single(x => x.Code == "DBC_ERROR").Diagnostics.Count);
        StringAssert.Contains(formatted, "Error DBC_ERROR (2)");
        StringAssert.Contains(formatted, "line 7: broken 1");
        StringAssert.Contains(formatted, "Warning DBC_WARN (1)");
    }

    private static void AssertStatus(
        DbcPublishingRegistrationReport report,
        string messageName,
        DbcPublishingRegistrationStatus expected)
    {
        var entry = report.Entries.Single(x => x.MessageName == messageName);
        Assert.AreEqual(expected, entry.Status, messageName);
    }

    private static DbcDocument LoadVehicleDocument()
    {
        return DbcLoader.LoadText(VehicleDocumentText, DbcLoadOptions.Strict).GetDocumentOrThrow();
    }

    private static DbcDocument LoadPublishingDocument()
    {
        const string dbcText = """
            VERSION ""
            BU_: VCU HOST Gateway Sensor

            BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000;
            BA_DEF_ BO_ "GenMsgSendType" ENUM "cyclic","event","cyclicIfActive","cyclicAndEvent","noMsgSendType";

            BO_ 256 CyclicStatus: 8 VCU
             SG_ CyclicValue : 0|8@1+ (1,0) [0|255] "" HOST
            BO_ 257 EventStatus: 8 VCU
             SG_ EventValue : 0|8@1+ (1,0) [0|255] "" HOST
            BO_ 258 ActiveStatus: 8 VCU
             SG_ ActiveValue : 0|8@1+ (1,0) [0|255] "" HOST
            BO_ 259 MixedStatus: 8 VCU
             SG_ MixedValue : 0|8@1+ (1,0) [0|255] "" HOST
            BO_ 260 NoCycleStatus: 8 VCU
             SG_ NoCycleValue : 0|8@1+ (1,0) [0|255] "" HOST
            BO_ 261 LargeStatus: 65 VCU
             SG_ LargeValue : 0|8@1+ (1,0) [0|255] "" HOST
            BO_ 262 GatewayStatus: 8 Vector__XXX
             SG_ GatewayValue : 0|8@1+ (1,0) [0|255] "" HOST
            BO_ 263 SensorStatus: 8 Sensor
             SG_ SensorValue : 0|8@1+ (1,0) [0|255] "" HOST

            BO_TX_BU_ 262 : Gateway;

            BA_ "GenMsgCycleTime" BO_ 256 10;
            BA_ "GenMsgCycleTime" BO_ 257 10;
            BA_ "GenMsgCycleTime" BO_ 258 10;
            BA_ "GenMsgCycleTime" BO_ 259 10;
            BA_ "GenMsgCycleTime" BO_ 261 10;
            BA_ "GenMsgCycleTime" BO_ 262 20;
            BA_ "GenMsgCycleTime" BO_ 263 20;
            BA_ "GenMsgSendType" BO_ 256 0;
            BA_ "GenMsgSendType" BO_ 257 1;
            BA_ "GenMsgSendType" BO_ 258 2;
            BA_ "GenMsgSendType" BO_ 259 3;
            BA_ "GenMsgSendType" BO_ 261 0;
            """;

        var result = DbcLoader.LoadText(dbcText, DbcLoadOptions.Lenient);
        Assert.IsNotNull(result.Document);
        return result.Document;
    }

    private const string VehicleDocumentText = """
        VERSION ""
        BU_: VCU HOST

        BO_ 256 VehicleStatus: 8 VCU
         SG_ VehicleSpeed : 0|16@1+ (0.01,0) [0|250] "km/h" HOST
        BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000;
        BA_ "GenMsgCycleTime" BO_ 256 10;
        """;

    private static DbcTimestamp Ms(int milliseconds)
    {
        return DbcTimestamp.FromElapsed(TimeSpan.FromMilliseconds(milliseconds));
    }
}
