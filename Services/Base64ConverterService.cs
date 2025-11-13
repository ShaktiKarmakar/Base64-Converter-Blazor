using System.Text;
using System.Text.Json;

namespace Base64AppBlazor.Services;

public class Base64ConverterService
{
    private const int MaxFileSize = 50 * 1024 * 1024; // 50MB

    public async Task<(string base64, string filename, long size)> FileToBase64Async(Stream fileStream, string filename)
    {
        if (fileStream.Length > MaxFileSize)
        {
            throw new Exception($"File too large. Maximum size is {MaxFileSize / (1024 * 1024)}MB");
        }

        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream);
        var fileBytes = memoryStream.ToArray();
        var base64String = Convert.ToBase64String(fileBytes);

        return (base64String, filename, fileBytes.Length);
    }

    public (byte[] fileData, string filename, string mimeType) Base64ToFile(string base64String, string? filename = null)
    {
        // Remove data URL prefix if present
        if (base64String.Contains(','))
        {
            base64String = base64String.Split(',')[1];
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

        return ("converted_file", "application/octet-stream");
    }

    public bool IsValidBase64(string str)
    {
        if (string.IsNullOrWhiteSpace(str) || str.Length < 4)
            return false;

        // Remove data URL prefix if present
        var cleanStr = str.Contains(',') ? str.Split(',')[1] : str;

        // Base64 regex pattern
        var base64Regex = new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9+/]*={0,2}$");
        if (!base64Regex.IsMatch(cleanStr))
            return false;

        // Try to decode it
        try
        {
            var decoded = Convert.FromBase64String(cleanStr);
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
                var strValue = element.GetString() ?? "";
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
                            Path = path == "" ? "root" : path,
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
                    var newPath = path == "" ? $"[{index}]" : $"{path}[{index}]";
                    FindBase64InElement(item, newPath, detectedFiles);
                    index++;
                }
                break;

            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var newPath = path == "" ? prop.Name : $"{path}.{prop.Name}";
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

