namespace MasterFlow.Application;

/// <summary>Turns an image file into a data: URI for embedding in reports and project files.</summary>
public static class LogoData
{
    public const int MaxBytes = 1_000_000;

    public static string FromFile(string path)
    {
        string mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            ".gif" => "image/gif",
            _ => throw new InvalidDataException("Use a PNG, JPEG, GIF or SVG image."),
        };
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaxBytes) throw new InvalidDataException($"The image is {bytes.Length / 1024} KB; the limit is {MaxBytes / 1000} KB.");
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }
}
