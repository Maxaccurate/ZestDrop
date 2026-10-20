using System;

namespace ZestDrop.Cli;

public static class CliHelp
{
    public const string Version = "1.0.0";

    public static string Text(string? command = null)
    {
        return command?.ToLowerInvariant() switch
        {
            "convert" => ConvertHelp,
            "watch" => WatchHelp,
            "formats" => FormatsHelp,
            _ => GeneralHelp
        };
    }

    private const string GeneralHelp = """
        ZestDrop - Drag. Drop. Convert. Desktop & CLI File Conversion Suite.
        Version 1.0.0

        USAGE:
          zestdrop <command> [arguments...] [options...]

        COMMANDS:
          convert   Convert one or more files to a target format
          watch     Monitor a folder and convert incoming files automatically
          formats   List supported file formats and categories
          help      Show this help message or help for a specific command
          version   Show the ZestDrop version

        EXAMPLES:
          zestdrop convert model.step --to stl
          zestdrop convert drawing.dwg -t pdf -o ./output
          zestdrop convert table.csv -t xlsx
          zestdrop convert book.epub -t mobi
          zestdrop convert ./photos -r -t webp
          zestdrop watch ./inbox -t png -s -m
          zestdrop formats --category cad

        Use 'zestdrop help <command>' for more information about a command.
        """;

    private const string ConvertHelp = """
        zestdrop convert - Convert files to a target format

        USAGE:
          zestdrop convert <file|directory...> --to <format> [options]

        OPTIONS:
          --to, -t <ext>         Target format extension (required, e.g. pdf, stl, xlsx, docx)
          --output, -o <dir>     Output directory (defaults to each input file's folder)
          --recursive, -r        Search input directories recursively for files
          --json                 Output conversion result as a structured JSON object
          --quiet, -q            Suppress informational messages and progress

        EXAMPLES:
          zestdrop convert sample.dwg -t svg
          zestdrop convert notes.md -t docx
          zestdrop convert subs.smi -t ass
          zestdrop convert doc.docx -t html
          zestdrop convert *.csv -t xlsx -o ./spreadsheets
        """;

    private const string WatchHelp = """
        zestdrop watch - Automatically convert files added to a folder

        USAGE:
          zestdrop watch <folder> --to <format> [options]

        OPTIONS:
          --to, -t <ext>         Target format extension (required)
          --output, -o <dir>     Output folder (defaults to <folder>/converted)
          --subfolders, -s       Include and watch subdirectories recursively
          --move-originals, -m   Move converted source files to <folder>/originals
          --quiet, -q            Suppress output

        EXAMPLES:
          zestdrop watch "C:\DropBox\CAD" -t pdf -s
          zestdrop watch "C:\DropBox\Images" -t webp -m
        """;

    private const string FormatsHelp = """
        zestdrop formats - List supported conversion formats

        USAGE:
          zestdrop formats [options]

        OPTIONS:
          --category, -c <name>  Filter by category: 3d, cad, data, fonts, ebooks, office, subtitles, images, audio, video
          --json                 Output format list as JSON
        """;
}
