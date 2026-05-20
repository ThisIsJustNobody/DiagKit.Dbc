using System.Diagnostics;
using DiagKit.Dbc;

if (CorpusVerificationOptions.TryParse(args, out var corpusOptions))
{
    var result = VerifyCorpus(corpusOptions);
    PrintCorpusResult(result);
    Environment.ExitCode = result.FailedFiles == 0 ? 0 : 1;
    return;
}

var options = BenchmarkOptions.Parse(args);
var document = CreateDocument();
var message = document.ResolveMessage("EnvironmentStatus");
var payload = new byte[message.DataLength];
message.TryEncodeSignal("Speed", payload, 88.88);
message.TryEncodeSignal("Temperature", payload, 42.5);
message.TryEncodeSignal("Current", payload, -12);
message.TryEncodeSignal("State", payload, 3);

var sampleSink = new NoopSampleSink();
var frameSink = new NoopFrameSink();

var scenarios = options.UseMatrix
    ? new[]
    {
        new BenchmarkScenario("Smoke / 1 channel", options with { Channels = 1 }),
        new BenchmarkScenario("Baseline / 4 channels", options with { Channels = 4 }),
        new BenchmarkScenario("Observation / 8 channels", options with { Channels = 8 }),
    }
    : options.UseSoak
        ? new[]
        {
            new BenchmarkScenario("Soak / configured channels", options),
        }
    : new[]
    {
        new BenchmarkScenario("DiagKit.Dbc benchmark smoke", options),
    };

for (var i = 0; i < scenarios.Length; i++)
{
    if (i > 0)
    {
        Console.WriteLine();
    }

    var scenario = scenarios[i];
    var measurement = MeasureScenario(scenario.Options, document, message.Identifier, payload, sampleSink, frameSink);

    Console.WriteLine(scenario.Title);
    Console.WriteLine($"Channels: {scenario.Options.Channels}");
    Console.WriteLine($"Seconds: {scenario.Options.Seconds}");
    Console.WriteLine($"Receive frames/channel/sec: {scenario.Options.ReceiveFramesPerChannelPerSecond}");
    Console.WriteLine($"Poll ticks/channel/sec: {scenario.Options.SendPollsPerChannelPerSecond}");
    Console.WriteLine($"Received frames: {measurement.Result.ReceivedFrames}");
    Console.WriteLine($"Due frames: {measurement.Result.DueFrames}");
    Console.WriteLine($"Samples: {measurement.Samples}");
    Console.WriteLine($"Elapsed ms: {measurement.Elapsed.TotalMilliseconds:F3}");
    Console.WriteLine($"Allocated bytes/thread: {measurement.AllocatedBytes}");
    Console.WriteLine($"Throughput frames/sec: {(measurement.Result.ReceivedFrames + measurement.Result.DueFrames) / measurement.Elapsed.TotalSeconds:F0}");
}

static BenchmarkMeasurement MeasureScenario(
    BenchmarkOptions options,
    DbcDocument document,
    CanIdentifier identifier,
    byte[] payload,
    NoopSampleSink sampleSink,
    NoopFrameSink frameSink)
{
    RunScenario(options, CreateChannels(document, options.Channels), identifier, payload, sampleSink, frameSink);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var channels = CreateChannels(document, options.Channels);
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();
    var result = RunScenario(options, channels, identifier, payload, sampleSink, frameSink);
    stopwatch.Stop();
    var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

    return new BenchmarkMeasurement(result, sampleSink.Count, stopwatch.Elapsed, allocatedBytes);
}

static BenchmarkResult RunScenario(
    BenchmarkOptions options,
    DbcChannelRuntime[] channels,
    CanIdentifier identifier,
    byte[] payload,
    NoopSampleSink sampleSink,
    NoopFrameSink frameSink)
{
    sampleSink.Count = 0;
    frameSink.Count = 0;

    long receivedFrames = 0;
    long dueFrames = 0;
    var receiveFrames = options.ReceiveFramesPerChannelPerSecond * options.Seconds;
    var sendPolls = options.SendPollsPerChannelPerSecond * options.Seconds;
    var receiveTimestampStep = TimeSpan.TicksPerSecond / options.ReceiveFramesPerChannelPerSecond;
    var sendTimestampStep = TimeSpan.TicksPerSecond / options.SendPollsPerChannelPerSecond;

    for (var channelIndex = 0; channelIndex < channels.Length; channelIndex++)
    {
        var channel = channels[channelIndex];
        for (var i = 0; i < receiveFrames; i++)
        {
            var timestamp = new DbcTimestamp(i * receiveTimestampStep, DbcTimestampKind.MonotonicTicks);
            receivedFrames += channel.ProcessReceivedFrame(new DbcFrameView(identifier, payload, timestamp: timestamp), sampleSink) > 0 ? 1 : 0;
        }

        for (var i = 0; i < sendPolls; i++)
        {
            var timestamp = new DbcTimestamp(i * sendTimestampStep, DbcTimestampKind.MonotonicTicks);
            dueFrames += channel.PollDueFrames(timestamp, frameSink);
        }
    }

    return new BenchmarkResult(receivedFrames, dueFrames);
}

static DbcChannelRuntime[] CreateChannels(DbcDocument document, int channelCount)
{
    var channels = new DbcChannelRuntime[channelCount];
    var session = DbcRuntimeSession.Create(document);
    for (var i = 0; i < channels.Length; i++)
    {
        var channel = session.CreateChannel($"CAN{i + 1}");
        var messageHandle = channel.ResolveMessage("EnvironmentStatus");
        var speedHandle = channel.ResolveSignal(messageHandle, "Speed");
        channel.AddObservingMessage(messageHandle);
        channel.AddPublishingMessage(messageHandle, TimeSpan.FromMilliseconds(10));
        channel.SetPhysicalValue(speedHandle, 12.34);
        channels[i] = channel;
    }

    return channels;
}

static DbcDocument CreateDocument()
{
    const string dbcText = """
        VERSION ""
        BU_: VCU HOST

        BO_ 256 EnvironmentStatus: 8 VCU
         SG_ Speed : 0|16@1+ (0.01,0) [0|250] "km/h" HOST
         SG_ Temperature : 16|16@1+ (0.1,-40) [-40|215] "degC" HOST
         SG_ Current : 32|12@1- (1,0) [-2048|2047] "A" HOST
         SG_ State : 44|4@1+ (1,0) [0|15] "" HOST
        BA_DEF_ BO_ "GenMsgCycleTime" INT 0 100000;
        BA_ "GenMsgCycleTime" BO_ 256 10;
        """;

    return DbcLoader.LoadText(dbcText, DbcLoadOptions.Strict).GetDocumentOrThrow();
}

static CorpusVerificationResult VerifyCorpus(CorpusVerificationOptions options)
{
    var files = EnumerateCorpusFiles(options.Inputs, options.MaxFiles).ToArray();
    if (files.Length == 0)
    {
        return new CorpusVerificationResult(
            0,
            0,
            1,
            0,
            0,
            0,
            0,
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<CorpusFileClassification, int>());
    }

    var diagnosticCodes = new Dictionary<string, int>(StringComparer.Ordinal);
    var passedFiles = 0;
    var failedFiles = 0;
    var totalDiagnostics = 0;
    var warningDiagnostics = 0;
    var errorDiagnostics = 0;
    var exceptionCount = 0;
    var classifications = new Dictionary<CorpusFileClassification, int>();

    foreach (var file in files)
    {
        try
        {
            var result = DbcLoader.LoadFile(file, options.LoadOptions);
            totalDiagnostics += result.Diagnostics.Count;
            foreach (var diagnostic in result.Diagnostics)
            {
                if (diagnostic.Severity == DbcDiagnosticSeverity.Warning)
                {
                    warningDiagnostics++;
                }
                else if (diagnostic.Severity == DbcDiagnosticSeverity.Error)
                {
                    errorDiagnostics++;
                }

                diagnosticCodes.TryGetValue(diagnostic.Code, out var count);
                diagnosticCodes[diagnostic.Code] = count + 1;
            }

            var classification = ClassifyCorpusFile(result);
            IncrementClassification(classifications, classification);
            var failed = !result.Succeeded || (options.FailOnDiagnostics && result.Diagnostics.Count > 0);
            if (failed)
            {
                failedFiles++;
                PrintCorpusFailure(file, result, classification);
            }
            else
            {
                passedFiles++;
            }
        }
        catch (Exception ex)
        {
            exceptionCount++;
            failedFiles++;
            IncrementClassification(classifications, CorpusFileClassification.InvalidDbc);
            Console.WriteLine($"[EXCEPTION] {file}");
            Console.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
        }
    }

    return new CorpusVerificationResult(
        files.Length,
        passedFiles,
        failedFiles,
        totalDiagnostics,
        warningDiagnostics,
        errorDiagnostics,
        exceptionCount,
        diagnosticCodes,
        classifications);
}

static IEnumerable<string> EnumerateCorpusFiles(IReadOnlyList<string> inputs, int? maxFiles)
{
    var files = new List<string>();
    foreach (var input in inputs)
    {
        if (File.Exists(input))
        {
            if (Path.GetExtension(input).Equals(".dbc", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(Path.GetFullPath(input));
            }

            continue;
        }

        if (Directory.Exists(input))
        {
            files.AddRange(Directory.EnumerateFiles(input, "*.dbc", SearchOption.AllDirectories).Select(Path.GetFullPath));
        }
        else
        {
            Console.WriteLine($"[MISSING] {input}");
        }
    }

    var ordered = files
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase);

    return maxFiles is > 0 ? ordered.Take(maxFiles.Value) : ordered;
}

static void PrintCorpusFailure(string file, DbcLoadResult result, CorpusFileClassification classification)
{
    Console.WriteLine($"[FAIL] {file}");
    Console.WriteLine($"  Classification: {classification}; Succeeded: {result.Succeeded}; Diagnostics: {result.Diagnostics.Count}");
    foreach (var diagnostic in result.Diagnostics.Take(5))
    {
        Console.WriteLine($"  {diagnostic.Severity} {diagnostic.Code} line {diagnostic.LineNumber}: {diagnostic.Message}");
    }

    if (result.Diagnostics.Count > 5)
    {
        Console.WriteLine($"  ... {result.Diagnostics.Count - 5} more diagnostics");
    }
}

static CorpusFileClassification ClassifyCorpusFile(DbcLoadResult result)
{
    if (HasDiagnostic(result, "DBC_MESSAGE_RUNTIME_UNSUPPORTED"))
    {
        return CorpusFileClassification.RuntimeUnsupportedProtocol;
    }

    if (result.Succeeded)
    {
        return result.Diagnostics.Count == 0
            ? CorpusFileClassification.Pass
            : CorpusFileClassification.WarningOnly;
    }

    if (HasDiagnostic(result, "DBC_EXTENDED_MULTIPLEXING_UNSUPPORTED") ||
        HasDiagnostic(result, "DBC_RELATION_ATTRIBUTE_UNAPPLIED"))
    {
        return CorpusFileClassification.NeedsSpecClarification;
    }

    return CorpusFileClassification.InvalidDbc;
}

static bool HasDiagnostic(DbcLoadResult result, string code)
{
    return result.Diagnostics.Any(x => string.Equals(x.Code, code, StringComparison.Ordinal));
}

static void IncrementClassification(Dictionary<CorpusFileClassification, int> classifications, CorpusFileClassification classification)
{
    classifications.TryGetValue(classification, out var count);
    classifications[classification] = count + 1;
}

static void PrintCorpusResult(CorpusVerificationResult result)
{
    Console.WriteLine();
    Console.WriteLine("DBC corpus verification");
    Console.WriteLine($"Files: {result.TotalFiles}");
    Console.WriteLine($"Passed files: {result.PassedFiles}");
    Console.WriteLine($"Failed files: {result.FailedFiles}");
    Console.WriteLine($"Diagnostics: {result.TotalDiagnostics}");
    Console.WriteLine($"Warnings: {result.WarningDiagnostics}");
    Console.WriteLine($"Errors: {result.ErrorDiagnostics}");
    Console.WriteLine($"Exceptions: {result.ExceptionCount}");

    if (result.Classifications.Count > 0)
    {
        Console.WriteLine("Classifications:");
        foreach (var pair in result.Classifications.OrderBy(x => x.Key))
        {
            Console.WriteLine($"  {pair.Key}: {pair.Value}");
        }
    }

    if (result.TotalFiles == 0)
    {
        Console.WriteLine("No .dbc files were found. Use --corpus <file-or-directory>.");
    }

    if (result.DiagnosticCodes.Count > 0)
    {
        Console.WriteLine("Top diagnostic codes:");
        foreach (var pair in result.DiagnosticCodes.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(10))
        {
            Console.WriteLine($"  {pair.Key}: {pair.Value}");
        }
    }
}

internal readonly record struct BenchmarkOptions(
    int Channels,
    int Seconds,
    int ReceiveFramesPerChannelPerSecond,
    int SendPollsPerChannelPerSecond,
    bool UseMatrix,
    bool UseSoak)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var options = new BenchmarkOptions(4, 1, 1_000, 100, false, false);
        for (var i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--matrix":
                    options = options with { UseMatrix = true };
                    break;
                case "--soak":
                    options = options with { UseSoak = true, Seconds = options.Seconds == 1 ? 30 : options.Seconds };
                    break;
                case "--channels" when int.TryParse(value, out var channels):
                    options = options with { Channels = channels };
                    i++;
                    break;
                case "--seconds" when int.TryParse(value, out var seconds):
                    options = options with { Seconds = seconds };
                    i++;
                    break;
                case "--rx" when int.TryParse(value, out var rx):
                    options = options with { ReceiveFramesPerChannelPerSecond = rx };
                    i++;
                    break;
                case "--polls" when int.TryParse(value, out var polls):
                    options = options with { SendPollsPerChannelPerSecond = polls };
                    i++;
                    break;
            }
        }

        if (options.Channels <= 0 || options.Seconds <= 0 || options.ReceiveFramesPerChannelPerSecond <= 0 || options.SendPollsPerChannelPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "Benchmark options must be positive.");
        }

        return options;
    }
}

internal readonly record struct BenchmarkScenario(string Title, BenchmarkOptions Options);

internal readonly record struct BenchmarkMeasurement(BenchmarkResult Result, long Samples, TimeSpan Elapsed, long AllocatedBytes);

internal readonly record struct BenchmarkResult(long ReceivedFrames, long DueFrames);

internal sealed record CorpusVerificationOptions(
    IReadOnlyList<string> Inputs,
    DbcLoadOptions LoadOptions,
    bool FailOnDiagnostics,
    int? MaxFiles)
{
    public static bool TryParse(string[] args, out CorpusVerificationOptions options)
    {
        var inputs = new List<string>();
        var loadOptions = DbcLoadOptions.Lenient;
        var failOnDiagnostics = false;
        int? maxFiles = null;

        for (var i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--corpus" when !string.IsNullOrWhiteSpace(value):
                    inputs.Add(value);
                    i++;
                    break;
                case "--strict":
                    loadOptions = DbcLoadOptions.Strict;
                    break;
                case "--lenient":
                    loadOptions = DbcLoadOptions.Lenient;
                    break;
                case "--fail-on-diagnostics":
                    failOnDiagnostics = true;
                    break;
                case "--max-files" when int.TryParse(value, out var parsedMaxFiles):
                    maxFiles = parsedMaxFiles;
                    i++;
                    break;
            }
        }

        options = new CorpusVerificationOptions(inputs, loadOptions, failOnDiagnostics, maxFiles);
        return inputs.Count > 0 || args.Contains("--corpus", StringComparer.Ordinal);
    }
}

internal sealed record CorpusVerificationResult(
    int TotalFiles,
    int PassedFiles,
    int FailedFiles,
    int TotalDiagnostics,
    int WarningDiagnostics,
    int ErrorDiagnostics,
    int ExceptionCount,
    IReadOnlyDictionary<string, int> DiagnosticCodes,
    IReadOnlyDictionary<CorpusFileClassification, int> Classifications);

internal enum CorpusFileClassification
{
    Pass,
    WarningOnly,
    InvalidDbc,
    RuntimeUnsupportedProtocol,
    NeedsSpecClarification,
}

internal sealed class NoopSampleSink : ISignalSampleSink
{
    public long Count { get; set; }

    public void OnSignalSample(in SignalSample sample)
    {
        Count++;
    }
}

internal sealed class NoopFrameSink : IDbcFrameSink
{
    public long Count { get; set; }

    public void OnFrame(CanIdentifier identifier, ReadOnlySpan<byte> data, DbcFrameFlags flags, DbcTimestamp timestamp)
    {
        Count++;
    }
}
