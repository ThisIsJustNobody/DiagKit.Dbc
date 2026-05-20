namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class CanIdentifierTests
{
    [TestMethod]
    public void RawDbcMessageId_DetectsExtendedFlagAndKeepsRuntimeCanIdSeparate()
    {
        var rawId = new DbcRawMessageId(DbcRawMessageId.ExtendedFrameFlag | 0x18FF50E5u);

        var identifier = rawId.ToCanIdentifier();

        Assert.AreEqual(0x80000000u | 0x18FF50E5u, rawId.Value);
        Assert.AreEqual(0x18FF50E5u, identifier.Value);
        Assert.AreEqual(CanIdFormat.Extended, identifier.Format);
    }

    [TestMethod]
    public void CanIdentifier_RejectsOutOfRangeStandardId()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CanIdentifier(0x800, CanIdFormat.Standard));
    }

    [TestMethod]
    public void ComparisonOperatorsFollowCompareTo()
    {
        var lower = new CanIdentifier(0x100, CanIdFormat.Standard);
        var equal = new CanIdentifier(0x100, CanIdFormat.Standard);
        var higher = new CanIdentifier(0x101, CanIdFormat.Standard);

        Assert.AreEqual(lower.CompareTo(higher) < 0, InvokeComparisonOperator("op_LessThan", lower, higher));
        Assert.AreEqual(higher.CompareTo(lower) > 0, InvokeComparisonOperator("op_GreaterThan", higher, lower));
        Assert.AreEqual(lower.CompareTo(equal) <= 0, InvokeComparisonOperator("op_LessThanOrEqual", lower, equal));
        Assert.AreEqual(lower.CompareTo(equal) >= 0, InvokeComparisonOperator("op_GreaterThanOrEqual", lower, equal));
    }

    private static bool InvokeComparisonOperator(string name, CanIdentifier left, CanIdentifier right)
    {
        var method = typeof(CanIdentifier).GetMethod(name, [typeof(CanIdentifier), typeof(CanIdentifier)]);
        Assert.IsNotNull(method, $"Missing operator method '{name}'.");
        return (bool)method.Invoke(null, [left, right])!;
    }
}
