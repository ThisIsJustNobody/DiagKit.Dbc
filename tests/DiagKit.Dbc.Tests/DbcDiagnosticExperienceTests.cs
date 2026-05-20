namespace DiagKit.Dbc.Tests;

[TestClass]
public sealed class DbcDiagnosticExperienceTests
{
    [TestMethod]
    public void LoadResult_SeparatesDiagnosticsAndThrowsFormattedErrors()
    {
        var result = new DbcLoadResult(
            null,
            [
                new DbcDiagnostic(DbcDiagnosticSeverity.Warning, "DBC_WARN", "recoverable", 12),
                new DbcDiagnostic(DbcDiagnosticSeverity.Error, "DBC_ERROR", "broken", 7),
                new DbcDiagnostic(DbcDiagnosticSeverity.Info, "DBC_INFO", "note", 3),
            ]);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(result.HasWarnings);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.AreEqual(1, result.Warnings.Count);

        var formatted = DbcDiagnosticFormatter.Format(result);
        StringAssert.Contains(formatted, "1 error");
        StringAssert.Contains(formatted, "1 warning");
        StringAssert.Contains(formatted, "Error DBC_ERROR line 7: broken");

        var exception = Assert.ThrowsExactly<DbcException>(() => result.ThrowIfErrors());
        StringAssert.Contains(exception.Message, "DBC_ERROR");
        StringAssert.Contains(exception.Message, "broken");
    }

    [TestMethod]
    public void LoadResult_ThrowIfErrorsAllowsWarningOnlyDiagnostics()
    {
        var result = new DbcLoadResult(
            null,
            [new DbcDiagnostic(DbcDiagnosticSeverity.Warning, "DBC_WARN", "recoverable", 12)]);

        result.ThrowIfErrors();

        Assert.IsFalse(result.HasErrors);
        Assert.IsTrue(result.HasWarnings);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.AreEqual(1, result.Warnings.Count);
    }
}
