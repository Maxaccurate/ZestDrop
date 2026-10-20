using System;
using System.Collections.Generic;
using System.IO;

namespace ZestDrop.Cli;

public sealed class CliOptions
{
    public string Command { get; init; } = "help";
    public List<string> Inputs { get; set; } = [];
    public string? TargetFormat { get; set; }
    public string? OutputFolder { get; set; }
    public bool Recursive { get; set; }
    public bool Json { get; set; }
    public bool Quiet { get; set; }
    public bool Subfolders { get; set; }
    public bool MoveOriginals { get; set; }
    public string? Category { get; set; }
    public string? Error { get; set; }

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0)
            return new CliOptions { Command = "help" };

        var first = args[0].ToLowerInvariant();
        if (first is "help" or "--help" or "-h" or "/?")
            return new CliOptions { Command = "help", Inputs = args.Length > 1 ? [args[1]] : [] };

        if (first is "version" or "--version" or "-v")
            return new CliOptions { Command = "version" };

        if (first is "formats" or "--formats")
        {
            var opt = new CliOptions { Command = "formats" };
            for (int i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg is "--json") opt.Json = true;
                else if (arg is "--category" or "-c" && i + 1 < args.Length) opt.Category = args[++i];
            }
            return opt;
        }

        if (first is "watch")
        {
            var opt = new CliOptions { Command = "watch" };
            for (int i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg is "--to" or "-t" && i + 1 < args.Length) opt.TargetFormat = args[++i];
                else if (arg is "--output" or "-o" && i + 1 < args.Length) opt.OutputFolder = args[++i];
                else if (arg is "--subfolders" or "-s") opt.Subfolders = true;
                else if (arg is "--move-originals" or "-m") opt.MoveOriginals = true;
                else if (arg is "--quiet" or "-q") opt.Quiet = true;
                else if (!arg.StartsWith('-')) opt.Inputs.Add(arg);
                else return new CliOptions { Error = $"Unknown watch argument: {arg}" };
            }
            if (opt.Inputs.Count == 0) opt.Error = "watch requires a folder path.";
            if (string.IsNullOrEmpty(opt.TargetFormat)) opt.Error = "watch requires a target format (--to <ext>).";
            return opt;
        }

        if (first is "convert")
        {
            var opt = new CliOptions { Command = "convert" };
            for (int i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg is "--to" or "-t" && i + 1 < args.Length) opt.TargetFormat = args[++i];
                else if (arg is "--output" or "-o" && i + 1 < args.Length) opt.OutputFolder = args[++i];
                else if (arg is "--recursive" or "-r") opt.Recursive = true;
                else if (arg is "--json") opt.Json = true;
                else if (arg is "--quiet" or "-q") opt.Quiet = true;
                else if (!arg.StartsWith('-')) opt.Inputs.Add(arg);
                else return new CliOptions { Error = $"Unknown convert argument: {arg}" };
            }
            if (opt.Inputs.Count == 0) opt.Error = "convert requires at least one input file or directory.";
            if (string.IsNullOrEmpty(opt.TargetFormat)) opt.Error = "convert requires a target format (--to <ext>).";
            return opt;
        }

        // Fallback: check if first argument is a file or target format
        return new CliOptions { Error = $"Unknown command: '{args[0]}'. Run 'zestdrop help' for usage." };
    }
}
