using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using System.Text.RegularExpressions;
using Transcript;

namespace YouTubeMetadataExtractor;

public class YouTubeMetadata
{
    [Required]
    public required string Title { get; set; }
    [Required]
    public required string Description { get; set; }
    [Required]
    public TimeSpan Duration { get; set; }

    [Required]
    public required string ChannelTitle { get; set; }

    [Required]
    public DateTimeOffset PublishedAt { get; set; }

   
    public string? ThumbnailUrl { get; set; }

    [Required]
    public required string VideoUrl { get; set; }
    public string? Transcript { get; set; }

    private sealed record YtDlpOptions(
        string VideoUrl,
        string OutputDirectory,
        string Language = "en",
        bool WriteAutoSubs = true,
        bool UseImpersonation = true,
        bool AllowInsecureSsl = false
    );

    public static async Task<YouTubeMetadata> GetYouTubeMetadataAsync(string videoUrl)
    {
        string? vttFile = null;

        // During debugging, set this to true
        // Later, flip to false to avoid leaving temp files around
        var keepVttFile = false;

        try 
        {
            var videoId = GetVideoIdFromUrl(videoUrl);

            //get the API key from the environment variable
            var apiKey = Environment.GetEnvironmentVariable("YOUTUBE_API_KEY")
                ?? throw new InvalidOperationException("Missing YOUTUBE_API_KEY.");

            var youtube = new YouTubeService(new BaseClientService.Initializer
            {
                ApiKey = apiKey,
                ApplicationName = "YouTubeMarkdownExtractor"
            });

            
            var request = youtube.Videos.List("snippet,contentDetails,statistics");
            request.Id = videoId;

            var response = await request.ExecuteAsync();

            var video = response.Items.FirstOrDefault();
            if (video is null)
            {
                throw new InvalidOperationException("Video not found.");
            }

            //set workDir to the temp directory in this project
            var workDir = Path.Combine(
                Directory.GetCurrentDirectory(),
                "temp"
            );

            Directory.CreateDirectory(workDir);

            // Clean up any existing .vtt files for this video ID before starting
            DeleteMatchingVttFiles(workDir, videoId);

            // set YtDlpOptions for transcript extraction
             var options = new YtDlpOptions(
                VideoUrl: $"https://www.youtube.com/watch?v={video.Id}",
                OutputDirectory: workDir,
                AllowInsecureSsl: true
            );

            var startInfo = BuildYtDlpProcessStartInfo(options);

            vttFile = await RunTDlpAndReadTranscriptAsync(startInfo, workDir);

            string? transcript = null;

            if (vttFile is not null)
            {
                var vttContents = await File.ReadAllTextAsync(vttFile);

                var transcriptMarkdown = TranscriptCleaner.ConvertVttToMarkdown(vttContents, video.Id, chunkSeconds: 60);

                transcript = transcriptMarkdown.ToString();
            }

            return new YouTubeMetadata
            {
                Title = video.Snippet.Title,
                Description = video.Snippet.Description,
                Duration = System.Xml.XmlConvert.ToTimeSpan(video.ContentDetails.Duration),
                ChannelTitle = video.Snippet.ChannelTitle,
                PublishedAt = video.Snippet.PublishedAtDateTimeOffset ?? DateTime.MinValue,
                ThumbnailUrl = video.Snippet.Thumbnails?.Default__?.Url,
                VideoUrl = $"https://www.youtube.com/watch?v={video.Id}",
                Transcript = transcript
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting metadata: {ex.Message}");
            throw;
        }
        finally
        {
            if (!keepVttFile && vttFile is not null)
            {
                TryDeletFile(vttFile);
            }
        }
    }   

    private static string GetVideoIdFromUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input was empty.");

        input = input.Trim();

        // Handle shell-escaped URLs (e.g., "https://www.youtube.com/watch?v=VIDEO_ID" or 'https://www.youtube.com/watch?v=VIDEO_ID')
        input = Regex.Replace(input, @"\\([?&=])", "$1");

        // Allow raw YouTube video IDs.
        if (input.Length == 11 && !input.Contains('/'))
            return input;

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
            throw new ArgumentException("Input is not a valid URL.");

        var host = uri.Host.ToLowerInvariant();

        // Short links: https://youtu.be/VIDEO_ID
        if (host == "youtu.be")
        {
            var id = uri.AbsolutePath.Trim('/');
            if (!string.IsNullOrWhiteSpace(id))
                return id;
        }

        // Regular links: https://www.youtube.com/watch?v=VIDEO_ID
        if (host.EndsWith("youtube.com"))
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var v = query["v"];

            if (!string.IsNullOrWhiteSpace(v))
                return v;

            // Shorts or embed links:
            // /shorts/VIDEO_ID
            // /embed/VIDEO_ID
            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 2 &&
                (segments[0] == "shorts" || segments[0] == "embed"))
            {
                return segments[1];
            }
        }

        throw new ArgumentException("Could not find a YouTube video ID.");
    }


    private static IReadOnlyList<string> BuildYtdlpArguments(YtDlpOptions options)
{
    var args = new List<string>
    {
        "--no-playlist",
        "--skip-download"
    };

    args.Add(options.WriteAutoSubs ? "--write-auto-subs" : "--write-subs");

    args.Add("--sub-langs");
    args.Add(options.Language);

    args.Add("--sub-format");
    args.Add("vtt");

    args.Add("--sleep-subtitles");
    args.Add("5");

    args.Add("--sleep-requests");
    args.Add("1");

    if (options.UseImpersonation)
    {
        args.Add("--impersonate");
        args.Add("chrome-136:macos-15");
    }

    if (options.AllowInsecureSsl)
    {
        args.Add("--no-check-certificates");
    }

    args.Add("-o");
    args.Add(Path.Combine(options.OutputDirectory, "%(id)s.%(ext)s"));

    args.Add(options.VideoUrl);

    return args;
}

    private static ProcessStartInfo BuildYtDlpProcessStartInfo(YtDlpOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in BuildYtdlpArguments(options))
        {
            startInfo.ArgumentList.Add(arg);
        }
        return startInfo;
    }

    private static async Task<string?> RunTDlpAndReadTranscriptAsync(
        ProcessStartInfo startInfo,
        string workDir)
    {
        using var process = new Process { StartInfo = startInfo };

        if (!process.Start()) { throw new InvalidOperationException("Failed to start yt-dlp process."); }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"yt-dlp failed with exit code {process.ExitCode}.{Environment.NewLine}{stderr}"
            );
        }

        return Directory
            .EnumerateFiles(workDir, "*.vtt", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        
    }

    private static void DeleteMatchingVttFiles(string workDir, string videoId)
    {
        foreach (var file in Directory.EnumerateFiles(workDir, $"{videoId}*.vtt")) { TryDeletFile(file);}
    }

    private static void TryDeletFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath)){ File.Delete(filePath); }
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to delete file {filePath}: {ex.Message}", ex);
        }
    }
}



