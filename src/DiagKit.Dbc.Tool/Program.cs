using DiagKit.Dbc.Workbook;

namespace DiagKit.Dbc.Tool;

/// <summary>
/// `diagkit-dbc` command-line entry point。<br/>
/// `diagkit-dbc` command-line entry point.
/// </summary>
public static class Program
{
    /// <summary>
    /// 执行命令。<br/>
    /// Executes the command.
    /// </summary>
    public static int Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteUsage(Console.Out);
            return 0;
        }

        if (!string.Equals(args[0], "workbook", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            WriteUsage(Console.Error);
            return 2;
        }

        if (args.Length < 2 || IsHelp(args[1]))
        {
            WriteWorkbookUsage(Console.Out);
            return 0;
        }

        return args[1].ToLowerInvariant() switch
        {
            "template" => TemplateWorkbook(args[2..]),
            "export" => ExportWorkbook(args[2..]),
            "import" => ImportWorkbook(args[2..]),
            "validate" => ValidateWorkbook(args[2..]),
            _ => UnknownWorkbookCommand(args[1]),
        };
    }

    private static int TemplateWorkbook(string[] args)
    {
        if (!TryParseOutputCommand(args, minPositionals: 0, out _, out var outputPath) ||
            string.IsNullOrWhiteSpace(outputPath))
        {
            WriteWorkbookUsage(Console.Error);
            return 2;
        }

        var result = DbcWorkbookExporter.WriteTemplate(outputPath);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(DbcDiagnosticFormatter.Format(result.Diagnostics));
            return 1;
        }

        Console.WriteLine(outputPath);
        return 0;
    }

    private static int ExportWorkbook(string[] args)
    {
        if (!TryParseOutputCommand(args, minPositionals: 1, out var positionals, out var outputPath))
        {
            WriteWorkbookUsage(Console.Error);
            return 2;
        }

        var inputDbc = positionals[0];
        outputPath ??= Path.ChangeExtension(inputDbc, ".xlsx");
        var result = DbcWorkbookExporter.ExportFile(inputDbc);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(DbcDiagnosticFormatter.Format(result.Diagnostics));
            return 1;
        }

        File.WriteAllBytes(outputPath, result.WorkbookBytes!);
        Console.WriteLine(outputPath);
        return 0;
    }

    private static int ImportWorkbook(string[] args)
    {
        if (!TryParseOutputCommand(args, minPositionals: 1, out var positionals, out var outputPath))
        {
            WriteWorkbookUsage(Console.Error);
            return 2;
        }

        var workbookPath = positionals[0];
        outputPath ??= Path.ChangeExtension(workbookPath, ".dbc");

        var result = DbcWorkbookImporter.ImportWorkbookFile(workbookPath);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(DbcDiagnosticFormatter.Format(result.Diagnostics));
            return 1;
        }

        var writeResult = DbcWriter.WriteFile(outputPath, result.Document!);
        if (!writeResult.Succeeded)
        {
            Console.Error.WriteLine(DbcDiagnosticFormatter.Format(writeResult.Diagnostics));
            return 1;
        }

        Console.WriteLine(outputPath);
        return 0;
    }

    private static int ValidateWorkbook(string[] args)
    {
        if (args.Length != 1)
        {
            WriteWorkbookUsage(Console.Error);
            return 2;
        }

        var result = DbcWorkbookImporter.ImportWorkbookFile(args[0]);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(DbcDiagnosticFormatter.Format(result.Diagnostics));
            return 1;
        }

        Console.WriteLine("DBC Excel file is valid.");
        return 0;
    }

    private static int UnknownWorkbookCommand(string command)
    {
        Console.Error.WriteLine($"Unknown workbook command '{command}'.");
        WriteWorkbookUsage(Console.Error);
        return 2;
    }

    private static bool TryParseOutputCommand(string[] args, int minPositionals, out string[] positionals, out string? outputPath)
    {
        var items = new List<string>();
        outputPath = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "-o", StringComparison.Ordinal) ||
                string.Equals(args[i], "--output", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length)
                {
                    positionals = [];
                    return false;
                }

                outputPath = args[++i];
                continue;
            }

            items.Add(args[i]);
        }

        positionals = items.ToArray();
        return positionals.Length == minPositionals;
    }

    private static bool IsHelp(string value)
    {
        return string.Equals(value, "-h", StringComparison.Ordinal) ||
            string.Equals(value, "--help", StringComparison.Ordinal) ||
            string.Equals(value, "help", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  diagkit-dbc workbook template -o <edit.xlsx>");
        writer.WriteLine("  diagkit-dbc workbook export <input.dbc> -o <edit.xlsx>");
        writer.WriteLine("  diagkit-dbc workbook validate <edit.xlsx>");
        writer.WriteLine("  diagkit-dbc workbook import <edit.xlsx> -o <output.dbc>");
    }

    private static void WriteWorkbookUsage(TextWriter writer)
    {
        writer.WriteLine("Workbook commands:");
        writer.WriteLine("  template -o <edit.xlsx>");
        writer.WriteLine("  export <input.dbc> -o <edit.xlsx>");
        writer.WriteLine("  validate <edit.xlsx>");
        writer.WriteLine("  import <edit.xlsx> -o <output.dbc>");
    }
}
