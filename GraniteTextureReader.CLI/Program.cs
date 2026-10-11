// Copyright (c) 2026 Nenkai
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

using CommandLine;

using GraniteLib;
using GraniteLib.GDEX;
using GraniteLib.TileSet;

namespace GraniteTextureReader.CLI;

public class Program
{
    public const string Version = "1.1.5";

    /// <summary>
    /// Default name of the folder extracted textures are written to.
    /// </summary>
    private const string ExtractedFolderName = "_extracted";

    //======================
    //Main Program
    //======================
    static void Main(string[] args)
    {
        Console.WriteLine("---------------------------------------------");
        Console.WriteLine($"- GraniteTextureReader {Version} by Nenkai/WistfulHopes/AlphaSatanOmega");
        Console.WriteLine("---------------------------------------------");
        Console.WriteLine("- https://github.com/Nenkai");
        Console.WriteLine("- https://github.com/WistfulHopes");
        Console.WriteLine("- https://github.com/AlphaSatanOmega");
        Console.WriteLine("---------------------------------------------");

        // Files and folders dropped onto the executable arrive as bare paths, with no verb.
        if (IsDroppedPaths(args))
        {
            try
            {
                ExtractDroppedPaths(args);
            }
            catch (Exception ex)
            {
                // Caught so the pause below still runs and the error stays readable.
                Console.WriteLine($"ERROR: {ex.Message}");
                Environment.ExitCode = 1;
            }

            PauseIfOwnConsoleWindow();
            return;
        }

        var p = Parser.Default.ParseArguments<ExtractVerbs, ExtractAllVerbs, ExtractFolderVerbs, ExtractProjectFileVerbs, DowngradeVerbs>(args);

        p.WithParsed<ExtractVerbs>(ExtractSpecific)
        .WithParsed<ExtractAllVerbs>(ExtractAll)
        .WithParsed<ExtractFolderVerbs>(ExtractFolder)
        .WithParsed<ExtractProjectFileVerbs>(ExtractProjectFile)
        .WithParsed<DowngradeVerbs>(Downgrade)
        .WithNotParsed(HandleNotParsedArgs);

        // Double-clicked from Explorer: keep the help on screen and point at drag and drop.
        if (args.Length == 0 && IsOwnConsoleWindow())
        {
            Console.WriteLine("Tip: drag and drop a .gts file, or a folder containing them, onto this executable to extract all of its textures.");
            PauseIfOwnConsoleWindow();
        }
    }

    //======================
    //Methods
    //======================

    public static void ExtractSpecific(ExtractVerbs verbs)
    {
        if (!File.Exists(verbs.TileSetPath))
        {
            Console.WriteLine($"ERROR: Tile set file '{verbs.TileSetPath}' does not exist.");
            return;
        }

        try
        {
            if (string.IsNullOrEmpty(verbs.OutputDir))
                verbs.OutputDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(verbs.TileSetPath)), "_extracted");

            Directory.CreateDirectory(verbs.OutputDir);

            var graniteProcessor = GraniteProcessor.CreateFromTileSet(verbs.TileSetPath);
            graniteProcessor.Extract(verbs.LayerToExtract, verbs.FileToExtract, verbs.OutputDir);

            Console.WriteLine($"Done. Files can be found in the \"_extracted\" folder next to {verbs.TileSetPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to extract from {verbs.TileSetPath} - {ex.Message}");
            return;
        }
    }

    public static void ExtractAll(ExtractAllVerbs verbs)
    {
        if (Directory.Exists(verbs.TileSetPath))
        {
            ExtractFolder(new ExtractFolderVerbs
            {
                InputDir = verbs.TileSetPath,
                LayerToExtract = verbs.LayerToExtract,
                OutputDir = verbs.OutputDir,
            });
            return;
        }

        if (!ConfirmExtraction())
            return;

        if (!File.Exists(verbs.TileSetPath))
        {
            Console.WriteLine($"ERROR: Tile set file '{verbs.TileSetPath}' does not exist.");
            return;
        }

        if (string.IsNullOrEmpty(verbs.OutputDir))
            verbs.OutputDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(verbs.TileSetPath)), "_extracted");

        Directory.CreateDirectory(verbs.OutputDir);

        try
        {
            var graniteProcessor = GraniteProcessor.CreateFromTileSet(verbs.TileSetPath);
            graniteProcessor.ExtractAll(verbs.LayerToExtract, verbs.OutputDir);

            Console.WriteLine($"Done. Files can be found in the \"_extracted\" folder next to {verbs.TileSetPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to extract from {verbs.TileSetPath} - {ex.Message}");
            return;
        }
    }

    public static void ExtractFolder(ExtractFolderVerbs verbs)
    {
        if (!Directory.Exists(verbs.InputDir))
        {
            Console.WriteLine($"ERROR: Folder '{verbs.InputDir}' does not exist.");
            Environment.ExitCode = 1;
            return;
        }

        string inputDir = Path.GetFullPath(verbs.InputDir);
        string outputDir = string.IsNullOrEmpty(verbs.OutputDir)
            ? Path.Combine(inputDir, ExtractedFolderName)
            : Path.GetFullPath(verbs.OutputDir);

        List<ExtractionJob> jobs = FindTileSets(inputDir, outputDir, recursive: !verbs.TopLevelOnly);
        if (jobs.Count == 0)
        {
            Console.WriteLine($"ERROR: No .gts tile set files found in '{inputDir}'{(verbs.TopLevelOnly ? "" : " or its sub-directories")}.");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Found {jobs.Count} tile set file(s) in '{inputDir}'.");
        if (!verbs.AssumeYes && !ConfirmExtraction())
            return;

        ExtractTileSets(jobs, verbs.LayerToExtract);
    }

    /// <summary>
    /// Handles paths given without a verb, which is how Windows passes files and folders dropped onto
    /// the executable. All layers are extracted, as there is no way to pass options this way.
    /// </summary>
    public static void ExtractDroppedPaths(string[] paths)
    {
        var jobs = new List<ExtractionJob>();
        foreach (string path in paths)
        {
            string fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                List<ExtractionJob> found = FindTileSets(fullPath, Path.Combine(fullPath, ExtractedFolderName), recursive: true);
                if (found.Count == 0)
                    Console.WriteLine($"No .gts tile set files found in '{fullPath}' or its sub-directories.");

                jobs.AddRange(found);
            }
            else if (Path.GetExtension(fullPath).Equals(".gts", StringComparison.OrdinalIgnoreCase))
            {
                string outputDir = Path.Combine(Path.GetDirectoryName(fullPath)!, ExtractedFolderName, Path.GetFileNameWithoutExtension(fullPath));
                jobs.Add(new ExtractionJob(fullPath, outputDir));
            }
            else if (Path.GetExtension(fullPath).Equals(".gtp", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Skipping '{fullPath}': .gtp page files are read through their .gts tile set. Drop the .gts file or its folder instead.");
            }
            else
            {
                Console.WriteLine($"Skipping '{fullPath}': not a .gts tile set file or a folder.");
            }
        }

        // A tile set can be dropped alongside the folder that contains it.
        jobs = jobs.DistinctBy(job => job.TileSetPath, PathComparer).ToList();
        if (jobs.Count == 0)
        {
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Found {jobs.Count} tile set file(s). All texture layers will be extracted.");
        if (!ConfirmExtraction())
            return;

        ExtractTileSets(jobs, layer: -1);
    }

    public static void ExtractProjectFile(ExtractProjectFileVerbs verbs)
    {
        if (!File.Exists(verbs.TileSetPath))
        {
            Console.WriteLine($"ERROR: Tile set file '{verbs.TileSetPath}' does not exist.");
            return;
        }

        var tileSetFile = new TileSetFile();
        try
        {
            tileSetFile.Initialize(verbs.TileSetPath);
            var project = tileSetFile.GetProjectFile();
            if (string.IsNullOrEmpty(project))
            {
                Console.WriteLine($"No project file in tile set.");
                return;
            }

            string projPath = Path.GetDirectoryName(Path.GetFullPath(verbs.TileSetPath));
            string outputPath = Path.Combine(projPath, "proj.xml");
            File.WriteAllText(outputPath, project);

            Console.WriteLine($"Project file extracted to {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to extract from {verbs.TileSetPath} - {ex.Message}");
            return;
        }
    }

    public static void Downgrade(DowngradeVerbs verbs)
    {
        if (!File.Exists(verbs.TileSetPath))
        {
            Console.WriteLine($"ERROR: Tile set file '{verbs.TileSetPath}' does not exist.");
            return;
        }

        var tileSetFile = new TileSetFile();
        try
        {
            // Downgrade compatibility & build version. These are checked.
            tileSetFile.Initialize(verbs.TileSetPath);
            if (tileSetFile.Version == 5)
            {
                Console.WriteLine("ERROR: Tile set file is already version 5.");
                return;
            }

            if (tileSetFile.Version < 5)
            {
                Console.WriteLine("ERROR: Tile set under version 5 is not supported.");
                return;
            }

            GDEXItem info = tileSetFile.Metadata[GDEXTags.Information];
            GDEXItem comp = info[GDEXTags.Compatibility];

            GDEXItem compWith = comp[GDEXTags.CompatibleWith];
            compWith[GDEXTags.VersionMajor].SetInt(5);
            compWith[GDEXTags.VersionMinor].SetInt(0);

            GDEXItem buildVersion = comp[GDEXTags.BuildVersion];
            buildVersion[GDEXTags.VersionMajor].SetInt(5);
            buildVersion[GDEXTags.VersionMinor].SetInt(0);

            string dir = Path.GetDirectoryName(Path.GetFullPath(verbs.TileSetPath));

            Console.Write("GTP files needs to be overwritten and aligned to page size. Proceed? [y/n]");
            if (Console.ReadKey().Key != ConsoleKey.Y)
            {
                Console.WriteLine();
                Console.WriteLine("Aborted.");
                return;
            }

            // Align all GTP files to page size. Version 6 seems to have removed this requirement
            foreach (var file in Directory.GetFiles(dir, "*.gtp", SearchOption.TopDirectoryOnly))
            {
                Console.WriteLine($"Padding {Path.GetFileName(file)} to page size, required for V5 (0x{tileSetFile.CustomPageSize:X8})");
                using var fs = File.OpenWrite(file);
                fs.Position = fs.Length;
                Syroot.BinaryData.StreamExtensions.Align(fs, tileSetFile.CustomPageSize, grow: true);
                fs.Position -= 1;
                fs.WriteByte(0); // Just incase it doesn't actually add the bytes at the end of the file
            }

            // Output new .gts file with the appropriate version.
            string outputFile = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(verbs.TileSetPath)}_v5.gts");
            using (var output = new FileStream(outputFile, FileMode.Create))
                tileSetFile.Write(output, 5);

            Console.WriteLine($"Done, saved as '{outputFile}'.");
            Console.WriteLine($"NOTE: Thumbnail data omitted as it is not yet supported, but the official tile set viewer should read the file just fine.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to extract from {verbs.TileSetPath} - {ex.Message}");
            return;
        }
    }

    public static void HandleNotParsedArgs(IEnumerable<Error> errors)
    {

    }

    //======================
    //Helpers
    //======================

    private readonly record struct ExtractionJob(string TileSetPath, string OutputDir);

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Finds every .gts file in a folder. Each gets its own output folder, laid out like the source
    /// tree, as tile sets sharing a folder can hold textures with the same names.
    /// </summary>
    private static List<ExtractionJob> FindTileSets(string inputDir, string outputDir, bool recursive)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            MatchCasing = MatchCasing.CaseInsensitive,
            IgnoreInaccessible = true,
        };

        return Directory.EnumerateFiles(inputDir, "*.gts", options)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(tileSetPath =>
            {
                string relativeDir = Path.GetRelativePath(inputDir, Path.GetDirectoryName(tileSetPath)!);
                string tileSetOutputDir = Path.Combine(outputDir, relativeDir, Path.GetFileNameWithoutExtension(tileSetPath));
                return new ExtractionJob(tileSetPath, Path.GetFullPath(tileSetOutputDir));
            })
            .ToList();
    }

    /// <summary>
    /// Extracts each tile set in turn. A tile set that fails is reported and skipped rather than
    /// stopping the rest.
    /// </summary>
    private static void ExtractTileSets(List<ExtractionJob> jobs, int layer)
    {
        var failures = new List<string>();
        for (int i = 0; i < jobs.Count; i++)
        {
            ExtractionJob job = jobs[i];
            Console.WriteLine();
            Console.WriteLine($"[Tile set {i + 1}/{jobs.Count}] {job.TileSetPath}");

            try
            {
                // Disposing closes the tile set's .gtp files before moving on to the next one.
                using var graniteProcessor = GraniteProcessor.CreateFromTileSet(job.TileSetPath);

                // Created after parsing so that unreadable tile sets don't leave empty folders behind.
                Directory.CreateDirectory(job.OutputDir);
                graniteProcessor.ExtractAll(layer, job.OutputDir);

                Console.WriteLine($"Extracted to {job.OutputDir}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Failed to extract from {job.TileSetPath} - {ex.Message}");
                failures.Add($"{job.TileSetPath} - {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("---------------------------------------------");
        Console.WriteLine($"Done. Extracted {jobs.Count - failures.Count} of {jobs.Count} tile set(s).");
        if (failures.Count > 0)
        {
            Console.WriteLine($"{failures.Count} failed:");
            foreach (string failure in failures)
                Console.WriteLine($"  - {failure}");

            Environment.ExitCode = 1;
        }
    }

    private static bool ConfirmExtraction()
    {
        Console.Write("!!!Warning!!!" +
                      "\nExtracting all the textures of a .gts takes up a lot of disk space." +
                      "\nMake sure you have enough extra space reserved on the same drive." +
                      "\nAre you sure you want to continue?. [y/n]");

        bool confirmed;
        if (Console.IsInputRedirected)
        {
            // Console.ReadKey throws when input is piped, e.g. `echo y | GraniteTextureReader ...`
            string? answer = Console.ReadLine();
            confirmed = answer is not null && answer.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            confirmed = Console.ReadKey().Key == ConsoleKey.Y;
        }

        Console.WriteLine();
        if (!confirmed)
            Console.WriteLine("Aborted.");

        return confirmed;
    }

    private static bool IsDroppedPaths(string[] args)
    {
        return args.Length > 0 && args.All(arg => File.Exists(arg) || Directory.Exists(arg));
    }

    /// <summary>
    /// Whether Windows created the console for this process alone, which happens when it is started
    /// from Explorer (double-click or drag and drop). That window closes as soon as the process exits.
    /// </summary>
    private static bool IsOwnConsoleWindow()
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected || Console.IsOutputRedirected)
            return false;

        var processIds = new uint[2];
        return GetConsoleProcessList(processIds, (uint)processIds.Length) == 1;
    }

    private static void PauseIfOwnConsoleWindow()
    {
        if (!IsOwnConsoleWindow())
            return;

        Console.WriteLine();
        Console.Write("Press any key to exit...");
        Console.ReadKey(intercept: true);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    //======================
    //Command Line Arguments
    //======================

    [Verb("extract", HelpText = "Extract a specific texture file from a .gts Tile Set file.")]
    public class ExtractVerbs
    {
        [Option(
            't', "tileset",
            Required = true,
            HelpText = "Input .gts file. NOTE: Only version 6 is supported.")]
        public string TileSetPath { get; set; }

        [Option(
            'f', "file",
            Required = true,
            HelpText = ".gtp texture file name from the tile set to extract.")]
        public string FileToExtract { get; set; }

        [Option(
            'l', "layer(s)",
            Required = false,
            HelpText = "Which texture layers to extract (-1 = All, 0 = Albedo, 1 = Normal, 2 = RGB Mask 1, 3 = RGB Mask 2). Defaults to 0.",
            Default = (int)0)]
        public int LayerToExtract { get; set; }


        [Option(
            'o', "output",
            Required = false,
            HelpText = "Output directory. Defaults to '_extracted' folder next to .gts file.")]
        public string OutputDir { get; set; }
    }

    [Verb("extract-all", HelpText = "Extract all texture files from a .gts Tile Set file.")]
    public class ExtractAllVerbs
    {
        [Option(
            't', "tileset",
            Required = true,
            HelpText = "Input .gts file, or a folder to extract every .gts file in (see extract-folder). NOTE: Only version 6 is supported.")]
        public string TileSetPath { get; set; }

        [Option(
            'l', "layer(s)",
            Required = false,
            HelpText = "Which texture layers to extract (-1 = All, 0 = Albedo, 1 = Normal, 2 = RGB Mask 1, 3 = RGB Mask 2). Defaults to 0.",
            Default = (int)0)]
        public int LayerToExtract { get; set; }

        [Option(
            'o', "output",
            Required = false,
            HelpText = "Output directory. Defaults to '_extracted' folder next to .gts file.")]
        public string OutputDir { get; set; }

        // [Option('f', "filter", Required = false, HelpText = "Filter. Only paths starting with the specified filter will be extracted.")]
        // public string Filter { get; set; }

        // [Option("overwrite", Required = false, HelpText = "Whether to overwrite if files have already been extracted.")]
        // public bool Overwrite { get; set; }
    }

    [Verb("extract-folder", HelpText = "Extract all texture files from every .gts Tile Set file in a folder and its sub-directories.")]
    public class ExtractFolderVerbs
    {
        [Option(
            'i', "input",
            Required = true,
            HelpText = "Folder to search for .gts files. NOTE: Only version 6 is supported.")]
        public string InputDir { get; set; } = string.Empty;

        [Option(
            'l', "layer",
            Required = false,
            HelpText = "Which texture layers to extract (-1 = All, 0 = Albedo, 1 = Normal, 2 = RGB Mask 1, 3 = RGB Mask 2). Defaults to 0.",
            Default = (int)0)]
        public int LayerToExtract { get; set; }

        [Option(
            'o', "output",
            Required = false,
            HelpText = "Output directory. Defaults to '_extracted' inside the input folder. Sub-directories are recreated, with one folder per tile set.")]
        public string? OutputDir { get; set; }

        [Option(
            "top-level-only",
            Required = false,
            HelpText = "Only search the input folder itself, not its sub-directories.")]
        public bool TopLevelOnly { get; set; }

        [Option(
            'y', "yes",
            Required = false,
            HelpText = "Skip the disk space confirmation prompt.")]
        public bool AssumeYes { get; set; }
    }

    [Verb("extract-project-file", HelpText = "Extract the projects file out of the .gts tile set file (if one exists).")]
    public class ExtractProjectFileVerbs
    {
        [Option(
            't', "tileset",
            Required = true,
            HelpText = "Input .gts file.")]
        public string TileSetPath { get; set; }
    }

    [Verb("downgrade", HelpText = "Downgrades a tile set file from version 6 to 5 for use in the granite tile set viewer tool.")]
    public class DowngradeVerbs
    {
        [Option(
            't', "tileset",
            Required = true,
            HelpText = "Input .gts file.")]
        public string TileSetPath { get; set; }
    }
}