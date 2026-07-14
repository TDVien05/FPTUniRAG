using FPTUniRAG.BusinessLayer.Rag.Configuration;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.BusinessLayer.Rag.Ingestion;

public sealed class TesseractOcrService : ITesseractOcrService
{
    private readonly RagIngestionOptions _options;

    public TesseractOcrService(IOptions<RagIngestionOptions> options)
    {
        _options = options.Value;
    }

    public async Task<string> ExtractTextAsync(
        IReadOnlyList<byte> imageBytes,
        string fileExtension,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Tesseract.Enabled)
        {
            return string.Empty;
        }

        if (imageBytes.Count == 0)
        {
            return string.Empty;
        }

        ValidateOptions();

        var extension = NormalizeExtension(fileExtension);
        var tempRoot = Path.Combine(Path.GetTempPath(), "fptunirag-tesseract");
        Directory.CreateDirectory(tempRoot);

        var inputPath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}{extension}");
        var outputBasePath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}");
        var outputTextPath = outputBasePath + ".txt";

        try
        {
            await File.WriteAllBytesAsync(inputPath, imageBytes.ToArray(), cancellationToken);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _options.Tesseract.ExecutablePath,
                    Arguments = BuildArguments(inputPath, outputBasePath),
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Tesseract OCR failed with exit code {process.ExitCode}: {standardError}");
            }

            if (!File.Exists(outputTextPath))
            {
                return string.Empty;
            }

            var ocrText = await File.ReadAllTextAsync(outputTextPath, Encoding.UTF8, cancellationToken);
            return NormalizeWhitespace(ocrText);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"Tesseract executable '{_options.Tesseract.ExecutablePath}' was not found. Install Tesseract OCR and update RagIngestion:Tesseract:ExecutablePath.",
                exception);
        }
        finally
        {
            SafeDelete(inputPath);
            SafeDelete(outputTextPath);
        }
    }

    private string BuildArguments(string inputPath, string outputBasePath)
    {
        var segments = new List<string>
        {
            Quote(inputPath),
            Quote(outputBasePath),
            "-l",
            Quote(_options.Tesseract.Language)
        };

        if (!string.IsNullOrWhiteSpace(_options.Tesseract.LanguageDataPath))
        {
            segments.Add("--tessdata-dir");
            segments.Add(Quote(_options.Tesseract.LanguageDataPath));
        }

        return string.Join(" ", segments);
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Tesseract.ExecutablePath))
        {
            throw new InvalidOperationException("RagIngestion:Tesseract:ExecutablePath is missing in appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(_options.Tesseract.Language))
        {
            throw new InvalidOperationException("RagIngestion:Tesseract:Language is missing in appsettings.json.");
        }
    }

    private static string Quote(string value)
    {
        return $"\"{value}\"";
    }

    private static string NormalizeExtension(string fileExtension)
    {
        var extension = string.IsNullOrWhiteSpace(fileExtension) ? ".png" : fileExtension.Trim();
        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        return extension.ToLowerInvariant();
    }

    private static void SafeDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string NormalizeWhitespace(string content)
    {
        return content.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }
}
