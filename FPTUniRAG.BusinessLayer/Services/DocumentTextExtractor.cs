using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FPTUniRAG.BusinessLayer.Options;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace FPTUniRAG.BusinessLayer.Services;

public sealed class DocumentTextExtractor : IDocumentTextExtractor
{
    private readonly RagIngestionOptions _options;
    private readonly ITesseractOcrService _tesseractOcrService;

    public DocumentTextExtractor(
        IOptions<RagIngestionOptions> options,
        ITesseractOcrService tesseractOcrService)
    {
        _options = options.Value;
        _tesseractOcrService = tesseractOcrService;
    }

    public async Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        stream.Position = 0;

        return extension switch
        {
            ".txt" or ".md" => await ReadPlainTextAsync(stream, cancellationToken),
            ".docx" => await ReadDocxAsync(stream, cancellationToken),
            ".pdf" => await ReadPdfAsync(stream, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported file type '{extension}'.")
        };
    }

    private static async Task<string> ReadPlainTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        return NormalizeWhitespace(content);
    }

    private static async Task<string> ReadDocxAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("word/document.xml");
        if (entry is null)
        {
            throw new InvalidOperationException("The uploaded DOCX file is missing its main document XML.");
        }

        await using var entryStream = entry.Open();
        var document = await XDocument.LoadAsync(entryStream, LoadOptions.None, cancellationToken);
        XNamespace wordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var paragraphs = document
            .Descendants(wordNamespace + "p")
            .Select(paragraph => string.Concat(
                paragraph
                    .Descendants(wordNamespace + "t")
                    .Select(textNode => textNode.Value)))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Trim());

        return NormalizeWhitespace(string.Join(Environment.NewLine + Environment.NewLine, paragraphs));
    }

    private async Task<string> ReadPdfAsync(Stream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var pdfDocument = PdfDocument.Open(stream);
        var pages = pdfDocument.GetPages().ToList();
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("The uploaded PDF does not contain any readable pages.");
        }

        var extractedPages = new List<string>();
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageText = NormalizeWhitespace(page.Text);
            var imageOcrText = await TryExtractTextFromPageImagesAsync(page, pageText, cancellationToken);

            var mergedText = MergePageContent(pageText, imageOcrText);
            if (!string.IsNullOrWhiteSpace(mergedText))
            {
                extractedPages.Add(mergedText);
            }
        }

        if (extractedPages.Count == 0)
        {
            throw new InvalidOperationException(
                "The uploaded PDF does not contain readable text or OCR-extractable images. Verify the PDF content and Tesseract OCR configuration.");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, extractedPages);
    }

    private static string NormalizeWhitespace(string content)
    {
        return content.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    private async Task<string> TryExtractTextFromPageImagesAsync(Page page, string pageText, CancellationToken cancellationToken)
    {
        if (!_options.Tesseract.Enabled || page.NumberOfImages == 0)
        {
            return string.Empty;
        }

        var imageTexts = new List<string>();
        foreach (var image in page.GetImages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetOcrCandidate(image, out var imageBytes, out var fileExtension))
            {
                continue;
            }

            string ocrText;
            try
            {
                ocrText = await _tesseractOcrService.ExtractTextAsync(imageBytes, fileExtension, cancellationToken);
            }
            catch (InvalidOperationException) when (!string.IsNullOrWhiteSpace(pageText))
            {
                return string.Empty;
            }

            ocrText = NormalizeWhitespace(ocrText);
            if (string.IsNullOrWhiteSpace(ocrText))
            {
                continue;
            }

            if (!ContainsNormalized(pageText, ocrText)
                && !imageTexts.Any(existing => ContainsNormalized(existing, ocrText)))
            {
                imageTexts.Add(ocrText);
            }
        }

        return string.Join(Environment.NewLine + Environment.NewLine, imageTexts);
    }

    private static string MergePageContent(string pageText, string imageOcrText)
    {
        if (string.IsNullOrWhiteSpace(pageText))
        {
            return imageOcrText;
        }

        if (string.IsNullOrWhiteSpace(imageOcrText))
        {
            return pageText;
        }

        return $"{pageText}{Environment.NewLine}{Environment.NewLine}{imageOcrText}";
    }

    private static bool TryGetOcrCandidate(IPdfImage image, out IReadOnlyList<byte> imageBytes, out string fileExtension)
    {
        imageBytes = [];
        fileExtension = ".png";

        if (image.TryGetPng(out var pngBytes) && pngBytes is { Length: > 0 })
        {
            imageBytes = pngBytes;
            fileExtension = ".png";
            return true;
        }

        var rawBytes = image.RawBytes;
        if (rawBytes is null || rawBytes.Count == 0)
        {
            return false;
        }

        var guessedExtension = GuessRasterExtension(rawBytes);
        if (guessedExtension is null)
        {
            return false;
        }

        imageBytes = rawBytes;
        fileExtension = guessedExtension;
        return true;
    }

    private static string? GuessRasterExtension(IReadOnlyList<byte> bytes)
    {
        if (bytes.Count >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47)
        {
            return ".png";
        }

        if (bytes.Count >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Count >= 4
            && bytes[0] == 0x49
            && bytes[1] == 0x49
            && bytes[2] == 0x2A
            && bytes[3] == 0x00)
        {
            return ".tif";
        }

        if (bytes.Count >= 4
            && bytes[0] == 0x4D
            && bytes[1] == 0x4D
            && bytes[2] == 0x00
            && bytes[3] == 0x2A)
        {
            return ".tif";
        }

        if (bytes.Count >= 2
            && bytes[0] == 0x42
            && bytes[1] == 0x4D)
        {
            return ".bmp";
        }

        if (bytes.Count >= 6
            && bytes[0] == 0x47
            && bytes[1] == 0x49
            && bytes[2] == 0x46)
        {
            return ".gif";
        }

        return null;
    }

    private static bool ContainsNormalized(string source, string candidate)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalizedSource = CollapseWhitespace(source).ToLowerInvariant();
        var normalizedCandidate = CollapseWhitespace(candidate).ToLowerInvariant();
        return normalizedSource.Contains(normalizedCandidate, StringComparison.Ordinal);
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(" ", value
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
