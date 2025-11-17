using System.Text;
using System.Text.Json;

namespace Base64AppBlazor.Services;

public class Base64ConverterService
{
    private const int MaxFileSize = 50 * 1024 * 1024; // 50MB
    private static readonly System.Text.RegularExpressions.Regex Base64Regex = new(@"^[A-Za-z0-9+/]*={0,2}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<(string base64, string filename, long size)> FileToBase64Async(Stream fileStream, string filename)
    {
        // Check stream length if available, otherwise we'll check after reading
        if (fileStream.CanSeek && fileStream.Length > MaxFileSize)
        {
            throw new Exception($"File too large. Maximum size is {MaxFileSize / (1024 * 1024)}MB");
        }

        // Pre-size MemoryStream if we know the length, otherwise use default
        int capacity = 8192;
        if (fileStream.CanSeek && fileStream.Length > 0 && fileStream.Length <= MaxFileSize)
        {
            capacity = (int)fileStream.Length;
        }

        using var memoryStream = new MemoryStream(capacity);
        await fileStream.CopyToAsync(memoryStream);
        
        // Check size after reading (in case Length wasn't available)
        if (memoryStream.Length > MaxFileSize)
        {
            throw new Exception($"File too large. Maximum size is {MaxFileSize / (1024 * 1024)}MB");
        }

        // Use ToArray() - it's optimized in .NET and returns only the used portion
        var fileBytes = memoryStream.ToArray();
        var base64String = Convert.ToBase64String(fileBytes);

        return (base64String, filename, fileBytes.Length);
    }

    public (byte[] fileData, string filename, string mimeType) Base64ToFile(string base64String, string? filename = null)
    {
        // Optimize: Use IndexOf instead of Contains + Split
        var commaIndex = base64String.IndexOf(',');
        if (commaIndex >= 0)
        {
            base64String = base64String.Substring(commaIndex + 1);
        }

        try
        {
            var fileData = Convert.FromBase64String(base64String);
            var detectedInfo = DetectFileType(fileData);
            
            var finalFilename = filename ?? detectedInfo.filename;
            var mimeType = detectedInfo.mimeType;

            return (fileData, finalFilename, mimeType);
        }
        catch
        {
            throw new Exception("Invalid base64 string");
        }
    }

    private (string filename, string mimeType) DetectFileType(byte[] data)
    {
        if (data.Length < 4)
        {
            return ("converted_file", "application/octet-stream");
        }

        // Optimized: Check binary formats first (fast path)
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
        // ZIP
        if (data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04)
        {
            return ("converted_file.zip", "application/zip");
        }

        // Optimized: Check for JSON only on smaller files and check first bytes quickly
        // JSON files start with { or [ (possibly after whitespace)
        if (data.Length < 10_000_000) // Only check JSON for files under 10MB
        {
            // Quick check: skip whitespace and see if first non-whitespace is { or [
            int startIndex = 0;
            while (startIndex < data.Length && startIndex < 100 && 
                   (data[startIndex] == 0x20 || data[startIndex] == 0x09 || 
                    data[startIndex] == 0x0A || data[startIndex] == 0x0D))
            {
                startIndex++;
            }

            if (startIndex < data.Length && (data[startIndex] == 0x7B || data[startIndex] == 0x5B))
            {
                try
                {
                    // Only parse if it looks like JSON
                    using var doc = JsonDocument.Parse(data);
                    return ("converted_file.json", "application/json");
                }
                catch
                {
                    // If parsing fails, still check if it looks like JSON structure
                    if (data[startIndex] == 0x7B || data[startIndex] == 0x5B)
                    {
                        return ("converted_file.json", "application/json");
                    }
                }
            }
        }

        return ("converted_file", "application/octet-stream");
    }

    public bool IsValidBase64(string str)
    {
        if (string.IsNullOrWhiteSpace(str) || str.Length < 4)
            return false;

        // Optimize: Use IndexOf instead of Contains + Split
        ReadOnlySpan<char> cleanStr = str;
        var commaIndex = str.IndexOf(',');
        if (commaIndex >= 0)
        {
            cleanStr = str.AsSpan(commaIndex + 1);
        }

        // Early validation: Base64 strings must have length multiple of 4 (after padding)
        // and only contain valid characters
        if (cleanStr.Length == 0)
            return false;

        // Quick check: Base64 strings are typically much longer and have specific character set
        // Skip obvious non-base64 strings early
        var hasInvalidChar = false;
        var hasValidChar = false;
        foreach (var c in cleanStr)
        {
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || 
                (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '=')
            {
                hasValidChar = true;
            }
            else if (!char.IsWhiteSpace(c))
            {
                hasInvalidChar = true;
                break;
            }
        }

        if (hasInvalidChar || !hasValidChar)
            return false;

        // Use compiled regex for better performance
        if (!Base64Regex.IsMatch(cleanStr.ToString()))
            return false;

        // Try to decode it (most expensive operation, do last)
        try
        {
            var decoded = Convert.FromBase64String(cleanStr.ToString());
            return decoded.Length > 0;
        }
        catch
        {
            return false;
        }
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
            throw new Exception("Invalid JSON format");
        }

        return detectedFiles;
    }

    private void FindBase64InElement(JsonElement element, string path, List<DetectedFile> detectedFiles)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var strValue = element.GetString();
                
                // Early exit for obviously non-base64 strings
                if (string.IsNullOrEmpty(strValue) || strValue.Length < 4)
                    break;

                if (IsValidBase64(strValue))
                {
                    try
                    {
                        var (fileData, filename, mimeType) = Base64ToFile(strValue);
                        var detectedFile = new DetectedFile
                        {
                            Base64Value = strValue,
                            FileData = fileData,
                            Filename = filename,
                            MimeType = mimeType,
                            Path = string.IsNullOrEmpty(path) ? "root" : path,
                            Size = fileData.Length
                        };
                        detectedFiles.Add(detectedFile);
                    }
                    catch
                    {
                        // Skip invalid base64
                    }
                }
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
            // Parse the original JSON to ensure it's valid
            using var doc = JsonDocument.Parse(originalJson);
            var originalJsonElement = doc.RootElement;

            // Create the new JSON structure
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                
                // Add selected base64 strings array
                writer.WritePropertyName("selectedBase64Strings");
                writer.WriteStartArray();
                foreach (var base64 in selectedBase64Strings)
                {
                    writer.WriteStringValue(base64);
                }
                writer.WriteEndArray();

                // Add original JSON at the end
                writer.WritePropertyName("originalJson");
                originalJsonElement.WriteTo(writer);
                
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            throw new Exception("Invalid JSON format");
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

