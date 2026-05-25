using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Transcript;

public static class TranscriptCleaner
{
    private sealed record VttCue(TimeSpan Start, TimeSpan End, string Text);
    private sealed record TranscriptSegment(TimeSpan Start, string Text);
    private sealed record TranscriptChunk(TimeSpan Start, string Text);


    public static string ConvertVttToMarkdown(
        string vtt,
        string videoId,
        int chunkSeconds = 60
    )
    {
        var cues = ParseVttCues(vtt);
        var segments = BuildDedupedSegments(cues);
        var chunks = BuildTranscriptChunks(
            segments,
            TimeSpan.FromSeconds(chunkSeconds)
        );

        var markdown = new StringBuilder();

        foreach (var chunk in chunks)
        {
            var seconds = (int)Math.Floor(chunk.Start.TotalSeconds);
            var timestamp = FormatTimestamp(chunk.Start);
            var youtubeUrl = $"https://www.youtube.com/watch?v={videoId}&t={seconds}s";

            markdown
                .Append('[')
                .Append(timestamp)
                .Append("])")
                .Append(youtubeUrl)
                .Append(") ")
                .AppendLine(chunk.Text)
                .AppendLine();
        }

        return markdown.ToString().TrimEnd();
    }

    private static IReadOnlyList<VttCue> ParseVttCues(string vtt)
    {
        var cues = new List<VttCue>();

        var lines = vtt
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');

        TimeSpan? start = null;
        TimeSpan? end = null;

        var textLines = new List<string>();

        void FlushCue()
        {
            if (start is not null && textLines.Count > 0)
            {
                var text = CleanVttText(string.Join(" ", textLines));

                if (!string.IsNullOrWhiteSpace(text))
                {
                    cues.Add(new VttCue(start.Value, end ?? start.Value, text));
                }
            }

            start = null;
            end = null;
            textLines.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.Length == 0)
            {
                FlushCue();
                continue;
            }

            if (line.Equals("WEBVTT", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Kind:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Language:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("STYLE", StringComparison.InvariantCultureIgnoreCase))
            {
                continue;
            }

            if (line.Contains("-->"))
            {
                FlushCue();

                var parts = line.Split("-->", 2, StringSplitOptions.TrimEntries);

                start = ParseVttTimestamp(parts[0]);

                var endToken = parts[1]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

                end = endToken is null ? start : ParseVttTimestamp(endToken);

                continue;
            }

            if (start is not null) { textLines.Add(line); }

        }

        FlushCue();
        return cues;
    }

    private static string CleanVttText(string text)
    {
        // Remove HTML tags
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    private static IReadOnlyList<TranscriptSegment> BuildDedupedSegments(IReadOnlyList<VttCue> cues)
    {
        var segments = new List<TranscriptSegment>();
        var emmittedWords = new List<string>();

        foreach (var cue in cues)
        {
            var words = cue.Text
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            if (words.Length == 0) continue;

            var overlap = FindLargestOverlap(emmittedWords, words);

            var deltaWords = words.Skip(overlap).ToArray();

            if( deltaWords.Length == 0) continue;

            var deltaText = string.Join(" ", deltaWords);

            segments.Add(new TranscriptSegment(cue.Start, deltaText));
            emmittedWords.AddRange(deltaWords);
        }
        return segments;
    }

    private static int FindLargestOverlap(
        IReadOnlyList<string> emittedWords,
        IReadOnlyList<string> incomingWords
    )
    {
        var max = Math.Min(emittedWords.Count, incomingWords.Count);
        for (var length = max; length > 0; length--)
        {
            var matches = true;
            for (var i = 0; i < length; i++)
            {
                var emittedIndex = emittedWords.Count - length + i;

                if(!string.Equals(
                    emittedWords[emittedIndex],
                    incomingWords[i],
                    StringComparison.Ordinal)
                )
                {
                    matches = false;
                    break;
                }
            }

            if(matches) return length;
        }
        return 0;
    }

    private static IReadOnlyList<TranscriptChunk> BuildTranscriptChunks(
        IReadOnlyList<TranscriptSegment> segments,
        TimeSpan chunkSize)
    {
        var chunks = new List<TranscriptChunk>();

        TimeSpan? currentStart = null;
        var parts = new List<string>();

        void Flush()
        {
            if (currentStart is null || parts.Count == 0) return;

            var text = Regex.Replace(
                string.Join(" ", parts),
                @"\s+",
                " ")
                .Trim();
                
                if (!string.IsNullOrWhiteSpace(text)){ chunks.Add(new TranscriptChunk(currentStart.Value, text));}

                parts.Clear();
        }

        foreach (var segment in segments)
        {
            currentStart ??= FloorToChunk(segment.Start, chunkSize);

            if(segment.Start >= currentStart.Value + chunkSize && parts.Count > 0)
            {
                Flush();
                currentStart = FloorToChunk(segment.Start, chunkSize);
            }

            parts.Add(segment.Text);
        }

        Flush();
        return chunks;
    }

    private static TimeSpan FloorToChunk(TimeSpan time, TimeSpan chunkSize)
    {
        var chunkSeconds = chunkSize.TotalSeconds;

        var flooredSeconds = 
            Math.Floor(time.TotalSeconds /chunkSeconds) * chunkSeconds;

        return TimeSpan.FromSeconds(flooredSeconds);
    }
    

    private static TimeSpan ParseVttTimestamp(string value)
    {
        value = value.Trim();

        var token = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .First();

        var formats = new[]
        {
            @"h\:mm\:ss\.fff",
            @"@hh\:mm\:ss\.fff",
            @"m\:ss\.fff",
            @"mm\:ss\.fff"
        };

        if (TimeSpan.TryParseExact(
            token,
            formats,
            CultureInfo.InvariantCulture,
            out var result))
        {
            return result;
        }
        
        throw new FormatException($"Invalid VTT timestamp:  {value}");
    }

    private static string FormatTimestamp(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? time.ToString(@"h\:MM\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }
}