using Figgle;
using Figgle.Fonts;
using YouTubeMetadataExtractor;
using MarkdownFileGen;
using Spectre.Console;

//clear console
Console.Clear();

//show banner
RenderBannerByWord();
//get the video info
// get the video url from the command line arguments
if (args.Length == 0)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Usage:  dotnet run -- \"<video_url_or_id>\" --out \"<output_folder>\"");
    Console.ResetColor();
    return;
}
try
{
    YouTubeMetadata? video = null;
    string? markdownFile = null;
    //get the video url from the command line arguments
    var videoUrl = args[0];
    //get the output folder from the command line arguments
    var outputFolder = args.Length > 1 ? args[1] :
        Path.Combine(Directory.GetCurrentDirectory(), "output");

    
    Console.WriteLine(outputFolder);

    // start process indicator
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("green"))
        .StartAsync("Extracting YouTube metadata and transcript...", async ctx =>
        {
            video = await YouTubeMetadata.GetYouTubeMetadataAsync(videoUrl);

            ctx.Status("Generating markdown file...");

           
            markdownFile = await Markdown.WriteMarkdownFileAsync(video, outputFolder);
        });
    AnsiConsole.MarkupLine($"[green]Markdown file generated at:[/] [blue]{markdownFile}[/]");    

}
catch (Exception ex)
{
    AnsiConsole.MarkupLine(
        $"[red]Error:[/] {Markup.Escape(ex.Message)}");
}



static void RenderBannerByWord()
{
    var words = new[]
    {
        ("YoutTube", "red"),
        ("Metadata", "green"),
        ("Extractor", "blue")
    };

    var renderedWords = words
        .Select(w => new
        {
            Word = w.Item1,
            Color = w.Item2,
            Lines = FiggleFonts.Slant
                .Render(w.Item1)
                .Split('\n')
        }).ToList();

    var height = renderedWords.Max(w => w.Lines.Length);

    for (var row = 0; row < height; row++)
    {
        foreach (var word in renderedWords)
        {
            var line = row < word.Lines.Length ? word.Lines[row] : string.Empty;

            AnsiConsole.Markup($"[bold {word.Color}]{line}[/]");
        }

        AnsiConsole.WriteLine();
    }
}

