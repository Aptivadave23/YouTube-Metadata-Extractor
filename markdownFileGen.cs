using System.Text;
using YouTubeMetadataExtractor;
namespace MarkdownFileGen;

public static class Markdown
{
    public static async Task<string> WriteMarkdownFileAsync(
        YouTubeMetadata metadata,
        string outputDirectory
    )
    {
        try 
        {
            Directory.CreateDirectory(outputDirectory);

            var fileName = GetSafeFileName($"{metadata.PublishedAt:yyyy-MM-dd} - {metadata.Title}.md");
            var filePath = Path.Combine(outputDirectory, fileName);
            var markdownContent = BuildMarkdown(metadata);

            await File.WriteAllTextAsync(filePath, markdownContent, Encoding.UTF8);
            return filePath;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"No permission to write to '{outputDirectory}'.",
                ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Failed to write markdown output to '{outputDirectory}'.",
                ex);
        }
        
    }
    
    private static string BuildMarkdown(YouTubeMetadata metadata)
    {
        var sb = new StringBuilder();

        sb.AppendLine("---");
        sb.AppendLine($"source: youtube");
        sb.AppendLine($"title:  {YamlQuote(metadata.Title)}");
        sb.AppendLine($"channel: {YamlQuote(metadata.ChannelTitle)}");
        sb.AppendLine($"published:  {metadata.PublishedAt:yyyy-MM-dd}");
        sb.AppendLine($"duration:  {metadata.Duration}");
        sb.AppendLine($"url:  {metadata.VideoUrl}");

        //get the thunbnail url if it exists
        if(!string.IsNullOrWhiteSpace(metadata.ThumbnailUrl)){ sb.AppendLine($"thumbnail:  {YamlQuote(metadata.ThumbnailUrl)}"); }
        
        sb.AppendLine("tags:");
        sb.AppendLine("  - youtube");
        sb.AppendLine("  - clipped");
        sb.AppendLine("---");
        sb.AppendLine();

        sb.AppendLine($"# {metadata.Title}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(metadata.ThumbnailUrl))
        {
            sb.AppendLine($"![Thumbnail]({metadata.ThumbnailUrl})");
            sb.AppendLine();
        }

        sb.AppendLine("## About");
        sb.AppendLine();
        sb.AppendLine($"- **Chanel:** {metadata.ChannelTitle}");
        sb.AppendLine($"- **Published:** {metadata.PublishedAt:yyyy-MM-dd}");
        sb.AppendLine($"- **Duration:** {metadata.Duration}");
        sb.AppendLine($"- **URL:** {metadata.VideoUrl}");
        sb.AppendLine();

        sb.AppendLine("## Description");
        sb.AppendLine();
        sb.AppendLine(metadata.Description ?? string.Empty);
        sb.AppendLine();

        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine();

        sb.AppendLine("## Transcript");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(metadata.Transcript))
        {
            sb.AppendLine(metadata.Transcript.Trim());
        }
        else
        {
            sb.AppendLine("_Transcript not available._");
        }

        return sb.ToString();
    }
    // helpers
    private static string YamlQuote(string? value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string GetSafeFileName(string fileName)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '-');
        }
        return fileName;
    }
}