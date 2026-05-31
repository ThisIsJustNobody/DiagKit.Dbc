using System.IO.Compression;
using System.Xml.Linq;

namespace DiagKit.Dbc.Workbook.Tests;

internal sealed class WorkbookTestXlsx
{
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private readonly Dictionary<string, WorkbookTestSheet> sheets;

    private WorkbookTestXlsx(Dictionary<string, WorkbookTestSheet> sheets)
    {
        this.sheets = sheets;
    }

    public IReadOnlyList<string> SheetNames => sheets.Keys.ToArray();

    public WorkbookTestSheet GetSheet(string name)
    {
        return sheets[name];
    }

    public static WorkbookTestXlsx Open(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var sheetPaths = GetSheetPaths(archive);
        var result = new Dictionary<string, WorkbookTestSheet>(StringComparer.Ordinal);
        foreach (var (name, path) in sheetPaths)
        {
            result[name] = WorkbookTestSheet.Read(ReadEntry(archive, path));
        }

        return new WorkbookTestXlsx(result);
    }

    public static string ReadAllText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return string.Join(
            Environment.NewLine,
            archive.Entries
                .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
                .Select(entry => entry.FullName + Environment.NewLine + ReadEntry(archive, entry.FullName)));
    }

    public static byte[] WithCells(byte[] bytes, string sheetName, IReadOnlyDictionary<string, string> cells)
    {
        using var input = new MemoryStream(bytes);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        var entries = source.Entries.ToDictionary(entry => entry.FullName, entry => ReadEntry(source, entry.FullName), StringComparer.Ordinal);
        var sheetPath = GetSheetPaths(source)[sheetName];
        var document = XDocument.Parse(entries[sheetPath]);
        foreach (var item in cells)
        {
            SetCell(document, item.Key, item.Value);
        }

        entries[sheetPath] = document.ToString(SaveOptions.DisableFormatting);

        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = target.CreateEntry(item.Key);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(item.Value);
            }
        }

        return output.ToArray();
    }

    public static byte[] WithOnlySheets(byte[] bytes, params string[] sheetNames)
    {
        var keep = sheetNames.ToHashSet(StringComparer.Ordinal);
        return RewriteWorkbook(bytes, entries =>
        {
            var workbook = XDocument.Parse(entries["xl/workbook.xml"]);
            var rels = XDocument.Parse(entries["xl/_rels/workbook.xml.rels"]);
            var contentTypes = XDocument.Parse(entries["[Content_Types].xml"]);
            var sheetElements = workbook
                .Root!
                .Element(SpreadsheetNs + "sheets")!
                .Elements(SpreadsheetNs + "sheet")
                .ToArray();
            var relElements = rels.Root!.Elements(PackageRelationshipsNs + "Relationship").ToDictionary(
                rel => rel.Attribute("Id")!.Value,
                rel => rel,
                StringComparer.Ordinal);

            foreach (var sheet in sheetElements)
            {
                var sheetName = sheet.Attribute("name")!.Value;
                if (keep.Contains(sheetName))
                {
                    continue;
                }

                var relId = sheet.Attribute(RelationshipsNs + "id")!.Value;
                if (relElements.TryGetValue(relId, out var rel))
                {
                    var path = "xl/" + rel.Attribute("Target")!.Value.Replace("\\", "/", StringComparison.Ordinal);
                    entries.Remove(path);
                    RemoveContentTypeOverride(contentTypes, "/" + path);
                    rel.Remove();
                }

                sheet.Remove();
            }

            entries["xl/workbook.xml"] = workbook.ToString(SaveOptions.DisableFormatting);
            entries["xl/_rels/workbook.xml.rels"] = rels.ToString(SaveOptions.DisableFormatting);
            entries["[Content_Types].xml"] = contentTypes.ToString(SaveOptions.DisableFormatting);
        });
    }

    public static byte[] WithDuplicateSheet(byte[] bytes, string sourceSheetName, string newSheetName)
    {
        return RewriteWorkbook(bytes, entries =>
        {
            using var stream = new MemoryStream(bytes);
            using var sourceArchive = new ZipArchive(stream, ZipArchiveMode.Read);
            var sourcePath = GetSheetPaths(sourceArchive)[sourceSheetName];
            var workbook = XDocument.Parse(entries["xl/workbook.xml"]);
            var rels = XDocument.Parse(entries["xl/_rels/workbook.xml.rels"]);
            var contentTypes = XDocument.Parse(entries["[Content_Types].xml"]);
            var newSheetIndex = NextWorksheetIndex(entries.Keys);
            var newPath = $"xl/worksheets/sheet{newSheetIndex}.xml";
            var newRelId = NextRelationshipId(rels);
            var newSheetId = NextSheetId(workbook);

            entries[newPath] = entries[sourcePath];
            workbook
                .Root!
                .Element(SpreadsheetNs + "sheets")!
                .Add(
                    new XElement(
                        SpreadsheetNs + "sheet",
                        new XAttribute("name", newSheetName),
                        new XAttribute("sheetId", newSheetId),
                        new XAttribute(RelationshipsNs + "id", newRelId)));
            rels.Root!.Add(
                new XElement(
                    PackageRelationshipsNs + "Relationship",
                    new XAttribute("Id", newRelId),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", $"worksheets/sheet{newSheetIndex}.xml")));
            contentTypes.Root!.Add(
                new XElement(
                    ContentTypesNs + "Override",
                    new XAttribute("PartName", "/" + newPath),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));

            entries["xl/workbook.xml"] = workbook.ToString(SaveOptions.DisableFormatting);
            entries["xl/_rels/workbook.xml.rels"] = rels.ToString(SaveOptions.DisableFormatting);
            entries["[Content_Types].xml"] = contentTypes.ToString(SaveOptions.DisableFormatting);
        });
    }

    private static byte[] RewriteWorkbook(byte[] bytes, Action<Dictionary<string, string>> update)
    {
        using var input = new MemoryStream(bytes);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        var entries = source.Entries.ToDictionary(entry => entry.FullName, entry => ReadEntry(source, entry.FullName), StringComparer.Ordinal);
        update(entries);

        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var entry = target.CreateEntry(item.Key);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(item.Value);
            }
        }

        return output.ToArray();
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
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException($"Entry '{path}' not found.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static void RemoveContentTypeOverride(XDocument contentTypes, string partName)
    {
        contentTypes
            .Root!
            .Elements(ContentTypesNs + "Override")
            .Where(element => string.Equals(element.Attribute("PartName")?.Value, partName, StringComparison.Ordinal))
            .Remove();
    }

    private static int NextWorksheetIndex(IEnumerable<string> paths)
    {
        return paths
            .Where(path => path.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal) && path.EndsWith(".xml", StringComparison.Ordinal))
            .Select(path => path["xl/worksheets/sheet".Length..^".xml".Length])
            .Select(value => int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    private static string NextRelationshipId(XDocument rels)
    {
        var next = rels
            .Root!
            .Elements(PackageRelationshipsNs + "Relationship")
            .Select(rel => rel.Attribute("Id")?.Value)
            .Where(id => id is not null && id.StartsWith("rId", StringComparison.Ordinal))
            .Select(id => int.TryParse(id![3..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return "rId" + next.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int NextSheetId(XDocument workbook)
    {
        return workbook
            .Root!
            .Element(SpreadsheetNs + "sheets")!
            .Elements(SpreadsheetNs + "sheet")
            .Select(sheet => int.TryParse(sheet.Attribute("sheetId")?.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    private static void SetCell(XDocument document, string reference, string value)
    {
        var cell = document.Descendants(SpreadsheetNs + "c").SingleOrDefault(x => x.Attribute("r")?.Value == reference);
        if (cell is null)
        {
            cell = CreateCell(document, reference);
        }

        cell.RemoveNodes();
        cell.SetAttributeValue("t", "inlineStr");
        cell.Add(new XElement(SpreadsheetNs + "is", new XElement(SpreadsheetNs + "t", value)));
    }

    private static XElement CreateCell(XDocument document, string reference)
    {
        var (rowNumber, columnNumber) = ParseCellReference(reference);
        var sheetData = document.Root!.Element(SpreadsheetNs + "sheetData")!;
        var row = sheetData
            .Elements(SpreadsheetNs + "row")
            .SingleOrDefault(x => x.Attribute("r")?.Value == rowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (row is null)
        {
            row = new XElement(SpreadsheetNs + "row", new XAttribute("r", rowNumber));
            var nextRow = sheetData
                .Elements(SpreadsheetNs + "row")
                .FirstOrDefault(x => int.Parse(x.Attribute("r")!.Value, System.Globalization.CultureInfo.InvariantCulture) > rowNumber);
            if (nextRow is null)
            {
                sheetData.Add(row);
            }
            else
            {
                nextRow.AddBeforeSelf(row);
            }
        }

        var cell = new XElement(SpreadsheetNs + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"));
        var nextCell = row
            .Elements(SpreadsheetNs + "c")
            .FirstOrDefault(x => ParseCellReference(x.Attribute("r")!.Value).Column > columnNumber);
        if (nextCell is null)
        {
            row.Add(cell);
        }
        else
        {
            nextCell.AddBeforeSelf(cell);
        }

        return cell;
    }

    private static (int Row, int Column) ParseCellReference(string reference)
    {
        var split = 0;
        while (split < reference.Length && char.IsLetter(reference[split]))
        {
            split++;
        }

        var column = 0;
        foreach (var ch in reference[..split])
        {
            column = (column * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        var row = int.Parse(reference[split..], System.Globalization.CultureInfo.InvariantCulture);
        return (row, column);
    }
}

internal sealed class WorkbookTestSheet
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private readonly Dictionary<string, string> cells;

    private WorkbookTestSheet(Dictionary<string, string> cells, IReadOnlyList<int> hiddenColumns)
    {
        this.cells = cells;
        HiddenColumns = hiddenColumns;
    }

    public IReadOnlyList<int> HiddenColumns { get; }

    public string GetCell(string reference)
    {
        return cells.TryGetValue(reference, out var value) ? value : string.Empty;
    }

    public static WorkbookTestSheet Read(string xml)
    {
        var document = XDocument.Parse(xml);
        var hiddenColumns = document
            .Descendants(SpreadsheetNs + "col")
            .Where(col => col.Attribute("hidden")?.Value == "1")
            .SelectMany(col =>
            {
                var min = int.Parse(col.Attribute("min")!.Value, System.Globalization.CultureInfo.InvariantCulture);
                var max = int.Parse(col.Attribute("max")!.Value, System.Globalization.CultureInfo.InvariantCulture);
                return Enumerable.Range(min, max - min + 1);
            })
            .ToArray();
        var cells = document
            .Descendants(SpreadsheetNs + "c")
            .Where(cell => cell.Attribute("r") is not null)
            .ToDictionary(
                cell => cell.Attribute("r")!.Value,
                ReadCell,
                StringComparer.Ordinal);
        return new WorkbookTestSheet(cells, hiddenColumns);
    }

    private static string ReadCell(XElement cell)
    {
        if (cell.Attribute("t")?.Value == "inlineStr")
        {
            return cell.Element(SpreadsheetNs + "is")?.Element(SpreadsheetNs + "t")?.Value ?? string.Empty;
        }

        return cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
    }
}
