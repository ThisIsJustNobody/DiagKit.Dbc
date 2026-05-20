using System.Globalization;
using System.Text;

namespace DiagKit.Dbc.Tests;

internal static class DeterministicDbcFuzzer
{
    public static IEnumerable<DeterministicDbcFuzzCase> GenerateCases(int seed, int count)
    {
        var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var valid = CreateValidDocument(random, $"fuzz_valid_{i}", messageCount: 1 + random.Next(3));
            yield return (i % 8) switch
            {
                0 => valid,
                1 => valid with { Id = $"fuzz_missing_message_colon_{i}", Text = valid.Text.Replace(":", "", StringComparison.Ordinal) },
                2 => new DeterministicDbcFuzzCase(
                    $"fuzz_orphan_signal_{i}",
                    """
                    VERSION ""
                    BU_: ECU HOST
                     SG_ Orphan : 0|8@1+ (1,0) [0|255] "" HOST
                    BO_ 256 LaterMessage: 8 ECU
                     SG_ Value : 0|8@1+ (1,0) [0|255] "" HOST
                    """),
                3 => new DeterministicDbcFuzzCase(
                    $"fuzz_unterminated_quote_{i}",
                    """
                    VERSION ""
                    BU_: ECU HOST
                    BO_ 256 BrokenComment: 8 ECU
                     SG_ Value : 0|8@1+ (1,0) [0|255] "" HOST
                    CM_ SG_ 256 Value "missing end
                    """),
                4 => valid with
                {
                    Id = $"fuzz_unknown_attribute_{i}",
                    Text = valid.Text + Environment.NewLine + $$"""BA_ "MissingAttribute{{i}}" BO_ 256 10;""",
                },
                5 => new DeterministicDbcFuzzCase(
                    $"fuzz_out_of_range_length_{i}",
                    """
                    VERSION ""
                    BU_: ECU HOST
                    BO_ 512 LargeMessage: 128 ECU
                     SG_ Value : 0|8@1+ (1,0) [0|255] "" HOST
                    """),
                6 => new DeterministicDbcFuzzCase(
                    $"fuzz_unsupported_statement_{i}",
                    """
                    VERSION ""
                    BU_: ECU HOST
                    BO_ 768 UnsupportedTail: 8 ECU
                     SG_ Value : 0|8@1+ (1,0) [0|255] "" HOST
                    SIG_GROUP_ 768 Group1 1 : Value;
                    """),
                _ => valid with
                {
                    Id = $"fuzz_extra_semicolons_{i}",
                    Text = valid.Text.Replace("VERSION \"\"", "VERSION \"\"; ; ;", StringComparison.Ordinal),
                },
            };
        }
    }

    public static IEnumerable<DeterministicDbcFuzzCase> GenerateValidDocuments(int seed, int count)
    {
        var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            yield return CreateValidDocument(random, $"valid_{i}", messageCount: 1 + random.Next(4));
        }
    }

    private static DeterministicDbcFuzzCase CreateValidDocument(Random random, string id, int messageCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("VERSION \"\"");
        builder.AppendLine("NS_ :");
        builder.AppendLine("BS_:");
        builder.AppendLine("BU_: ECU HOST AUX");
        builder.AppendLine("BA_DEF_ \"ProjectName\" STRING;");
        builder.AppendLine($$"""BA_ "ProjectName" "Generated {{Escape(id)}}";""");

        var signalCount = 0;
        var baseRawId = 256 + random.Next(128);
        for (var messageIndex = 0; messageIndex < messageCount; messageIndex++)
        {
            var rawId = baseRawId + messageIndex;
            builder.AppendLine(CultureInfo.InvariantCulture, $"BO_ {rawId} Msg_{id}_{messageIndex}: 8 ECU");
            var startBit = 0;
            var signalsInMessage = 1 + random.Next(4);
            for (var signalIndex = 0; signalIndex < signalsInMessage; signalIndex++)
            {
                var bitLength = signalIndex == signalsInMessage - 1
                    ? Math.Min(16, 64 - startBit)
                    : 8;
                var receiver = signalIndex % 2 == 0 ? "HOST" : "AUX";
                var factor = signalIndex % 2 == 0 ? "1" : "0.5";
                var offset = signalIndex % 3 == 0 ? "0" : "-10";
                var minimum = signalIndex % 3 == 0 ? "0" : "-10";
                var maximum = bitLength == 16 ? "65535" : "255";
                var unit = signalIndex % 2 == 0 ? "unit" : "unit\\\"quoted";
                builder.AppendLine(CultureInfo.InvariantCulture, $" SG_ Sig_{messageIndex}_{signalIndex} : {startBit}|{bitLength}@1+ ({factor},{offset}) [{minimum}|{maximum}] \"{unit}\" {receiver}");
                builder.AppendLine(CultureInfo.InvariantCulture, $"CM_ SG_ {rawId} Sig_{messageIndex}_{signalIndex} \"Generated signal {signalIndex}; still quoted\";");
                startBit += bitLength;
                signalCount++;
            }

            builder.AppendLine(CultureInfo.InvariantCulture, $"VAL_ {rawId} Sig_{messageIndex}_0 0 \"Zero\" 1 \"One\\\"Quoted\";");
        }

        return new DeterministicDbcFuzzCase(id, builder.ToString(), messageCount, signalCount);
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}

internal readonly record struct DeterministicDbcFuzzCase(
    string Id,
    string Text,
    int ExpectedMessageCount = 0,
    int ExpectedSignalCount = 0);
