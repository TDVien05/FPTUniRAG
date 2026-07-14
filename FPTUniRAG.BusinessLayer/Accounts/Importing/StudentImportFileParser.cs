using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;

namespace FPTUniRAG.BusinessLayer.Accounts.Importing;

internal static class StudentImportFileParser
{
    private static readonly string[] RequiredHeaders = ["MSSV", "Name", "Email"];

    public static async Task<IReadOnlyList<ImportedStudentRow>> ParseAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension switch
        {
            ".csv" => await ParseCsvAsync(fileStream, cancellationToken),
            ".xlsx" => await ParseXlsxAsync(fileStream, cancellationToken),
            ".xls" => throw new InvalidOperationException("Legacy .xls files are not supported. Please save the sheet as .xlsx or .csv."),
            _ => throw new InvalidOperationException("Unsupported file format. Please upload a .csv or .xlsx file.")
        };
    }

    private static async Task<IReadOnlyList<ImportedStudentRow>> ParseCsvAsync(
        Stream fileStream,
        CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        using var parser = new TextFieldParser(memoryStream, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");

        if (parser.EndOfData)
        {
            return [];
        }

        var headers = parser.ReadFields() ?? [];
        var indexes = ResolveHeaderIndexes(headers);

        var rows = new List<ImportedStudentRow>();
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = parser.ReadFields() ?? [];
            if (IsEmptyRow(fields))
            {
                continue;
            }

            rows.Add(new ImportedStudentRow(
                Convert.ToInt32(parser.LineNumber, CultureInfo.InvariantCulture),
                GetField(fields, indexes["MSSV"]),
                GetField(fields, indexes["Name"]),
                GetField(fields, indexes["Email"])));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<ImportedStudentRow>> ParseXlsxAsync(
        Stream fileStream,
        CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read, leaveOpen: false);
        var sharedStrings = ReadSharedStrings(archive);
        var worksheetPath = ResolveFirstWorksheetPath(archive);
        var worksheetEntry = archive.GetEntry(worksheetPath)
            ?? throw new InvalidOperationException("The uploaded workbook does not contain a readable worksheet.");

        using var worksheetStream = worksheetEntry.Open();
        var worksheet = XDocument.Load(worksheetStream);
        XNamespace spreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var rowElements = worksheet
            .Descendants(spreadsheetNs + "sheetData")
            .Elements(spreadsheetNs + "row")
            .ToList();

        if (rowElements.Count == 0)
        {
            return [];
        }

        var headerCells = ReadRowCells(rowElements[0], sharedStrings, spreadsheetNs);
        var indexes = ResolveHeaderIndexes(headerCells);

        var rows = new List<ImportedStudentRow>();
        foreach (var rowElement in rowElements.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cells = ReadRowCells(rowElement, sharedStrings, spreadsheetNs);
            if (IsEmptyRow(cells))
            {
                continue;
            }

            var rowNumber = int.TryParse(rowElement.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRow)
                ? parsedRow
                : rows.Count + 2;

            rows.Add(new ImportedStudentRow(
                rowNumber,
                GetField(cells, indexes["MSSV"]),
                GetField(cells, indexes["Name"]),
                GetField(cells, indexes["Email"])));
        }

        return rows;
    }

    private static Dictionary<string, int> ResolveHeaderIndexes(IReadOnlyList<string> headers)
    {
        var normalized = headers
            .Select((header, index) => new
            {
                Header = header.Trim(),
                Index = index
            })
            .GroupBy(item => item.Header, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        foreach (var requiredHeader in RequiredHeaders)
        {
            if (!normalized.ContainsKey(requiredHeader))
            {
                throw new InvalidOperationException("The uploaded file must contain the headers: MSSV, Name, Email.");
            }
        }

        return normalized;
    }

    private static bool IsEmptyRow(IReadOnlyList<string> fields)
    {
        return fields.Count == 0 || fields.All(string.IsNullOrWhiteSpace);
    }

    private static string GetField(IReadOnlyList<string> fields, int index)
    {
        return index < fields.Count ? fields[index].Trim() : string.Empty;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace spreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        return document
            .Descendants(spreadsheetNs + "si")
            .Select(item => string.Concat(
                item.Descendants(spreadsheetNs + "t")
                    .Select(text => text.Value)))
            .ToList();
    }

    private static string ResolveFirstWorksheetPath(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var workbookStream = (archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidOperationException("The uploaded workbook is missing workbook metadata.")).Open();
        using var relationshipsStream = (archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidOperationException("The uploaded workbook is missing sheet relationships.")).Open();

        var workbook = XDocument.Load(workbookStream);
        var relationships = XDocument.Load(relationshipsStream);

        var firstSheetRelationshipId = workbook
            .Descendants(workbookNs + "sheet")
            .Select(sheet => sheet.Attribute(relationshipNs + "id")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? throw new InvalidOperationException("The uploaded workbook does not define any sheets.");

        var target = relationships
            .Descendants(packageRelationshipNs + "Relationship")
            .FirstOrDefault(relationship => string.Equals(
                relationship.Attribute("Id")?.Value,
                firstSheetRelationshipId,
                StringComparison.Ordinal))
            ?.Attribute("Target")?.Value
            ?? throw new InvalidOperationException("The uploaded workbook does not contain a resolvable first worksheet.");

        return $"xl/{target.TrimStart('/')}";
    }

    private static List<string> ReadRowCells(
        XElement rowElement,
        IReadOnlyList<string> sharedStrings,
        XNamespace spreadsheetNs)
    {
        var cells = new SortedDictionary<int, string>();
        foreach (var cell in rowElement.Elements(spreadsheetNs + "c"))
        {
            var cellReference = cell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(cellReference))
            {
                continue;
            }

            var columnIndex = GetColumnIndex(cellReference);
            cells[columnIndex] = ReadCellValue(cell, sharedStrings, spreadsheetNs);
        }

        if (cells.Count == 0)
        {
            return [];
        }

        var values = new List<string>();
        var maxIndex = cells.Keys.Max();
        for (var index = 0; index <= maxIndex; index++)
        {
            values.Add(cells.TryGetValue(index, out var value) ? value : string.Empty);
        }

        return values;
    }

    private static string ReadCellValue(
        XElement cell,
        IReadOnlyList<string> sharedStrings,
        XNamespace spreadsheetNs)
    {
        var dataType = cell.Attribute("t")?.Value;
        if (string.Equals(dataType, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cell.Descendants(spreadsheetNs + "t").Select(text => text.Value));
        }

        var rawValue = cell.Element(spreadsheetNs + "v")?.Value ?? string.Empty;
        if (string.Equals(dataType, "s", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex)
            && sharedStringIndex >= 0
            && sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        return rawValue;
    }

    private static int GetColumnIndex(string cellReference)
    {
        var columnPart = new string(cellReference
            .TakeWhile(character => char.IsLetter(character))
            .ToArray());

        var index = 0;
        foreach (var character in columnPart.ToUpperInvariant())
        {
            index = (index * 26) + (character - 'A' + 1);
        }

        return Math.Max(index - 1, 0);
    }
}
