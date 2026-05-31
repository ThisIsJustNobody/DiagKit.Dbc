using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace DiagKit.Dbc.Workbook;

internal sealed class SpreadsheetWorkbook
{
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly DateTimeOffset DeterministicEntryTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly List<SpreadsheetSheet> sheets = [];

    public IReadOnlyList<SpreadsheetSheet> Sheets => sheets;

    public void AddSheet(string name, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, int hiddenColumns = 0)
    {
        var allRows = new List<IReadOnlyList<object?>> { headers.Cast<object?>().ToArray() };
        allRows.AddRange(rows);
        sheets.Add(new SpreadsheetSheet(name, allRows, hiddenColumns));
    }

    public byte[] Save()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", CreateContentTypes());
            WriteEntry(archive, "_rels/.rels", CreateRootRelationships());
            WriteEntry(archive, "xl/workbook.xml", CreateWorkbook());
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", CreateWorkbookRelationships());
            WriteEntry(archive, "xl/styles.xml", CreateStyles());

            for (var i = 0; i < sheets.Count; i++)
            {
                WriteEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", CreateWorksheet(sheets[i]));
            }
        }

        return stream.ToArray();
    }

    public static SpreadsheetWorkbook Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var workbook = new SpreadsheetWorkbook();
        var sheetPaths = GetSheetPaths(archive);
        foreach (var (name, path) in sheetPaths)
        {
            workbook.sheets.Add(ReadSheet(name, ReadEntry(archive, path)));
        }

        return workbook;
    }

    public SpreadsheetSheet GetSheet(string name)
    {
        return sheets.FirstOrDefault(sheet => string.Equals(sheet.Name, name, StringComparison.Ordinal))
            ?? throw new DbcException($"Workbook sheet '{name}' was not found.");
    }

    private XDocument CreateContentTypes()
    {
        var document = new XDocument(
            new XElement(
                ContentTypesNs + "Types",
                new XElement(ContentTypesNs + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ContentTypesNs + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(ContentTypesNs + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ContentTypesNs + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
                sheets.Select((_, index) =>
                    new XElement(
                        ContentTypesNs + "Override",
                        new XAttribute("PartName", $"/xl/worksheets/sheet{index + 1}.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
        return document;
    }

    private static XDocument CreateRootRelationships()
    {
        return new XDocument(
            new XElement(
                PackageRelationshipsNs + "Relationships",
                new XElement(
                    PackageRelationshipsNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml"))));
    }

    private XDocument CreateWorkbook()
    {
        return new XDocument(
            new XElement(
                SpreadsheetNs + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", RelationshipsNs),
                new XElement(
                    SpreadsheetNs + "sheets",
                    sheets.Select((sheet, index) =>
                        new XElement(
                            SpreadsheetNs + "sheet",
                            new XAttribute("name", sheet.Name),
                            new XAttribute("sheetId", index + 1),
                            new XAttribute(RelationshipsNs + "id", $"rId{index + 1}"))))));
    }

    private XDocument CreateWorkbookRelationships()
    {
        var relationships = sheets
            .Select((_, index) =>
                new XElement(
                    PackageRelationshipsNs + "Relationship",
                    new XAttribute("Id", $"rId{index + 1}"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", $"worksheets/sheet{index + 1}.xml")))
            .Concat(
                [
                    new XElement(
                        PackageRelationshipsNs + "Relationship",
                        new XAttribute("Id", $"rId{sheets.Count + 1}"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                        new XAttribute("Target", "styles.xml")),
                ]);

        return new XDocument(new XElement(PackageRelationshipsNs + "Relationships", relationships));
    }

    private static XDocument CreateStyles()
    {
        return new XDocument(
            new XElement(
                SpreadsheetNs + "styleSheet",
                new XElement(
                    SpreadsheetNs + "fonts",
                    new XAttribute("count", "1"),
                    new XElement(SpreadsheetNs + "font", new XElement(SpreadsheetNs + "sz", new XAttribute("val", "11")))),
                new XElement(
                    SpreadsheetNs + "fills",
                    new XAttribute("count", "1"),
                    new XElement(SpreadsheetNs + "fill", new XElement(SpreadsheetNs + "patternFill", new XAttribute("patternType", "none")))),
                new XElement(
                    SpreadsheetNs + "borders",
                    new XAttribute("count", "1"),
                    new XElement(SpreadsheetNs + "border")),
                new XElement(
                    SpreadsheetNs + "cellStyleXfs",
                    new XAttribute("count", "1"),
                    new XElement(SpreadsheetNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"))),
                new XElement(
                    SpreadsheetNs + "cellXfs",
                    new XAttribute("count", "1"),
                    new XElement(SpreadsheetNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0")))));
    }

    private static XDocument CreateWorksheet(SpreadsheetSheet sheet)
    {
        var rows = sheet.Rows.Select((row, rowIndex) =>
            new XElement(
                SpreadsheetNs + "row",
                new XAttribute("r", rowIndex + 1),
                row.Select((value, columnIndex) => CreateCell(rowIndex + 1, columnIndex + 1, value))));

        var root = new XElement(SpreadsheetNs + "worksheet");
        if (sheet.HiddenColumns > 0)
        {
            root.Add(
                new XElement(
                    SpreadsheetNs + "cols",
                    new XElement(
                        SpreadsheetNs + "col",
                        new XAttribute("min", "1"),
                        new XAttribute("max", sheet.HiddenColumns.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("width", "12"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("customWidth", "1"))));
        }

        root.Add(new XElement(SpreadsheetNs + "sheetData", rows));
        return new XDocument(root);
    }

    private static XElement CreateCell(int row, int column, object? value)
    {
        var reference = GetCellReference(row, column);
        if (value is null)
        {
            return new XElement(SpreadsheetNs + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"), new XElement(SpreadsheetNs + "is", new XElement(SpreadsheetNs + "t", string.Empty)));
        }

        if (value is int or long or uint or ulong or double or float or decimal)
        {
            return new XElement(
                SpreadsheetNs + "c",
                new XAttribute("r", reference),
                new XElement(SpreadsheetNs + "v", Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("G17", CultureInfo.InvariantCulture)));
        }

        return new XElement(
            SpreadsheetNs + "c",
            new XAttribute("r", reference),
            new XAttribute("t", "inlineStr"),
            new XElement(SpreadsheetNs + "is", new XElement(SpreadsheetNs + "t", value.ToString() ?? string.Empty)));
    }

    private static SpreadsheetSheet ReadSheet(string name, string xml)
    {
        var document = XDocument.Parse(xml);
        var rowValues = new SortedDictionary<int, SortedDictionary<int, string>>();
        foreach (var cell in document.Descendants(SpreadsheetNs + "c"))
        {
            var reference = cell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(reference) || !TryParseCellReference(reference, out var row, out var column))
            {
                continue;
            }

            if (!rowValues.TryGetValue(row, out var values))
            {
                values = [];
                rowValues[row] = values;
            }

            values[column] = ReadCellValue(cell);
        }

        var width = rowValues.Values.Select(row => row.Count == 0 ? 0 : row.Keys.Max()).DefaultIfEmpty(0).Max();
        var rows = rowValues
            .Select(item =>
            {
                var row = new object?[width];
                for (var i = 1; i <= width; i++)
                {
                    row[i - 1] = item.Value.TryGetValue(i, out var value) ? value : string.Empty;
                }

                return (IReadOnlyList<object?>)row;
            })
            .ToArray();

        return new SpreadsheetSheet(name, rows, 0);
    }

    private static string ReadCellValue(XElement cell)
    {
        if (cell.Attribute("t")?.Value == "inlineStr")
        {
            return cell.Element(SpreadsheetNs + "is")?.Element(SpreadsheetNs + "t")?.Value ?? string.Empty;
        }

        return cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
    }

    private static Dictionary<string, string> GetSheetPaths(ZipArchive archive)
    {
        var workbook = XDocument.Parse(ReadEntry(archive, "xl/workbook.xml"));
        var rels = XDocument.Parse(ReadEntry(archive, "xl/_rels/workbook.xml.rels"))
            .Root!
            .Elements(PackageRelationshipsNs + "Relationship")
            .ToDictionary(
                rel => rel.Attribute("Id")!.Value,
                rel => "xl/" + rel.Attribute("Target")!.Value.Replace("\\", "/", StringComparison.Ordinal),
                StringComparer.Ordinal);

        return workbook
            .Root!
            .Element(SpreadsheetNs + "sheets")!
            .Elements(SpreadsheetNs + "sheet")
            .ToDictionary(
                sheet => sheet.Attribute("name")!.Value,
                sheet => rels[sheet.Attribute(RelationshipsNs + "id")!.Value],
                StringComparer.Ordinal);
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new DbcException($"Workbook entry '{path}' was not found.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static void WriteEntry(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path);
        entry.LastWriteTime = DeterministicEntryTimestamp;
        using var writer = new StreamWriter(entry.Open());
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static string GetCellReference(int row, int column)
    {
        return GetColumnName(column) + row.ToString(CultureInfo.InvariantCulture);
    }

    private static string GetColumnName(int column)
    {
        var value = column;
        var chars = new Stack<char>();
        while (value > 0)
        {
            value--;
            chars.Push((char)('A' + value % 26));
            value /= 26;
        }

        return new string(chars.ToArray());
    }

    private static bool TryParseCellReference(string reference, out int row, out int column)
    {
        var index = 0;
        column = 0;
        while (index < reference.Length && char.IsAsciiLetter(reference[index]))
        {
            column = column * 26 + char.ToUpperInvariant(reference[index]) - 'A' + 1;
            index++;
        }

        return int.TryParse(reference[index..], NumberStyles.None, CultureInfo.InvariantCulture, out row) && column > 0;
    }
}

internal sealed record SpreadsheetSheet(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows, int HiddenColumns)
{
    public IReadOnlyList<string> Headers => Rows.Count == 0
        ? Array.Empty<string>()
        : Rows[0].Select(value => value?.ToString() ?? string.Empty).ToArray();

    public IEnumerable<SpreadsheetRow> DataRows
    {
        get
        {
            for (var i = 1; i < Rows.Count; i++)
            {
                yield return new SpreadsheetRow(Name, i + 1, Headers, Rows[i]);
            }
        }
    }
}

internal sealed class SpreadsheetRow
{
    private readonly IReadOnlyList<string> headers;
    private readonly IReadOnlyList<object?> values;

    public SpreadsheetRow(string sheetName, int rowNumber, IReadOnlyList<string> headers, IReadOnlyList<object?> values)
    {
        SheetName = sheetName;
        RowNumber = rowNumber;
        this.headers = headers;
        this.values = values;
    }

    public string SheetName { get; }

    public int RowNumber { get; }

    public string Get(string header)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i], header, StringComparison.Ordinal))
            {
                return i < values.Count ? values[i]?.ToString() ?? string.Empty : string.Empty;
            }
        }

        return string.Empty;
    }

    public string CellAddress(string header)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i], header, StringComparison.Ordinal))
            {
                return $"{SheetName}!{GetColumnName(i + 1)}{RowNumber}";
            }
        }

        return $"{SheetName}!{header}{RowNumber}";
    }

    private static string GetColumnName(int column)
    {
        var value = column;
        var chars = new Stack<char>();
        while (value > 0)
        {
            value--;
            chars.Push((char)('A' + value % 26));
            value /= 26;
        }

        return new string(chars.ToArray());
    }
}
