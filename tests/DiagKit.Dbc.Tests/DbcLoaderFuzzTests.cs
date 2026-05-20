namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcLoaderFuzzTests
{
    [TestMethod]
    public void LoadText_DeterministicFuzzCasesNeverThrowAndReportStructuredDiagnostics()
    {
        foreach (var fuzzCase in DeterministicDbcFuzzer.GenerateCases(seed: 0xDBC2026, count: 160))
        {
            DbcLoadResult result;
            try
            {
                result = DbcLoader.LoadText(fuzzCase.Text, DbcLoadOptions.Lenient);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Fuzz case {fuzzCase.Id} threw {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{fuzzCase.Text}");
                return;
            }

            AssertDiagnosticsAreStructured(fuzzCase, result.Diagnostics);
            if (result.Succeeded)
            {
                AssertDocumentInvariants(fuzzCase, result.GetDocumentOrThrow());
            }
        }
    }

    [TestMethod]
    public void LoadText_GeneratedValidCorpusPreservesExpectedModelShape()
    {
        foreach (var fuzzCase in DeterministicDbcFuzzer.GenerateValidDocuments(seed: 0xDBC600D, count: 64))
        {
            var result = DbcLoader.LoadText(fuzzCase.Text, DbcLoadOptions.Strict);

            Assert.IsTrue(result.Succeeded, $"Generated valid case {fuzzCase.Id} should load without errors.{Environment.NewLine}{string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Severity} {x.Code} line {x.LineNumber}: {x.Message}"))}{Environment.NewLine}{fuzzCase.Text}");
            Assert.AreEqual(0, result.Diagnostics.Count, $"Generated valid case {fuzzCase.Id} should not produce diagnostics.");

            var document = result.GetDocumentOrThrow();
            AssertDocumentInvariants(fuzzCase, document);
            Assert.AreEqual(fuzzCase.ExpectedMessageCount, document.Messages.Count, $"Generated valid case {fuzzCase.Id} message count mismatch.");
            Assert.AreEqual(fuzzCase.ExpectedSignalCount, document.Messages.Sum(x => x.Signals.Count), $"Generated valid case {fuzzCase.Id} signal count mismatch.");
        }
    }

    private static void AssertDiagnosticsAreStructured(DeterministicDbcFuzzCase fuzzCase, IReadOnlyList<DbcDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(diagnostic.Code), $"Fuzz case {fuzzCase.Id} produced a diagnostic without a code.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(diagnostic.Message), $"Fuzz case {fuzzCase.Id} produced a diagnostic without a message.");
            Assert.IsTrue(diagnostic.LineNumber >= 0, $"Fuzz case {fuzzCase.Id} produced a negative diagnostic line.");
        }
    }

    private static void AssertDocumentInvariants(DeterministicDbcFuzzCase fuzzCase, DbcDocument document)
    {
        var messageNames = new HashSet<string>(StringComparer.Ordinal);
        var identifiers = new HashSet<CanIdentifier>();
        foreach (var message in document.Messages)
        {
            Assert.IsTrue(messageNames.Add(message.Name), $"Fuzz case {fuzzCase.Id} produced duplicate message name '{message.Name}'.");
            Assert.IsTrue(identifiers.Add(message.Identifier), $"Fuzz case {fuzzCase.Id} produced duplicate message identifier '{message.Identifier}'.");
            Assert.IsTrue(message.DataLength >= 0, $"Fuzz case {fuzzCase.Id} produced invalid message length {message.DataLength}.");
            Assert.AreEqual(message.DataLength <= 64, message.SupportsSingleFrameRuntime, $"Fuzz case {fuzzCase.Id} produced inconsistent runtime support for message length {message.DataLength}.");

            foreach (var signal in message.Signals)
            {
                Assert.IsTrue(signal.BitLength is >= 1 and <= 64, $"Fuzz case {fuzzCase.Id} produced invalid bit length {signal.BitLength}.");
                Assert.IsTrue(signal.StartBit >= 0, $"Fuzz case {fuzzCase.Id} produced negative start bit {signal.StartBit}.");
                Assert.AreSame(message, signal.Message, $"Fuzz case {fuzzCase.Id} produced a signal with wrong parent message.");
            }
        }
    }
}
