using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Base64AppBlazor.Services;

public class Base64ConverterService
{
    private const int MaxFileSize = 50 * 1024 * 1024; // 50MB
    private const int CopyBufferSize = 81920;

    // Strings shorter than this are almost never embedded files -- they are ordinary
    // JSON values that happen to be valid base64 (e.g. "test" decodes to 3 bytes).
    private const int MinDetectedFileBytes = 8;

    private const string TooLargeMessage = "File too large. Maximum size is 50MB";
    private static readonly (string filename, string mimeType) UnknownType = ("converted_file", "application/octet-stream");

    /// <summary>
    /// Encodes a stream to base64. Pass <paramref name="knownSize"/> (e.g. IBrowserFile.Size)
    /// whenever it is available: Blazor's browser file stream is never seekable, so without it
    /// the buffer has to grow by repeated doubling and copying.
    /// </summary>
    public async Task<(string base64, string filename, long size)> FileToBase64Async(
        Stream fileStream,
        string filename,
        long knownSize = -1,
        CancellationToken cancellationToken = default)
    {
        if (knownSize < 0 && fileStream.CanSeek)
        {
            knownSize = fileStream.Length;
        }

        if (knownSize > MaxFileSize)
        {
            throw new InvalidOperationException(TooLargeMessage);
        }

        if (knownSize >= 0)
        {
            // Size known up front: one pooled buffer, zero reallocations.
            var rented = ArrayPool<byte>.Shared.Rent((int)Math.Max(knownSize, 1));
            try
            {
                var read = await FillAsync(fileStream, rented, (int)knownSize, cancellationToken);
                return (Convert.ToBase64String(rented, 0, read), filename, read);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        // Size unknown: grow as we read, but reject as soon as we cross the limit rather
        // than buffering an oversized file in full before checking.
        using var memoryStream = new MemoryStream(CopyBufferSize);
        await CopyWithLimitAsync(fileStream, memoryStream, cancellationToken);

        // GetBuffer avoids the extra full-length copy that ToArray would make.
        return (Convert.ToBase64String(memoryStream.GetBuffer(), 0, (int)memoryStream.Length),
                filename,
                memoryStream.Length);
    }

    private static async Task<int> FillAsync(Stream source, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < count)
        {
            var read = await source.ReadAsync(buffer.AsMemory(total, count - total), cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
        }
        return total;
    }

    private static async Task CopyWithLimitAsync(Stream source, MemoryStream destination, CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            int read;
            while ((read = await source.ReadAsync(rented, cancellationToken)) > 0)
            {
                if (destination.Length + read > MaxFileSize)
                {
                    throw new InvalidOperationException(TooLargeMessage);
                }
                destination.Write(rented, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Validates and decodes in a single pass. Cheap rejections happen before the decoder is
    /// touched, and callers get the bytes back so nothing has to decode the same string twice.
    /// </summary>
    public bool TryDecodeBase64(string? value, out byte[] data)
    {
        data = Array.Empty<byte>();

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var span = value.AsSpan();
        var commaIndex = value.IndexOf(',');
        if (commaIndex >= 0)
        {
            span = span[(commaIndex + 1)..];
        }

        // O(1) rejections: base64 is always a non-empty multiple of 4 once padded, which is
        // what the decoder requires anyway. This discards most non-base64 strings for free.
        if (span.Length < 4 || (span.Length & 3) != 0)
        {
            return false;
        }

        var maxBytes = (span.Length >> 2) * 3;
        var rented = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            if (!Convert.TryFromBase64Chars(span, rented, out var written) || written == 0)
            {
                return false;
            }

            data = rented.AsSpan(0, written).ToArray();
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public (byte[] fileData, string filename, string mimeType) Base64ToFile(string base64String, string? filename = null)
    {
        if (!TryDecodeBase64(base64String, out var fileData))
        {
            throw new InvalidOperationException("Invalid base64 string");
        }

        var detected = DetectFileType(fileData);
        return (fileData, filename ?? detected.filename, detected.mimeType);
    }

    public bool IsValidBase64(string str) => TryDecodeBase64(str, out _);

    private static (string filename, string mimeType) DetectFileType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4)
        {
            // PNG
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            {
                return ("converted_file.png", "image/png");
            }
            // JPEG
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            {
                return ("converted_file.jpg", "image/jpeg");
            }
            // GIF
            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
            {
                return ("converted_file.gif", "image/gif");
            }
            // PDF
            if (data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46)
            {
                return ("converted_file.pdf", "application/pdf");
            }
            // ZIP (also docx/xlsx/pptx)
            if (data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04)
            {
                return ("converted_file.zip", "application/zip");
            }
        }

        // JSON: first non-whitespace byte. This used to also run a full JsonDocument.Parse over
        // the payload, but both the success and failure branches returned "json", so the parse
        // could never change the answer -- it was pure work. Dropping it also removes the 10MB
        // ceiling that only existed to bound that parse.
        var limit = Math.Min(data.Length, 100);
        for (var i = 0; i < limit; i++)
        {
            var b = data[i];
            if (b == 0x20 || b == 0x09 || b == 0x0A || b == 0x0D)
            {
                continue;
            }

            return b is 0x7B or 0x5B
                ? ("converted_file.json", "application/json")
                : UnknownType;
        }

        return UnknownType;
    }

    public List<DetectedFile> FindBase64InJson(string jsonContent)
    {
        var detectedFiles = new List<DetectedFile>();

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            FindBase64InElement(doc.RootElement, "", detectedFiles);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Invalid JSON format");
        }

        return detectedFiles;
    }

    private void FindBase64InElement(JsonElement element, string path, List<DetectedFile> detectedFiles)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var strValue = element.GetString();

                // Single decode: validates the string and produces the bytes used for both
                // type detection and the eventual download.
                if (!TryDecodeBase64(strValue, out var fileData) || fileData.Length < MinDetectedFileBytes)
                {
                    break;
                }

                var detected = DetectFileType(fileData);
                detectedFiles.Add(new DetectedFile
                {
                    Base64Value = strValue!,
                    FileData = fileData,
                    Filename = detected.filename,
                    MimeType = detected.mimeType,
                    Path = string.IsNullOrEmpty(path) ? "root" : path,
                    Size = fileData.Length
                });
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var newPath = string.IsNullOrEmpty(path) ? $"[{index}]" : $"{path}[{index}]";
                    FindBase64InElement(item, newPath, detectedFiles);
                    index++;
                }
                break;

            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var newPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
                    FindBase64InElement(prop.Value, newPath, detectedFiles);
                }
                break;
        }
    }

    public string GenerateJsonWithSelectedBase64(string originalJson, List<string> selectedBase64Strings)
    {
        try
        {
            using var doc = JsonDocument.Parse(originalJson);
            var originalJsonElement = doc.RootElement;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();

                writer.WritePropertyName("selectedBase64Strings");
                writer.WriteStartArray();
                foreach (var base64 in selectedBase64Strings)
                {
                    writer.WriteStringValue(base64);
                }
                writer.WriteEndArray();

                writer.WritePropertyName("originalJson");
                originalJsonElement.WriteTo(writer);

                writer.WriteEndObject();
            }

            // GetBuffer avoids a second full-length copy of what can be a very large payload.
            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Invalid JSON format");
        }
    }
}

public class DetectedFile
{
    public string Base64Value { get; set; } = "";
    public byte[] FileData { get; set; } = Array.Empty<byte>();
    public string Filename { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }
}
