using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using ContentEditor;
using ContentEditor.App.FileLoaders;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mdf;

static string? GetArg(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}
static IReadOnlyList<string> GetArgs(string[] args, string name)
{
    var values = new List<string>();
    for (int i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            values.Add(args[i + 1]);
    return values;
}
static float? GetFloatArg(string[] args, string name)
{
    var value = GetArg(args, name);
    if (string.IsNullOrWhiteSpace(value)) return null;
    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        throw new ArgumentException($"{name} must be a number");
    return parsed;
}
static bool HasFlag(string[] args, string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

static void PrintUsage()
{
    Console.WriteLine("REE-Content-Exporter - REE Content Editor pipeline wrapper");
    Console.WriteLine("Usage:");
    Console.WriteLine("  REE-Content-Exporter [--wizard] [--reset-config] [--config <path>]");
    Console.WriteLine("  REE-Content-Exporter --mesh <mesh.path> [--additional-mesh <mesh.path> ...] [--streaming <meshstream.path>] [--additional-streaming <mesh.path=meshstream.path> ...] [--mdf <mdf2.path>] [--motlist <motlist.path> ...|--motlist-dir <folder>|--mot <mot.path> ...] --output <file.fbx|file.glb|folder> [--animation-name <contains>] [--batch-motlist|--split-animations|--split-motlists] [--skip-missing-animation-bones|--no-placeholder-animation-bones] [--no-animations] [--no-textures] [--texture-format png|dds] [--fbx-scale <scale>] [--include-lods] [--include-occlusion] [--allow-missing-streaming]");
}

static Dictionary<string, string> ParseAdditionalStreamingArgs(IEnumerable<string> values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var value in values)
    {
        var separatorIndex = value.IndexOf('=');
        if (separatorIndex < 0) separatorIndex = value.IndexOf('|');
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
        {
            throw new ArgumentException("--additional-streaming must use <additional-mesh-path>=<streaming-mesh-path>.");
        }

        var meshPath = value[..separatorIndex].Trim().Trim('"');
        var streamingPath = value[(separatorIndex + 1)..].Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(meshPath) || string.IsNullOrWhiteSpace(streamingPath))
        {
            throw new ArgumentException("--additional-streaming must use non-empty <additional-mesh-path>=<streaming-mesh-path> values.");
        }
        if (result.ContainsKey(meshPath))
        {
            throw new ArgumentException($"Duplicate --additional-streaming entry for additional mesh: {meshPath}");
        }
        result.Add(meshPath, streamingPath);
    }

    return result;
}

static void RunWizard(string? configPathOverride)
{
    Console.WriteLine("REE-Content-Exporter interactive wizard");
    var configPath = ResolveWizardConfigPath(configPathOverride);
    var config = LoadWizardConfig(configPath);
    var reason = "";
    if (config == null || !ValidateWizardConfig(config, out reason))
    {
        if (!string.IsNullOrWhiteSpace(reason)) Console.WriteLine($"Config setup required: {reason}");
        config = PromptForWizardConfig(config);
        SaveWizardConfig(configPath, config);
        Console.WriteLine($"Saved wizard config: {configPath}");
    }

    var index = LoadPragmataIndex();
    var mesh = PromptForAsset("Primary mesh", AssetKind.Mesh, config, index);
    var additionalMeshes = new List<ResolvedAsset>();
    while (PromptYesNo("Add another mesh part?", defaultValue: false))
    {
        additionalMeshes.Add(PromptForAsset("Additional mesh", AssetKind.Mesh, config, index));
    }

    var streaming = FindStreamingCandidate(mesh.Path);
    var additionalStreaming = additionalMeshes
        .Select(asset => new { Mesh = asset.Path, Streaming = FindStreamingCandidate(asset.Path) })
        .Where(pair => pair.Streaming != null)
        .ToDictionary(pair => pair.Mesh, pair => pair.Streaming!, StringComparer.OrdinalIgnoreCase);

    var inspected = InspectMeshForWizard(mesh.Path, streaming);
    var isSkeletal = inspected.BoneCount > 0;
    Console.WriteLine($"Mesh type: {(isSkeletal ? "skeletal" : "static")} (bones={inspected.BoneCount}, materials={inspected.MaterialCount}, lods={inspected.LodCount}, requiresStreaming={inspected.RequiresStreaming})");

    var animation = WizardAnimationSelection.None;
    if (isSkeletal && PromptYesNo("Include animations?", defaultValue: false))
    {
        animation = PromptForAnimationSelection(config, index);
    }

    var exportRoot = PromptExportRoot(config.DefaultExportRoot);
    var scriptPath = GenerateWizardScript(config, exportRoot, mesh.Path, additionalMeshes.Select(m => m.Path).ToList(), streaming, additionalStreaming, animation, isSkeletal);
    Console.WriteLine($"Generated script: {scriptPath}");

    if (PromptYesNo("Run the generated script now?", defaultValue: false))
    {
        RunGeneratedScript(scriptPath);
    }
}

static WizardConfig? LoadWizardConfig(string path)
{
    try
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<WizardConfig>(File.ReadAllText(path));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"WARNING: failed to read config {path}: {ex.Message}");
        return null;
    }
}

static void SaveWizardConfig(string path, WizardConfig config)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
    config.UpdatedUtc = DateTimeOffset.UtcNow;
    if (config.CreatedUtc == default) config.CreatedUtc = config.UpdatedUtc;
    File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
}

static bool ValidateWizardConfig(WizardConfig config, out string reason)
{
    if (string.IsNullOrWhiteSpace(config.ExtractRoot) || !Directory.Exists(config.ExtractRoot))
    {
        reason = "game extract path is missing or does not exist";
        return false;
    }
    if (!HasLikelyExtractLayout(config.ExtractRoot))
    {
        reason = "game extract path does not look like a PRAGMATA loose-file extract";
        return false;
    }
    if (string.IsNullOrWhiteSpace(config.DefaultExportRoot))
    {
        reason = "default export path is missing";
        return false;
    }
    if (string.IsNullOrWhiteSpace(config.BlenderPath) || !File.Exists(config.BlenderPath))
    {
        reason = "Blender executable is missing or does not exist";
        return false;
    }
    reason = "";
    return true;
}

static WizardConfig PromptForWizardConfig(WizardConfig? existing)
{
    var extractRoot = PromptExistingExtractRoot(existing?.ExtractRoot);
    var defaultExportRoot = PromptDirectoryPath("Default export folder", existing?.DefaultExportRoot, mustExist: false);
    var blenderPath = PromptFilePath("Blender 4.5.9 executable", existing?.BlenderPath ?? @"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe", mustExist: true);
    return new WizardConfig
    {
        ExtractRoot = extractRoot,
        DefaultExportRoot = defaultExportRoot,
        BlenderPath = blenderPath,
        TextureFormat = string.IsNullOrWhiteSpace(existing?.TextureFormat) ? "png" : existing!.TextureFormat,
        CreatedUtc = existing?.CreatedUtc == default ? DateTimeOffset.UtcNow : existing!.CreatedUtc,
        UpdatedUtc = DateTimeOffset.UtcNow,
    };
}

static string PromptExistingExtractRoot(string? defaultValue)
{
    while (true)
    {
        var input = PromptText("Game extract folder or any file/folder inside it", defaultValue);
        var inferred = InferExtractRoot(input);
        if (inferred != null && Directory.Exists(inferred) && HasLikelyExtractLayout(inferred))
            return inferred;
        Console.WriteLine("Could not infer an existing extract root. Paste a folder such as re_chunk_000, natives\\stm, or a file inside the extract.");
    }
}

static string PromptDirectoryPath(string label, string? defaultValue, bool mustExist)
{
    while (true)
    {
        var input = NormalizeUserPath(PromptText(label, defaultValue));
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine("Path cannot be empty.");
            continue;
        }
        if (Directory.Exists(input) || !mustExist)
            return Path.GetFullPath(input);
        Console.WriteLine("Folder does not exist.");
    }
}

static string PromptFilePath(string label, string? defaultValue, bool mustExist)
{
    while (true)
    {
        var input = NormalizeUserPath(PromptText(label, defaultValue));
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine("Path cannot be empty.");
            continue;
        }
        if (File.Exists(input) || !mustExist)
            return Path.GetFullPath(input);
        Console.WriteLine("File does not exist.");
    }
}

static string PromptText(string label, string? defaultValue = null)
{
    Console.Write(string.IsNullOrWhiteSpace(defaultValue) ? $"{label}: " : $"{label} [{defaultValue}]: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) && !string.IsNullOrWhiteSpace(defaultValue)) return defaultValue;
    return input?.Trim() ?? "";
}

static bool PromptYesNo(string label, bool defaultValue)
{
    var suffix = defaultValue ? "Y/n" : "y/N";
    while (true)
    {
        Console.Write($"{label} [{suffix}]: ");
        var input = (Console.ReadLine() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(input)) return defaultValue;
        if (input.Equals("y", StringComparison.OrdinalIgnoreCase) || input.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (input.Equals("n", StringComparison.OrdinalIgnoreCase) || input.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
        Console.WriteLine("Enter yes or no.");
    }
}

static ResolvedAsset PromptForAsset(string label, AssetKind kind, WizardConfig config, IReadOnlyList<PragmataIndexEntry> index)
{
    while (true)
    {
        var query = PromptText($"{label} filename/path");
        if (string.IsNullOrWhiteSpace(query)) continue;
        var matches = ResolveAssetQuery(query, kind, config, index);
        if (matches.Count == 0)
        {
            Console.WriteLine("No matching existing file was found. You can paste a full file path or a filename from the extract.");
            continue;
        }
        if (matches.Count == 1) return matches[0];
        return ChooseAsset(label, matches);
    }
}

static ResolvedAsset ChooseAsset(string label, IReadOnlyList<ResolvedAsset> matches)
{
    var limit = Math.Min(matches.Count, 25);
    Console.WriteLine($"{label} matches:");
    for (var i = 0; i < limit; i++)
    {
        Console.WriteLine($"  {i + 1}. {matches[i].Path}");
    }
    if (matches.Count > limit) Console.WriteLine($"  ... {matches.Count - limit} more matches hidden. Type a more specific query to narrow them.");
    while (true)
    {
        Console.Write($"Choose 1-{limit}: ");
        if (int.TryParse(Console.ReadLine(), out var selected) && selected >= 1 && selected <= limit)
            return matches[selected - 1];
        Console.WriteLine("Invalid selection.");
    }
}

static WizardAnimationSelection PromptForAnimationSelection(WizardConfig config, IReadOnlyList<PragmataIndexEntry> index)
{
    if (PromptYesNo("Use a MOTLIST folder instead of selecting MOTLIST files one by one?", defaultValue: true))
    {
        while (true)
        {
            var query = PromptText("MOTLIST folder path or search term");
            var matches = ResolveMotlistDirectoryQuery(query, config, index);
            if (matches.Count == 0)
            {
                Console.WriteLine("No matching MOTLIST folder was found.");
                continue;
            }
            var folder = matches.Count == 1 ? matches[0] : ChoosePath("MOTLIST folder", matches);
            return WizardAnimationSelection.FromMotlistDirectory(folder);
        }
    }

    var motlists = new List<string>();
    while (true)
    {
        var query = PromptText(motlists.Count == 0 ? "MOTLIST filename/path" : "Next MOTLIST filename/path, or done");
        if (IsDoneInput(query))
        {
            if (motlists.Count > 0) break;
            Console.WriteLine("Select at least one MOTLIST, or restart and choose no animations.");
            continue;
        }
        var matches = ResolveAssetQuery(query, AssetKind.Motlist, config, index);
        if (matches.Count == 0)
        {
            Console.WriteLine("No matching MOTLIST was found.");
            continue;
        }
        var selected = matches.Count == 1 ? matches[0] : ChooseAsset("MOTLIST", matches);
        if (!motlists.Contains(selected.Path, StringComparer.OrdinalIgnoreCase)) motlists.Add(selected.Path);
    }
    return WizardAnimationSelection.FromMotlists(motlists);
}

static string ChoosePath(string label, IReadOnlyList<string> paths)
{
    var limit = Math.Min(paths.Count, 25);
    Console.WriteLine($"{label} matches:");
    for (var i = 0; i < limit; i++)
    {
        Console.WriteLine($"  {i + 1}. {paths[i]}");
    }
    if (paths.Count > limit) Console.WriteLine($"  ... {paths.Count - limit} more matches hidden. Type a more specific query to narrow them.");
    while (true)
    {
        Console.Write($"Choose 1-{limit}: ");
        if (int.TryParse(Console.ReadLine(), out var selected) && selected >= 1 && selected <= limit)
            return paths[selected - 1];
        Console.WriteLine("Invalid selection.");
    }
}

static bool IsDoneInput(string value)
{
    var normalized = value.Trim().Trim('.').Trim();
    return string.IsNullOrWhiteSpace(normalized)
        || normalized.Equals("done", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("all", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("that's all", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("thats all", StringComparison.OrdinalIgnoreCase);
}

static string PromptExportRoot(string defaultExportRoot)
{
    if (PromptYesNo($"Use default export folder ({defaultExportRoot})?", defaultValue: true))
        return defaultExportRoot;
    return PromptDirectoryPath("Custom export folder", null, mustExist: false);
}

static WizardMeshInspection InspectMeshForWizard(string meshPath, string? streamingPath)
{
    var mesh = LoadMesh(meshPath, streamingPath, allowMissingStreaming: false);
    return new WizardMeshInspection(
        BoneCount: mesh.BoneData?.Bones.Count ?? 0,
        MaterialCount: mesh.MaterialNames.Count,
        LodCount: mesh.MeshData?.LODs.Count ?? 0,
        RequiresStreaming: mesh.RequiresStreamingData);
}

static IReadOnlyList<PragmataIndexEntry> LoadPragmataIndex()
{
    Stream? stream = null;
    var localList = Path.Combine(AppContext.BaseDirectory, "pragmata.list");
    if (File.Exists(localList))
    {
        stream = File.OpenRead(localList);
    }
    else if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "pragmata.list")))
    {
        stream = File.OpenRead(Path.Combine(Directory.GetCurrentDirectory(), "pragmata.list"));
    }
    else
    {
        stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("pragmata.list");
    }
    if (stream == null) throw new FileNotFoundException("Could not find pragmata.list beside the executable or as an embedded resource.");

    using var reader = new StreamReader(stream);
    var entries = new List<PragmataIndexEntry>();
    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        line = line.Trim();
        if (line.Length == 0) continue;
        entries.Add(new PragmataIndexEntry(line));
    }
    return entries;
}

static IReadOnlyList<ResolvedAsset> ResolveAssetQuery(string query, AssetKind kind, WizardConfig config, IReadOnlyList<PragmataIndexEntry> index)
{
    query = NormalizeUserPath(query);
    var direct = ResolveDirectAsset(query, kind);
    if (direct != null) return [direct];

    var normalizedQuery = NormalizeIndexPath(query);
    var fileQuery = Path.GetFileName(query);
    var matches = index
        .Where(entry => EntryMatchesKind(entry, kind))
        .Where(entry => EntryMatchesQuery(entry, normalizedQuery, fileQuery))
        .SelectMany(entry => GenerateDiskCandidates(config.ExtractRoot, entry.RelativePath).Select(path => new ResolvedAsset(path, entry.RelativePath)))
        .Where(asset => File.Exists(asset.Path))
        .Where(asset => kind != AssetKind.Mesh || !IsStreamingPath(asset.Path))
        .DistinctBy(asset => Path.GetFullPath(asset.Path), StringComparer.OrdinalIgnoreCase)
        .OrderBy(asset => asset.Path.Contains(Path.DirectorySeparatorChar + "natives" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
        .ThenBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
        .ToList();

    return matches;
}

static ResolvedAsset? ResolveDirectAsset(string query, AssetKind kind)
{
    if (!File.Exists(query)) return null;
    var full = Path.GetFullPath(query);
    if (kind == AssetKind.Mesh && (!IsMeshPath(full) || IsStreamingPath(full))) return null;
    if (kind == AssetKind.Motlist && !IsMotlistPath(full)) return null;
    return new ResolvedAsset(full, null);
}

static IReadOnlyList<string> ResolveMotlistDirectoryQuery(string query, WizardConfig config, IReadOnlyList<PragmataIndexEntry> index)
{
    query = NormalizeUserPath(query);
    if (Directory.Exists(query))
    {
        var full = Path.GetFullPath(query);
        if (Directory.GetFiles(full, "*.motlist*", SearchOption.AllDirectories).Length > 0) return [full];
    }

    var normalizedQuery = NormalizeIndexPath(query);
    var dirs = index
        .Where(entry => EntryMatchesKind(entry, AssetKind.Motlist))
        .Where(entry => entry.RelativeDirectory.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) || entry.FileName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        .Select(entry => entry.RelativeDirectory)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .SelectMany(relativeDir => GenerateDiskCandidates(config.ExtractRoot, relativeDir))
        .Where(Directory.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();
    return dirs;
}

static bool EntryMatchesKind(PragmataIndexEntry entry, AssetKind kind) => kind switch
{
    AssetKind.Mesh => IsMeshPath(entry.RelativePath) && !IsStreamingPath(entry.RelativePath),
    AssetKind.Motlist => IsMotlistPath(entry.RelativePath),
    _ => false,
};

static bool EntryMatchesQuery(PragmataIndexEntry entry, string normalizedQuery, string fileQuery)
{
    if (!string.IsNullOrWhiteSpace(fileQuery) && entry.FileName.Equals(fileQuery, StringComparison.OrdinalIgnoreCase)) return true;
    if (entry.RelativePath.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase)) return true;
    if (entry.RelativePath.EndsWith("/" + normalizedQuery, StringComparison.OrdinalIgnoreCase)) return true;
    return entry.RelativePath.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
}

static IEnumerable<string> GenerateDiskCandidates(string configuredRoot, string relativePath)
{
    var root = Path.GetFullPath(NormalizeUserPath(configuredRoot));
    var rel = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
    var directRel = StripNativesStmPrefix(rel);
    var roots = new List<string> { root };
    if (Directory.Exists(Path.Combine(root, "re_chunk_000"))) roots.Add(Path.Combine(root, "re_chunk_000"));
    if (EndsWithSegments(root, "natives", "stm"))
    {
        roots.Add(Path.GetFullPath(Path.Combine(root, "..", "..")));
    }

    foreach (var candidateRoot in roots.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        yield return Path.Combine(candidateRoot, rel);
        yield return Path.Combine(candidateRoot, directRel);
        yield return Path.Combine(candidateRoot, "re_chunk_000", rel);
        yield return Path.Combine(candidateRoot, "re_chunk_000", directRel);
    }
}

static string StripNativesStmPrefix(string rel)
{
    var prefix = "natives" + Path.DirectorySeparatorChar + "stm" + Path.DirectorySeparatorChar;
    return rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? rel[prefix.Length..] : rel;
}

static string NormalizeIndexPath(string value) => NormalizeUserPath(value).Replace('\\', '/').TrimStart('/');

static string NormalizeUserPath(string value)
{
    value = value.Trim().Trim('"').Trim('\'');
    return Environment.ExpandEnvironmentVariables(value);
}

static string? InferExtractRoot(string input)
{
    input = NormalizeUserPath(input);
    if (string.IsNullOrWhiteSpace(input)) return null;
    var anchored = InferExtractRootFromAnchors(input);
    if (anchored != null && HasLikelyExtractLayout(anchored)) return anchored;

    var original = input;
    if (File.Exists(input))
    {
        input = Path.GetDirectoryName(Path.GetFullPath(input)) ?? input;
    }
    else if (!Directory.Exists(input) && Path.HasExtension(input))
    {
        input = Path.GetDirectoryName(input) ?? input;
    }

    var ancestor = FindExtractRootAncestor(input);
    if (ancestor != null) return ancestor;

    var full = Directory.Exists(input) ? Path.GetFullPath(input) : Path.GetFullPath(original);
    var parts = full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Where(p => p.Length > 0).ToList();
    var reChunkIndex = parts.FindIndex(p => p.Equals("re_chunk_000", StringComparison.OrdinalIgnoreCase));
    if (reChunkIndex >= 0)
    {
        var candidate = RebuildPath(parts.Take(reChunkIndex + 1));
        return Directory.Exists(candidate) ? candidate : null;
    }
    for (var i = 0; i < parts.Count - 1; i++)
    {
        if (parts[i].Equals("natives", StringComparison.OrdinalIgnoreCase) && parts[i + 1].Equals("stm", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = RebuildPath(parts.Take(i));
            return Directory.Exists(candidate) ? candidate : null;
        }
    }
    var topLevelIndex = parts.FindIndex(IsTopLevelGameFolder);
    if (topLevelIndex >= 0)
    {
        var candidate = RebuildPath(parts.Take(topLevelIndex));
        return Directory.Exists(candidate) ? candidate : null;
    }
    return Directory.Exists(full) ? full : null;
}

static string? FindExtractRootAncestor(string path)
{
    var current = Directory.Exists(path) ? new DirectoryInfo(Path.GetFullPath(path)) : new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
    while (current != null)
    {
        if (HasLikelyExtractLayout(current.FullName)) return current.FullName;
        current = current.Parent;
    }
    return null;
}

static string? InferExtractRootFromAnchors(string input)
{
    var normalized = input.Replace('/', Path.DirectorySeparatorChar);
    var reChunkMarker = Path.DirectorySeparatorChar + "re_chunk_000";
    var reChunkIndex = normalized.IndexOf(reChunkMarker, StringComparison.OrdinalIgnoreCase);
    if (reChunkIndex >= 0)
    {
        var end = reChunkIndex + reChunkMarker.Length;
        var candidate = normalized[..end];
        return Directory.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    var nativesMarker = Path.DirectorySeparatorChar + "natives" + Path.DirectorySeparatorChar + "stm";
    var nativesIndex = normalized.IndexOf(nativesMarker, StringComparison.OrdinalIgnoreCase);
    if (nativesIndex >= 0)
    {
        var candidate = normalized[..nativesIndex];
        return Directory.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    foreach (var markerName in new[] { "character", "camera", "event", "object", "stage", "streaming" })
    {
        var marker = Path.DirectorySeparatorChar + markerName + Path.DirectorySeparatorChar;
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index <= 0) continue;
        var candidate = normalized[..index];
        return Directory.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    return null;
}

static bool HasLikelyExtractLayout(string root)
{
    if (!Directory.Exists(root)) return false;
    if (Directory.Exists(Path.Combine(root, "natives", "stm"))) return true;
    if (Directory.Exists(Path.Combine(root, "re_chunk_000"))) return true;
    return Directory.EnumerateDirectories(root)
        .Select(Path.GetFileName)
        .Where(name => name != null)
        .Any(name => IsTopLevelGameFolder(name!));
}

static string RebuildPath(IEnumerable<string> parts)
{
    var list = parts.ToList();
    if (list.Count == 0) return Directory.GetCurrentDirectory();
    var path = list[0] + Path.DirectorySeparatorChar;
    foreach (var part in list.Skip(1)) path = Path.Combine(path, part);
    return Path.GetFullPath(path);
}

static bool EndsWithSegments(string path, params string[] segments)
{
    var parts = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Where(p => p.Length > 0).ToArray();
    if (parts.Length < segments.Length) return false;
    for (var i = 0; i < segments.Length; i++)
    {
        if (!parts[parts.Length - segments.Length + i].Equals(segments[i], StringComparison.OrdinalIgnoreCase)) return false;
    }
    return true;
}

static bool IsTopLevelGameFolder(string value) => value.Equals("camera", StringComparison.OrdinalIgnoreCase)
    || value.Equals("character", StringComparison.OrdinalIgnoreCase)
    || value.Equals("effect", StringComparison.OrdinalIgnoreCase)
    || value.Equals("event", StringComparison.OrdinalIgnoreCase)
    || value.Equals("gui", StringComparison.OrdinalIgnoreCase)
    || value.Equals("leveldesign", StringComparison.OrdinalIgnoreCase)
    || value.Equals("object", StringComparison.OrdinalIgnoreCase)
    || value.Equals("render", StringComparison.OrdinalIgnoreCase)
    || value.Equals("scene", StringComparison.OrdinalIgnoreCase)
    || value.Equals("sound", StringComparison.OrdinalIgnoreCase)
    || value.Equals("stage", StringComparison.OrdinalIgnoreCase)
    || value.Equals("streaming", StringComparison.OrdinalIgnoreCase)
    || value.Equals("systems", StringComparison.OrdinalIgnoreCase)
    || value.Equals("ui", StringComparison.OrdinalIgnoreCase)
    || value.Equals("userdata", StringComparison.OrdinalIgnoreCase);

static bool IsMeshPath(string path) => path.Contains(".mesh.", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mesh", StringComparison.OrdinalIgnoreCase);
static bool IsMotlistPath(string path) => path.Contains(".motlist.", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".motlist", StringComparison.OrdinalIgnoreCase);
static bool IsStreamingPath(string path) => NormalizeIndexPath(path).Split('/').Contains("streaming", StringComparer.OrdinalIgnoreCase);

static string ResolveWizardConfigPath(string? overridePath)
{
    if (!string.IsNullOrWhiteSpace(overridePath)) return Path.GetFullPath(NormalizeUserPath(overridePath));
    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REE-Content-Exporter", "config.json");
}

static string GenerateWizardScript(
    WizardConfig config,
    string exportRoot,
    string meshPath,
    IReadOnlyList<string> additionalMeshes,
    string? streamingPath,
    IReadOnlyDictionary<string, string> additionalStreaming,
    WizardAnimationSelection animation,
    bool isSkeletal)
{
    Directory.CreateDirectory(exportRoot);
    var scriptDir = Path.Combine(exportRoot, "generated-scripts");
    Directory.CreateDirectory(scriptDir);
    var assetName = SanitizeFileName(PathUtils.GetFilenameWithoutExtensionOrVersion(meshPath).ToString());
    var scriptPath = Path.Combine(scriptDir, $"{assetName}_unreal_export_{DateTime.Now:yyyyMMdd_HHmmss}.ps1");
    var exporterPath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "REE-Content-Exporter.exe");
    var script = BuildWizardPowerShell(config, exporterPath, exportRoot, meshPath, additionalMeshes, streamingPath, additionalStreaming, animation, isSkeletal);
    File.WriteAllText(scriptPath, script, Encoding.UTF8);
    return scriptPath;
}

static string BuildWizardPowerShell(
    WizardConfig config,
    string exporterPath,
    string exportRoot,
    string meshPath,
    IReadOnlyList<string> additionalMeshes,
    string? streamingPath,
    IReadOnlyDictionary<string, string> additionalStreaming,
    WizardAnimationSelection animation,
    bool isSkeletal)
{
    var sourceName = SanitizeFileName(PathUtils.GetFilenameWithoutExtensionOrVersion(meshPath).ToString()) + "_source.fbx";
    var args = new List<string>
    {
        "--mesh", meshPath,
        "--texture-format", string.IsNullOrWhiteSpace(config.TextureFormat) ? "png" : config.TextureFormat,
        "--fbx-scale", "100",
        "--output", "$OutputRequest",
    };
    if (!string.IsNullOrWhiteSpace(streamingPath)) args.InsertRange(0, ["--streaming", streamingPath]);
    foreach (var additional in additionalMeshes)
    {
        args.Add("--additional-mesh");
        args.Add(additional);
        if (additionalStreaming.TryGetValue(additional, out var addStreaming))
        {
            args.Add("--additional-streaming");
            args.Add(additional + "=" + addStreaming);
        }
    }
    if (animation.Mode == WizardAnimationMode.None)
    {
        args.Add("--no-animations");
    }
    else
    {
        args.Add("--no-placeholder-animation-bones");
        if (animation.Mode == WizardAnimationMode.MotlistDirectory)
        {
            args.Add("--motlist-dir");
            args.Add(animation.MotlistDirectory!);
            args.Add("--split-motlists");
        }
        else
        {
            foreach (var motlist in animation.Motlists)
            {
                args.Add("--motlist");
                args.Add(motlist);
            }
            args.Add("--split-motlists");
        }
    }

    var argLines = args.Select(arg => arg == "$OutputRequest" ? "    $OutputRequest" : "    " + PsQuote(arg)).ToList();
    var outputRequestLine = animation.Mode == WizardAnimationMode.None
        ? $"$OutputRequest = Join-Path $ExportRoot {PsQuote(sourceName)}"
        : $"$OutputRequest = Join-Path $ExportRoot {PsQuote(sourceName)}";
    var sourceDiscovery = animation.Mode == WizardAnimationMode.None
        ? $$"""
    $Source = Get-ChildItem $ExportRoot -Recurse -File -Filter {{PsQuote(sourceName)}} |
        Where-Object { $_.LastWriteTime -ge $Start.AddMinutes(-2) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (!$Source) { throw "Could not find generated source FBX under $ExportRoot" }
    $OutDir = Split-Path $Source.FullName -Parent
    $Sources = @($Source)
"""
        : """
    $RecentSources = Get-ChildItem $ExportRoot -Recurse -File -Filter "*_all_animations.fbx" |
        Where-Object { $_.LastWriteTime -ge $Start.AddMinutes(-2) } |
        Sort-Object FullName
    if (!$RecentSources -or $RecentSources.Count -eq 0) { throw "Could not find split MOTLIST source FBX files under $ExportRoot" }
    $OutDir = ($RecentSources | Sort-Object LastWriteTime -Descending | Select-Object -First 1).DirectoryName
    $Sources = @(Get-ChildItem $OutDir -File -Filter "*_all_animations.fbx" | Sort-Object Name)
    if (!$Sources -or $Sources.Count -eq 0) { throw "No source FBX files found in split MOTLIST job folder: $OutDir" }
    Write-Host "SPLIT_MOTLIST_SOURCE_COUNT=$($Sources.Count)"
    Write-Host "JOB_DIR=$OutDir"
""";

    return $$"""
param(
    [switch]$KeepSourceFbx
)

$ErrorActionPreference = "Stop"
$Exporter = {{PsQuote(exporterPath)}}
$ExportRoot = {{PsQuote(exportRoot)}}
$Blender = {{PsQuote(config.BlenderPath)}}
$RunStamp = Get-Date -Format "yyyyMMdd_HHmmss"
$LogTemp = Join-Path $env:TEMP ("ree_export_wizard__{0}.log" -f $RunStamp)
$TranscriptStarted = $false
$LogCompleted = $false
$OutDir = $null
$BlenderSkipped = New-Object System.Collections.Generic.List[object]

function Complete-ExportLog {
    param([ValidateSet("SUCCESS", "FAIL")][string]$Status)
    if ($script:LogCompleted) { return }
    $script:LogCompleted = $true
    if ($script:TranscriptStarted) {
        Stop-Transcript | Out-Null
        $script:TranscriptStarted = $false
    }
    if (Test-Path $script:LogTemp) {
        $name = "ree_export_wizard-$Status.log"
        if ($script:OutDir -and (Test-Path $script:OutDir)) {
            $target = Join-Path $script:OutDir $name
            Move-Item -LiteralPath $script:LogTemp -Destination $target -Force
            Write-Host "EXPORT_LOG=$target"
        } else {
            $target = Join-Path ([System.IO.Path]::GetDirectoryName($script:LogTemp)) ("ree_export_wizard-$Status__$($script:RunStamp).log")
            Move-Item -LiteralPath $script:LogTemp -Destination $target -Force
            Write-Host "EXPORT_LOG_TEMP=$target"
        }
    }
}

function Invoke-BlenderReexport {
    param(
        [System.IO.FileInfo]$Source,
        [string]$Target,
        [int]$Index,
        [int]$Total,
        [bool]$ExpectAnimations,
        [string]$StatusPath
    )

    $Py = Join-Path $env:TEMP ("blender_ree_wizard_{0}_{1}.py" -f $RunStamp, $Index)
@"
import bpy
import builtins
from pathlib import Path
src = Path(r'$($Source.FullName)')
out = Path(r'$Target')
status_path = Path(r'$StatusPath')
index = $Index
total = $Total
expect_animations = $($ExpectAnimations.ToString().ToLowerInvariant())

def write_status(status, reason='', action_count=0):
    status_path.write_text(f'STATUS={status}\nREASON={reason}\nACTION_COUNT={action_count}\n', encoding='utf-8')

def log(message):
    print(f'BLENDER_PROGRESS {message}', flush=True)

def install_fbx_pose_progress(action_names):
    real_print = builtins.print
    state = {'pose_count': 0}
    total_actions = max(1, len(action_names))
    def progress_print(*args, **kwargs):
        if len(args) == 1 and isinstance(args[0], tuple) and len(args[0]) >= 2 and args[0][1] == 'POSE':
            state['pose_count'] += 1
            pose_index = state['pose_count']
            if pose_index <= len(action_names):
                real_print(f'BLENDER_PROGRESS File {index}/{total} exporting animation {pose_index}/{total_actions}: {action_names[pose_index - 1]}', flush=True)
            else:
                real_print(f'BLENDER_PROGRESS File {index}/{total} exporting additional FBX pose data: event {pose_index}', flush=True)
            return
        real_print(*args, **kwargs)
    builtins.print = progress_print
    return real_print

log(f'File {index}/{total} 1/6 clearing scene')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()
for datablocks in (bpy.data.actions, bpy.data.armatures, bpy.data.meshes):
    for datablock in list(datablocks):
        datablocks.remove(datablock, do_unlink=True)

bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 0.01

log(f'File {index}/{total} 2/6 importing source FBX')
bpy.ops.import_scene.fbx(
    filepath=str(src),
    use_anim=expect_animations,
    automatic_bone_orientation=False,
    ignore_leaf_bones=False,
    force_connect_children=False,
)

armatures = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
actions = list(bpy.data.actions)
print(f'IMPORTED file={index}/{total} armatures={len(armatures)} meshes={len(meshes)} actions={len(actions)}')
if expect_animations and not armatures:
    write_status('FAILED', 'No armature imported from animated source FBX', len(actions))
    raise RuntimeError('No armature imported from animated source FBX')
if expect_animations and not actions:
    write_status('SKIPPED', 'No actions imported from source FBX', 0)
    raise SystemExit(0)
if not meshes and not armatures:
    write_status('FAILED', 'No mesh or armature imported from source FBX', len(actions))
    raise RuntimeError('No mesh or armature imported from source FBX')

for arm_index, arm in enumerate(armatures, start=1):
    log(f'File {index}/{total} 3/6 applying armature transform {arm_index}/{len(armatures)}: {arm.name}')
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True, properties=True)

if expect_animations:
    for arm in armatures:
        arm.animation_data_create()
        arm.animation_data.action = None
        for track in list(arm.animation_data.nla_tracks):
            arm.animation_data.nla_tracks.remove(track)
        for action_index, action in enumerate(actions, start=1):
            log(f'File {index}/{total} 4/6 preparing NLA strip {action_index}/{len(actions)}: {action.name}')
            start, end = action.frame_range
            track = arm.animation_data.nla_tracks.new()
            track.name = action.name
            strip = track.strips.new(action.name, 0, action)
            strip.name = action.name
            strip.action_frame_start = start
            strip.action_frame_end = end
            strip.frame_start = 0
            strip.frame_end = max(1, end - start)
            strip.blend_type = 'REPLACE'
            strip.extrapolation = 'NOTHING'
    for action in bpy.data.actions:
        action.use_fake_user = True
    max_frame = 1
    for action in bpy.data.actions:
        if action.frame_range:
            max_frame = max(max_frame, int(action.frame_range[1] - action.frame_range[0]))
    bpy.context.scene.frame_start = 0
    bpy.context.scene.frame_end = max_frame
    bpy.context.scene.render.fps = 60

log(f'File {index}/{total} 5/6 exporting Unreal FBX')
object_types = {'MESH', 'ARMATURE'} if armatures else {'MESH'}
real_print = install_fbx_pose_progress([action.name for action in actions]) if expect_animations else builtins.print
try:
    bpy.ops.export_scene.fbx(
        filepath=str(out),
        check_existing=False,
        use_selection=False,
        object_types=object_types,
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        use_armature_deform_only=False,
        bake_anim=expect_animations,
        bake_anim_use_all_bones=expect_animations,
        bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=expect_animations,
        bake_anim_force_startend_keying=expect_animations,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        axis_forward='-Z',
        axis_up='Y',
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        use_space_transform=True,
        bake_space_transform=False,
        path_mode='AUTO',
        embed_textures=False,
    )
finally:
    builtins.print = real_print

log(f'File {index}/{total} 6/6 done')
write_status('EXPORTED', '', len(actions))
print(f'EXPORTED {out} size={out.stat().st_size if out.exists() else 0}')
"@ | Set-Content -Encoding UTF8 $Py

    Remove-Item -LiteralPath $StatusPath -Force -ErrorAction SilentlyContinue
    & $Blender --background --factory-startup --python $Py
    if ($LASTEXITCODE -ne 0) { throw "Blender re-export failed with exit code $LASTEXITCODE for $($Source.FullName)" }
    if (!(Test-Path $StatusPath)) { throw "Missing Blender status file for $($Source.FullName): $StatusPath" }
}

try {
    if (!(Test-Path $Exporter)) { throw "Missing exporter: $Exporter" }
    if (!(Test-Path $Blender)) { throw "Missing Blender 4.5.9 executable: $Blender" }
    if (!(Test-Path $ExportRoot)) { New-Item -ItemType Directory -Force -Path $ExportRoot | Out-Null }

    $BlenderVersionLine = (& $Blender --version 2>&1 | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0) { throw "Could not query Blender version from: $Blender" }
    if ($BlenderVersionLine -notmatch 'Blender\s+4\.5\.9') { throw "Expected Blender 4.5.9 LTS, but found: $BlenderVersionLine" }

    Start-Transcript -Path $LogTemp -Force | Out-Null
    $TranscriptStarted = $true
    $Start = Get-Date
    {{outputRequestLine}}
    $argsList = @(
{{string.Join(",\n", argLines)}}
    )
    & $Exporter @argsList
    if ($LASTEXITCODE -ne 0) { throw "Exporter failed with exit code $LASTEXITCODE" }

{{sourceDiscovery}}

    $TextureDir = Join-Path $OutDir "textures"
    if (!(Test-Path $TextureDir)) { throw "Texture folder missing after export: $TextureDir" }
    $TextureCount = (Get-ChildItem $TextureDir -File -ErrorAction Stop | Measure-Object).Count
    if ($TextureCount -le 0) { throw "Texture folder exists but is empty: $TextureDir" }

    for ($i = 0; $i -lt $Sources.Count; $i++) {
        $Source = $Sources[$i]
        $base = [System.IO.Path]::GetFileNameWithoutExtension($Source.Name)
        $base = $base -replace '^\d{4}_', ''
        $base = $base -replace '_source$', ''
        $base = $base -replace '_all_animations$', ''
        $Target = Join-Path $Source.DirectoryName ($base + "_unreal.fbx")
        $SourceReport = Join-Path $Source.DirectoryName "$([System.IO.Path]::GetFileNameWithoutExtension($Source.Name)).skipped-animation-bones.md"
        $FinalReport = Join-Path $Source.DirectoryName "$base.skipped-animation-bones.md"
        $StatusPath = Join-Path $env:TEMP ("ree_wizard_blender_status_{0}_{1}.txt" -f $RunStamp, $i)
        $statusText = ""
        Write-Host "SOURCE_FBX=$($Source.FullName)"
        Write-Host "BLENDER_TARGET=$Target"
        Invoke-BlenderReexport -Source $Source -Target $Target -Index ($i + 1) -Total $Sources.Count -ExpectAnimations ${{(animation.Mode == WizardAnimationMode.None ? "false" : "true")}} -StatusPath $StatusPath
        if (Test-Path $StatusPath) {
            $statusText = Get-Content -LiteralPath $StatusPath -Raw
            if ($statusText -match 'STATUS=SKIPPED') {
                $BlenderSkipped.Add([pscustomobject]@{ Source = $Source.FullName; Target = $Target; Reason = "No actions imported from source FBX" })
            }
        }
        if (!(Test-Path $Target) -and !($statusText -match 'STATUS=SKIPPED')) { throw "Missing Blender output: $Target" }
        if ($statusText -match 'STATUS=SKIPPED') {
            Write-Host "BLENDER_SKIPPED_SOURCE=$($Source.FullName)"
            if (!$KeepSourceFbx) {
                Remove-Item -LiteralPath $Source.FullName -Force
                Write-Host "SOURCE_FBX_REMOVED=$($Source.FullName)"
                if (Test-Path $SourceReport) {
                    Remove-Item -LiteralPath $SourceReport -Force
                    Write-Host "SOURCE_SKIPPED_BONE_REPORT_REMOVED=$SourceReport"
                }
            }
            continue
        }
        if (Test-Path $SourceReport) {
            Move-Item -LiteralPath $SourceReport -Destination $FinalReport -Force
            Write-Host "SKIPPED_BONE_REPORT=$FinalReport"
        }
        if (!$KeepSourceFbx) {
            Remove-Item -LiteralPath $Source.FullName -Force
            Write-Host "SOURCE_FBX_REMOVED=$($Source.FullName)"
        }
        Write-Host "BLENDER_FBX=$Target"
    }

    if ($BlenderSkipped.Count -gt 0) {
        $ReportPath = Join-Path $OutDir "skipped-blender-motlists.md"
        $lines = New-Object System.Collections.Generic.List[string]
        $lines.Add("# Skipped Blender MOTLIST Re-exports")
        $lines.Add("")
        foreach ($item in $BlenderSkipped) { $lines.Add("- $($item.Source): $($item.Reason)") }
        $lines | Set-Content -Encoding UTF8 $ReportPath
        Write-Host "BLENDER_SKIPPED_MOTLIST_REPORT=$ReportPath"
    }

    Complete-ExportLog -Status SUCCESS
    Write-Host "EXPORT_DIR=$OutDir"
} catch {
    Write-Host "EXPORT_FAILED=$($_.Exception.Message)"
    Complete-ExportLog -Status FAIL
    throw
}
""";
}

static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

static void RunGeneratedScript(string scriptPath)
{
    var psi = new ProcessStartInfo
    {
        FileName = "powershell",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    psi.ArgumentList.Add("-ExecutionPolicy");
    psi.ArgumentList.Add("Bypass");
    psi.ArgumentList.Add("-File");
    psi.ArgumentList.Add(scriptPath);
    using var proc = Process.Start(psi) ?? throw new Exception("Failed to start PowerShell.");
    proc.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
    proc.BeginOutputReadLine();
    proc.BeginErrorReadLine();
    proc.WaitForExit();
    if (proc.ExitCode == 0)
    {
        Console.WriteLine("Script completed successfully.");
    }
    else
    {
        Console.WriteLine($"Script failed with exit code {proc.ExitCode}.");
    }
}

var wizardConfigPath = GetArg(args, "--config");
if (HasFlag(args, "--reset-config"))
{
    var path = ResolveWizardConfigPath(wizardConfigPath);
    if (File.Exists(path))
    {
        File.Delete(path);
        Console.WriteLine($"Deleted wizard config: {path}");
    }
}
if (args.Length == 0 || HasFlag(args, "--wizard") || HasFlag(args, "--reset-config"))
{
    RunWizard(wizardConfigPath);
    return;
}
if (HasFlag(args, "--help"))
{
    PrintUsage();
    return;
}

using var progress = new ProgressStatus();

var meshPath = GetArg(args, "--mesh") ?? throw new ArgumentException("Missing --mesh");
var additionalMeshPaths = GetArgs(args, "--additional-mesh").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
var streamingPath = GetArg(args, "--streaming");
var additionalStreamingByMesh = ParseAdditionalStreamingArgs(GetArgs(args, "--additional-streaming"));
var mdfPath = GetArg(args, "--mdf");
var motlistPaths = GetArgs(args, "--motlist").ToList();
var motlistDir = GetArg(args, "--motlist-dir");
if (!string.IsNullOrWhiteSpace(motlistDir))
{
    motlistPaths.AddRange(Directory.GetFiles(motlistDir, "*.motlist*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
}
motlistPaths = motlistPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
var motPaths = GetArgs(args, "--mot").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
var outputPath = GetArg(args, "--output") ?? throw new ArgumentException("Missing --output");
var animationFilter = GetArg(args, "--animation-name");
var includeAnimations = !HasFlag(args, "--no-animations");
var includeTextures = !HasFlag(args, "--no-textures");
var textureFormat = (GetArg(args, "--texture-format") ?? "png").ToLowerInvariant();
if (textureFormat is not ("png" or "dds")) throw new ArgumentException("--texture-format must be png or dds");
var fbxScale = GetFloatArg(args, "--fbx-scale") ?? 1f;
if (fbxScale <= 0) throw new ArgumentException("--fbx-scale must be greater than 0");
AppConfig.Settings.Import.ExportScale = fbxScale;
var batchMotlist = HasFlag(args, "--batch-motlist");
var splitAnimations = HasFlag(args, "--split-animations");
var splitMotlists = HasFlag(args, "--split-motlists");
var skipMissingAnimationBones = HasFlag(args, "--skip-missing-animation-bones");
var noPlaceholderAnimationBones = HasFlag(args, "--no-placeholder-animation-bones");
var includeLods = HasFlag(args, "--include-lods");
var includeOcc = HasFlag(args, "--include-occlusion");
var allowMissingStreaming = HasFlag(args, "--allow-missing-streaming");

Console.WriteLine("REE Content Editor native export path");
Console.WriteLine($"Mesh: {meshPath}");
Console.WriteLine($"Additional meshes: {(additionalMeshPaths.Count == 0 ? "-" : string.Join("; ", additionalMeshPaths))}");
Console.WriteLine($"Streaming: {streamingPath ?? "-"}");
Console.WriteLine($"Additional streaming: {(additionalStreamingByMesh.Count == 0 ? "-" : string.Join("; ", additionalStreamingByMesh.Select(kvp => kvp.Key + " => " + kvp.Value)))}");
Console.WriteLine($"MDF: {mdfPath ?? "auto"}");
Console.WriteLine($"Motlists: {(motlistPaths.Count == 0 ? "-" : string.Join("; ", motlistPaths))}");
Console.WriteLine($"Mots: {(motPaths.Count == 0 ? "-" : string.Join("; ", motPaths))}");
Console.WriteLine($"Output: {outputPath}");

var unknownAdditionalStreamingKeys = additionalStreamingByMesh.Keys
    .Where(key => !additionalMeshPaths.Contains(key, StringComparer.OrdinalIgnoreCase))
    .ToList();
if (unknownAdditionalStreamingKeys.Count != 0)
{
    throw new ArgumentException("--additional-streaming keys must match a supplied --additional-mesh path. Unknown key(s): " + string.Join("; ", unknownAdditionalStreamingKeys));
}

var mesh = LoadMesh(meshPath, streamingPath, allowMissingStreaming);

var motions = new List<(string Source, MotFileBase Motion)>();
var motlistGroups = new List<(string SourceName, string MotlistPath, List<MotFileBase> Motions)>();
if (includeAnimations)
{
    foreach (var motlistPath in motlistPaths)
    {
        using var mlHandler = new FileHandler(motlistPath);
        var motlist = new MotlistFile(mlHandler);
        if (!motlist.Read()) throw new Exception("REE-Lib failed to read motlist");
        IEnumerable<MotFileBase> files = motlist.MotFiles;
        if (!string.IsNullOrWhiteSpace(animationFilter))
            files = files.Where(m => m.Name.Contains(animationFilter, StringComparison.OrdinalIgnoreCase));
        var selected = files.ToList();
        var sourceName = string.IsNullOrWhiteSpace(motlist.Name)
            ? PathUtils.GetFilenameWithoutExtensionOrVersion(motlistPath).ToString()
            : motlist.Name;
        motlistGroups.Add((sourceName, motlistPath, selected));
        motions.AddRange(selected.Select(m => (sourceName, m)));
        Console.WriteLine($"Loaded motlist {motlist.Name}: total={motlist.MotFiles.Count} selected={selected.Count}");
    }
    foreach (var motPath in motPaths)
    {
        using var motHandler = new FileHandler(motPath);
        var mot = new MotFile(motHandler);
        if (!mot.Read()) throw new Exception("REE-Lib failed to read mot");
        mot.ReadBones(null);
        if (string.IsNullOrWhiteSpace(animationFilter) || mot.Name.Contains(animationFilter, StringComparison.OrdinalIgnoreCase))
            motions.Add((PathUtils.GetFilenameWithoutExtensionOrVersion(motPath).ToString(), mot));
        Console.WriteLine($"Loaded mot {mot.Name}");
    }
}

var name = PathUtils.GetFilenameWithoutExtensionOrVersion(meshPath).ToString();
var animationSourceCount = motlistPaths.Count + motPaths.Count;
var exportSeparateAnimationFiles = splitAnimations || (batchMotlist && animationSourceCount <= 1);
if (splitMotlists && splitAnimations)
{
    throw new ArgumentException("--split-motlists and --split-animations cannot be used together.");
}
if (batchMotlist && animationSourceCount > 1 && !splitAnimations)
{
    Console.WriteLine("INFO: multiple MOT/MOTLIST sources detected; exporting all selected animations into one file. Use --split-animations to force one file per animation.");
}
var resource = new CommonMeshResource(name, null!)
{
    NativeMesh = mesh,
    GameVersion = GameName.pragmata,
    ExportTextureFormat = textureFormat,
    ExportRootNodeName = "Armature",
    ExportStripMeshNamePrefix = true,
    ExportSkipMotionsWithMissingBones = skipMissingAnimationBones,
    ExportNoPlaceholderAnimationBones = noPlaceholderAnimationBones,
};
var additionalResources = new List<CommonMeshResource>();
foreach (var additionalMeshPath in additionalMeshPaths)
{
    var additionalName = PathUtils.GetFilenameWithoutExtensionOrVersion(additionalMeshPath).ToString();
    additionalResources.Add(new CommonMeshResource(additionalName, null!)
    {
        NativeMesh = LoadMesh(additionalMeshPath, additionalStreamingByMesh.TryGetValue(additionalMeshPath, out var additionalStreamingPath) ? additionalStreamingPath : null, allowMissingStreaming),
        GameVersion = GameName.pragmata,
        ExportTextureFormat = textureFormat,
        ExportRootNodeName = "Armature",
        ExportStripMeshNamePrefix = true,
    });
}

MaterialGroupWrapper? materialWrapper = null;
var materialWrappers = new List<(MaterialGroupWrapper Materials, string MeshPath)>();
if (includeTextures)
{
    mdfPath ??= FindMdfCandidate(meshPath);
    if (mdfPath != null)
    {
        using var mdfHandler = new FileHandler(mdfPath);
        var mdf = new MdfFile(mdfHandler);
        if (mdf.Read())
        {
            materialWrapper = new MaterialGroupWrapper(mdf);
            materialWrapper.UpdateMaterialLookups();
            resource.SetImportedMaterials(materialWrapper);
            materialWrappers.Add((materialWrapper, meshPath));
            Console.WriteLine($"Loaded MDF materials={materialWrapper.Materials.Count}: {mdfPath}");
        }
        else
        {
            Console.WriteLine($"WARNING: failed to read MDF: {mdfPath}");
        }
    }
    else
    {
        Console.WriteLine("WARNING: no MDF found; material texture slots will not be linked.");
    }

    foreach (var additionalMeshPath in additionalMeshPaths)
    {
        var additionalMdfPath = FindMdfCandidate(additionalMeshPath);
        if (additionalMdfPath == null)
        {
            Console.WriteLine($"WARNING: no MDF found for additional mesh; material texture slots may be missing: {additionalMeshPath}");
            continue;
        }
        using var additionalMdfHandler = new FileHandler(additionalMdfPath);
        var additionalMdf = new MdfFile(additionalMdfHandler);
        if (!additionalMdf.Read())
        {
            Console.WriteLine($"WARNING: failed to read additional mesh MDF: {additionalMdfPath}");
            continue;
        }
        var additionalMaterialWrapper = new MaterialGroupWrapper(additionalMdf);
        additionalMaterialWrapper.UpdateMaterialLookups();
        resource.AddImportedMaterials(additionalMaterialWrapper);
        materialWrappers.Add((additionalMaterialWrapper, additionalMeshPath));
        Console.WriteLine($"Loaded additional mesh MDF materials={additionalMaterialWrapper.Materials.Count}: {additionalMdfPath}");
    }
}

if (splitMotlists)
{
    if (motlistGroups.Count == 0) throw new ArgumentException("--split-motlists requires --motlist-dir or at least one --motlist.");
    if (motPaths.Count > 0) throw new ArgumentException("--split-motlists only splits MOTLIST inputs; remove --mot or use --split-animations.");

    var outExt = Path.GetExtension(outputPath);
    if (string.IsNullOrEmpty(outExt)) outExt = ".glb";
    var jobDir = ResolveSplitMotlistOutputDirectory(outputPath, meshPath, BuildSourceFiles(meshPath, additionalMeshPaths, motlistPaths, []), animationFilter);
    var nonEmptyMotlistGroups = motlistGroups.Where(group => group.Motions.Count > 0).ToList();
    var emptyMotlistGroups = motlistGroups.Where(group => group.Motions.Count == 0).ToList();
    WriteSkippedMotlistReport(jobDir, emptyMotlistGroups.Select(group => (group.SourceName, group.MotlistPath)).ToList(), animationFilter, progress);
    foreach (var group in emptyMotlistGroups)
    {
        var label = string.IsNullOrWhiteSpace(group.SourceName) ? group.MotlistPath : group.SourceName;
        progress.WriteLine($"Skipping empty motlist with no selected animations: {label}");
    }
    if (nonEmptyMotlistGroups.Count == 0) throw new ArgumentException("--split-motlists found no motlists with selected animations.");

    progress.WriteLine($"Split-motlist exporting {nonEmptyMotlistGroups.Count}/{motlistGroups.Count} non-empty motlists to {jobDir} (*{outExt})");

    for (var i = 0; i < nonEmptyMotlistGroups.Count; i++)
    {
        var group = nonEmptyMotlistGroups[i];
        var safe = SanitizeFileName(string.IsNullOrWhiteSpace(group.SourceName) ? $"motlist_{i:0000}" : group.SourceName);
        var target = Path.Combine(jobDir, $"{i:0000}_{safe}_all_animations{outExt}");
        ExportOne(resource, target, includeLods, includeOcc, group.Motions, materialWrappers, includeTextures && i == 0, additionalResources, progress, i + 1, nonEmptyMotlistGroups.Count, safe);
        progress.WriteLine($"[{i + 1}/{nonEmptyMotlistGroups.Count}] {target}");
    }
}
else if (exportSeparateAnimationFiles)
{
    if (motions.Count == 0) throw new ArgumentException("--batch-motlist requires --motlist with at least one selected motion");
    var ext = Path.GetExtension(outputPath);
    var outDir = string.IsNullOrEmpty(ext) ? outputPath : (Path.GetDirectoryName(outputPath) ?? ".");
    var outExt = string.IsNullOrEmpty(ext) ? ".glb" : ext;
    Directory.CreateDirectory(outDir);
    Console.WriteLine($"Batch exporting {motions.Count} motions to {outDir} (*{outExt})");
    var index = 0;
    var includeSourceInName = motlistPaths.Count + motPaths.Count > 1;
    foreach (var (source, motion) in motions)
    {
        var safe = SanitizeFileName(string.IsNullOrWhiteSpace(motion.Name) ? $"motion_{index:0000}" : motion.Name);
        var sourcePrefix = includeSourceInName ? SanitizeFileName(source) + "_" : "";
        var targetBase = Path.Combine(outDir, $"{index:0000}_{sourcePrefix}{safe}{outExt}");
        var target = ResolveExportJobOutputPath(targetBase, meshPath, BuildSourceFiles(meshPath, additionalMeshPaths, [source], []), safe);
        ExportOne(resource, target, includeLods, includeOcc, [motion], materialWrappers, includeTextures, additionalResources, progress, index + 1, motions.Count, safe);
        progress.WriteLine($"[{index + 1}/{motions.Count}] {target}");
        index++;
    }
}
else
{
    var singleOutputPath = ResolveSingleOutputPath(outputPath, meshPath, name, BuildSourceFiles(meshPath, additionalMeshPaths, motlistPaths, motPaths), animationFilter);
    ExportOne(resource, singleOutputPath, includeLods, includeOcc, motions.Select(m => m.Motion), materialWrappers, includeTextures, additionalResources, progress);
}

progress.WriteLine("DONE");

static void ExportOne(
    CommonMeshResource resource,
    string target,
    bool includeLods,
    bool includeOcc,
    IEnumerable<MotFileBase> motions,
    IReadOnlyList<(MaterialGroupWrapper Materials, string MeshPath)> materialWrappers,
    bool includeTextures,
    IReadOnlyList<CommonMeshResource> additionalResources,
    ProgressStatus progress,
    int? exportIndex = null,
    int? exportTotal = null,
    string? exportLabel = null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
    try
    {
        if (includeTextures && materialWrappers.Count > 0)
            ExportMaterialTextures(materialWrappers, Path.Combine(Path.GetDirectoryName(target) ?? ".", "textures"), resource.ExportTextureFormat, progress);

        resource.ExportAnimationProgress = (current, total, name) => progress.Update($"Exporting animation {current}/{total}: {name}");
        resource.ExportProgress = message => progress.Update(FormatExportProgress(message, exportIndex, exportTotal, exportLabel));
        progress.Start("Preparing export");
        resource.ExportToFile(target, includeLods, includeOcc, null, motions, additionalResources);
        progress.Update("Finalizing output");
        NormalizeGlbNames(target);
        if (resource.ExportSkipMotionsWithMissingBones)
            WriteSkippedAnimationReport(target, resource.ExportSkippedAnimations, progress);
        if (resource.ExportNoPlaceholderAnimationBones)
            WriteSkippedAnimationBoneChannelReport(target, resource.ExportSkippedAnimationBoneChannels, progress);
    }
    finally
    {
        resource.ExportAnimationProgress = null;
        resource.ExportProgress = null;
        progress.Stop();
    }
    progress.WriteLine($"Exported {target} bytes={new FileInfo(target).Length}");
}

static string FormatExportProgress(string message, int? exportIndex, int? exportTotal, string? exportLabel)
{
    if (exportIndex == null || exportTotal == null) return message;

    var formatted = $"{message} {exportIndex}/{exportTotal}";
    if (!string.IsNullOrWhiteSpace(exportLabel))
        formatted += $": {exportLabel}";
    return formatted;
}

static void WriteSkippedAnimationReport(string target, IReadOnlyList<string> skippedAnimations, ProgressStatus progress)
{
    var reportPath = Path.Combine(
        Path.GetDirectoryName(target) ?? ".",
        Path.GetFileNameWithoutExtension(target) + ".skipped-animations.md");
    using var writer = new StreamWriter(reportPath, append: false, Encoding.UTF8);
    writer.WriteLine("# Skipped animations");
    writer.WriteLine();
    writer.WriteLine($"Output: `{target}`");
    writer.WriteLine();
    writer.WriteLine("Reason: `--skip-missing-animation-bones` was enabled, so animations that reference bones missing from the exported mesh skeleton were excluded instead of adding placeholder `hash...` bones.");
    writer.WriteLine();
    if (skippedAnimations.Count == 0)
    {
        writer.WriteLine("No animations were skipped.");
    }
    else
    {
        writer.WriteLine($"Skipped animation count: {skippedAnimations.Count}");
        writer.WriteLine();
        foreach (var skipped in skippedAnimations)
        {
            writer.WriteLine($"- {skipped}");
        }
    }
    progress.WriteLine($"Wrote skipped animation report: {reportPath}");
}

static void WriteSkippedAnimationBoneChannelReport(string target, IReadOnlyList<string> skippedBoneChannels, ProgressStatus progress)
{
    var reportPath = Path.Combine(
        Path.GetDirectoryName(target) ?? ".",
        Path.GetFileNameWithoutExtension(target) + ".skipped-animation-bones.md");
    using var writer = new StreamWriter(reportPath, append: false, Encoding.UTF8);
    writer.WriteLine("# Skipped animation bone channels");
    writer.WriteLine();
    writer.WriteLine($"Output: `{target}`");
    writer.WriteLine();
    writer.WriteLine("Reason: `--no-placeholder-animation-bones` was enabled, so animations were kept but channels that target missing skeleton bones were skipped instead of creating placeholder `hash...` bones.");
    writer.WriteLine();
    if (skippedBoneChannels.Count == 0)
    {
        writer.WriteLine("No animation bone channels were skipped.");
    }
    else
    {
        writer.WriteLine($"Skipped bone channel count: {skippedBoneChannels.Count}");
        writer.WriteLine();
        foreach (var skipped in skippedBoneChannels)
        {
            writer.WriteLine($"- {skipped}");
        }
    }
    progress.WriteLine($"Wrote skipped animation bone channel report: {reportPath}");
}

static void WriteSkippedMotlistReport(string jobDir, IReadOnlyList<(string SourceName, string MotlistPath)> skippedMotlists, string? animationFilter, ProgressStatus progress)
{
    var reportPath = Path.Combine(jobDir, "skipped-motlists.md");
    using var writer = new StreamWriter(reportPath, append: false, Encoding.UTF8);
    writer.WriteLine("# Skipped MOTLISTs");
    writer.WriteLine();
    writer.WriteLine($"Output directory: `{jobDir}`");
    writer.WriteLine();
    writer.WriteLine("Reason: `--split-motlists` skips MOTLISTs with zero selected animations, so empty FBX files and unnecessary Blender re-export work are not created.");
    writer.WriteLine();
    if (!string.IsNullOrWhiteSpace(animationFilter))
    {
        writer.WriteLine($"Animation filter: `{animationFilter}`");
        writer.WriteLine();
    }
    if (skippedMotlists.Count == 0)
    {
        writer.WriteLine("No MOTLISTs were skipped.");
    }
    else
    {
        writer.WriteLine($"Skipped MOTLIST count: {skippedMotlists.Count}");
        writer.WriteLine();
        foreach (var (sourceName, motlistPath) in skippedMotlists)
        {
            var label = string.IsNullOrWhiteSpace(sourceName) ? PathUtils.GetFilenameWithoutExtensionOrVersion(motlistPath).ToString() : sourceName;
            writer.WriteLine($"- `{label}`");
            writer.WriteLine($"  - Path: `{motlistPath}`");
        }
    }
    progress.WriteLine($"Wrote skipped MOTLIST report: {reportPath}");
}

static string ResolveSingleOutputPath(string outputPath, string meshPath, string meshName, IReadOnlyList<string> sourceFiles, string? animationFilter)
{
    if (!string.IsNullOrEmpty(Path.GetExtension(outputPath)))
        return ResolveExportJobOutputPath(outputPath, meshPath, sourceFiles, animationFilter);

    Directory.CreateDirectory(outputPath);
    return ResolveExportJobOutputPath(Path.Combine(outputPath, $"{SanitizeFileName(meshName)}_all_animations.glb"), meshPath, sourceFiles, animationFilter);
}

static string ResolveSplitMotlistOutputDirectory(string outputPath, string meshPath, IReadOnlyList<string> sourceFiles, string? animationFilter)
{
    var parentDir = string.IsNullOrEmpty(Path.GetExtension(outputPath))
        ? outputPath
        : (Path.GetDirectoryName(outputPath) ?? ".");
    Directory.CreateDirectory(parentDir);

    var jobDir = Path.Combine(parentDir, BuildExportJobFolderName(meshPath, sourceFiles, string.IsNullOrWhiteSpace(animationFilter) ? "split_motlists" : $"split_motlists_{animationFilter}"));
    Directory.CreateDirectory(jobDir);
    return jobDir;
}

static string ResolveExportJobOutputPath(string outputFilePath, string meshPath, IReadOnlyList<string> sourceFiles, string? label)
{
    var parentDir = Path.GetDirectoryName(outputFilePath) ?? ".";
    var outputFileName = Path.GetFileName(outputFilePath);
    var jobDir = Path.Combine(parentDir, BuildExportJobFolderName(meshPath, sourceFiles, label));
    Directory.CreateDirectory(jobDir);
    return Path.Combine(jobDir, outputFileName);
}

static IReadOnlyList<string> BuildSourceFiles(string meshPath, IReadOnlyList<string> additionalMeshPaths, IReadOnlyList<string> motlistPathsOrNames, IReadOnlyList<string> motPaths)
{
    var files = new List<string> { meshPath };
    files.AddRange(additionalMeshPaths);
    files.AddRange(motlistPathsOrNames);
    files.AddRange(motPaths);
    return files;
}

static string BuildExportJobFolderName(string meshPath, IReadOnlyList<string> sourceFiles, string? label)
{
    var meshName = SourceNamePart(meshPath);
    if (string.IsNullOrWhiteSpace(meshName)) meshName = "export";
    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var hash = ShortHash(string.Join("|", sourceFiles) + "|" + label + "|" + timestamp);
    return $"{meshName}__{timestamp}__{hash}";
}

static string SourceNamePart(string source)
{
    if (source.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !source.Contains(Path.DirectorySeparatorChar) && !source.Contains(Path.AltDirectorySeparatorChar))
        return SanitizeFileName(source);

    return SanitizeFileName(PathUtils.GetFilenameWithoutExtensionOrVersion(source).ToString());
}

static string ShortHash(string value)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(bytes, 0, 3).ToLowerInvariant();
}

static void NormalizeGlbNames(string target)
{
    if (!Path.GetExtension(target).Equals(".glb", StringComparison.OrdinalIgnoreCase)) return;

    var data = File.ReadAllBytes(target);
    if (data.Length < 20 || Encoding.ASCII.GetString(data, 0, 4) != "glTF") return;

    var version = BitConverter.ToUInt32(data, 4);
    var chunks = new List<(string Type, byte[] Data)>();
    var offset = 12;
    while (offset + 8 <= data.Length)
    {
        var chunkLength = checked((int)BitConverter.ToUInt32(data, offset));
        var chunkType = Encoding.ASCII.GetString(data, offset + 4, 4);
        offset += 8;
        if (offset + chunkLength > data.Length) return;
        chunks.Add((chunkType, data[offset..(offset + chunkLength)]));
        offset += chunkLength;
    }

    var jsonIndex = chunks.FindIndex(c => c.Type == "JSON");
    if (jsonIndex == -1) return;

    var jsonText = Encoding.UTF8.GetString(chunks[jsonIndex].Data).TrimEnd('\0', ' ', '\r', '\n', '\t');
    var root = JsonNode.Parse(jsonText)?.AsObject();
    if (root == null) return;

    if (root["nodes"] is JsonArray nodes)
    {
        if (nodes.Count > 0 && nodes[0] is JsonObject rootNode)
            rootNode["name"] = "Armature";

        foreach (var node in nodes.OfType<JsonObject>())
        {
            if (node["name"]?.GetValue<string>() is { } nodeName && nodeName.Contains("_Group_", StringComparison.Ordinal))
                node["name"] = StripMeshNamePrefix(nodeName);
        }
    }
    if (root["meshes"] is JsonArray meshes)
    {
        foreach (var mesh in meshes.OfType<JsonObject>())
        {
            if (mesh["name"]?.GetValue<string>() is { } meshName)
                mesh["name"] = StripMeshNamePrefix(meshName);
        }
    }
    if (root["skins"] is JsonArray skins)
    {
        foreach (var skin in skins.OfType<JsonObject>())
            skin["name"] = "Armature";
    }

    var newJson = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    chunks[jsonIndex] = ("JSON", PadChunk(newJson, 0x20));

    var totalLength = 12 + chunks.Sum(c => 8 + c.Data.Length);
    using var stream = new MemoryStream(totalLength);
    using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
    writer.Write(Encoding.ASCII.GetBytes("glTF"));
    writer.Write(version);
    writer.Write(totalLength);
    foreach (var (type, chunk) in chunks)
    {
        writer.Write(chunk.Length);
        writer.Write(Encoding.ASCII.GetBytes(type));
        writer.Write(chunk);
    }
    File.WriteAllBytes(target, stream.ToArray());
}

static byte[] PadChunk(byte[] data, byte padByte)
{
    var paddedLength = (data.Length + 3) & ~3;
    if (paddedLength == data.Length) return data;
    var output = new byte[paddedLength];
    Array.Copy(data, output, data.Length);
    Array.Fill(output, padByte, data.Length, paddedLength - data.Length);
    return output;
}

static string StripMeshNamePrefix(string name)
{
    var marker = "_Group_";
    var markerIndex = name.IndexOf(marker, StringComparison.Ordinal);
    return markerIndex >= 0 ? name[(markerIndex + 1)..] : name;
}

static void ExportMaterialTextures(IReadOnlyList<(MaterialGroupWrapper Materials, string MeshPath)> materialGroups, string outputDir, string textureFormat, ProgressStatus progress)
{
    Directory.CreateDirectory(outputDir);
    var exported = new Dictionary<string, object>();
    var textureCount = materialGroups.Sum(group => group.Materials.Materials.Sum(mat => mat.Textures.Count(tex => !string.IsNullOrWhiteSpace(tex.texPath) && !tex.texPath.Contains("/null", StringComparison.OrdinalIgnoreCase))));
    var textureIndex = 0;
    progress.Start($"Exporting textures 0/{textureCount}");
    foreach (var (materials, meshPath) in materialGroups)
    {
        foreach (var mat in materials.Materials)
        {
            var matEntries = new List<object>();
            foreach (var tex in mat.Textures)
            {
                if (string.IsNullOrWhiteSpace(tex.texPath) || tex.texPath.Contains("/null", StringComparison.OrdinalIgnoreCase)) continue;
                textureIndex++;
                progress.Update($"Exporting texture {textureIndex}/{textureCount}: {PathUtils.GetFilenameWithoutExtensionOrVersion(tex.texPath)}");
                var source = ResolveLooseGameFile(meshPath, tex.texPath, "tex");
                if (source == null) continue;
                FileHandler? texHandler = null;
                FileHandler? streamHandler = null;
                try
                {
                    texHandler = new FileHandler(source);
                    TexFile texFile = new TexFile(texHandler);
                    if (!texFile.Read()) continue;
                    DecompressTextureIfNeeded(texFile);
                    if (texFile.Header.flags.HasFlag(ReeLib.Tex.TexFlags.IsStreaming))
                    {
                        var streamCandidate = ResolveLooseGameFile(meshPath, PathUtils.GetStreamingPath(tex.texPath), "tex");
                        if (streamCandidate != null)
                        {
                            streamHandler = new FileHandler(streamCandidate);
                            var streamTex = new TexFile(streamHandler);
                            if (streamTex.Read())
                            {
                                DecompressTextureIfNeeded(streamTex);
                                texFile = streamTex;
                            }
                        }
                    }
                    var outName = TextureOutputName(mat, tex, textureFormat);
                    var outPath = Path.Combine(outputDir, outName);
                    if (textureFormat == "dds")
                    {
                        texFile.SaveAsDDS(outPath);
                    }
                    else
                    {
                        var tempDds = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(outName) + ".dds");
                        texFile.SaveAsDDS(tempDds);
                        ConvertDdsToPng(tempDds, outPath);
                        try { File.Delete(tempDds); }
                        catch (Exception cleanupError)
                        {
                            Console.WriteLine($"WARNING: temporary DDS cleanup failed {tempDds}: {cleanupError.Message}");
                        }
                    }
                    matEntries.Add(new { type = tex.texType, gamePath = tex.texPath, source, output = outPath });
                }
                catch (Exception ex)
                {
                    progress.WriteLine($"WARNING: texture export failed {tex.texPath}: {ex.Message}");
                }
                finally
                {
                    streamHandler?.Dispose();
                    texHandler?.Dispose();
                }
            }
            exported[mat.Name] = matEntries;
        }
    }
    var manifest = Path.Combine(outputDir, "materials.textures.json");
    File.WriteAllText(manifest, JsonSerializer.Serialize(exported, new JsonSerializerOptions { WriteIndented = true }));
    progress.WriteLine($"Exported material texture manifest: {manifest}");
    progress.Stop();
}

static void DecompressTextureIfNeeded(TexFile tex)
{
    if (!tex.MustBeCompressed || !tex.IsCompressed) return;

    tex.DecompressGDeflate(static (level, compressedBytes, decompressedBytes) =>
    {
        if (!GDeflateNet.GDeflate.Decompress(compressedBytes, decompressedBytes))
        {
            Console.WriteLine($"WARNING: failed to GDeflate-decompress texture mip level {level}");
            return level > 0;
        }
        return true;
    });
}

static string TextureOutputName(MaterialGroupWrapper.MaterialLookupData mat, TexHeader tex, string textureFormat)
{
    var name = SanitizeFileName(PathUtils.GetFilenameWithoutExtensionOrVersion(tex.texPath).ToString());
    var ext = "." + textureFormat;
    if (tex == mat.AlbedoTexture) return name + ext;
    if (tex == mat.NormalTexture) return name + "_normal" + ext;
    if (tex == mat.ATXXTexture) return name + "_alpha" + ext;
    return name + "_" + SanitizeFileName(tex.texType) + ext;
}

static void ConvertDdsToPng(string ddsPath, string pngPath)
{
    var texconv = ResolveTool("texconv")
        ?? Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"), "texconv.exe", SearchOption.AllDirectories).FirstOrDefault()
        ?? throw new FileNotFoundException("texconv.exe not found. Install Microsoft.DirectXTex.Texconv or use --texture-format dds.");
    var outDir = Path.GetDirectoryName(pngPath) ?? ".";
    var psi = new ProcessStartInfo
    {
        FileName = texconv,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    psi.ArgumentList.Add("-ft");
    psi.ArgumentList.Add("png");
    psi.ArgumentList.Add("-y");
    psi.ArgumentList.Add("-o");
    psi.ArgumentList.Add(outDir);
    psi.ArgumentList.Add(ddsPath);
    using var proc = Process.Start(psi) ?? throw new Exception("Failed to start texconv");
    proc.WaitForExit();
    if (proc.ExitCode != 0)
    {
        var err = proc.StandardError.ReadToEnd();
        var output = proc.StandardOutput.ReadToEnd();
        throw new Exception($"texconv DDS->PNG failed: {err}{output}");
    }
    var produced = Path.Combine(outDir, Path.GetFileNameWithoutExtension(ddsPath) + ".png");
    if (!File.Exists(produced)) throw new FileNotFoundException("texconv did not produce expected PNG", produced);
    if (!string.Equals(Path.GetFullPath(produced), Path.GetFullPath(pngPath), StringComparison.OrdinalIgnoreCase))
    {
        File.Move(produced, pngPath, overwrite: true);
    }
}

static string? ResolveTool(string exe)
{
    var path = Environment.GetEnvironmentVariable("PATH") ?? "";
    foreach (var dir in path.Split(Path.PathSeparator))
    {
        if (string.IsNullOrWhiteSpace(dir)) continue;
        var candidate = Path.Combine(dir.Trim(), exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : exe + ".exe");
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}

static MeshFile LoadMesh(string meshPath, string? explicitStreamingPath, bool allowMissingStreaming)
{
    using var meshHandler = new FileHandler(meshPath);
    var mesh = new MeshFile(meshHandler);
    if (!mesh.Read()) throw new Exception($"REE-Lib failed to read mesh: {meshPath}");
    Console.WriteLine($"Loaded mesh {meshPath} version={mesh.Header.version} requiresStreaming={mesh.RequiresStreamingData} materials={mesh.MaterialNames.Count} bones={mesh.BoneData?.Bones.Count ?? 0} lods={mesh.MeshData?.LODs.Count ?? 0}");

    if (!string.IsNullOrWhiteSpace(explicitStreamingPath))
    {
        using var streamingHandler = new FileHandler(explicitStreamingPath);
        mesh.LoadStreamingData(streamingHandler);
        Console.WriteLine($"Loaded explicit streaming buffer: {explicitStreamingPath}");
    }
    else if (mesh.RequiresStreamingData)
    {
        var candidate = FindStreamingCandidate(meshPath);
        if (candidate != null)
        {
            using var streamingHandler = new FileHandler(candidate);
            mesh.LoadStreamingData(streamingHandler);
            Console.WriteLine($"Loaded auto streaming buffer: {candidate}");
        }
        else if (allowMissingStreaming)
        {
            Console.WriteLine($"WARNING: mesh requires streaming data, but no candidate was found. Output may be invalid: {meshPath}");
        }
        else
        {
            throw new FileNotFoundException("Mesh requires streaming data, but no streaming buffer was found. Pass --streaming for the primary mesh or extract the natives/STM/streaming sibling path.", meshPath);
        }
    }

    return mesh;
}

static string? FindStreamingCandidate(string meshPath)
{
    var dir = Path.GetDirectoryName(meshPath) ?? ".";
    var baseNoVersion = Path.GetFileNameWithoutExtension(meshPath);
    var normalized = meshPath.Replace('/', Path.DirectorySeparatorChar).Replace("\\", Path.DirectorySeparatorChar.ToString());
    var marker = Path.DirectorySeparatorChar + "natives" + Path.DirectorySeparatorChar + "STM" + Path.DirectorySeparatorChar;
    string? stmStreaming = null;
    var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (idx >= 0)
    {
        var prefix = normalized[..(idx + marker.Length)];
        var suffix = normalized[(idx + marker.Length)..];
        stmStreaming = Path.Combine(prefix, "streaming", suffix);
    }

    string? reChunkStreaming = null;
    var reChunkMarker = Path.DirectorySeparatorChar + "re_chunk_000" + Path.DirectorySeparatorChar;
    var reChunkIdx = normalized.IndexOf(reChunkMarker, StringComparison.OrdinalIgnoreCase);
    if (reChunkIdx >= 0)
    {
        var prefix = normalized[..(reChunkIdx + reChunkMarker.Length)];
        var suffix = normalized[(reChunkIdx + reChunkMarker.Length)..];
        if (!suffix.StartsWith("streaming" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            reChunkStreaming = Path.Combine(prefix, "streaming", suffix);
        }
    }

    var first = Path.Combine(dir, baseNoVersion + "streaming");
    var candidates = new[] { stmStreaming, reChunkStreaming, meshPath + ".streaming", meshPath + ".meshstreaming", first, first + ".meshstreaming", Path.ChangeExtension(meshPath, ".meshstreaming") };
    return candidates.Where(c => !string.IsNullOrWhiteSpace(c)).FirstOrDefault(File.Exists);
}

static string? FindMdfCandidate(string meshPath)
{
    var dir = Path.GetDirectoryName(meshPath) ?? ".";
    var baseName = PathUtils.GetFilenameWithoutExtensionOrVersion(meshPath).ToString();
    var names = new[] { baseName + "_mat.mdf2", baseName + "_Mat.mdf2", baseName + ".mdf2", baseName + "_00.mdf2" };
    foreach (var n in names)
    {
        var exact = Path.Combine(dir, n);
        if (File.Exists(exact)) return exact;
        var glob = Directory.GetFiles(dir, n + ".*", SearchOption.TopDirectoryOnly).OrderByDescending(x => x).FirstOrDefault();
        if (glob != null) return glob;
    }
    return null;
}

static string? ResolveLooseGameFile(string meshPath, string gamePath, string extension)
{
    var rel = gamePath.Replace('/', Path.DirectorySeparatorChar).Replace("\\", Path.DirectorySeparatorChar.ToString()).TrimStart(Path.DirectorySeparatorChar);

    var candidateRoots = GetLooseRoots(meshPath).ToList();
    if (candidateRoots.Count == 0) return null;

    var candidateRels = new List<string>();
    AddUnique(candidateRels, rel);
    if (!rel.StartsWith("natives" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    {
        AddUnique(candidateRels, Path.Combine("natives", "STM", rel));
    }
    else
    {
        var nativesStmPrefix = "natives" + Path.DirectorySeparatorChar + "STM" + Path.DirectorySeparatorChar;
        if (rel.StartsWith(nativesStmPrefix, StringComparison.OrdinalIgnoreCase))
        {
            AddUnique(candidateRels, rel[nativesStmPrefix.Length..]);
        }
    }

    foreach (var root in candidateRoots)
    {
        foreach (var candidateRel in candidateRels)
        {
            var found = ResolveLooseGameFileCandidate(root, candidateRel, extension);
            if (found != null) return found;
        }
    }

    return null;
}

static string? ResolveLooseGameFileCandidate(string root, string rel, string extension)
{
    var noVersion = Path.Combine(root, rel);
    if (File.Exists(noVersion)) return noVersion;
    var dir = Path.GetDirectoryName(noVersion);
    var file = Path.GetFileName(noVersion);
    if (dir == null || !Directory.Exists(dir)) return null;
    var patterns = new[] { file + "." + extension + ".*", file + ".*", PathUtils.GetFilepathWithoutExtensionOrVersion(file).ToString() + "." + extension + ".*" };
    foreach (var pat in patterns)
    {
        var found = Directory.GetFiles(dir, pat, SearchOption.TopDirectoryOnly).OrderByDescending(x => x).FirstOrDefault();
        if (found != null) return found;
    }
    return null;
}

static IEnumerable<string> GetLooseRoots(string path)
{
    var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace("\\", Path.DirectorySeparatorChar.ToString());
    var roots = new List<string>();

    var marker = Path.DirectorySeparatorChar + "natives" + Path.DirectorySeparatorChar;
    var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (idx >= 0) AddUnique(roots, normalized[..idx]);

    var reChunkMarker = Path.DirectorySeparatorChar + "re_chunk_000" + Path.DirectorySeparatorChar;
    var reChunkIdx = normalized.IndexOf(reChunkMarker, StringComparison.OrdinalIgnoreCase);
    if (reChunkIdx >= 0) AddUnique(roots, normalized[..(reChunkIdx + reChunkMarker.Length)]);

    return roots;
}

static void AddUnique(List<string> values, string value)
{
    if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.OrdinalIgnoreCase))
    {
        values.Add(value);
    }
}

static string SanitizeFileName(string name)
{
    foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
    return string.IsNullOrWhiteSpace(name) ? "unnamed" : name;
}

sealed class ProgressStatus : IDisposable
{
    private readonly object sync = new();
    private readonly bool enabled = !Console.IsOutputRedirected;
    private readonly string[] frames = [".", "..", "..."];
    private Timer? timer;
    private string message = "";
    private int frameIndex;
    private int lastLength;
    private bool active;

    public void Start(string text)
    {
        if (!enabled)
        {
            Console.WriteLine(text);
            return;
        }

        lock (sync)
        {
            message = text;
            frameIndex = 0;
            active = true;
            timer ??= new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
            DrawLocked();
        }
    }

    public void Update(string text)
    {
        if (!enabled) return;

        lock (sync)
        {
            message = text;
            active = true;
            DrawLocked();
        }
    }

    public void Stop()
    {
        if (!enabled) return;

        lock (sync)
        {
            active = false;
            timer?.Dispose();
            timer = null;
            ClearLocked();
        }
    }

    public void WriteLine(string text)
    {
        if (!enabled)
        {
            Console.WriteLine(text);
            return;
        }

        lock (sync)
        {
            var redraw = active;
            ClearLocked();
            Console.WriteLine(text);
            if (redraw) DrawLocked();
        }
    }

    public void Dispose() => Stop();

    private void Tick()
    {
        if (!enabled) return;

        lock (sync)
        {
            if (!active) return;
            frameIndex = (frameIndex + 1) % frames.Length;
            DrawLocked();
        }
    }

    private void DrawLocked()
    {
        if (!active) return;

        var line = $"{message} {frames[frameIndex]}";
        Console.Write('\r');
        Console.Write(line);
        if (lastLength > line.Length)
            Console.Write(new string(' ', lastLength - line.Length));
        lastLength = line.Length;
    }

    private void ClearLocked()
    {
        if (lastLength <= 0) return;

        Console.Write('\r');
        Console.Write(new string(' ', lastLength));
        Console.Write('\r');
        lastLength = 0;
    }
}

sealed class WizardConfig
{
    public string ExtractRoot { get; set; } = "";
    public string DefaultExportRoot { get; set; } = "";
    public string BlenderPath { get; set; } = "";
    public string TextureFormat { get; set; } = "png";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

sealed record PragmataIndexEntry(string RelativePath)
{
    public string FileName { get; } = Path.GetFileName(RelativePath.Replace('/', Path.DirectorySeparatorChar));
    public string RelativeDirectory { get; } = Path.GetDirectoryName(RelativePath.Replace('/', Path.DirectorySeparatorChar))?.Replace('\\', '/') ?? "";
}

sealed record ResolvedAsset(string Path, string? IndexedRelativePath);

sealed record WizardMeshInspection(int BoneCount, int MaterialCount, int LodCount, bool RequiresStreaming);

enum AssetKind
{
    Mesh,
    Motlist,
}

enum WizardAnimationMode
{
    None,
    MotlistDirectory,
    Motlists,
}

sealed class WizardAnimationSelection
{
    public static WizardAnimationSelection None { get; } = new(WizardAnimationMode.None, null, []);

    public WizardAnimationMode Mode { get; }
    public string? MotlistDirectory { get; }
    public IReadOnlyList<string> Motlists { get; }

    private WizardAnimationSelection(WizardAnimationMode mode, string? motlistDirectory, IReadOnlyList<string> motlists)
    {
        Mode = mode;
        MotlistDirectory = motlistDirectory;
        Motlists = motlists;
    }

    public static WizardAnimationSelection FromMotlistDirectory(string path) => new(WizardAnimationMode.MotlistDirectory, path, []);
    public static WizardAnimationSelection FromMotlists(IReadOnlyList<string> paths) => new(WizardAnimationMode.Motlists, null, paths);
}
