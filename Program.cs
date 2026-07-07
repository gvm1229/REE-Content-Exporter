using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;
using ContentEditor;
using ContentEditor.App.FileLoaders;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mdf;
using ReeLib.Mot;

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
    Console.WriteLine("  REE-Content-Exporter-GUI [--gui|--wizard] [--reset-config] [--config <path>]");
    Console.WriteLine("  REE-Content-Exporter-CLI --mesh <mesh.path> [--game <game-id>] [--additional-mesh <mesh.path> ...] [--streaming <meshstream.path>] [--additional-streaming <mesh.path=meshstream.path> ...] [--mdf <mdf2.path>] [--motlist <motlist.path> ...|--motlist-dir <folder>|--mot <mot.path> ...] --output <file.fbx|file.glb|folder> [--animation-name <contains>] [--scene-actor <actor-id>] [--allow-mixed-scene-animations] [--batch-motlist|--split-animations|--split-motlists] [--skip-missing-animation-bones|--no-placeholder-animation-bones] [--no-animations] [--no-textures] [--texture-format png|dds] [--fbx-scale <scale>] [--fix-ch6500-armblade-translation] [--unreal-ready-fbx --blender <blender.exe> [--keep-source-fbx] [--bone-spacing-reference-fbx <fbx> [--bone-spacing-reference-action <contains>] [--bone-spacing-allow-translation <bones>]]] [--include-lods] [--include-occlusion] [--allow-missing-streaming]");
    Console.WriteLine("  REE-Content-Exporter-CLI --dependency-versions");
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

const string ReePakToolProjectsRawBaseUrl = "https://raw.githubusercontent.com/Ekey/REE.PAK.Tool/refs/heads/main/Projects/";

static WizardGameDefinition GetGameDefinition(string gameId)
{
    if (TryGetGameDefinition(gameId, out var game)) return game;
    throw new ArgumentException($"Unsupported game: {gameId}");
}

static bool TryGetGameDefinition(string? gameId, out WizardGameDefinition definition)
{
    if (!string.IsNullOrWhiteSpace(gameId))
    {
        var normalized = NormalizeGameId(gameId);
        foreach (var game in WizardGames.Definitions)
        {
            if (game.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                || game.DisplayName.Equals(gameId, StringComparison.OrdinalIgnoreCase)
                || game.GameName.ToString().Equals(gameId, StringComparison.OrdinalIgnoreCase))
            {
                definition = game;
                return true;
            }
        }
    }

    definition = null!;
    return false;
}

static string NormalizeGameId(string value) => value.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "");

static string ResolveWizardListDirectory(string configPath)
{
    var configDir = Path.GetDirectoryName(configPath);
    if (string.IsNullOrWhiteSpace(configDir)) configDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(configDir, "lists");
}

static string ResolveWizardListPath(string configPath, WizardGameDefinition game)
    => Path.Combine(ResolveWizardListDirectory(configPath), game.ListFileName);

static void DownloadGameList(WizardGameDefinition game, string targetPath, WizardLanguage language)
{
    Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ".");
    var url = ReePakToolProjectsRawBaseUrl + Uri.EscapeDataString(game.ListFileName);
    Console.WriteLine(language == WizardLanguage.Korean ? $"REE.PAK.Tool 목록을 다운로드합니다: {game.ListFileName}" : $"Downloading REE.PAK.Tool list: {game.ListFileName}");
    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.ParseAdd("REE-Content-Exporter/0.4");
    var bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
    if (bytes.Length == 0) throw new InvalidOperationException($"Downloaded list was empty: {url}");
    File.WriteAllBytes(targetPath, bytes);
    Console.WriteLine(language == WizardLanguage.Korean ? $"목록을 저장했습니다: {targetPath}" : $"Saved game list: {targetPath}");
}

static WizardConfig EnsureWizardGameConfig(WizardConfig? config, string configPath, WizardLanguage language)
{
    config ??= new WizardConfig();

    if (string.IsNullOrWhiteSpace(config.Game))
    {
        var selected = PromptWizardGame(language);
        config.Game = selected.Id;
        config.GameDisplayName = selected.DisplayName;
        config.GameListFile = selected.ListFileName;
        config.GameListPath = ResolveWizardListPath(configPath, selected);
        DownloadGameList(selected, config.GameListPath, language);
        SaveWizardConfig(configPath, config);
        return config;
    }

    if (!TryGetGameDefinition(config.Game, out var game))
    {
        throw new ArgumentException($"Unsupported configured game '{config.Game}'. Delete the \"game\" line from {configPath} to choose a supported game.");
    }

    config.Game = game.Id;
    config.GameDisplayName = game.DisplayName;
    config.GameListFile = game.ListFileName;
    if (string.IsNullOrWhiteSpace(config.GameListPath)
        || !Path.GetFileName(config.GameListPath).Equals(game.ListFileName, StringComparison.OrdinalIgnoreCase))
    {
        config.GameListPath = ResolveWizardListPath(configPath, game);
    }
    if (!File.Exists(config.GameListPath) || new FileInfo(config.GameListPath).Length == 0)
    {
        DownloadGameList(game, config.GameListPath, language);
        SaveWizardConfig(configPath, config);
    }

    return config;
}

static WizardGameDefinition PromptWizardGame(WizardLanguage language)
{
    Console.WriteLine(language == WizardLanguage.Korean ? "게임 구성 선택:" : "Select game configuration:");
    for (var i = 0; i < WizardGames.Definitions.Count; i++)
    {
        var game = WizardGames.Definitions[i];
        Console.WriteLine($"  {i + 1}. {game.DisplayName} ({game.Id}, {game.ListFileName})");
    }

    while (true)
    {
        Console.Write(language == WizardLanguage.Korean ? $"1-{WizardGames.Definitions.Count} 중 선택: " : $"Choose 1-{WizardGames.Definitions.Count}: ");
        var input = (Console.ReadLine() ?? "").Trim();
        if (int.TryParse(input, out var selected) && selected >= 1 && selected <= WizardGames.Definitions.Count)
        {
            PrintWizardPromptSeparator();
            return WizardGames.Definitions[selected - 1];
        }
        Console.WriteLine(language == WizardLanguage.Korean ? "잘못된 선택입니다." : "Invalid selection.");
    }
}

static void PrintCurrentGameConfiguration(WizardConfig config, string configPath, WizardLanguage language)
{
    var display = string.IsNullOrWhiteSpace(config.GameDisplayName) ? config.Game : config.GameDisplayName;
    Console.WriteLine(language == WizardLanguage.Korean
        ? $"현재 게임 구성: {display} (다른 게임을 설정하려면 {configPath} 파일에서 \"game\" 줄을 삭제하세요)"
        : $"Current game configuration: {display} (delete the \"game\" line from {configPath} to set a different game)");
    PrintWizardPromptSeparator();
}

static GameName ResolveConfiguredGameName(WizardConfig? config, string? explicitGame)
{
    if (!string.IsNullOrWhiteSpace(explicitGame))
        return GetGameDefinition(explicitGame).GameName;
    if (!string.IsNullOrWhiteSpace(config?.Game))
        return GetGameDefinition(config.Game).GameName;
    return GameName.unknown;
}

static void RunWizard(string? configPathOverride)
{
    var configPath = ResolveWizardConfigPath(configPathOverride);
    var config = LoadWizardConfig(configPath);
    var language = ResolveWizardLanguage(config);
    Console.WriteLine(language == WizardLanguage.Korean ? "REE-Content-Exporter 대화형 마법사" : "REE-Content-Exporter interactive wizard");

    if (config != null && string.IsNullOrWhiteSpace(config.Language))
    {
        config.Language = SerializeWizardLanguage(language);
        SaveWizardConfig(configPath, config);
        Console.WriteLine(language == WizardLanguage.Korean ? $"마법사 언어 설정을 저장했습니다: {configPath}" : $"Saved wizard language setting: {configPath}");
    }

    config = EnsureWizardGameConfig(config, configPath, language);
    config.Language = SerializeWizardLanguage(language);
    SaveWizardConfig(configPath, config);
    PrintCurrentGameConfiguration(config, configPath, language);

    var reason = "";
    if (config == null || !ValidateWizardConfig(config, out reason))
    {
        if (!string.IsNullOrWhiteSpace(reason)) Console.WriteLine(language == WizardLanguage.Korean ? $"설정이 필요합니다: {LocalizeConfigReason(reason, language)}" : $"Config setup required: {reason}");
        config = PromptForWizardConfig(config, language);
        config.Language = SerializeWizardLanguage(language);
        SaveWizardConfig(configPath, config);
        Console.WriteLine(language == WizardLanguage.Korean ? $"마법사 설정을 저장했습니다: {configPath}" : $"Saved wizard config: {configPath}");
    }

    var index = LoadGameIndex(config);
    var mode = PromptWizardMode(language);
    if (mode == WizardMode.BatchCsv)
    {
        var skeletalMode = PromptBatchSkeletalMode(language);
        var existingExportScan = PromptBatchExistingExportScan(language);
        RunBatchCsvWizard(config, index, skeletalMode, existingExportScan, language);
        return;
    }

    RunSingleMeshWizard(config, index, language);
}

static void RunSingleMeshWizard(WizardConfig config, IReadOnlyList<GameListEntry> index, WizardLanguage language)
{
    var mesh = PromptForAsset(language == WizardLanguage.Korean ? "기본 메시" : "Primary mesh", AssetKind.Mesh, config, index, language);
    var additionalMeshes = new List<ResolvedAsset>();
    while (PromptYesNo(language == WizardLanguage.Korean ? "다른 메시 파트를 추가할까요?" : "Add another mesh part?", defaultValue: false, language))
    {
        additionalMeshes.Add(PromptForAsset(language == WizardLanguage.Korean ? "추가 메시" : "Additional mesh", AssetKind.Mesh, config, index, language));
    }

    var streaming = FindStreamingCandidate(mesh.Path);
    var additionalStreaming = additionalMeshes
        .Select(asset => new { Mesh = asset.Path, Streaming = FindStreamingCandidate(asset.Path) })
        .Where(pair => pair.Streaming != null)
        .ToDictionary(pair => pair.Mesh, pair => pair.Streaming!, StringComparer.OrdinalIgnoreCase);

    var inspected = InspectMeshForWizard(mesh.Path, streaming);
    var isSkeletal = inspected.BoneCount > 0;
    Console.WriteLine(FormatMeshInspection(inspected, language));

    var animation = WizardAnimationSelection.None;
    if (isSkeletal && PromptYesNo(language == WizardLanguage.Korean ? "애니메이션을 포함할까요?" : "Include animations?", defaultValue: false, language))
    {
        animation = PromptForAnimationSelection(mesh.Path, config, index, language);
    }

    var exportRoot = PromptExportRoot(config.DefaultExportRoot, language);
    var scriptPath = GenerateWizardScript(config, exportRoot, mesh.Path, additionalMeshes.Select(m => m.Path).ToList(), streaming, additionalStreaming, animation, isSkeletal);
    Console.WriteLine(language == WizardLanguage.Korean ? $"스크립트를 생성했습니다: {scriptPath}" : $"Generated script: {scriptPath}");

    if (PromptYesNo(language == WizardLanguage.Korean ? "생성된 스크립트를 지금 실행할까요?" : "Run the generated script now?", defaultValue: false, language))
    {
        RunGeneratedScript(scriptPath, language);
    }
}

static void RunBatchCsvWizard(WizardConfig config, IReadOnlyList<GameListEntry> index, WizardBatchSkeletalMode skeletalMode, WizardBatchExistingExportScan existingExportScan, WizardLanguage language)
{
    var csvPath = PromptFilePath(language == WizardLanguage.Korean ? "CSV 파일 경로" : "CSV file path", null, mustExist: true, language);
    if (!csvPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException(language == WizardLanguage.Korean ? "배치 가져오기는 .csv 파일이 필요합니다." : "Batch import requires a .csv file.");

    var meshQueries = ReadWizardCsvMeshQueries(csvPath, language);
    Console.WriteLine(language == WizardLanguage.Korean ? $"CSV에서 메시 행 {meshQueries.Count}개를 불러왔습니다." : $"Loaded {meshQueries.Count} mesh row(s) from CSV.");

    var jobs = new List<WizardExportJob>();
    var skippedRows = new List<WizardBatchSkippedRow>();
    var usedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var (rowNumber, query) in meshQueries)
    {
        try
        {
            var mesh = ResolveCsvMesh(rowNumber, query, config, index, language);
            var streaming = FindStreamingCandidate(mesh.Path);
            var inspected = InspectMeshForWizard(mesh.Path, streaming);
            var isSkeletal = inspected.BoneCount > 0;
            var assetName = SanitizeFileName(PathUtils.GetFilenameWithoutExtensionOrVersion(mesh.Path).ToString());
            var outputFolderName = MakeUniqueWizardFolderName(assetName, usedFolders);

            Console.WriteLine(language == WizardLanguage.Korean ? $"CSV {rowNumber}행: {mesh.Path}" : $"CSV row {rowNumber}: {mesh.Path}");
            Console.WriteLine(FormatMeshInspection(inspected, language));

            var animation = WizardAnimationSelection.None;
            if (isSkeletal && skeletalMode == WizardBatchSkeletalMode.PromptForAnimations && PromptYesNo(language == WizardLanguage.Korean ? $"{assetName}의 애니메이션을 포함할까요?" : $"Include animations for {assetName}?", defaultValue: false, language))
            {
                animation = PromptForAnimationSelection(mesh.Path, config, index, language);
            }
            else if (isSkeletal && skeletalMode == WizardBatchSkeletalMode.SkipAnimationPrompts)
            {
                Console.WriteLine(language == WizardLanguage.Korean ? $"배치 스켈레탈 정책: {assetName}은(는) 스켈레톤을 포함하되 애니메이션 없이 자동으로 내보냅니다." : $"Batch skeletal policy: exporting {assetName} with its skeleton, but without animations.");
            }

            jobs.Add(new WizardExportJob(
                RowNumber: rowNumber,
                MeshQuery: query,
                MeshPath: mesh.Path,
                OutputFolderName: outputFolderName,
                StreamingPath: streaming,
                Inspection: inspected,
                Animation: animation));
        }
        catch (Exception ex)
        {
            skippedRows.Add(new WizardBatchSkippedRow(rowNumber, query, ex.Message));
            Console.WriteLine(language == WizardLanguage.Korean ? $"CSV {rowNumber}행을 건너뜁니다: {ex.Message}" : $"Skipping CSV row {rowNumber}: {ex.Message}");
        }
    }

    var exportRoot = PromptExportRoot(config.DefaultExportRoot, language);
    var scriptPath = GenerateWizardBatchScript(config, exportRoot, jobs, skippedRows, existingExportScan);
    Console.WriteLine(language == WizardLanguage.Korean ? $"배치 스크립트를 생성했습니다: {scriptPath}" : $"Generated batch script: {scriptPath}");

    if (PromptYesNo(language == WizardLanguage.Korean ? "생성된 배치 스크립트를 지금 실행할까요?" : "Run the generated batch script now?", defaultValue: false, language))
    {
        RunGeneratedScript(scriptPath, language);
    }
}

static WizardConfig? LoadWizardConfig(string path)
{
    try
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<WizardConfig>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"WARNING: failed to read config {path}: {ex.Message}");
        return null;
    }
}

static WizardLanguage ResolveWizardLanguage(WizardConfig? config)
{
    if (TryParseWizardLanguage(config?.Language, out var language))
        return language;
    return PromptWizardLanguage();
}

static bool TryParseWizardLanguage(string? value, out WizardLanguage language)
{
    if (value != null)
    {
        if (value.Equals("en", StringComparison.OrdinalIgnoreCase) || value.Equals("english", StringComparison.OrdinalIgnoreCase))
        {
            language = WizardLanguage.English;
            return true;
        }
        if (value.Equals("ko", StringComparison.OrdinalIgnoreCase) || value.Equals("kr", StringComparison.OrdinalIgnoreCase) || value.Equals("korean", StringComparison.OrdinalIgnoreCase))
        {
            language = WizardLanguage.Korean;
            return true;
        }
    }

    language = WizardLanguage.English;
    return false;
}

static string SerializeWizardLanguage(WizardLanguage language) => language == WizardLanguage.Korean ? "ko" : "en";

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
        reason = "game extract path does not look like a loose-file RE Engine extract";
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

static WizardConfig PromptForWizardConfig(WizardConfig? existing, WizardLanguage language)
{
    var extractRoot = PromptExistingExtractRoot(existing?.ExtractRoot, language);
    var defaultExportRoot = PromptDirectoryPath(language == WizardLanguage.Korean ? "기본 내보내기 폴더" : "Default export folder", existing?.DefaultExportRoot, mustExist: false, language);
    var blenderPath = PromptFilePath(language == WizardLanguage.Korean ? "Blender 4.5.9 실행 파일" : "Blender 4.5.9 executable", existing?.BlenderPath ?? @"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe", mustExist: true, language);
    return new WizardConfig
    {
        Game = existing?.Game ?? "",
        GameDisplayName = existing?.GameDisplayName ?? "",
        GameListFile = existing?.GameListFile ?? "",
        GameListPath = existing?.GameListPath ?? "",
        Language = SerializeWizardLanguage(language),
        ExtractRoot = extractRoot,
        DefaultExportRoot = defaultExportRoot,
        BlenderPath = blenderPath,
        TextureFormat = string.IsNullOrWhiteSpace(existing?.TextureFormat) ? "png" : existing!.TextureFormat,
        CreatedUtc = existing?.CreatedUtc == default ? DateTimeOffset.UtcNow : existing!.CreatedUtc,
        UpdatedUtc = DateTimeOffset.UtcNow,
    };
}

static string PromptExistingExtractRoot(string? defaultValue, WizardLanguage language)
{
    while (true)
    {
        var input = PromptText(language == WizardLanguage.Korean ? "게임 추출 폴더 또는 그 안의 파일/폴더" : "Game extract folder or any file/folder inside it", defaultValue);
        var inferred = InferExtractRoot(input);
        if (inferred != null && Directory.Exists(inferred) && HasLikelyExtractLayout(inferred))
            return inferred;
        Console.WriteLine(language == WizardLanguage.Korean ? "기존 추출 루트를 찾을 수 없습니다. re_chunk_000, natives\\stm 같은 폴더 또는 추출 폴더 안의 파일을 붙여넣어 주세요." : "Could not infer an existing extract root. Paste a folder such as re_chunk_000, natives\\stm, or a file inside the extract.");
    }
}

static string PromptDirectoryPath(string label, string? defaultValue, bool mustExist, WizardLanguage language = WizardLanguage.English)
{
    while (true)
    {
        var input = NormalizeUserPath(PromptText(label, defaultValue));
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine(language == WizardLanguage.Korean ? "경로는 비워둘 수 없습니다." : "Path cannot be empty.");
            continue;
        }
        if (Directory.Exists(input) || !mustExist)
            return Path.GetFullPath(input);
        Console.WriteLine(language == WizardLanguage.Korean ? "폴더가 존재하지 않습니다." : "Folder does not exist.");
    }
}

static string PromptFilePath(string label, string? defaultValue, bool mustExist, WizardLanguage language = WizardLanguage.English)
{
    while (true)
    {
        var input = NormalizeUserPath(PromptText(label, defaultValue));
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine(language == WizardLanguage.Korean ? "경로는 비워둘 수 없습니다." : "Path cannot be empty.");
            continue;
        }
        if (File.Exists(input) || !mustExist)
            return Path.GetFullPath(input);
        Console.WriteLine(language == WizardLanguage.Korean ? "파일이 존재하지 않습니다." : "File does not exist.");
    }
}

static string PromptText(string label, string? defaultValue = null)
{
    Console.Write(string.IsNullOrWhiteSpace(defaultValue) ? $"{label}: " : $"{label} [{defaultValue}]: ");
    var input = Console.ReadLine();
    PrintWizardPromptSeparator();
    if (string.IsNullOrWhiteSpace(input) && !string.IsNullOrWhiteSpace(defaultValue)) return defaultValue;
    return input?.Trim() ?? "";
}

static bool PromptYesNo(string label, bool defaultValue, WizardLanguage language = WizardLanguage.English)
{
    while (true)
    {
        Console.Write($"{label} [y/n]: ");
        var input = (Console.ReadLine() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine(language == WizardLanguage.Korean ? "y 또는 n을 입력해 주세요." : "Enter y or n.");
            continue;
        }
        if (input.Equals("y", StringComparison.OrdinalIgnoreCase) || input.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            PrintWizardPromptSeparator();
            return true;
        }
        if (input.Equals("n", StringComparison.OrdinalIgnoreCase) || input.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            PrintWizardPromptSeparator();
            return false;
        }
        Console.WriteLine(language == WizardLanguage.Korean ? "yes 또는 no를 입력해 주세요." : "Enter yes or no.");
    }
}

static void PrintWizardPromptSeparator()
{
    Console.WriteLine();
    Console.WriteLine("------------------------------------------------------------");
    Console.WriteLine();
}

static WizardLanguage PromptWizardLanguage()
{
    Console.WriteLine("Language / 언어:");
    Console.WriteLine("  1. English");
    Console.WriteLine("  2. Korean");
    while (true)
    {
        Console.Write("Choose 1-2: ");
        var input = (Console.ReadLine() ?? "").Trim();
        if (input == "1")
        {
            PrintWizardPromptSeparator();
            return WizardLanguage.English;
        }
        if (input == "2")
        {
            PrintWizardPromptSeparator();
            return WizardLanguage.Korean;
        }
        Console.WriteLine("Invalid selection. / 잘못된 선택입니다.");
    }
}

static string LocalizeConfigReason(string reason, WizardLanguage language)
{
    if (language != WizardLanguage.Korean) return reason;
    return reason switch
    {
        "game extract path is missing or does not exist" => "게임 추출 경로가 없거나 존재하지 않습니다",
        "game extract path does not look like a loose-file RE Engine extract" => "게임 추출 경로가 RE Engine loose-file 추출 구조처럼 보이지 않습니다",
        "default export path is missing" => "기본 내보내기 경로가 없습니다",
        "Blender executable is missing or does not exist" => "Blender 실행 파일이 없거나 존재하지 않습니다",
        _ => reason,
    };
}

static string FormatMeshInspection(WizardMeshInspection inspected, WizardLanguage language)
{
    var isSkeletal = inspected.BoneCount > 0;
    return language == WizardLanguage.Korean
        ? $"메시 유형: {(isSkeletal ? "스켈레탈" : "정적")} (본={inspected.BoneCount}, 머티리얼={inspected.MaterialCount}, LOD={inspected.LodCount}, 스트리밍필요={inspected.RequiresStreaming})"
        : $"Mesh type: {(isSkeletal ? "skeletal" : "static")} (bones={inspected.BoneCount}, materials={inspected.MaterialCount}, lods={inspected.LodCount}, requiresStreaming={inspected.RequiresStreaming})";
}

static WizardMode PromptWizardMode(WizardLanguage language)
{
    Console.WriteLine(language == WizardLanguage.Korean ? "마법사 내보내기 모드:" : "Wizard export mode:");
    Console.WriteLine(language == WizardLanguage.Korean ? "  1. 내보낼 메시 선택" : "  1. Select a mesh to export");
    Console.WriteLine(language == WizardLanguage.Korean ? "  2. 배치 메시 내보내기용 CSV 파일 선택" : "  2. Choose a CSV file for batch mesh export");
    while (true)
    {
        Console.Write(language == WizardLanguage.Korean ? "1-2 중 선택: " : "Choose 1-2: ");
        var input = (Console.ReadLine() ?? "").Trim();
        if (input == "1")
        {
            PrintWizardPromptSeparator();
            return WizardMode.SingleMesh;
        }
        if (input == "2")
        {
            PrintWizardPromptSeparator();
            return WizardMode.BatchCsv;
        }
        Console.WriteLine(language == WizardLanguage.Korean ? "잘못된 선택입니다." : "Invalid selection.");
    }
}

static WizardBatchSkeletalMode PromptBatchSkeletalMode(WizardLanguage language)
{
    Console.WriteLine(language == WizardLanguage.Korean ? "배치 스켈레탈 메시 처리:" : "Batch skeletal mesh handling:");
    Console.WriteLine(language == WizardLanguage.Korean ? "  1. 스켈레탈 메시를 찾으면 애니메이션 지정 여부 묻기" : "  1. Prompt for animations when a skeletal mesh is found");
    Console.WriteLine(language == WizardLanguage.Korean ? "  2. 스켈레탈 메시도 애니메이션 질문 없이 자동 처리" : "  2. Auto-export skeletal meshes without prompting for animations");
    while (true)
    {
        Console.Write(language == WizardLanguage.Korean ? "1-2 중 선택: " : "Choose 1-2: ");
        var input = (Console.ReadLine() ?? "").Trim();
        if (input == "1")
        {
            PrintWizardPromptSeparator();
            return WizardBatchSkeletalMode.PromptForAnimations;
        }
        if (input == "2")
        {
            PrintWizardPromptSeparator();
            return WizardBatchSkeletalMode.SkipAnimationPrompts;
        }
        Console.WriteLine(language == WizardLanguage.Korean ? "잘못된 선택입니다." : "Invalid selection.");
    }
}

static WizardBatchExistingExportScan PromptBatchExistingExportScan(WizardLanguage language)
{
    Console.WriteLine(language == WizardLanguage.Korean ? "기존 배치 내보내기 검색:" : "Existing batch export scan:");
    Console.WriteLine(language == WizardLanguage.Korean ? "  1. 기존 배치 내보내기 자동 검색" : "  1. Auto-scan the existing batch exports");
    Console.WriteLine(language == WizardLanguage.Korean ? "  2. 내보내기가 들어 있는 폴더 지정" : "  2. Designate a folder that houses the exports");
    while (true)
    {
        Console.Write(language == WizardLanguage.Korean ? "1-2 중 선택: " : "Choose 1-2: ");
        var input = (Console.ReadLine() ?? "").Trim();
        if (input == "1")
        {
            PrintWizardPromptSeparator();
            return WizardBatchExistingExportScan.Auto;
        }
        if (input == "2")
        {
            PrintWizardPromptSeparator();
            var path = PromptDirectoryPath(language == WizardLanguage.Korean ? "기존 내보내기가 들어 있는 폴더" : "Folder containing existing exports", null, mustExist: true, language);
            return WizardBatchExistingExportScan.Designated(path);
        }
        Console.WriteLine(language == WizardLanguage.Korean ? "잘못된 선택입니다." : "Invalid selection.");
    }
}

static IReadOnlyList<(int RowNumber, string Query)> ReadWizardCsvMeshQueries(string csvPath, WizardLanguage language)
{
    var rows = new List<(int RowNumber, string Query)>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var firstRow = true;
    var lineNumber = 0;
    foreach (var line in File.ReadLines(csvPath))
    {
        lineNumber++;
        var cells = ParseCsvLine(line, lineNumber, language);
        if (cells.Count != 1)
            throw new ArgumentException(language == WizardLanguage.Korean ? $"CSV {lineNumber}행은 정확히 한 개의 열만 포함해야 하지만 {cells.Count}개를 찾았습니다." : $"CSV row {lineNumber} must contain exactly one column, but found {cells.Count}.");

        var value = NormalizeCsvCell(cells[0]);
        if (firstRow)
        {
            firstRow = false;
            if (IsWizardCsvMeshHeader(value)) continue;
        }

        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(language == WizardLanguage.Korean ? $"CSV {lineNumber}행이 비어 있습니다. 배치 메시 CSV에서 빈 행을 제거해 주세요." : $"CSV row {lineNumber} is blank. Remove blank rows from the batch mesh CSV.");

        var normalized = NormalizeIndexPath(value);
        if (!seen.Add(normalized))
            throw new ArgumentException(language == WizardLanguage.Korean ? $"CSV {lineNumber}행이 이전 메시 항목과 중복됩니다: {value}" : $"CSV row {lineNumber} duplicates an earlier mesh entry: {value}");

        rows.Add((lineNumber, value));
    }

    if (rows.Count == 0)
        throw new ArgumentException(language == WizardLanguage.Korean ? "CSV 가져오기에 메시 이름이 없습니다." : "CSV import did not contain any mesh names.");

    return rows;
}

static IReadOnlyList<string> ParseCsvLine(string line, int lineNumber, WizardLanguage language = WizardLanguage.English)
{
    var cells = new List<string>();
    var current = new StringBuilder();
    var inQuotes = false;
    for (var i = 0; i < line.Length; i++)
    {
        var c = line[i];
        if (inQuotes)
        {
            if (c == '"')
            {
                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }
            }
            else
            {
                current.Append(c);
            }
            continue;
        }

        if (c == ',')
        {
            cells.Add(current.ToString());
            current.Clear();
            continue;
        }

        if (c == '"')
        {
            if (current.ToString().Trim().Length != 0)
                throw new ArgumentException(language == WizardLanguage.Korean ? $"CSV {lineNumber}행의 따옴표 없는 필드 안에 예상치 못한 따옴표가 있습니다." : $"CSV row {lineNumber} has an unexpected quote inside an unquoted field.");
            current.Clear();
            inQuotes = true;
            continue;
        }

        current.Append(c);
    }

    if (inQuotes)
        throw new ArgumentException(language == WizardLanguage.Korean ? $"CSV {lineNumber}행에 닫히지 않은 따옴표 필드가 있습니다." : $"CSV row {lineNumber} has an unterminated quoted field.");

    cells.Add(current.ToString());
    return cells;
}

static string NormalizeCsvCell(string value) => value.Trim().Trim('\uFEFF');

static bool IsWizardCsvMeshHeader(string value)
    => value.Equals("mesh", StringComparison.OrdinalIgnoreCase)
    || value.Equals("mesh_name", StringComparison.OrdinalIgnoreCase)
    || value.Equals("name", StringComparison.OrdinalIgnoreCase);

static ResolvedAsset ResolveCsvMesh(int rowNumber, string query, WizardConfig config, IReadOnlyList<GameListEntry> index, WizardLanguage language)
{
    var matches = ResolveAssetQuery(query, AssetKind.Mesh, config, index);
    if (matches.Count == 0)
        throw new ArgumentException(language == WizardLanguage.Korean ? $"CSV {rowNumber}행이 기존 non-streaming 메시로 해석되지 않았습니다: {query}" : $"CSV row {rowNumber} did not resolve to an existing non-streaming mesh: {query}");
    if (matches.Count == 1) return matches[0];
    Console.WriteLine(language == WizardLanguage.Korean ? $"CSV {rowNumber}행이 여러 메시와 일치합니다: {query}" : $"CSV row {rowNumber} matched multiple meshes for: {query}");
    return ChooseAsset(language == WizardLanguage.Korean ? $"CSV {rowNumber}행 메시" : $"CSV row {rowNumber} mesh", matches, language);
}

static string MakeUniqueWizardFolderName(string baseName, ISet<string> usedFolders)
{
    var safe = string.IsNullOrWhiteSpace(baseName) ? "mesh" : SanitizeFileName(baseName);
    var candidate = safe;
    var index = 2;
    while (!usedFolders.Add(candidate))
    {
        candidate = $"{safe}_{index}";
        index++;
    }
    return candidate;
}

static ResolvedAsset PromptForAsset(string label, AssetKind kind, WizardConfig config, IReadOnlyList<GameListEntry> index, WizardLanguage language)
{
    while (true)
    {
        var query = PromptText(language == WizardLanguage.Korean ? $"{label} 파일 이름/경로" : $"{label} filename/path");
        if (string.IsNullOrWhiteSpace(query)) continue;
        var matches = ResolveAssetQuery(query, kind, config, index);
        if (matches.Count == 0)
        {
            Console.WriteLine(language == WizardLanguage.Korean ? "일치하는 기존 파일을 찾지 못했습니다. 전체 파일 경로나 추출 파일의 파일 이름을 붙여넣을 수 있습니다." : "No matching existing file was found. You can paste a full file path or a filename from the extract.");
            continue;
        }
        if (matches.Count == 1) return matches[0];
        return ChooseAsset(label, matches, language);
    }
}

static ResolvedAsset ChooseAsset(string label, IReadOnlyList<ResolvedAsset> matches, WizardLanguage language)
{
    var limit = Math.Min(matches.Count, 25);
    Console.WriteLine(language == WizardLanguage.Korean ? $"{label} 일치 항목:" : $"{label} matches:");
    for (var i = 0; i < limit; i++)
    {
        Console.WriteLine($"  {i + 1}. {matches[i].Path}");
    }
    if (matches.Count > limit) Console.WriteLine(language == WizardLanguage.Korean ? $"  ... {matches.Count - limit}개의 추가 일치 항목은 숨겨졌습니다. 더 구체적인 검색어로 좁혀 주세요." : $"  ... {matches.Count - limit} more matches hidden. Type a more specific query to narrow them.");
    while (true)
    {
        Console.Write(language == WizardLanguage.Korean ? $"1-{limit} 중 선택: " : $"Choose 1-{limit}: ");
        if (int.TryParse(Console.ReadLine(), out var selected) && selected >= 1 && selected <= limit)
        {
            PrintWizardPromptSeparator();
            return matches[selected - 1];
        }
        Console.WriteLine(language == WizardLanguage.Korean ? "잘못된 선택입니다." : "Invalid selection.");
    }
}

static WizardAnimationSelection PromptForAnimationSelection(string meshPath, WizardConfig config, IReadOnlyList<GameListEntry> index, WizardLanguage language)
{
    var inferred = InferAnimationCandidates(meshPath, config, index);
    if (inferred.HasAnyCandidates)
    {
        return PromptForInferredAnimationSelection(inferred, language);
    }

    Console.WriteLine(language == WizardLanguage.Korean
        ? "메시 이름으로 자동 감지된 애니메이션 파일이 없습니다. 수동으로 선택합니다."
        : "No animation files were inferred from the mesh name. Choose animation sources manually.");
    return PromptForManualAnimationSelection(config, index, language);
}

static WizardAnimationSelection PromptForInferredAnimationSelection(WizardAnimationCandidates inferred, WizardLanguage language)
{
    Console.WriteLine(language == WizardLanguage.Korean ? "자동 감지된 애니메이션 후보:" : "Inferred animation candidates:");
    if (!string.IsNullOrWhiteSpace(inferred.MotlistDirectory))
    {
        Console.WriteLine($"[.motlist folder] \"{inferred.MotlistDirectory}\" found.");
    }
    if (inferred.MotFiles.Count == 1)
    {
        Console.WriteLine($"[.mot file] \"{inferred.MotFiles[0]}\" found.");
    }
    else if (inferred.MotFiles.Count > 1)
    {
        Console.WriteLine($"[.mot files] {inferred.MotFiles.Count} files found.");
    }

    var choices = new List<(int Number, string Label, Func<WizardAnimationSelection> Select)>();
    var next = 1;
    if (!string.IsNullOrWhiteSpace(inferred.MotlistDirectory))
    {
        choices.Add((next++, language == WizardLanguage.Korean ? "폴더의 모든 .motlist 파일 포함" : "Include all .motlist files in the folder", () => WizardAnimationSelection.FromMotlistDirectory(inferred.MotlistDirectory!)));
        choices.Add((next++, language == WizardLanguage.Korean ? "폴더에서 .motlist 파일 선택" : "Select .motlist file(s) from the folder", () => PromptForMotlistFilesFromFolder(inferred.MotlistDirectory!, language)));
    }
    if (inferred.MotFiles.Count > 0)
    {
        choices.Add((next++, language == WizardLanguage.Korean ? ".mot 파일만 포함" : "Select only the .mot file(s)", () => WizardAnimationSelection.FromMotFiles(inferred.MotFiles)));
    }
    if (!string.IsNullOrWhiteSpace(inferred.MotlistDirectory) && inferred.MotFiles.Count > 0)
    {
        choices.Add((next++, language == WizardLanguage.Korean ? "위 항목 모두 포함" : "Select all of the above", () => WizardAnimationSelection.FromMotlistDirectoryAndMotFiles(inferred.MotlistDirectory!, inferred.MotFiles)));
    }

    foreach (var choice in choices)
    {
        Console.WriteLine($"  {choice.Number}. {choice.Label}");
    }
    while (true)
    {
        Console.Write(language == WizardLanguage.Korean ? $"1-{choices.Count} 중 선택: " : $"Choose 1-{choices.Count}: ");
        if (int.TryParse(Console.ReadLine(), out var selected))
        {
            var choice = choices.FirstOrDefault(item => item.Number == selected);
            if (choice.Select != null)
            {
                PrintWizardPromptSeparator();
                return choice.Select();
            }
        }
        Console.WriteLine(language == WizardLanguage.Korean ? "잘못된 선택입니다." : "Invalid selection.");
    }
}

static WizardAnimationSelection PromptForMotlistFilesFromFolder(string folder, WizardLanguage language)
{
    var files = Directory.GetFiles(folder, "*.motlist*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (files.Count == 0)
    {
        throw new InvalidOperationException($"No .motlist files were found in {folder}");
    }

    Console.WriteLine(language == WizardLanguage.Korean ? "MOTLIST 파일:" : "MOTLIST files:");
    for (var i = 0; i < files.Count; i++)
    {
        Console.WriteLine($"  {i + 1}. {files[i]}");
    }

    while (true)
    {
        Console.Write(language == WizardLanguage.Korean ? "선택할 번호를 공백으로 구분해 입력: " : "Enter selected numbers separated by whitespace: ");
        var input = Console.ReadLine() ?? "";
        var selected = ParseNumberSelection(input, files.Count);
        if (selected.Count > 0)
        {
            PrintWizardPromptSeparator();
            return WizardAnimationSelection.FromMotlists(selected.Select(index => files[index - 1]).ToList());
        }
        Console.WriteLine(language == WizardLanguage.Korean ? "번호를 하나 이상 올바르게 입력하세요." : "Enter at least one valid number.");
    }
}

static IReadOnlyList<int> ParseNumberSelection(string input, int max)
{
    var selected = new List<int>();
    foreach (var token in input.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!int.TryParse(token, out var number) || number < 1 || number > max) return [];
        if (!selected.Contains(number)) selected.Add(number);
    }
    return selected;
}

static WizardAnimationSelection PromptForManualAnimationSelection(WizardConfig config, IReadOnlyList<GameListEntry> index, WizardLanguage language)
{
    Console.WriteLine(language == WizardLanguage.Korean ? "애니메이션 소스:" : "Animation source:");
    Console.WriteLine(language == WizardLanguage.Korean ? "  1. MOTLIST 폴더" : "  1. MOTLIST folder");
    Console.WriteLine(language == WizardLanguage.Korean ? "  2. MOTLIST 파일" : "  2. MOTLIST files");
    Console.WriteLine(language == WizardLanguage.Korean ? "  3. MOT 파일" : "  3. MOT files");
    int sourceMode;
    while (true)
    {
        Console.Write(language == WizardLanguage.Korean ? "1-3 중 선택: " : "Choose 1-3: ");
        if (int.TryParse(Console.ReadLine(), out sourceMode) && sourceMode >= 1 && sourceMode <= 3)
        {
            PrintWizardPromptSeparator();
            break;
        }
        Console.WriteLine(language == WizardLanguage.Korean ? "잘못된 선택입니다." : "Invalid selection.");
    }

    if (sourceMode == 1)
    {
        while (true)
        {
            var query = PromptText(language == WizardLanguage.Korean ? "MOTLIST 폴더 경로 또는 검색어" : "MOTLIST folder path or search term");
            var matches = ResolveMotlistDirectoryQuery(query, config, index);
            if (matches.Count == 0)
            {
                Console.WriteLine(language == WizardLanguage.Korean ? "일치하는 MOTLIST 폴더를 찾지 못했습니다." : "No matching MOTLIST folder was found.");
                continue;
            }
            var folder = matches.Count == 1 ? matches[0] : ChoosePath(language == WizardLanguage.Korean ? "MOTLIST 폴더" : "MOTLIST folder", matches, language);
            return WizardAnimationSelection.FromMotlistDirectory(folder);
        }
    }

    var kind = sourceMode == 3 ? AssetKind.Mot : AssetKind.Motlist;
    var label = sourceMode == 3 ? "MOT" : "MOTLIST";
    var selectedFiles = new List<string>();
    while (true)
    {
        var query = PromptText(selectedFiles.Count == 0
            ? (language == WizardLanguage.Korean ? $"{label} 파일 이름/경로" : $"{label} filename/path")
            : (language == WizardLanguage.Korean ? $"다음 {label} 파일 이름/경로, 또는 done" : $"Next {label} filename/path, or done"));
        if (IsDoneInput(query))
        {
            if (selectedFiles.Count > 0) break;
            Console.WriteLine(language == WizardLanguage.Korean ? $"{label} 파일을 하나 이상 선택하거나, 다시 시작해서 애니메이션 없음을 선택해 주세요." : $"Select at least one {label} file, or restart and choose no animations.");
            continue;
        }
        var matches = ResolveAssetQuery(query, kind, config, index);
        if (matches.Count == 0)
        {
            Console.WriteLine(language == WizardLanguage.Korean ? $"일치하는 {label} 파일을 찾지 못했습니다." : $"No matching {label} file was found.");
            continue;
        }
        var selected = matches.Count == 1 ? matches[0] : ChooseAsset(label, matches, language);
        if (!selectedFiles.Contains(selected.Path, StringComparer.OrdinalIgnoreCase)) selectedFiles.Add(selected.Path);
    }
    return sourceMode == 3
        ? WizardAnimationSelection.FromMotFiles(selectedFiles)
        : WizardAnimationSelection.FromMotlists(selectedFiles);
}

static string ChoosePath(string label, IReadOnlyList<string> paths, WizardLanguage language)
{
    var limit = Math.Min(paths.Count, 25);
    Console.WriteLine(language == WizardLanguage.Korean ? $"{label} 일치 항목:" : $"{label} matches:");
    for (var i = 0; i < limit; i++)
    {
        Console.WriteLine($"  {i + 1}. {paths[i]}");
    }
    if (paths.Count > limit) Console.WriteLine(language == WizardLanguage.Korean ? $"  ... {paths.Count - limit}개의 추가 일치 항목은 숨겨졌습니다. 더 구체적인 검색어로 좁혀 주세요." : $"  ... {paths.Count - limit} more matches hidden. Type a more specific query to narrow them.");
    while (true)
    {
        Console.Write(language == WizardLanguage.Korean ? $"1-{limit} 중 선택: " : $"Choose 1-{limit}: ");
        if (int.TryParse(Console.ReadLine(), out var selected) && selected >= 1 && selected <= limit)
        {
            PrintWizardPromptSeparator();
            return paths[selected - 1];
        }
        Console.WriteLine(language == WizardLanguage.Korean ? "잘못된 선택입니다." : "Invalid selection.");
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

static string PromptExportRoot(string defaultExportRoot, WizardLanguage language)
{
    if (PromptYesNo(language == WizardLanguage.Korean ? $"기본 내보내기 폴더를 사용할까요? ({defaultExportRoot})" : $"Use default export folder ({defaultExportRoot})?", defaultValue: true, language))
        return defaultExportRoot;
    return PromptDirectoryPath(language == WizardLanguage.Korean ? "사용자 지정 내보내기 폴더" : "Custom export folder", null, mustExist: false, language);
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

static IReadOnlyList<GameListEntry> LoadGameIndex(WizardConfig config)
{
    if (string.IsNullOrWhiteSpace(config.GameListPath))
        throw new FileNotFoundException("Configured game list path is missing. Delete the \"game\" line from config.json and run the wizard again.");
    if (!File.Exists(config.GameListPath))
        throw new FileNotFoundException("Configured game list file was not found. Delete the \"game\" line from config.json and run the wizard again.", config.GameListPath);

    using var stream = File.OpenRead(config.GameListPath);
    using var reader = new StreamReader(stream);
    var entries = new List<GameListEntry>();
    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        line = line.Trim();
        if (line.Length == 0) continue;
        entries.Add(new GameListEntry(line));
    }
    return entries;
}

static IReadOnlyList<ResolvedAsset> ResolveAssetQuery(string query, AssetKind kind, WizardConfig config, IReadOnlyList<GameListEntry> index)
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
    if (kind == AssetKind.Mot && !IsMotPath(full)) return null;
    return new ResolvedAsset(full, null);
}

static IReadOnlyList<string> ResolveMotlistDirectoryQuery(string query, WizardConfig config, IReadOnlyList<GameListEntry> index)
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

static WizardAnimationCandidates InferAnimationCandidates(string meshPath, WizardConfig config, IReadOnlyList<GameListEntry> index)
{
    var meshName = PathUtils.GetFilenameWithoutExtensionOrVersion(meshPath).ToString();
    if (string.IsNullOrWhiteSpace(meshName)) return WizardAnimationCandidates.Empty(meshName);
    var searchTerms = BuildAnimationSearchTerms(meshName);

    var motlistFiles = ResolveInferredAnimationFiles(searchTerms, AssetKind.Motlist, config, index);
    var motlistDirectory = motlistFiles
        .Select(Path.GetDirectoryName)
        .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        .GroupBy(path => path!, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Key)
        .FirstOrDefault();

    var motFiles = ResolveInferredAnimationFiles(searchTerms, AssetKind.Mot, config, index);
    return new WizardAnimationCandidates(meshName, motlistDirectory, motFiles);
}

static IReadOnlyList<string> BuildAnimationSearchTerms(string meshName)
{
    var terms = new List<string> { meshName };
    var parts = meshName.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    for (var take = parts.Length - 1; take >= 2; take--)
    {
        terms.Add(string.Join('_', parts.Take(take)));
    }
    if (parts.Length > 1) terms.Add(parts[0]);
    return terms
        .Where(term => term.Length >= 3)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(term => term.Length)
        .ToList();
}

static IReadOnlyList<string> ResolveInferredAnimationFiles(IReadOnlyList<string> searchTerms, AssetKind kind, WizardConfig config, IReadOnlyList<GameListEntry> index)
{
    return index
        .Where(entry => EntryMatchesKind(entry, kind))
        .Where(entry => EntryMatchesAnimationMeshName(entry, searchTerms))
        .SelectMany(entry => GenerateDiskCandidates(config.ExtractRoot, entry.RelativePath))
        .Where(File.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static bool EntryMatchesAnimationMeshName(GameListEntry entry, IReadOnlyList<string> searchTerms)
    => searchTerms.Any(term =>
        entry.RelativePath.Contains(term, StringComparison.OrdinalIgnoreCase)
        || entry.FileName.Contains(term, StringComparison.OrdinalIgnoreCase));

static bool EntryMatchesKind(GameListEntry entry, AssetKind kind) => kind switch
{
    AssetKind.Mesh => IsMeshPath(entry.RelativePath) && !IsStreamingPath(entry.RelativePath),
    AssetKind.Motlist => IsMotlistPath(entry.RelativePath),
    AssetKind.Mot => IsMotPath(entry.RelativePath),
    _ => false,
};

static bool EntryMatchesQuery(GameListEntry entry, string normalizedQuery, string fileQuery)
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
    var stmRel = AddNativesStmPrefix(directRel);
    var roots = new List<string> { root };
    if (Directory.Exists(Path.Combine(root, "re_chunk_000"))) roots.Add(Path.Combine(root, "re_chunk_000"));
    if (EndsWithSegments(root, "natives", "stm"))
    {
        roots.Add(Path.GetFullPath(Path.Combine(root, "..", "..")));
    }

    foreach (var candidateRoot in roots.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        foreach (var candidateRel in new[] { rel, directRel, stmRel }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(candidateRoot, candidateRel);
            yield return Path.Combine(candidateRoot, "re_chunk_000", candidateRel);
        }
    }
}

static string StripNativesStmPrefix(string rel)
{
    var prefix = "natives" + Path.DirectorySeparatorChar + "stm" + Path.DirectorySeparatorChar;
    return rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? rel[prefix.Length..] : rel;
}

static string AddNativesStmPrefix(string rel)
{
    var prefix = "natives" + Path.DirectorySeparatorChar + "stm" + Path.DirectorySeparatorChar;
    return rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? rel : prefix + rel;
}

static string NormalizeIndexPath(string value) => NormalizeUserPath(value).Replace('\\', '/').TrimStart('/');

static string NormalizeUserPath(string value)
{
    value = StripSurroundingPathQuotes(value.Trim());
    return Environment.ExpandEnvironmentVariables(value);
}

static string StripSurroundingPathQuotes(string value)
{
    while (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
    {
        value = value[1..^1].Trim();
    }
    return value;
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
    if (string.IsNullOrWhiteSpace(path)) return null;
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
static bool IsMotPath(string path) => (path.Contains(".mot.", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mot", StringComparison.OrdinalIgnoreCase)) && !IsMotlistPath(path);
static bool IsStreamingPath(string path) => NormalizeIndexPath(path).Split('/').Contains("streaming", StringComparer.OrdinalIgnoreCase);

static string ResolveWizardConfigPath(string? overridePath)
{
    if (!string.IsNullOrWhiteSpace(overridePath)) return Path.GetFullPath(NormalizeUserPath(overridePath));
    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REE-Content-Exporter", "config.json");
}

static string ResolveCliExecutablePath()
{
    var cliPath = Path.Combine(AppContext.BaseDirectory, "REE-Content-Exporter-CLI.exe");
    if (File.Exists(cliPath)) return cliPath;
    return Environment.ProcessPath ?? cliPath;
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
    var exporterPath = ResolveCliExecutablePath();
    var script = BuildWizardPowerShell(config, exporterPath, exportRoot, meshPath, additionalMeshes, streamingPath, additionalStreaming, animation, isSkeletal);
    File.WriteAllText(scriptPath, script, Encoding.UTF8);
    return scriptPath;
}

static string GenerateWizardBatchScript(WizardConfig config, string exportRoot, IReadOnlyList<WizardExportJob> jobs, IReadOnlyList<WizardBatchSkippedRow> skippedRows, WizardBatchExistingExportScan existingExportScan)
{
    Directory.CreateDirectory(exportRoot);
    var scriptDir = Path.Combine(exportRoot, "generated-scripts");
    Directory.CreateDirectory(scriptDir);
    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var batchRoot = Path.Combine(exportRoot, $"wizard_batch_{timestamp}");
    var scriptPath = Path.Combine(scriptDir, $"wizard_batch_unreal_export_{timestamp}.ps1");
    var exporterPath = ResolveCliExecutablePath();
    var script = BuildWizardBatchPowerShell(config, exporterPath, batchRoot, jobs, skippedRows, existingExportScan);
    File.WriteAllText(scriptPath, script, Encoding.UTF8);
    return scriptPath;
}

static string BuildWizardBatchPowerShell(WizardConfig config, string exporterPath, string batchRoot, IReadOnlyList<WizardExportJob> jobs, IReadOnlyList<WizardBatchSkippedRow> skippedRows, WizardBatchExistingExportScan existingExportScan)
{
    var emptyAdditionalStreaming = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var existingScanMode = existingExportScan.Mode.ToString();
    var existingScanRoot = existingExportScan.Mode == WizardBatchExistingExportScanMode.Designated
        ? existingExportScan.DirectoryPath!
        : Path.GetDirectoryName(batchRoot) ?? ".";
    var jobBlocks = new List<string>();
    foreach (var job in jobs)
    {
        var jobRoot = Path.Combine(batchRoot, job.OutputFolderName);
        var jobScript = BuildWizardPowerShell(config, exporterPath, jobRoot, job.MeshPath, [], job.StreamingPath, emptyAdditionalStreaming, job.Animation, job.IsSkeletal);
        var jobScriptBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(jobScript));
        jobBlocks.Add($$"""
    [pscustomobject]@{
        Row = {{job.RowNumber.ToString(CultureInfo.InvariantCulture)}}
        Query = {{PsQuote(job.MeshQuery)}}
        Mesh = {{PsQuote(job.MeshPath)}}
        Folder = {{PsQuote(job.OutputFolderName)}}
        IsSkeletal = {{PsBool(job.IsSkeletal)}}
        HasAnimations = {{PsBool(job.Animation.Mode != WizardAnimationMode.None)}}
        ScriptBase64 = {{PsQuote(jobScriptBase64)}}
    }
""");
    }

    var skippedBlocks = new List<string>();
    foreach (var skipped in skippedRows)
    {
        skippedBlocks.Add($$"""
    [pscustomobject]@{
        Row = {{skipped.RowNumber.ToString(CultureInfo.InvariantCulture)}}
        Query = {{PsQuote(skipped.MeshQuery)}}
        Mesh = ""
        Status = "Skipped"
        Output = ""
        Details = {{PsQuote(skipped.Reason)}}
        Log = ""
    }
""");
    }

    return $$"""
param(
    [switch]$KeepSourceFbx
)

$ErrorActionPreference = "Stop"
$BatchRoot = {{PsQuote(batchRoot)}}
$ExistingScanMode = {{PsQuote(existingScanMode)}}
$ExistingScanRoot = {{PsQuote(existingScanRoot)}}
$BatchLogDir = Join-Path $BatchRoot "batch-job-logs"
$RunStamp = Get-Date -Format "yyyyMMdd_HHmmss"
$Jobs = @(
{{string.Join(",\n", jobBlocks)}}
)
$PreflightSkipped = @(
{{string.Join(",\n", skippedBlocks)}}
)
$Results = New-Object System.Collections.Generic.List[object]

function Format-MarkdownCell {
    param([object]$Value)
    if ($null -eq $Value) { return "" }
    return ($Value.ToString() -replace '\|', '\|' -replace "(`r`n|`n|`r)", " ")
}

function Get-PrefixedValue {
    param(
        [string[]]$Lines,
        [string]$Prefix
    )
    $match = $Lines | Where-Object { $_.StartsWith($Prefix, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -Last 1
    if (!$match) { return "" }
    return $match.Substring($Prefix.Length)
}

function New-BatchJobLogPath {
    param(
        [int]$Row,
        [string]$Folder
    )
    $safeFolder = if ([string]::IsNullOrWhiteSpace($Folder)) { "preflight" } else { $Folder -replace '[\\/:*?"<>|]', '_' }
    return Join-Path $BatchLogDir ("row{0:000}_{1}.log" -f $Row, $safeFolder)
}

function Write-BatchJobLog {
    param(
        [string]$Path,
        [string[]]$Lines
    )
    $parent = Split-Path $Path -Parent
    if (!(Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $Lines | Set-Content -LiteralPath $Path -Encoding UTF8
    Write-Host "BATCH_JOB_LOG=$Path"
}

function Find-ExistingSuccessfulBatchExport {
    param([object]$Job)

    if (!(Test-Path $ExistingScanRoot)) { return $null }
    $scanRootItem = Get-Item -LiteralPath $ExistingScanRoot -ErrorAction SilentlyContinue
    if (!$scanRootItem -or !$scanRootItem.PSIsContainer) { return $null }

    $candidateFolders = New-Object System.Collections.Generic.List[object]
    if ($ExistingScanMode -eq "Designated") {
        if ($scanRootItem.Name -like "wizard_batch_*") {
            $candidateFolders.Add($scanRootItem) | Out-Null
        }
        $candidateFolders.Add($scanRootItem) | Out-Null
        Get-ChildItem -LiteralPath $scanRootItem.FullName -Directory -Filter "wizard_batch_*" -ErrorAction SilentlyContinue |
            ForEach-Object { $candidateFolders.Add($_) | Out-Null }
    } else {
        Get-ChildItem -LiteralPath $scanRootItem.FullName -Directory -Filter "wizard_batch_*" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -ne $BatchRoot } |
            ForEach-Object { $candidateFolders.Add($_) | Out-Null }
    }

    $batchFolders = $candidateFolders |
        Where-Object { $_.FullName -ne $BatchRoot } |
        Sort-Object LastWriteTime -Descending

    foreach ($batchFolder in $batchFolders) {
        $meshFolder = Join-Path $batchFolder.FullName $Job.Folder
        if (!(Test-Path $meshFolder)) { continue }

        $successLog = Get-ChildItem -LiteralPath $meshFolder -Recurse -File -Filter "ree_export_wizard-SUCCESS.log" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if (!$successLog) { continue }

        $outputDir = Split-Path $successLog.FullName -Parent
        $fbx = Get-ChildItem -LiteralPath $outputDir -File -Filter "*_unreal.fbx" -ErrorAction SilentlyContinue |
            Where-Object { $_.Length -gt 0 } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if (!$fbx) { continue }

        return [pscustomobject]@{
            BatchRoot = $batchFolder.FullName
            Output = $outputDir
            SuccessLog = $successLog.FullName
            Fbx = $fbx.FullName
        }
    }

    return $null
}

New-Item -ItemType Directory -Force -Path $BatchRoot | Out-Null
New-Item -ItemType Directory -Force -Path $BatchLogDir | Out-Null
Write-Host "BATCH_EXPORT_ROOT=$BatchRoot"
Write-Host "BATCH_EXISTING_SCAN_MODE=$ExistingScanMode"
Write-Host "BATCH_EXISTING_SCAN_ROOT=$ExistingScanRoot"
Write-Host "BATCH_JOB_LOG_DIR=$BatchLogDir"
Write-Host "BATCH_JOB_COUNT=$($Jobs.Count)"
Write-Host "BATCH_PREFLIGHT_SKIPPED_COUNT=$($PreflightSkipped.Count)"

foreach ($Skipped in $PreflightSkipped) {
    $Skipped.Log = New-BatchJobLogPath -Row $Skipped.Row -Folder "preflight"
    Write-BatchJobLog -Path $Skipped.Log -Lines @(
        "STATUS=Skipped",
        "ROW=$($Skipped.Row)",
        "QUERY=$($Skipped.Query)",
        "DETAILS=$($Skipped.Details)"
    )
    $Results.Add($Skipped) | Out-Null
    Write-Host "BATCH_JOB_SKIPPED row=$($Skipped.Row) reason=$($Skipped.Details)"
}

foreach ($Job in $Jobs) {
    Write-Host "BATCH_JOB_START row=$($Job.Row) mesh=$($Job.Mesh)"
    $JobLogPath = New-BatchJobLogPath -Row $Job.Row -Folder $Job.Folder
    $Existing = Find-ExistingSuccessfulBatchExport -Job $Job
    if ($Existing) {
        Write-BatchJobLog -Path $JobLogPath -Lines @(
            "STATUS=Skipped existing success",
            "ROW=$($Job.Row)",
            "QUERY=$($Job.Query)",
            "MESH=$($Job.Mesh)",
            "EXISTING_BATCH_ROOT=$($Existing.BatchRoot)",
            "EXISTING_OUTPUT=$($Existing.Output)",
            "EXISTING_SUCCESS_LOG=$($Existing.SuccessLog)",
            "EXISTING_FBX=$($Existing.Fbx)"
        )
        $Results.Add([pscustomobject]@{
            Row = $Job.Row
            Query = $Job.Query
            Mesh = $Job.Mesh
            Status = "Skipped existing success"
            Output = $Existing.Output
            Details = "Existing successful export found in $($Existing.BatchRoot)"
            Log = $JobLogPath
        }) | Out-Null
        Write-Host "BATCH_JOB_SKIPPED_EXISTING row=$($Job.Row) output=$($Existing.Output)"
        Write-Host "BATCH_JOB_DONE row=$($Job.Row) status=Skipped existing success"
        continue
    }

    $TempScript = Join-Path $env:TEMP ("ree_wizard_batch_{0}_row{1}.ps1" -f $RunStamp, $Job.Row)
    $TextOutput = @()
    $ExitCode = 1
    try {
        $ScriptText = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Job.ScriptBase64))
        $ScriptText | Set-Content -LiteralPath $TempScript -Encoding UTF8

        $Arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $TempScript)
        if ($KeepSourceFbx) { $Arguments += "-KeepSourceFbx" }
        $JobOutput = @(powershell @Arguments 2>&1)
        $ExitCode = $LASTEXITCODE
        $TextOutput = @($JobOutput | ForEach-Object { $_.ToString() })
    } catch {
        $TextOutput = @("BATCH_JOB_WRAPPER_FAILED=$($_.Exception.Message)")
        $ExitCode = 1
    }
    foreach ($Line in $TextOutput) { Write-Host $Line }

    $ExportDir = Get-PrefixedValue -Lines $TextOutput -Prefix "EXPORT_DIR="
    $ExportLog = Get-PrefixedValue -Lines $TextOutput -Prefix "EXPORT_LOG="
    if ([string]::IsNullOrWhiteSpace($ExportLog)) {
        $ExportLog = Get-PrefixedValue -Lines $TextOutput -Prefix "EXPORT_LOG_TEMP="
    }
    $Failure = Get-PrefixedValue -Lines $TextOutput -Prefix "EXPORT_FAILED="
    $SkippedCount = @($TextOutput | Where-Object { $_.StartsWith("BLENDER_SKIPPED_SOURCE=", [System.StringComparison]::OrdinalIgnoreCase) }).Count
    $Status = if ($ExitCode -eq 0) {
        if ($SkippedCount -gt 0) { "Exported with skips" } else { "Exported" }
    } else {
        "Failed"
    }
    $Details = if ($ExitCode -ne 0) {
        if ([string]::IsNullOrWhiteSpace($Failure)) { "Child export failed with exit code $ExitCode" } else { $Failure }
    } elseif ($SkippedCount -gt 0) {
        "$SkippedCount Blender MOTLIST source(s) skipped"
    } else {
        "Resolved and exported"
    }
    $LogLines = New-Object System.Collections.Generic.List[string]
    $LogLines.Add("STATUS=$Status")
    $LogLines.Add("ROW=$($Job.Row)")
    $LogLines.Add("QUERY=$($Job.Query)")
    $LogLines.Add("MESH=$($Job.Mesh)")
    $LogLines.Add("EXIT_CODE=$ExitCode")
    $LogLines.Add("OUTPUT=$ExportDir")
    $LogLines.Add("EXPORT_LOG=$ExportLog")
    $LogLines.Add("DETAILS=$Details")
    $LogLines.Add("")
    $LogLines.Add("---- child output ----")
    foreach ($Line in $TextOutput) { $LogLines.Add($Line) }
    Write-BatchJobLog -Path $JobLogPath -Lines $LogLines

    $Results.Add([pscustomobject]@{
        Row = $Job.Row
        Query = $Job.Query
        Mesh = $Job.Mesh
        Status = $Status
        Output = $ExportDir
        Details = $Details
        Log = $JobLogPath
    }) | Out-Null

    Remove-Item -LiteralPath $TempScript -Force -ErrorAction SilentlyContinue
    Write-Host "BATCH_JOB_DONE row=$($Job.Row) status=$Status"
}

$SummaryPath = Join-Path $BatchRoot "batch-summary.md"
$Lines = New-Object System.Collections.Generic.List[string]
$Lines.Add("# Wizard Batch Export Summary")
$Lines.Add("")
$Lines.Add("Batch root: ``$BatchRoot``")
$Lines.Add("")
$Lines.Add("| Row | Status | Mesh | Output | Log | Details |")
$Lines.Add("| --- | --- | --- | --- | --- | --- |")
foreach ($Result in $Results) {
    $Lines.Add("| $($Result.Row) | $(Format-MarkdownCell $Result.Status) | $(Format-MarkdownCell $Result.Mesh) | $(Format-MarkdownCell $Result.Output) | $(Format-MarkdownCell $Result.Log) | $(Format-MarkdownCell $Result.Details) |")
}
$Lines.Add("")
$Lines.Add("Resolved rows: $($Jobs.Count)")
$Lines.Add("Exported rows: $(@($Results | Where-Object { $_.Status -like 'Exported*' }).Count)")
$Lines.Add("Skipped rows: $(@($Results | Where-Object { $_.Status -like 'Skipped*' }).Count)")
$Lines.Add("Failed rows: $(@($Results | Where-Object { $_.Status -eq 'Failed' }).Count)")
$Lines | Set-Content -LiteralPath $SummaryPath -Encoding UTF8
Write-Host "BATCH_SUMMARY=$SummaryPath"

$FailedCount = @($Results | Where-Object { $_.Status -eq "Failed" }).Count
if ($FailedCount -gt 0) {
    Write-Host "BATCH_COMPLETED_WITH_FAILURES=$FailedCount"
    Write-Host "BATCH_FAILURE_DETAILS_BEGIN"
    foreach ($Failure in @($Results | Where-Object { $_.Status -eq "Failed" })) {
        Write-Host ("FAILED_ROW={0} MESH={1}" -f $Failure.Row, $Failure.Mesh)
        Write-Host ("FAILED_DETAILS={0}" -f $Failure.Details)
        Write-Host ("FAILED_LOG={0}" -f $Failure.Log)
        if (![string]::IsNullOrWhiteSpace($Failure.Output)) { Write-Host ("FAILED_OUTPUT={0}" -f $Failure.Output) }
    }
    Write-Host "BATCH_FAILURE_DETAILS_END"
    try {
        Read-Host "Batch export completed with failures. Review the summary/log paths above, then press Enter to close"
    } catch {
        Write-Host "BATCH_FAILURE_PAUSE_SKIPPED=$($_.Exception.Message)"
    }
    exit 1
}

Write-Host "BATCH_COMPLETED_SUCCESSFULLY"
""";
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
        "--game", config.Game,
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
        else if (animation.Mode == WizardAnimationMode.Motlists)
        {
            foreach (var motlist in animation.Motlists)
            {
                args.Add("--motlist");
                args.Add(motlist);
            }
            args.Add("--split-motlists");
        }
        else if (animation.Mode == WizardAnimationMode.MotFiles)
        {
            foreach (var mot in animation.MotFiles)
            {
                args.Add("--mot");
                args.Add(mot);
            }
        }
        else
        {
            args.Add("--motlist-dir");
            args.Add(animation.MotlistDirectory!);
            foreach (var mot in animation.MotFiles)
            {
                args.Add("--mot");
                args.Add(mot);
            }
        }
    }

    var argLines = args.Select(arg => arg == "$OutputRequest" ? "    $OutputRequest" : "    " + PsQuote(arg)).ToList();
    var isSplitMotlistExport = animation.Mode is WizardAnimationMode.MotlistDirectory or WizardAnimationMode.Motlists;
    var outputRequestLine = animation.Mode == WizardAnimationMode.None
        ? $"$OutputRequest = Join-Path $ExportRoot {PsQuote(sourceName)}"
        : $"$OutputRequest = Join-Path $ExportRoot {PsQuote(sourceName)}";
    var sourceDiscovery = !isSplitMotlistExport
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
    $BlenderLog = [System.IO.Path]::ChangeExtension($StatusPath, ".blender.log")
    $PythonExpectAnimations = if ($ExpectAnimations) { 'True' } else { 'False' }
@"
import bpy
import builtins
from pathlib import Path
src = Path(r'$($Source.FullName)')
out = Path(r'$Target')
status_path = Path(r'$StatusPath')
index = $Index
total = $Total
expect_animations = $PythonExpectAnimations

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
    Remove-Item -LiteralPath $BlenderLog -Force -ErrorAction SilentlyContinue
    & $Blender --background --factory-startup --python $Py 2>&1 | Tee-Object -FilePath $BlenderLog
    if ($LASTEXITCODE -ne 0) { throw "Blender re-export failed with exit code $LASTEXITCODE for $($Source.FullName). Log: $BlenderLog" }
    if (!(Test-Path $StatusPath)) {
        if ((Test-Path $Target) -and ((Get-Item -LiteralPath $Target).Length -gt 0)) {
            "STATUS=EXPORTED`nREASON=Recovered from missing Blender status file; target FBX exists.`nACTION_COUNT=0`n" | Set-Content -Encoding UTF8 $StatusPath
        } else {
            throw "Missing Blender status file for $($Source.FullName): $StatusPath. Log: $BlenderLog"
        }
    }
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
    $TextureManifest = Join-Path $TextureDir "materials.textures.json"
    if (Test-Path $TextureManifest) {
        $TextureCount = (Get-ChildItem $TextureDir -File -ErrorAction Stop | Where-Object { $_.Name -ne "materials.textures.json" } | Measure-Object).Count
        if ($TextureCount -le 0) { throw "Texture manifest exists but no texture files were exported: $TextureDir" }
    } elseif (Test-Path $TextureDir) {
        $TextureCount = (Get-ChildItem $TextureDir -File -ErrorAction Stop | Measure-Object).Count
        if ($TextureCount -le 0) { throw "Texture folder exists but is empty: $TextureDir" }
    } else {
        Write-Host "TEXTURE_EXPORT_SKIPPED=No texture folder was produced for this mesh."
    }

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
static string PsBool(bool value) => value ? "$true" : "$false";

static void RunGeneratedScript(string scriptPath, WizardLanguage language = WizardLanguage.English)
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
        Console.WriteLine(language == WizardLanguage.Korean ? "스크립트가 성공적으로 완료되었습니다." : "Script completed successfully.");
    }
    else
    {
        Console.WriteLine(language == WizardLanguage.Korean ? $"스크립트가 종료 코드 {proc.ExitCode}(으)로 실패했습니다." : $"Script failed with exit code {proc.ExitCode}.");
        Console.WriteLine(language == WizardLanguage.Korean ? "위의 요약과 로그 경로를 확인한 뒤 Enter 키를 눌러 닫으세요." : "Review the summary and log paths above, then press Enter to close.");
        try { Console.ReadLine(); } catch { }
    }
}

try
{
var wizardConfigPath = GetArg(args, "--config");
var executableName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? Environment.GetCommandLineArgs().FirstOrDefault() ?? "");
var isCliExecutable = executableName.Contains("CLI", StringComparison.OrdinalIgnoreCase);
var isGuiExecutable = executableName.Contains("GUI", StringComparison.OrdinalIgnoreCase);
if (HasFlag(args, "--reset-config"))
{
    var path = ResolveWizardConfigPath(wizardConfigPath);
    if (File.Exists(path))
    {
        File.Delete(path);
        Console.WriteLine($"Deleted wizard config: {path}");
    }
}
if (HasFlag(args, "--help"))
{
    PrintUsage();
    return;
}
if (HasFlag(args, "--dependency-versions"))
{
    DependencyVersions.Print(Console.Out);
    return;
}
if (args.Length == 0 && isCliExecutable)
{
    PrintUsage();
    PauseIfStandaloneConsole();
    return;
}

if (args.Length == 0 || HasFlag(args, "--gui") || (isGuiExecutable && IsConfigOnlyInvocation(args)))
{
    GuiWizardApplication.Run(wizardConfigPath);
    return;
}
if (HasFlag(args, "--wizard") || HasFlag(args, "--reset-config"))
{
    RunWizard(wizardConfigPath);
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
    RequireExistingDirectory(motlistDir, "--motlist-dir");
    motlistPaths.AddRange(Directory.GetFiles(motlistDir, "*.motlist*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
}
motlistPaths = motlistPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
var motPaths = GetArgs(args, "--mot").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
var outputPath = GetArg(args, "--output") ?? throw new ArgumentException("Missing --output");
var animationFilter = GetArg(args, "--animation-name");
var explicitSceneActor = NormalizeSceneActor(GetArg(args, "--scene-actor"), "--scene-actor");
var inferredSceneActor = explicitSceneActor ?? InferSceneActorFromMeshPath(meshPath);
var includeAnimations = !HasFlag(args, "--no-animations");
var includeTextures = !HasFlag(args, "--no-textures");
var textureFormat = (GetArg(args, "--texture-format") ?? "png").ToLowerInvariant();
if (textureFormat is not ("png" or "dds")) throw new ArgumentException("--texture-format must be png or dds");
var exportGame = ResolveConfiguredGameName(LoadWizardConfig(ResolveWizardConfigPath(wizardConfigPath)), GetArg(args, "--game"));
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
var unrealReadyFbx = HasFlag(args, "--unreal-ready-fbx");
var keepSourceFbx = HasFlag(args, "--keep-source-fbx");
var allowMixedSceneAnimations = HasFlag(args, "--allow-mixed-scene-animations");
var fixCh6500ArmBladeTranslation = HasFlag(args, "--fix-ch6500-armblade-translation");
var blenderPath = GetArg(args, "--blender") ?? LoadWizardConfig(ResolveWizardConfigPath(wizardConfigPath))?.BlenderPath;
var boneSpacingReferenceFbx = GetArg(args, "--bone-spacing-reference-fbx");
var boneSpacingReferenceAction = GetArg(args, "--bone-spacing-reference-action") ?? "ch0100_General_0100_Stan_Loop";
var boneSpacingAllowTranslation = ParseBoneSpacingAllowTranslation(GetArg(args, "--bone-spacing-allow-translation"));

Console.WriteLine("REE Content Editor native export path");
Console.WriteLine($"Mesh: {meshPath}");
Console.WriteLine($"Additional meshes: {(additionalMeshPaths.Count == 0 ? "-" : string.Join("; ", additionalMeshPaths))}");
Console.WriteLine($"Streaming: {streamingPath ?? "-"}");
Console.WriteLine($"Additional streaming: {(additionalStreamingByMesh.Count == 0 ? "-" : string.Join("; ", additionalStreamingByMesh.Select(kvp => kvp.Key + " => " + kvp.Value)))}");
Console.WriteLine($"MDF: {mdfPath ?? "auto"}");
Console.WriteLine($"Motlists: {(motlistPaths.Count == 0 ? "-" : string.Join("; ", motlistPaths))}");
Console.WriteLine($"Mots: {(motPaths.Count == 0 ? "-" : string.Join("; ", motPaths))}");
Console.WriteLine($"Output: {outputPath}");
Console.WriteLine($"Game: {exportGame}");
Console.WriteLine($"Scene actor: {explicitSceneActor ?? inferredSceneActor ?? "-"}{(explicitSceneActor != null ? " (explicit)" : inferredSceneActor != null ? " (inferred)" : "")}");
Console.WriteLine($"Unreal-ready FBX: {(unrealReadyFbx ? "yes" : "no")}");
Console.WriteLine($"ch6500 ArmBlade translation repair: {(fixCh6500ArmBladeTranslation ? "yes" : "no")}");
if (unrealReadyFbx) Console.WriteLine($"Blender: {blenderPath}");
if (!string.IsNullOrWhiteSpace(boneSpacingReferenceFbx))
{
    Console.WriteLine($"Bone spacing reference FBX: {boneSpacingReferenceFbx}");
    Console.WriteLine($"Bone spacing reference action: {boneSpacingReferenceAction}");
    Console.WriteLine($"Bone spacing translation allowlist: {string.Join(", ", boneSpacingAllowTranslation)}");
}

var unknownAdditionalStreamingKeys = additionalStreamingByMesh.Keys
    .Where(key => !additionalMeshPaths.Contains(key, StringComparer.OrdinalIgnoreCase))
    .ToList();
if (unknownAdditionalStreamingKeys.Count != 0)
{
    throw new ArgumentException("--additional-streaming keys must match a supplied --additional-mesh path. Unknown key(s): " + string.Join("; ", unknownAdditionalStreamingKeys));
}
ValidateExportInputs(meshPath, additionalMeshPaths, streamingPath, additionalStreamingByMesh, mdfPath, motlistPaths, motPaths, outputPath);
if (unrealReadyFbx)
{
    if (!Path.GetExtension(outputPath).Equals(".fbx", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(Path.GetExtension(outputPath)))
        throw new ArgumentException("--unreal-ready-fbx requires an FBX output path or output folder.");
    ValidateBlenderForUnrealExport(blenderPath);
}
if (!string.IsNullOrWhiteSpace(boneSpacingReferenceFbx))
{
    if (!unrealReadyFbx)
        throw new ArgumentException("--bone-spacing-reference-fbx requires --unreal-ready-fbx because spacing repair runs in the Blender finalization stage.");
    RequireExistingFile(boneSpacingReferenceFbx, "--bone-spacing-reference-fbx");
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
        var sourceName = string.IsNullOrWhiteSpace(motlist.Name)
            ? PathUtils.GetFilenameWithoutExtensionOrVersion(motlistPath).ToString()
            : motlist.Name;
        IEnumerable<MotFileBase> files = motlist.MotFiles;
        if (!string.IsNullOrWhiteSpace(animationFilter))
            files = files.Where(m => m.Name.Contains(animationFilter, StringComparison.OrdinalIgnoreCase));
        var nameFiltered = files.ToList();
        var selected = ApplySceneActorFilter(sourceName, motlistPath, nameFiltered, inferredSceneActor, explicitSceneActor != null, allowMixedSceneAnimations);
        motlistGroups.Add((sourceName, motlistPath, selected));
        motions.AddRange(selected.Select(m => (sourceName, m)));
        Console.WriteLine($"Loaded motlist {motlist.Name}: total={motlist.MotFiles.Count} nameFiltered={nameFiltered.Count} selected={selected.Count}");
    }
    foreach (var motPath in motPaths)
    {
        using var motHandler = new FileHandler(motPath);
        var mot = new MotFile(motHandler);
        if (!mot.Read()) throw new Exception("REE-Lib failed to read mot");
        if (string.IsNullOrWhiteSpace(animationFilter) || mot.Name.Contains(animationFilter, StringComparison.OrdinalIgnoreCase))
            motions.Add((PathUtils.GetFilenameWithoutExtensionOrVersion(motPath).ToString(), mot));
        Console.WriteLine($"Loaded mot {mot.Name}");
    }
}

if (fixCh6500ArmBladeTranslation)
{
    var repair = ApplyCh6500ArmBladeTranslationRepair(motions.Select(m => m.Motion));
    Console.WriteLine($"CH6500_ARMBLADE_REPAIR translationClips={repair.TranslationClipCount} translationKeys={repair.TranslationKeyCount} rotationClips={repair.RotationClipCount} rotationKeys={repair.RotationKeyCount}");
    foreach (var line in repair.Lines)
        Console.WriteLine($"CH6500_ARMBLADE_REPAIR_DETAIL {line}");
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
    GameVersion = exportGame,
    ExportTextureFormat = textureFormat,
    ExportRootNodeName = "Armature",
    ExportStripMeshNamePrefix = true,
    ExportSkipMotionsWithMissingBones = skipMissingAnimationBones,
    ExportNoPlaceholderAnimationBones = noPlaceholderAnimationBones,
    ExportBakeFbxRotationTracks = !unrealReadyFbx,
};
var additionalResources = new List<CommonMeshResource>();
foreach (var additionalMeshPath in additionalMeshPaths)
{
    var additionalName = PathUtils.GetFilenameWithoutExtensionOrVersion(additionalMeshPath).ToString();
    additionalResources.Add(new CommonMeshResource(additionalName, null!)
    {
        NativeMesh = LoadMesh(additionalMeshPath, additionalStreamingByMesh.TryGetValue(additionalMeshPath, out var additionalStreamingPath) ? additionalStreamingPath : null, allowMissingStreaming),
        GameVersion = exportGame,
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

var exportedSourceFbxFiles = new List<string>();

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
        exportedSourceFbxFiles.Add(target);
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
        exportedSourceFbxFiles.Add(target);
        progress.WriteLine($"[{index + 1}/{motions.Count}] {target}");
        index++;
    }
}
else
{
    var singleOutputPath = ResolveSingleOutputPath(outputPath, meshPath, name, BuildSourceFiles(meshPath, additionalMeshPaths, motlistPaths, motPaths), animationFilter);
    ExportOne(resource, singleOutputPath, includeLods, includeOcc, motions.Select(m => m.Motion), materialWrappers, includeTextures, additionalResources, progress);
    exportedSourceFbxFiles.Add(singleOutputPath);
}

if (unrealReadyFbx)
{
    var fbxSources = exportedSourceFbxFiles
        .Where(path => Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (fbxSources.Count == 0) throw new ArgumentException("--unreal-ready-fbx did not produce any source FBX files.");
    ReexportUnrealReadyFbxFiles(fbxSources, blenderPath!, keepSourceFbx, boneSpacingReferenceFbx, boneSpacingReferenceAction, boneSpacingAllowTranslation, fixCh6500ArmBladeTranslation, progress);
}

progress.WriteLine("DONE");
}
catch (ArgumentException ex)
{
    WriteCliError(ex);
    PrintUsage();
    Environment.ExitCode = 2;
    return;
}
catch (Exception ex)
{
    WriteCliError(ex);
    Environment.ExitCode = 1;
    return;
}

static bool IsConfigOnlyInvocation(string[] args)
{
    if (args.Length != 2) return false;
    return string.Equals(args[0], "--config", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(args[1]);
}

static void PauseIfStandaloneConsole()
{
    if (!OperatingSystem.IsWindows() || Console.IsInputRedirected) return;
    try
    {
        var processIds = new uint[4];
        if (GetConsoleProcessList(processIds, (uint)processIds.Length) <= 1)
        {
            Console.WriteLine();
            Console.Write("Press Enter to close...");
            Console.ReadLine();
        }
    }
    catch
    {
        // Best-effort Explorer double-click nicety; never block normal CLI use if detection fails.
    }
}

[DllImport("kernel32.dll", SetLastError = true)]
static extern uint GetConsoleProcessList([Out] uint[] processList, uint processCount);

static void WriteCliError(Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    if (IsDebugErrorEnabled())
    {
        Console.Error.WriteLine(ex);
    }
}

static bool IsDebugErrorEnabled()
{
    var value = Environment.GetEnvironmentVariable("REE_CONTENT_EXPORTER_DEBUG_ERRORS");
    return value is not null
        && !value.Equals("0", StringComparison.OrdinalIgnoreCase)
        && !value.Equals("false", StringComparison.OrdinalIgnoreCase)
        && !value.Equals("no", StringComparison.OrdinalIgnoreCase);
}

static void ValidateExportInputs(
    string meshPath,
    IReadOnlyList<string> additionalMeshPaths,
    string? streamingPath,
    IReadOnlyDictionary<string, string> additionalStreamingByMesh,
    string? mdfPath,
    IReadOnlyList<string> motlistPaths,
    IReadOnlyList<string> motPaths,
    string outputPath)
{
    RequireExistingFile(meshPath, "--mesh");
    foreach (var additionalMeshPath in additionalMeshPaths)
        RequireExistingFile(additionalMeshPath, "--additional-mesh");
    if (!string.IsNullOrWhiteSpace(streamingPath))
        RequireExistingFile(streamingPath, "--streaming");
    foreach (var additionalStreamingPath in additionalStreamingByMesh.Values)
        RequireExistingFile(additionalStreamingPath, "--additional-streaming");
    if (!string.IsNullOrWhiteSpace(mdfPath))
        RequireExistingFile(mdfPath, "--mdf");
    foreach (var motlistPath in motlistPaths)
        RequireExistingFile(motlistPath, "--motlist");
    foreach (var motPath in motPaths)
        RequireExistingFile(motPath, "--mot");
    EnsureOutputParentCanBeCreated(outputPath);
}

static string? NormalizeSceneActor(string? actor, string optionName)
{
    if (string.IsNullOrWhiteSpace(actor)) return null;
    var normalized = actor.Trim().ToLowerInvariant();
    if (!IsSceneActorToken(normalized))
        throw new ArgumentException($"{optionName} must look like an RE actor id, for example ch0100, ch0000, or wp0900.");
    return normalized;
}

static string? InferSceneActorFromMeshPath(string meshPath)
{
    var filename = Path.GetFileName(meshPath).ToLowerInvariant();
    var match = Regex.Match(filename, @"(?<![a-z0-9])([a-z]{2}\d{4})(?!\d)", RegexOptions.IgnoreCase);
    return match.Success && IsSceneActorToken(match.Groups[1].Value)
        ? match.Groups[1].Value.ToLowerInvariant()
        : null;
}

static List<MotFileBase> ApplySceneActorFilter(
    string sourceName,
    string motlistPath,
    IReadOnlyList<MotFileBase> selectedMotions,
    string? sceneActor,
    bool explicitSceneActor,
    bool allowMixedSceneAnimations)
{
    if (selectedMotions.Count == 0) return [];

    var actorCounts = selectedMotions
        .Select(motion => TryGetSceneMotionActor(sourceName, motion.Name))
        .Where(actor => actor != null)
        .GroupBy(actor => actor!, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    if (actorCounts.Count == 0) return selectedMotions.ToList();
    if (allowMixedSceneAnimations && !explicitSceneActor) return selectedMotions.ToList();

    var shouldFilter = explicitSceneActor || actorCounts.Count > 1;
    if (!shouldFilter) return selectedMotions.ToList();

    if (string.IsNullOrWhiteSpace(sceneActor))
    {
        var actors = string.Join(", ", actorCounts.Keys.OrderBy(actor => actor, StringComparer.OrdinalIgnoreCase));
        throw new ArgumentException($"Scene MOTLIST contains multiple actor prefixes but no actor could be inferred from --mesh. Pass --scene-actor <actor-id> or --allow-mixed-scene-animations. MOTLIST: {motlistPath}; actors: {actors}");
    }

    var filtered = selectedMotions
        .Where(motion => string.Equals(TryGetSceneMotionActor(sourceName, motion.Name), sceneActor, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (actorCounts.Count > 1 || explicitSceneActor)
    {
        var actors = string.Join(", ", actorCounts.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase).Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        Console.WriteLine($"Scene MOTLIST actor filter: source={sourceName} actor={sceneActor} selected={filtered.Count}/{selectedMotions.Count} actors={actors}");
    }

    return filtered;
}

static string? TryGetSceneMotionActor(string sourceName, string motionName)
{
    if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(motionName)) return null;
    var prefix = sourceName + "_";
    if (!motionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
    var remainder = motionName[prefix.Length..];
    var separatorIndex = remainder.IndexOf('_');
    if (separatorIndex <= 0) return null;
    var actor = remainder[..separatorIndex].ToLowerInvariant();
    return IsSceneActorToken(actor) ? actor : null;
}

static bool IsSceneActorToken(string value)
    => value.Length == 6
    && char.IsAsciiLetterLower(value[0])
    && char.IsAsciiLetterLower(value[1])
    && char.IsAsciiDigit(value[2])
    && char.IsAsciiDigit(value[3])
    && char.IsAsciiDigit(value[4])
    && char.IsAsciiDigit(value[5]);

static IReadOnlyList<string> ParseBoneSpacingAllowTranslation(string? raw)
{
    var values = string.IsNullOrWhiteSpace(raw)
        ? new[] { "root", "Hip", "Null_Offset" }
        : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var result = new List<string>();
    foreach (var value in values)
    {
        if (string.IsNullOrWhiteSpace(value)) continue;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            throw new ArgumentException("--bone-spacing-allow-translation contains an invalid bone name.");
        if (!result.Contains(value, StringComparer.OrdinalIgnoreCase))
            result.Add(value);
    }
    return result;
}

static (int TranslationClipCount, int TranslationKeyCount, int RotationClipCount, int RotationKeyCount, List<string> Lines) ApplyCh6500ArmBladeTranslationRepair(IEnumerable<MotFileBase> motionFiles)
{
    const string leftBlade = "L_ArmBlade_00";
    const string rightBlade = "R_ArmBlade_00";
    string[] rotationSpikeBones = ["R_ArmBlade_Gimic_01", "R_ArmBlade_Gimic_03"];
    var translationClipCount = 0;
    var translationKeyCount = 0;
    var rotationClipCount = 0;
    var rotationKeyCount = 0;
    var lines = new List<string>();

    foreach (var mot in motionFiles.OfType<MotFile>().Distinct())
    {
        var clipsByBone = mot.BoneClips
            .Select(clip => (Clip: clip, BoneName: GetMotionClipBoneName(mot, clip)))
            .Where(item => item.BoneName != null)
            .GroupBy(item => item.BoneName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Clip, StringComparer.OrdinalIgnoreCase);

        if (!clipsByBone.TryGetValue(leftBlade, out var leftBladeClip)
            || !clipsByBone.TryGetValue(rightBlade, out var rightBladeClip)
            || !leftBladeClip.HasTranslation
            || !rightBladeClip.HasTranslation
            || leftBladeClip.Translation?.translations == null
            || rightBladeClip.Translation?.translations == null)
        {
            continue;
        }

        var originalTranslations = clipsByBone
            .Where(kvp => kvp.Value.HasTranslation && kvp.Value.Translation?.translations is { Length: > 0 })
            .ToDictionary(
                kvp => kvp.Key,
                kvp => (Translations: kvp.Value.Translation!.translations!.ToArray(), Frames: kvp.Value.Translation.frameIndexes?.ToArray()),
                StringComparer.OrdinalIgnoreCase);

        var leftBladeOriginal = originalTranslations[leftBlade];
        var rightBladeOriginal = originalTranslations[rightBlade];
        var leftIdleY = SelectCh6500ArmBladeRestY(leftBladeOriginal.Translations);
        var leftExtendedY = SelectCh6500ArmBladeHighY(leftBladeOriginal.Translations);
        var rightIdleY = SelectCh6500ArmBladeHighY(rightBladeOriginal.Translations);
        var rightExtendedY = SelectCh6500ArmBladeRestY(rightBladeOriginal.Translations);

        foreach (var item in clipsByBone.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            var boneName = item.Key;
            var clip = item.Value;
            if (!clip.HasTranslation || clip.Translation?.translations == null || clip.Translation.translations.Length == 0)
                continue;

            if (!boneName.StartsWith("L_ArmBlade_", StringComparison.OrdinalIgnoreCase)
                && !boneName.StartsWith("R_ArmBlade_", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryGetCh6500ArmBladeCounterpart(boneName, out var counterpartBone)
                || !originalTranslations.TryGetValue(counterpartBone, out var counterpartOriginal)
                || !originalTranslations.TryGetValue(boneName, out var original))
            {
                continue;
            }

            var isLeft = boneName.StartsWith("L_", StringComparison.OrdinalIgnoreCase);
            var idleReferenceBone = isLeft ? boneName : counterpartBone;
            if (!originalTranslations.TryGetValue(idleReferenceBone, out var idleReferenceOriginal))
                continue;

            var translations = clip.Translation.translations;
            var changed = 0;
            for (var i = 0; i < translations.Length; i++)
            {
                var frame = GetTrackFrame(clip.Translation, i);
                var current = translations[i];
                var idleReference = SampleCh6500VectorTrack(idleReferenceOriginal.Translations, idleReferenceOriginal.Frames, 0);
                Vector3 desired;
                if (isLeft)
                {
                    var leftSignal = SampleCh6500VectorTrack(leftBladeOriginal.Translations, leftBladeOriginal.Frames, frame).Y;
                    var alpha = NormalizeCh6500BladeAlpha(leftSignal, leftIdleY, leftExtendedY);
                    var rightExtended = SampleCh6500VectorTrack(counterpartOriginal.Translations, counterpartOriginal.Frames, frame);
                    desired = Vector3.Lerp(idleReference, MirrorCh6500RightArmBladeTranslationToLeft(rightExtended), alpha);
                }
                else
                {
                    var rightSignal = SampleCh6500VectorTrack(rightBladeOriginal.Translations, rightBladeOriginal.Frames, frame).Y;
                    var alpha = NormalizeCh6500BladeAlpha(rightSignal, rightIdleY, rightExtendedY);
                    var rightExtended = SampleCh6500VectorTrack(original.Translations, original.Frames, frame);
                    desired = Vector3.Lerp(idleReference, rightExtended, alpha);
                }

                if (Vector3.DistanceSquared(current, desired) <= 0.000001f)
                    continue;

                translations[i] = desired;
                changed++;
            }

            if (changed == 0)
                continue;

            translationClipCount++;
            translationKeyCount += changed;
            lines.Add($"motion={mot.Name} bone={boneName} changedTranslationKeys={changed}/{translations.Length}");
        }

        foreach (var boneName in rotationSpikeBones)
        {
            if (!clipsByBone.TryGetValue(boneName, out var clip)
                || !clip.HasRotation
                || clip.Rotation?.rotations == null
                || clip.Rotation.rotations.Length < 3)
            {
                continue;
            }

            var changed = RepairCh6500OneFrameRotationOutliers(clip.Rotation);
            if (changed == 0) continue;

            rotationClipCount++;
            rotationKeyCount += changed;
            lines.Add($"motion={mot.Name} bone={boneName} changedRotationKeys={changed}/{clip.Rotation.rotations.Length}");
        }
    }

    return (translationClipCount, translationKeyCount, rotationClipCount, rotationKeyCount, lines);
}

static string? GetMotionClipBoneName(MotFile mot, BoneMotionClip clip)
    => clip.ClipHeader.boneName
        ?? clip.ClipHeader.OriginalName
        ?? mot.GetBoneByHash(clip.ClipHeader.boneHash)?.boneName;

static float SelectCh6500ArmBladeRestY(IReadOnlyList<Vector3> translations)
{
    var maxAbsY = translations.Select(v => MathF.Abs(v.Y)).DefaultIfEmpty(0).Max();
    if (maxAbsY <= 0.000001f) return 0;

    var lowThreshold = maxAbsY * 0.25f;
    var noiseThreshold = maxAbsY * 0.001f;
    var lowCluster = translations
        .Select(v => v.Y)
        .Where(y => MathF.Abs(y) <= lowThreshold && MathF.Abs(y) > noiseThreshold)
        .OrderBy(y => y)
        .ToArray();

    if (lowCluster.Length > 0)
        return lowCluster[lowCluster.Length / 2];

    return translations
        .Select(v => v.Y)
        .OrderBy(y => MathF.Abs(y))
        .First();
}

static float SelectCh6500ArmBladeHighY(IReadOnlyList<Vector3> translations)
    => translations
        .Select(v => v.Y)
        .OrderByDescending(y => MathF.Abs(y))
        .FirstOrDefault();

static bool TryGetCh6500ArmBladeCounterpart(string boneName, out string counterpart)
{
    if (boneName.StartsWith("L_ArmBlade_", StringComparison.OrdinalIgnoreCase))
    {
        counterpart = "R_" + boneName[2..];
        return true;
    }
    if (boneName.StartsWith("R_ArmBlade_", StringComparison.OrdinalIgnoreCase))
    {
        counterpart = "L_" + boneName[2..];
        return true;
    }

    counterpart = string.Empty;
    return false;
}

static float GetTrackFrame(Track track, int index)
{
    if (track.frameIndexes != null && index >= 0 && index < track.frameIndexes.Length)
        return track.frameIndexes[index];
    return 0;
}

static Vector3 SampleCh6500VectorTrack(IReadOnlyList<Vector3> values, IReadOnlyList<int>? frames, float frame)
{
    if (values.Count == 0) return Vector3.Zero;
    if (frames == null || frames.Count == 0) return values[0];

    var lastValueIndex = values.Count - 1;
    if (frame <= frames[0]) return values[0];
    for (var i = 1; i < frames.Count; i++)
    {
        if (frames[i] < frame) continue;
        var first = Math.Min(i - 1, lastValueIndex);
        var second = Math.Min(i, lastValueIndex);
        var frameSpan = frames[i] - frames[i - 1];
        var interpolation = frameSpan <= 0 ? 0 : (frame - frames[i - 1]) / frameSpan;
        return Vector3.Lerp(values[first], values[second], Math.Clamp(interpolation, 0, 1));
    }

    return values[lastValueIndex];
}

static float NormalizeCh6500BladeAlpha(float value, float idleValue, float extendedValue)
{
    var range = extendedValue - idleValue;
    if (MathF.Abs(range) <= 0.000001f) return 0;
    return Math.Clamp((value - idleValue) / range, 0, 1);
}

static Vector3 MirrorCh6500RightArmBladeTranslationToLeft(Vector3 right)
    => new(-right.X, right.Y, right.Z);

static int RepairCh6500OneFrameRotationOutliers(Track rotationTrack)
{
    var rotations = rotationTrack.rotations;
    if (rotations == null || rotations.Length < 3) return 0;

    var changed = 0;
    for (var i = 1; i < rotations.Length - 1; i++)
    {
        var previous = NormalizeCh6500Quaternion(rotations[i - 1]);
        var current = NormalizeCh6500Quaternion(rotations[i]);
        var next = NormalizeCh6500Quaternion(rotations[i + 1]);
        if (Quaternion.Dot(previous, next) < 0)
            next = NegateCh6500Quaternion(next);

        var angleToPrevious = Ch6500QuaternionAngleDegrees(previous, current);
        var angleToNext = Ch6500QuaternionAngleDegrees(current, next);
        var bridgeAngle = Ch6500QuaternionAngleDegrees(previous, next);
        if (angleToPrevious < 120 || angleToNext < 120 || bridgeAngle > 60)
            continue;

        var previousFrame = GetTrackFrame(rotationTrack, i - 1);
        var currentFrame = GetTrackFrame(rotationTrack, i);
        var nextFrame = GetTrackFrame(rotationTrack, i + 1);
        var frameSpan = nextFrame - previousFrame;
        var interpolation = frameSpan <= 0 ? 0.5f : Math.Clamp((currentFrame - previousFrame) / frameSpan, 0, 1);
        var repaired = Quaternion.Normalize(Quaternion.Slerp(previous, next, interpolation));
        if (repaired.W < 0)
            repaired = NegateCh6500Quaternion(repaired);

        rotations[i] = repaired;
        changed++;
    }

    return changed;
}

static Quaternion NormalizeCh6500Quaternion(Quaternion rotation)
{
    var lengthSquared = rotation.LengthSquared();
    if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.000001f) return Quaternion.Identity;
    return Quaternion.Normalize(rotation);
}

static Quaternion NegateCh6500Quaternion(Quaternion rotation)
    => new(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);

static float Ch6500QuaternionAngleDegrees(Quaternion a, Quaternion b)
{
    var dot = MathF.Abs(Quaternion.Dot(a, b));
    dot = Math.Clamp(dot, 0, 1);
    return 2 * MathF.Acos(dot) * 180 / MathF.PI;
}

static void RequireExistingFile(string path, string optionName)
{
    if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException($"{optionName} requires a non-empty path.");
    if (!File.Exists(path))
        throw new FileNotFoundException($"{optionName} path does not exist: {path}", path);
}

static void RequireExistingDirectory(string path, string optionName)
{
    if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException($"{optionName} requires a non-empty path.");
    if (!Directory.Exists(path))
        throw new DirectoryNotFoundException($"{optionName} directory does not exist: {path}");
}

static void EnsureOutputParentCanBeCreated(string outputPath)
{
    if (string.IsNullOrWhiteSpace(outputPath))
        throw new ArgumentException("--output requires a non-empty path.");
    var parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (string.IsNullOrWhiteSpace(parent)) return;
    if (File.Exists(parent))
        throw new IOException($"--output parent path is a file, not a directory: {parent}");
    Directory.CreateDirectory(parent);
}

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

static void ValidateBlenderForUnrealExport(string? blenderPath)
{
    if (string.IsNullOrWhiteSpace(blenderPath))
        throw new ArgumentException("--unreal-ready-fbx requires --blender <path> or a saved Blender path in config.json.");
    RequireExistingFile(blenderPath, "--blender");

    var psi = new ProcessStartInfo
    {
        FileName = blenderPath,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    psi.ArgumentList.Add("--version");
    using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start Blender: {blenderPath}");
    var firstLine = process.StandardOutput.ReadLine() ?? process.StandardError.ReadLine() ?? "";
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"Could not query Blender version from: {blenderPath}");
    if (!firstLine.Contains("Blender 4.5.9", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Expected Blender 4.5.9 LTS, but found: {firstLine}");
}

static void ReexportUnrealReadyFbxFiles(
    IReadOnlyList<string> sourceFbxFiles,
    string blenderPath,
    bool keepSourceFbx,
    string? boneSpacingReferenceFbx,
    string boneSpacingReferenceAction,
    IReadOnlyList<string> boneSpacingAllowTranslation,
    bool fixCh6500ArmBladeTranslation,
    ProgressStatus progress)
{
    var scriptPath = Path.Combine(Path.GetTempPath(), $"ree_unreal_fbx_reexport_{Guid.NewGuid():N}.py");
    File.WriteAllText(scriptPath, GetUnrealReadyBlenderScript(), Encoding.UTF8);
    var skipped = new List<(string Source, string Target, string Reason, int ActionCount)>();
    string? reportDir = null;
    try
    {
        for (var i = 0; i < sourceFbxFiles.Count; i++)
        {
            var source = sourceFbxFiles[i];
            var target = ResolveUnrealReadyFbxTarget(source);
            reportDir ??= Path.GetDirectoryName(source);
            var statusPath = Path.Combine(Path.GetTempPath(), $"ree_unreal_fbx_status_{Guid.NewGuid():N}.txt");
            progress.WriteLine($"SOURCE_FBX={source}");
            progress.WriteLine($"BLENDER_TARGET={target}");
            RunBlenderReexport(
                blenderPath,
                scriptPath,
                source,
                target,
                statusPath,
                i + 1,
                sourceFbxFiles.Count,
                boneSpacingReferenceFbx,
                boneSpacingReferenceAction,
                boneSpacingAllowTranslation,
                fixCh6500ArmBladeTranslation,
                progress);
            var status = ReadBlenderStatus(statusPath);
            File.Delete(statusPath);

            if (status.Status.Equals("SKIPPED", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add((source, target, status.Reason, status.ActionCount));
                progress.WriteLine($"BLENDER_SKIPPED_SOURCE={source}");
                if (!keepSourceFbx) RemoveIntermediateSource(source, progress);
                continue;
            }
            if (!status.Status.Equals("EXPORTED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(status.Reason) ? $"Unexpected Blender status: {status.Status}" : status.Reason);
            if (!File.Exists(target) || new FileInfo(target).Length == 0)
                throw new IOException($"Missing Blender output: {target}");

            MoveSkippedBoneReport(source, target, progress);
            if (!keepSourceFbx) RemoveIntermediateSource(source, progress);
            progress.WriteLine($"BLENDER_FBX={target}");
        }

        if (skipped.Count > 0 && reportDir != null)
            WriteSkippedBlenderMotlistsReport(reportDir, skipped, progress);
    }
    finally
    {
        try { File.Delete(scriptPath); } catch { }
    }
}

static void RunBlenderReexport(
    string blenderPath,
    string scriptPath,
    string source,
    string target,
    string statusPath,
    int index,
    int total,
    string? boneSpacingReferenceFbx,
    string boneSpacingReferenceAction,
    IReadOnlyList<string> boneSpacingAllowTranslation,
    bool fixCh6500ArmBladeTranslation,
    ProgressStatus progress)
{
    var psi = new ProcessStartInfo
    {
        FileName = blenderPath,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    psi.ArgumentList.Add("--background");
    psi.ArgumentList.Add("--factory-startup");
    psi.ArgumentList.Add("--python");
    psi.ArgumentList.Add(scriptPath);
    psi.ArgumentList.Add("--");
    psi.ArgumentList.Add(source);
    psi.ArgumentList.Add(target);
    psi.ArgumentList.Add(statusPath);
    psi.ArgumentList.Add(index.ToString(CultureInfo.InvariantCulture));
    psi.ArgumentList.Add(total.ToString(CultureInfo.InvariantCulture));
    psi.ArgumentList.Add(boneSpacingReferenceFbx ?? "");
    psi.ArgumentList.Add(boneSpacingReferenceAction);
    psi.ArgumentList.Add(string.Join(",", boneSpacingAllowTranslation));
    psi.ArgumentList.Add(fixCh6500ArmBladeTranslation ? "1" : "0");

    using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start Blender: {blenderPath}");
    process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress.WriteLine(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress.WriteLine(e.Data); };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"Blender re-export failed with exit code {process.ExitCode}: {source}");
    if (!File.Exists(statusPath))
        throw new IOException($"Missing Blender status file: {statusPath}");
}

static (string Status, string Reason, int ActionCount) ReadBlenderStatus(string statusPath)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in File.ReadAllLines(statusPath))
    {
        var parts = line.Split('=', 2);
        if (parts.Length == 2) values[parts[0]] = parts[1];
    }
    values.TryGetValue("STATUS", out var status);
    values.TryGetValue("REASON", out var reason);
    var actionCount = 0;
    if (values.TryGetValue("ACTION_COUNT", out var rawCount))
        int.TryParse(rawCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out actionCount);
    return (status ?? "", reason ?? "", actionCount);
}

static string ResolveUnrealReadyFbxTarget(string source)
{
    var directory = Path.GetDirectoryName(source) ?? ".";
    var name = Path.GetFileNameWithoutExtension(source);
    if (name.Length > 5
        && name[4] == '_'
        && name.Take(4).All(char.IsDigit))
    {
        name = name[5..];
    }
    if (name.EndsWith("_all_animations", StringComparison.OrdinalIgnoreCase))
        name = name[..^"_all_animations".Length];
    if (name.EndsWith("_source", StringComparison.OrdinalIgnoreCase))
        name = name[..^"_source".Length];
    return Path.Combine(directory, $"{name}_unreal.fbx");
}

static void MoveSkippedBoneReport(string source, string target, ProgressStatus progress)
{
    var sourceReport = Path.Combine(Path.GetDirectoryName(source) ?? ".", Path.GetFileNameWithoutExtension(source) + ".skipped-animation-bones.md");
    if (!File.Exists(sourceReport)) return;

    var targetReport = Path.Combine(Path.GetDirectoryName(target) ?? ".", Path.GetFileNameWithoutExtension(target) + ".skipped-animation-bones.md");
    File.Move(sourceReport, targetReport, overwrite: true);
    progress.WriteLine($"SKIPPED_BONE_REPORT={targetReport}");
}

static void RemoveIntermediateSource(string source, ProgressStatus progress)
{
    if (File.Exists(source))
    {
        File.Delete(source);
        progress.WriteLine($"SOURCE_FBX_REMOVED={source}");
    }

    var sourceReport = Path.Combine(Path.GetDirectoryName(source) ?? ".", Path.GetFileNameWithoutExtension(source) + ".skipped-animation-bones.md");
    if (File.Exists(sourceReport))
    {
        File.Delete(sourceReport);
        progress.WriteLine($"SOURCE_SKIPPED_BONE_REPORT_REMOVED={sourceReport}");
    }
}

static void WriteSkippedBlenderMotlistsReport(string directory, IReadOnlyList<(string Source, string Target, string Reason, int ActionCount)> skipped, ProgressStatus progress)
{
    var reportPath = Path.Combine(directory, "skipped-blender-motlists.md");
    using var writer = new StreamWriter(reportPath, append: false, Encoding.UTF8);
    writer.WriteLine("# Skipped Blender MOTLIST Re-exports");
    writer.WriteLine();
    writer.WriteLine("This report lists source FBX files that were created by REE-Content-Exporter but intentionally skipped during the Blender Unreal-ready re-export phase.");
    writer.WriteLine();
    writer.WriteLine("| Source FBX | Intended Unreal FBX | Reason | Imported Actions |");
    writer.WriteLine("| --- | --- | --- | --- |");
    foreach (var item in skipped)
    {
        writer.WriteLine($"| {Path.GetFileName(item.Source)} | {Path.GetFileName(item.Target)} | {item.Reason.Replace("|", "/")} | {item.ActionCount} |");
    }
    progress.WriteLine($"BLENDER_SKIPPED_MOTLIST_REPORT={reportPath}");
}

static string GetUnrealReadyBlenderScript() => """
import bpy
import builtins
import sys
from pathlib import Path
from mathutils import Quaternion

argv = sys.argv[sys.argv.index('--') + 1:]
src = Path(argv[0])
out = Path(argv[1])
status_path = Path(argv[2])
index = int(argv[3])
total = int(argv[4])
reference_fbx = Path(argv[5]) if len(argv) > 5 and argv[5] else None
reference_action_filter = argv[6] if len(argv) > 6 else ''
translation_allowlist = {part.strip().lower() for part in (argv[7] if len(argv) > 7 else '').split(',') if part.strip()}
fix_ch6500_armblade = len(argv) > 8 and argv[8] == '1'

def write_status(status, reason='', action_count=0):
    status_path.write_text(f'STATUS={status}\nREASON={reason}\nACTION_COUNT={action_count}\n', encoding='utf-8')

def log(message):
    print(f'BLENDER_PROGRESS {message}', flush=True)

def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()
    for datablocks in (bpy.data.actions, bpy.data.armatures, bpy.data.meshes):
        for datablock in list(datablocks):
            datablocks.remove(datablock, do_unlink=True)

def get_pose_bone_name(data_path):
    prefix = 'pose.bones["'
    if not data_path.startswith(prefix):
        return None
    tail = data_path[len(prefix):]
    end = tail.find('"]')
    if end < 0:
        return None
    return tail[:end]

def find_reference_action(actions, action_filter):
    if action_filter:
        for action in actions:
            if action_filter.lower() in action.name.lower():
                return action
    return actions[0] if actions else None

def read_reference_locations(path, action_filter):
    log(f'File {index}/{total} reading bone spacing reference: {path}')
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True, automatic_bone_orientation=False, ignore_leaf_bones=False, force_connect_children=False)
    ref_actions = list(bpy.data.actions)
    ref_action = find_reference_action(ref_actions, action_filter)
    if ref_action is None:
        raise RuntimeError(f'Bone spacing reference FBX has no actions: {path}')
    frame = int(ref_action.frame_range[0]) if ref_action.frame_range else 1
    grouped = {}
    for curve in ref_action.fcurves:
        if not curve.data_path.endswith('.location'):
            continue
        bone = get_pose_bone_name(curve.data_path)
        if not bone:
            continue
        grouped.setdefault(bone, {})[curve.array_index] = curve
    locations = {}
    for bone, curves in grouped.items():
        if all(axis in curves for axis in range(3)):
            locations[bone] = tuple(curves[axis].evaluate(frame) for axis in range(3))
    if not locations:
        raise RuntimeError(f'Bone spacing reference action has no pose-bone location curves: {ref_action.name}')
    print(f'BONE_SPACING_REFERENCE action={ref_action.name} frame={frame} bones={len(locations)} allow_translation={",".join(sorted(translation_allowlist))}', flush=True)
    return locations

def replace_curve_with_constant(action, bone, axis, value, start, end):
    data_path = f'pose.bones["{bone}"].location'
    curve = action.fcurves.find(data_path, index=axis)
    if curve is None:
        curve = action.fcurves.new(data_path=data_path, index=axis)
    while len(curve.keyframe_points) > 0:
        curve.keyframe_points.remove(curve.keyframe_points[-1], fast=True)
    curve.keyframe_points.insert(start, value, options={'FAST'})
    if end != start:
        curve.keyframe_points.insert(end, value, options={'FAST'})
    for key in curve.keyframe_points:
        key.interpolation = 'LINEAR'
    curve.update()

def apply_bone_spacing_reference(actions, reference_locations):
    if not reference_locations:
        return
    allow = translation_allowlist
    total_bones = 0
    total_curves = 0
    for action in actions:
        start = int(action.frame_range[0]) if action.frame_range else 1
        end = int(action.frame_range[1]) if action.frame_range else start
        action_bones = 0
        for bone, values in reference_locations.items():
            if bone.lower() in allow:
                continue
            action_bones += 1
            for axis, value in enumerate(values):
                replace_curve_with_constant(action, bone, axis, value, start, end)
                total_curves += 1
        total_bones += action_bones
        print(f'BONE_SPACING_REPAIR action={action.name} clamped_bones={action_bones} allow_translation={",".join(sorted(allow))}', flush=True)
    print(f'BONE_SPACING_REPAIR_TOTAL actions={len(actions)} clamped_bones={total_bones} clamped_curves={total_curves}', flush=True)

def find_location_curves(action, bone):
    curves = {}
    for curve in action.fcurves:
        if curve.array_index not in (0, 1, 2):
            continue
        if not curve.data_path.endswith('.location'):
            continue
        if get_pose_bone_name(curve.data_path) == bone:
            curves[curve.array_index] = curve
    return curves

def find_quaternion_rotation_groups(action):
    grouped = {}
    for curve in action.fcurves:
        if curve.array_index not in (0, 1, 2, 3):
            continue
        if not curve.data_path.endswith('.rotation_quaternion'):
            continue
        bone = get_pose_bone_name(curve.data_path)
        if not bone:
            continue
        grouped.setdefault(bone, {})[curve.array_index] = curve
    return {bone: curves for bone, curves in grouped.items() if all(axis in curves for axis in range(4))}

def normalize_quaternion_values(values, previous=None):
    length_squared = sum(value * value for value in values)
    if length_squared <= 0.000000001:
        return previous if previous is not None else (1.0, 0.0, 0.0, 0.0)
    quat = Quaternion((values[0], values[1], values[2], values[3])).normalized()
    current = (quat.w, quat.x, quat.y, quat.z)
    if previous is not None:
        dot = sum(previous[index] * current[index] for index in range(4))
        if dot < 0.0:
            current = tuple(-value for value in current)
    return current

def lerp_quaternion_values(first, second, alpha):
    if sum(first[index] * second[index] for index in range(4)) < 0.0:
        second = tuple(-value for value in second)
    values = tuple(first[index] + (second[index] - first[index]) * alpha for index in range(4))
    return normalize_quaternion_values(values, first)

def replace_quaternion_rotation_curves(action, bone, frame_values):
    data_path = f'pose.bones["{bone}"].rotation_quaternion'
    for axis in range(4):
        curve = action.fcurves.find(data_path, index=axis)
        if curve is None:
            curve = action.fcurves.new(data_path=data_path, index=axis)
        while len(curve.keyframe_points) > 0:
            curve.keyframe_points.remove(curve.keyframe_points[-1], fast=True)
        for frame, values in frame_values:
            curve.keyframe_points.insert(frame, values[axis], options={'FAST'})
        for key in curve.keyframe_points:
            key.interpolation = 'LINEAR'
        curve.update()

def resample_sparse_pose_quaternion_curves(actions):
    changed_actions = 0
    changed_groups = 0
    changed_curves = 0
    written_keys = 0
    for action in actions:
        start = int(action.frame_range[0]) if action.frame_range else 0
        end = int(action.frame_range[1]) if action.frame_range else start
        if end <= start:
            continue
        dense_frames = list(range(start, end + 1))
        action_groups = 0
        for bone, curves in find_quaternion_rotation_groups(action).items():
            source_frames = sorted({round(key.co.x, 6) for curve in curves.values() for key in curve.keyframe_points})
            if len(source_frames) <= 1 or len(source_frames) >= len(dense_frames):
                continue

            source_values = []
            previous = None
            for frame in source_frames:
                values = [curves[axis].evaluate(frame) for axis in range(4)]
                current = normalize_quaternion_values(values, previous)
                source_values.append((frame, current))
                previous = current

            if len(source_values) <= 1:
                continue

            frame_values = []
            source_index = 0
            for frame in dense_frames:
                while source_index + 1 < len(source_values) and source_values[source_index + 1][0] < frame:
                    source_index += 1
                if frame <= source_values[0][0]:
                    current = source_values[0][1]
                elif frame >= source_values[-1][0]:
                    current = source_values[-1][1]
                else:
                    while source_index + 1 < len(source_values) and source_values[source_index + 1][0] < frame:
                        source_index += 1
                    first_frame, first_quat = source_values[source_index]
                    second_frame, second_quat = source_values[source_index + 1]
                    span = max(0.000001, second_frame - first_frame)
                    alpha = max(0.0, min(1.0, (frame - first_frame) / span))
                    current = lerp_quaternion_values(first_quat, second_quat, alpha)
                frame_values.append((frame, current))

            replace_quaternion_rotation_curves(action, bone, frame_values)
            action_groups += 1
            changed_groups += 1
            changed_curves += 4
            written_keys += len(frame_values) * 4

        if action_groups:
            changed_actions += 1
            print(f'QUATERNION_REBAKE action={action.name} rotation_groups={action_groups} frames={len(dense_frames)}', flush=True)

    print(f'QUATERNION_REBAKE_TOTAL actions={changed_actions} rotation_groups={changed_groups} curves={changed_curves} keys={written_keys}', flush=True)

def make_axis_quaternion(axis, angle):
    import math
    values = [math.cos(angle * 0.5), 0.0, 0.0, 0.0]
    values[axis + 1] = math.sin(angle * 0.5)
    return tuple(values)

def unwrap_angle(angle, reference):
    import math
    while angle - reference > math.pi:
        angle -= math.tau
    while angle - reference < -math.pi:
        angle += math.tau
    return angle

def axis_angle_from_quaternion(values, axis, reference=None):
    import math
    angle = 2.0 * math.atan2(values[axis + 1], values[0])
    if reference is not None:
        angle = unwrap_angle(angle, reference)
    return angle

def stabilize_root_rotation_axis(actions):
    import math
    changed_actions = 0
    changed_runs = 0
    changed_keys = 0
    axis_names = ('X', 'Y', 'Z')
    off_axis_threshold = 0.02
    max_bad_fraction = 0.35

    for action in actions:
        curves = find_quaternion_rotation_groups(action).get('root')
        if curves is None:
            continue
        start = int(action.frame_range[0]) if action.frame_range else 0
        end = int(action.frame_range[1]) if action.frame_range else start
        if end <= start:
            continue

        frames = list(range(start, end + 1))
        values = []
        previous = None
        for frame in frames:
            current = normalize_quaternion_values([curves[axis].evaluate(frame) for axis in range(4)], previous)
            values.append(current)
            previous = current

        component_sums = [sum(abs(value[axis + 1]) for value in values) for axis in range(3)]
        primary_axis = max(range(3), key=lambda axis: component_sums[axis])
        off_axis = []
        for value in values:
            off_axis.append(math.sqrt(sum(value[axis + 1] * value[axis + 1] for axis in range(3) if axis != primary_axis)))
        bad = [amount > off_axis_threshold for amount in off_axis]
        bad_count = sum(1 for item in bad if item)
        if bad_count < 1 or bad_count > max(2, int(len(frames) * max_bad_fraction)):
            continue
        if max(off_axis) < 0.05:
            continue

        replacements = list(values)
        run_count = 0
        index = 0
        while index < len(frames):
            if not bad[index]:
                index += 1
                continue
            run_start = index
            while index < len(frames) and bad[index]:
                index += 1
            run_end = index - 1
            before = run_start - 1
            after = run_end + 1
            if before < 0 or after >= len(frames):
                continue

            before_angle = axis_angle_from_quaternion(replacements[before], primary_axis)
            after_angle = axis_angle_from_quaternion(values[after], primary_axis, before_angle)
            span = max(1, after - before)
            for value_index in range(run_start, run_end + 1):
                alpha = (value_index - before) / span
                angle = before_angle + (after_angle - before_angle) * alpha
                replacements[value_index] = make_axis_quaternion(primary_axis, angle)
                changed_keys += 4
            run_count += 1

        if run_count == 0:
            continue

        continuous = []
        previous = None
        for value in replacements:
            current = normalize_quaternion_values(value, previous)
            continuous.append(current)
            previous = current
        replace_quaternion_rotation_curves(action, 'root', list(zip(frames, continuous)))
        changed_actions += 1
        changed_runs += run_count
        print(
            f'ROOT_ROTATION_STABILIZE action={action.name} axis={axis_names[primary_axis]} '
            f'runs={run_count} bad_frames={bad_count} max_off_axis={max(off_axis):.6f}',
            flush=True
        )

    print(f'ROOT_ROTATION_STABILIZE_TOTAL actions={changed_actions} runs={changed_runs} keys={changed_keys}', flush=True)

def eval_location(curves, frame):
    return tuple(curves[axis].evaluate(frame) if axis in curves else 0.0 for axis in range(3))

def replace_location_curve(action, bone, axis, values_by_frame):
    data_path = f'pose.bones["{bone}"].location'
    curve = None
    for candidate in action.fcurves:
        if candidate.array_index == axis and candidate.data_path.endswith('.location') and get_pose_bone_name(candidate.data_path) == bone:
            curve = candidate
            break
    if curve is None:
        curve = action.fcurves.new(data_path=data_path, index=axis)
    while len(curve.keyframe_points) > 0:
        curve.keyframe_points.remove(curve.keyframe_points[-1], fast=True)
    for frame, value in values_by_frame:
        curve.keyframe_points.insert(frame, value, options={'FAST'})
    for key in curve.keyframe_points:
        key.interpolation = 'LINEAR'
    curve.update()

def mix_location(a, b, alpha):
    return tuple(a[axis] + (b[axis] - a[axis]) * alpha for axis in range(3))

def mirror_right_location_to_left(values):
    return (-values[0], values[1], values[2])

def mirror_left_location_to_right(values):
    # Closed/idling ArmBlade values are centered near zero/negative-rest and should
    # be copied, while extended positive-left values mirror across X.
    x = -values[0] if values[0] > 10.0 else values[0]
    return (x, values[1], values[2])

def action_has_token(action, token):
    return token.lower() in action.name.lower()

def get_action_by_token(actions, token):
    for action in actions:
        if action_has_token(action, token):
            return action
    return None

def get_reference_ch6500_extension(actions):
    ref_action = get_action_by_token(actions, '0575')
    if ref_action is None:
        ref_action = get_action_by_token(actions, '0570')
    if ref_action is None:
        return None

    curves = {
        'L_ArmBlade_00': find_location_curves(ref_action, 'L_ArmBlade_00'),
        'L_ArmBlade_Gimic_05': find_location_curves(ref_action, 'L_ArmBlade_Gimic_05'),
    }
    if not all(len(curves[bone]) == 3 for bone in curves):
        return None

    start = int(ref_action.frame_range[0]) if ref_action.frame_range else 0
    end = int(ref_action.frame_range[1]) if ref_action.frame_range else start
    frames = list(range(start, end + 1))
    blade_frame = max(frames, key=lambda frame: eval_location(curves['L_ArmBlade_00'], frame)[0])
    gimic_frame = max(frames, key=lambda frame: eval_location(curves['L_ArmBlade_Gimic_05'], frame)[0])
    left_blade = eval_location(curves['L_ArmBlade_00'], blade_frame)
    left_gimic = eval_location(curves['L_ArmBlade_Gimic_05'], gimic_frame)
    return {
        'L_ArmBlade_00': left_blade,
        'R_ArmBlade_00': mirror_left_location_to_right(left_blade),
        'L_ArmBlade_Gimic_05': left_gimic,
        'R_ArmBlade_Gimic_05': mirror_left_location_to_right(left_gimic),
    }

def rewrite_location_frames(action, bone, frame_values):
    for axis in range(3):
        replace_location_curve(action, bone, axis, [(frame, values[axis]) for frame, values in frame_values])

def force_bone_constant(action, bone, value, start, end):
    rewrite_location_frames(action, bone, [(frame, value) for frame in range(start, end + 1)])
    return True

def force_bone_x_constant(action, bone, value, start, end):
    replace_location_curve(action, bone, 0, [(frame, value) for frame in range(start, end + 1)])
    return True

def force_bone_x_transition(action, bone, extended_x, transition_start, transition_end, idle_x=0.0):
    start = int(action.frame_range[0]) if action.frame_range else 0
    end = int(action.frame_range[1]) if action.frame_range else start
    values = []
    span = max(1, transition_end - transition_start)
    for frame in range(start, end + 1):
        if frame <= transition_start:
            value = extended_x
        elif frame >= transition_end:
            value = idle_x
        else:
            alpha = (frame - transition_start) / span
            value = extended_x + (idle_x - extended_x) * alpha
        values.append((frame, value))
    replace_location_curve(action, bone, 0, values)
    return True

def force_bone_window(action, bone, value, window_start, window_end):
    curves = find_location_curves(action, bone)
    if len(curves) != 3:
        return False
    start = int(action.frame_range[0]) if action.frame_range else 0
    end = int(action.frame_range[1]) if action.frame_range else start
    frame_values = []
    for frame in range(start, end + 1):
        if window_start <= frame <= window_end:
            frame_values.append((frame, value))
        else:
            frame_values.append((frame, eval_location(curves, frame)))
    rewrite_location_frames(action, bone, frame_values)
    return True

def amplify_left_extension(action, bone, full_extension):
    curves = find_location_curves(action, bone)
    if len(curves) != 3:
        return False
    start = int(action.frame_range[0]) if action.frame_range else 0
    end = int(action.frame_range[1]) if action.frame_range else start
    frames = list(range(start, end + 1))
    idle = eval_location(curves, start)
    xs = [(frame, eval_location(curves, frame)[0]) for frame in frames]
    peak_frame, peak_x = max(xs, key=lambda item: item[1])
    span = peak_x - idle[0]
    if abs(span) <= 0.05:
        force_bone_constant(action, bone, full_extension, start, end)
        return True

    frame_values = []
    for frame in frames:
        current = eval_location(curves, frame)
        alpha = max(0.0, min(1.0, (current[0] - idle[0]) / span))
        frame_values.append((frame, mix_location(idle, full_extension, alpha)))
    rewrite_location_frames(action, bone, frame_values)
    return True

def amplify_left_extension_pair(action, blade_bone, blade_full_extension, gimic_bone, gimic_full_extension):
    blade_curves = find_location_curves(action, blade_bone)
    gimic_curves = find_location_curves(action, gimic_bone)
    if len(blade_curves) != 3 or len(gimic_curves) != 3:
        return 0
    start = int(action.frame_range[0]) if action.frame_range else 0
    end = int(action.frame_range[1]) if action.frame_range else start
    frames = list(range(start, end + 1))
    blade_idle = eval_location(blade_curves, start)
    gimic_idle = eval_location(gimic_curves, start)
    xs = [(frame, eval_location(blade_curves, frame)[0]) for frame in frames]
    peak_frame, peak_x = max(xs, key=lambda item: item[1])
    span = peak_x - blade_idle[0]
    if abs(span) <= 0.05:
        force_bone_constant(action, blade_bone, blade_full_extension, start, end)
        force_bone_constant(action, gimic_bone, gimic_full_extension, start, end)
        return 6

    blade_values = []
    gimic_values = []
    for frame in frames:
        current = eval_location(blade_curves, frame)
        alpha = max(0.0, min(1.0, (current[0] - blade_idle[0]) / span))
        blade_values.append((frame, mix_location(blade_idle, blade_full_extension, alpha)))
        gimic_values.append((frame, mix_location(gimic_idle, gimic_full_extension, alpha)))
    rewrite_location_frames(action, blade_bone, blade_values)
    rewrite_location_frames(action, gimic_bone, gimic_values)
    return 6

def amplify_left_extension_pair_x(action, blade_bone, blade_extended_x, gimic_bone, gimic_extended_x):
    blade_curves = find_location_curves(action, blade_bone)
    gimic_curves = find_location_curves(action, gimic_bone)
    if 0 not in blade_curves or 0 not in gimic_curves:
        return 0
    start = int(action.frame_range[0]) if action.frame_range else 0
    end = int(action.frame_range[1]) if action.frame_range else start
    frames = list(range(start, end + 1))
    blade_idle_x = blade_curves[0].evaluate(start)
    gimic_idle_x = gimic_curves[0].evaluate(start)
    blade_values = [(frame, blade_curves[0].evaluate(frame)) for frame in frames]
    peak_frame, peak_x = max(blade_values, key=lambda item: item[1])
    span = peak_x - blade_idle_x
    if abs(span) <= 0.05:
        return 0
    blade_replacement = []
    gimic_replacement = []
    for frame, current_x in blade_values:
        alpha = max(0.0, min(1.0, (current_x - blade_idle_x) / span))
        blade_replacement.append((frame, blade_idle_x + (blade_extended_x - blade_idle_x) * alpha))
        gimic_replacement.append((frame, gimic_idle_x + (gimic_extended_x - gimic_idle_x) * alpha))
    replace_location_curve(action, blade_bone, 0, blade_replacement)
    replace_location_curve(action, gimic_bone, 0, gimic_replacement)
    return 2

def mirror_right_from_left(action, left_bone, right_bone):
    left_curves = find_location_curves(action, left_bone)
    if len(left_curves) != 3:
        return False
    start = int(action.frame_range[0]) if action.frame_range else 0
    end = int(action.frame_range[1]) if action.frame_range else start
    frame_values = [(frame, mirror_left_location_to_right(eval_location(left_curves, frame))) for frame in range(start, end + 1)]
    rewrite_location_frames(action, right_bone, frame_values)
    return True

def force_right_idle_x(action, blade_x=0.0):
    start = int(action.frame_range[0]) if action.frame_range else 0
    end = int(action.frame_range[1]) if action.frame_range else start
    changed = 0
    if force_bone_x_constant(action, 'R_ArmBlade_00', blade_x, start, end):
        changed += 1
    if force_bone_x_constant(action, 'R_ArmBlade_Gimic_05', 0.0, start, end):
        changed += 1
    return changed

def force_raid_end_retraction(action):
    left_blade_curves = find_location_curves(action, 'L_ArmBlade_00')
    left_gimic_curves = find_location_curves(action, 'L_ArmBlade_Gimic_05')
    if len(left_blade_curves) != 3 or len(left_gimic_curves) != 3:
        return 0
    start = int(action.frame_range[0]) if action.frame_range else 0
    left_blade_x = eval_location(left_blade_curves, start)[0]
    left_gimic_x = eval_location(left_gimic_curves, start)[0]
    changed = 0
    if force_bone_x_transition(action, 'L_ArmBlade_00', left_blade_x, 99, 107, 0.0):
        changed += 1
    if force_bone_x_transition(action, 'L_ArmBlade_Gimic_05', left_gimic_x, 99, 107, 0.0):
        changed += 1
    if force_bone_x_transition(action, 'R_ArmBlade_00', -left_blade_x, 99, 107, 0.0):
        changed += 1
    if force_bone_x_transition(action, 'R_ArmBlade_Gimic_05', -left_gimic_x, 99, 107, 0.0):
        changed += 1
    return changed

def apply_ch6500_attack_targeted_blade_cases(actions):
    if not fix_ch6500_armblade:
        return
    reference = get_reference_ch6500_extension(actions)
    if reference is None:
        print('CH6500_ARMBLADE_TARGETED_REPAIR skipped=no_reference_extension', flush=True)
        return

    good_tokens = {'1010', '0700', '0570', '0575', '0254', '0252', '0250'}
    left_amplify_tokens = {'0510', '0270', '0231'}
    left_window_tokens = {'0230'}
    exception_tokens = {'0001'}
    left_extend_right_idle_tokens = {'0231', '0270', '0510'}
    raid_hold_extended_tokens = {'1012', '1014', '1016'}
    raid_end_tokens = {'1018'}
    right_idle_double_blade_tokens = {'0005'}
    changed_actions = 0
    changed_curves = 0

    for action in actions:
        if 'Attack_' not in action.name:
            continue
        token = None
        for candidate in [
            '0000','0001','0005','0010','0230','0231','0240','0250','0252','0254',
            '0260','0270','0300','0510','0500','0520','0521','0522','0570','0575',
            '0700','1010','1012','1014','1016','1018'
        ]:
            if action_has_token(action, candidate):
                token = candidate
                break
        if token is None or token in good_tokens:
            continue

        action_curves = 0
        if token in left_window_tokens:
            if force_bone_window(action, 'L_ArmBlade_00', reference['L_ArmBlade_00'], 12, 148):
                action_curves += 3
            if force_bone_window(action, 'L_ArmBlade_Gimic_05', reference['L_ArmBlade_Gimic_05'], 12, 148):
                action_curves += 3
        elif token in left_amplify_tokens:
            action_curves += amplify_left_extension_pair(
                action,
                'L_ArmBlade_00',
                reference['L_ArmBlade_00'],
                'L_ArmBlade_Gimic_05',
                reference['L_ArmBlade_Gimic_05'],
            )
            if token in left_extend_right_idle_tokens:
                action_curves += force_right_idle_x(action)
        elif token in raid_end_tokens:
            action_curves += force_raid_end_retraction(action)

        if token in exception_tokens:
            if mirror_right_from_left(action, 'L_ArmBlade_00', 'R_ArmBlade_00'):
                action_curves += 3
            if mirror_right_from_left(action, 'L_ArmBlade_Gimic_05', 'R_ArmBlade_Gimic_05'):
                action_curves += 3
        elif token in raid_hold_extended_tokens:
            if mirror_right_from_left(action, 'L_ArmBlade_00', 'R_ArmBlade_00'):
                action_curves += 3
            if mirror_right_from_left(action, 'L_ArmBlade_Gimic_05', 'R_ArmBlade_Gimic_05'):
                action_curves += 3
        elif token not in left_amplify_tokens and token not in left_window_tokens and token not in raid_end_tokens:
            blade_x = -21.328344 if token in right_idle_double_blade_tokens else 0.0
            action_curves += force_right_idle_x(action, blade_x)

        if action_curves:
            changed_actions += 1
            changed_curves += action_curves
            print(f'CH6500_ARMBLADE_TARGETED_REPAIR action={action.name} token={token} curves={action_curves}', flush=True)

    print(f'CH6500_ARMBLADE_TARGETED_REPAIR_TOTAL actions={changed_actions} curves={changed_curves}', flush=True)

def apply_ch6500_non_attack_armblade_auto_repair(actions):
    if not fix_ch6500_armblade:
        return

    left_extended_blade_x = 86.318321
    left_extended_gimic_x = 19.725996
    changed_actions = 0
    changed_curves = 0

    for action in actions:
        if 'Attack_' in action.name:
            continue
        curves = {
            'L_ArmBlade_00': find_location_curves(action, 'L_ArmBlade_00'),
            'R_ArmBlade_00': find_location_curves(action, 'R_ArmBlade_00'),
            'L_ArmBlade_Gimic_05': find_location_curves(action, 'L_ArmBlade_Gimic_05'),
            'R_ArmBlade_Gimic_05': find_location_curves(action, 'R_ArmBlade_Gimic_05'),
        }
        if not all(0 in curves[bone] for bone in curves):
            continue

        start = int(action.frame_range[0]) if action.frame_range else 0
        end = int(action.frame_range[1]) if action.frame_range else start
        frames = list(range(start, end + 1))
        left_xs = [curves['L_ArmBlade_00'][0].evaluate(frame) for frame in frames]
        right_xs = [curves['R_ArmBlade_00'][0].evaluate(frame) for frame in frames]
        right_gimic_xs = [curves['R_ArmBlade_Gimic_05'][0].evaluate(frame) for frame in frames]
        left_span = max(left_xs) - min(left_xs)
        right_span = max(right_xs) - min(right_xs)
        right_gimic_span = max(right_gimic_xs) - min(right_gimic_xs)

        action_curves = 0
        classes = []
        if 0.5 <= left_span < 10.0 and max(left_xs) < 20.0:
            action_curves += amplify_left_extension_pair_x(
                action,
                'L_ArmBlade_00',
                left_extended_blade_x,
                'L_ArmBlade_Gimic_05',
                left_extended_gimic_x,
            )
            classes.append('left_underextension')

        right_idle_offset = abs(curves['R_ArmBlade_00'][0].evaluate(start)) > 10.0 or abs(curves['R_ArmBlade_Gimic_05'][0].evaluate(start)) > 10.0
        if right_idle_offset and right_span < 1.0 and right_gimic_span < 1.0:
            action_curves += force_right_idle_x(action)
            classes.append('right_idle_offset')

        if action_curves:
            changed_actions += 1
            changed_curves += action_curves
            print(f'CH6500_ARMBLADE_NON_ATTACK_AUTO_REPAIR action={action.name} classes={"+".join(classes)} curves={action_curves}', flush=True)

    print(f'CH6500_ARMBLADE_NON_ATTACK_AUTO_REPAIR_TOTAL actions={changed_actions} curves={changed_curves}', flush=True)

def apply_ch6500_armblade_blender_repair(actions):
    if not fix_ch6500_armblade:
        return

    total_actions = 0
    total_curves = 0
    for action in actions:
        curve_groups = {
            'L_ArmBlade_00': find_location_curves(action, 'L_ArmBlade_00'),
            'R_ArmBlade_00': find_location_curves(action, 'R_ArmBlade_00'),
            'L_ArmBlade_Gimic_05': find_location_curves(action, 'L_ArmBlade_Gimic_05'),
            'R_ArmBlade_Gimic_05': find_location_curves(action, 'R_ArmBlade_Gimic_05'),
        }
        if not all(len(curve_groups[bone]) == 3 for bone in curve_groups):
            continue

        start = int(action.frame_range[0]) if action.frame_range else 0
        end = int(action.frame_range[1]) if action.frame_range else start
        if end <= start:
            continue

        frames = list(range(start, end + 1))
        right_x = [(frame, eval_location(curve_groups['R_ArmBlade_00'], frame)[0]) for frame in frames]
        idle_frame = start
        idle_x = eval_location(curve_groups['R_ArmBlade_00'], idle_frame)[0]
        extended_frame, extended_x = min(right_x, key=lambda item: item[1])
        if abs(extended_x - idle_x) < 1.0:
            continue

        left_idle_00 = eval_location(curve_groups['L_ArmBlade_00'], idle_frame)
        left_idle_g05 = eval_location(curve_groups['L_ArmBlade_Gimic_05'], idle_frame)
        right_extended_00 = eval_location(curve_groups['R_ArmBlade_00'], extended_frame)
        right_extended_g05 = eval_location(curve_groups['R_ArmBlade_Gimic_05'], extended_frame)

        replacements = {
            'R_ArmBlade_00': [],
            'L_ArmBlade_00': [],
            'R_ArmBlade_Gimic_05': [],
            'L_ArmBlade_Gimic_05': [],
        }
        for frame, rx in right_x:
            alpha = max(0.0, min(1.0, (rx - idle_x) / (extended_x - idle_x)))
            replacements['R_ArmBlade_00'].append((frame, mix_location(left_idle_00, right_extended_00, alpha)))
            replacements['L_ArmBlade_00'].append((frame, mix_location(left_idle_00, mirror_right_location_to_left(right_extended_00), alpha)))
            replacements['R_ArmBlade_Gimic_05'].append((frame, mix_location(left_idle_g05, right_extended_g05, alpha)))
            replacements['L_ArmBlade_Gimic_05'].append((frame, mix_location(left_idle_g05, mirror_right_location_to_left(right_extended_g05), alpha)))

        for bone, frame_values in replacements.items():
            for axis in range(3):
                replace_location_curve(action, bone, axis, [(frame, values[axis]) for frame, values in frame_values])
                total_curves += 1

        total_actions += 1
        print(
            f'CH6500_ARMBLADE_BLENDER_REPAIR action={action.name} frames={len(frames)} '
            f'idle_frame={idle_frame} extended_frame={extended_frame} idle_x={idle_x:.6f} extended_x={extended_x:.6f}',
            flush=True
        )

    print(f'CH6500_ARMBLADE_BLENDER_REPAIR_TOTAL actions={total_actions} curves={total_curves}', flush=True)

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
clear_scene()

bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 0.01

reference_locations = {}
if reference_fbx is not None:
    reference_locations = read_reference_locations(reference_fbx, reference_action_filter)
    clear_scene()

log(f'File {index}/{total} 2/6 importing source FBX')
bpy.ops.import_scene.fbx(filepath=str(src), use_anim=True, automatic_bone_orientation=False, ignore_leaf_bones=False, force_connect_children=False)
armatures = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
actions = list(bpy.data.actions)
print(f'IMPORTED file={index}/{total} armatures={len(armatures)} meshes={len(meshes)} actions={len(actions)}')
if not armatures:
    write_status('FAILED', 'No armature imported from animated source FBX', len(actions))
    raise RuntimeError('No armature imported from animated source FBX')
if not actions:
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

resample_sparse_pose_quaternion_curves(actions)
stabilize_root_rotation_axis(actions)
apply_bone_spacing_reference(actions, reference_locations)
apply_ch6500_armblade_blender_repair(actions)
apply_ch6500_attack_targeted_blade_cases(actions)
apply_ch6500_non_attack_armblade_auto_repair(actions)

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
real_print = install_fbx_pose_progress([action.name for action in actions])
try:
    bpy.ops.export_scene.fbx(
        filepath=str(out),
        check_existing=False,
        use_selection=False,
        object_types={'MESH', 'ARMATURE'},
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        use_armature_deform_only=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=True,
        bake_anim_force_startend_keying=True,
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
""";

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
    var failures = new List<string>();
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
                if (source == null)
                {
                    var message = $"texture source not found {tex.texPath}";
                    failures.Add(message);
                    progress.WriteLine($"WARNING: {message}");
                    continue;
                }
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
                        try
                        {
                            texFile.SaveAsDDS(tempDds);
                            ConvertDdsToPng(tempDds, outPath);
                        }
                        finally
                        {
                            try
                            {
                                if (File.Exists(tempDds)) File.Delete(tempDds);
                            }
                            catch (Exception cleanupError)
                            {
                                Console.WriteLine($"WARNING: temporary DDS cleanup failed {tempDds}: {cleanupError.Message}");
                            }
                        }
                    }
                    matEntries.Add(new { type = tex.texType, gamePath = tex.texPath, source, output = outPath });
                }
                catch (Exception ex)
                {
                    var message = $"texture export failed {tex.texPath}: {ex.Message}";
                    if (!IsNonFatalTextureExportWarning(ex))
                        failures.Add(message);
                    progress.WriteLine($"WARNING: {message}");
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
    if (failures.Count > 0)
    {
        throw new Exception($"Texture export failed for {failures.Count} texture(s). See warnings above.");
    }
}

static bool IsNonFatalTextureExportWarning(Exception ex)
{
    return ex is NotSupportedException
        && ex.Message.Contains("Depth > 1 textures not supported", StringComparison.OrdinalIgnoreCase);
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
        ?? ResolveWinGetTool("texconv")
        ?? throw new FileNotFoundException("texconv.exe not found. Released builds must include texconv.exe beside the exporter executables. Source builds can install Microsoft.DirectXTex.Texconv or pass -p:TexconvPath=<path> during build.");
    var outDir = Path.GetDirectoryName(pngPath) ?? ".";

    if (TryRunTexconv(null, out var defaultError)) return;

    Console.WriteLine("WARNING: texconv DDS->PNG default conversion failed; retrying with R8G8B8A8_UNORM PNG-compatible output.");
    if (TryRunTexconv("R8G8B8A8_UNORM", out var rgbaError)) return;

    throw new Exception($"texconv DDS->PNG failed. Default conversion: {defaultError}RGBA fallback: {rgbaError}");

    bool TryRunTexconv(string? outputFormat, out string error)
    {
        var produced = Path.Combine(outDir, Path.GetFileNameWithoutExtension(ddsPath) + ".png");
        DeleteIfExists(produced);
        if (!string.Equals(Path.GetFullPath(produced), Path.GetFullPath(pngPath), StringComparison.OrdinalIgnoreCase))
        {
            DeleteIfExists(pngPath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = texconv,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (!string.IsNullOrWhiteSpace(outputFormat))
        {
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(outputFormat);
        }
        psi.ArgumentList.Add("-ft");
        psi.ArgumentList.Add("png");
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outDir);
        psi.ArgumentList.Add(ddsPath);
        using var proc = Process.Start(psi) ?? throw new Exception("Failed to start texconv");
        proc.WaitForExit();
        var err = proc.StandardError.ReadToEnd();
        var output = proc.StandardOutput.ReadToEnd();
        error = err + output;
        if (proc.ExitCode != 0) return false;
        if (!File.Exists(produced))
        {
            error = $"texconv did not produce expected PNG: {produced}{Environment.NewLine}{error}";
            return false;
        }
        if (!string.Equals(Path.GetFullPath(produced), Path.GetFullPath(pngPath), StringComparison.OrdinalIgnoreCase))
        {
            File.Move(produced, pngPath, overwrite: true);
        }
        return true;
    }

    static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

static string? ResolveTool(string exe)
{
    var fileName = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : exe + ".exe";
    foreach (var dir in GetBundledToolDirectories())
    {
        var candidate = Path.Combine(dir, fileName);
        if (File.Exists(candidate)) return candidate;
    }

    var path = Environment.GetEnvironmentVariable("PATH") ?? "";
    foreach (var dir in path.Split(Path.PathSeparator))
    {
        if (string.IsNullOrWhiteSpace(dir)) continue;
        var candidate = Path.Combine(dir.Trim(), fileName);
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}

static IEnumerable<string> GetBundledToolDirectories()
{
    var dirs = new List<string>();

    AddUniqueToolDir(AppContext.BaseDirectory);
    if (Environment.ProcessPath is { } processPath)
    {
        var processDir = Path.GetDirectoryName(processPath);
        if (!string.IsNullOrWhiteSpace(processDir)) AddUniqueToolDir(processDir);
    }

    return dirs;

    void AddUniqueToolDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        var full = Path.GetFullPath(dir);
        if (!dirs.Contains(full, StringComparer.OrdinalIgnoreCase))
            dirs.Add(full);
    }
}

static string? ResolveWinGetTool(string exe)
{
    var packagesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
    if (!Directory.Exists(packagesDir)) return null;

    var fileName = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : exe + ".exe";
    return Directory.GetFiles(packagesDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
}

static MeshFile LoadMesh(string meshPath, string? explicitStreamingPath, bool allowMissingStreaming)
{
    using var meshHandler = new FileHandler(meshPath);
    var magic = meshHandler.Read<uint>(0);
    meshHandler.Seek(0);

    if (magic == MplyMeshFile.Magic)
    {
        var mply = new MplyMeshFile(meshHandler);
        if (!mply.Read()) throw new Exception($"REE-Lib failed to read MPLY mesh: {meshPath}");

        var converted = mply.ConvertToMergedClassicMesh();
        converted.FileHandler = mply.FileHandler;
        Console.WriteLine($"Loaded MPLY mesh {meshPath} version={converted.Header.version} convertedToClassic=True materials={converted.MaterialNames.Count} bones={converted.BoneData?.Bones.Count ?? 0} lods={converted.MeshData?.LODs.Count ?? 0}");

        if (!string.IsNullOrWhiteSpace(explicitStreamingPath))
        {
            Console.WriteLine($"MPLY streaming sibling found but not loaded as a classic streaming buffer: {explicitStreamingPath}");
        }

        return converted;
    }

    if (magic != MeshFile.Magic)
    {
        throw new NotSupportedException($"Unknown mesh type 0x{magic:X8}: {meshPath}");
    }

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
    private System.Threading.Timer? timer;
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
            timer ??= new System.Threading.Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
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
    [JsonPropertyName("game")]
    public string Game { get; set; } = "";
    [JsonPropertyName("gameDisplayName")]
    public string GameDisplayName { get; set; } = "";
    [JsonPropertyName("gameListFile")]
    public string GameListFile { get; set; } = "";
    [JsonPropertyName("gameListPath")]
    public string GameListPath { get; set; } = "";
    public string Language { get; set; } = "";
    public string ExtractRoot { get; set; } = "";
    public string DefaultExportRoot { get; set; } = "";
    public string BlenderPath { get; set; } = "";
    public string TextureFormat { get; set; } = "png";
    [JsonPropertyName("guiExportOptionsMode")]
    public string GuiExportOptionsMode { get; set; } = "";
    [JsonPropertyName("guiSplitMotlists")]
    public bool GuiSplitMotlists { get; set; }
    [JsonPropertyName("guiSplitAnimations")]
    public bool GuiSplitAnimations { get; set; }
    [JsonPropertyName("guiNoTextures")]
    public bool GuiNoTextures { get; set; }
    [JsonPropertyName("guiIncludeLods")]
    public bool GuiIncludeLods { get; set; }
    [JsonPropertyName("guiIncludeOcclusion")]
    public bool GuiIncludeOcclusion { get; set; }
    [JsonPropertyName("guiNoPlaceholderBones")]
    public bool GuiNoPlaceholderBones { get; set; }
    [JsonPropertyName("guiAllowMissingStreaming")]
    public bool GuiAllowMissingStreaming { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

sealed record WizardGameDefinition(string Id, GameName GameName, string DisplayName, string ListFileName);

static class WizardGames
{
    public static readonly IReadOnlyList<WizardGameDefinition> Definitions =
    [
        new("re2", GameName.re2, "Resident Evil 2", "RE2_STM_Release.list"),
        new("re2rt", GameName.re2rt, "Resident Evil 2 RT", "RE2_RT_STM_Release.list"),
        new("re3", GameName.re3, "Resident Evil 3", "RE3_STM_Release.list"),
        new("re3rt", GameName.re3rt, "Resident Evil 3 RT", "RE3_RT_STM_Release.list"),
        new("re4", GameName.re4, "Resident Evil 4", "RE4_STM_Release.list"),
        new("re7", GameName.re7, "Resident Evil 7", "RE7_STM_Release.list"),
        new("re7rt", GameName.re7rt, "Resident Evil 7 RT", "RE7_RT_STM_Release.list"),
        new("re8", GameName.re8, "Resident Evil Village", "RE8_STM_Release.list"),
        new("re9", GameName.re9, "Resident Evil Requiem", "RE9_STM_Release.list"),
        new("dmc5", GameName.dmc5, "Devil May Cry 5", "DMC5_STM_Release.list"),
        new("mhrise", GameName.mhrise, "Monster Hunter Rise", "MHR_STM_Release.list"),
        new("sf6", GameName.sf6, "Street Fighter 6", "SF6_STM_Release.list"),
        new("dd2", GameName.dd2, "Dragon's Dogma 2", "DD2_STM_Release.list"),
        new("gtrick", GameName.gtrick, "Ghost Trick", "GTPD_STM_Release.list"),
        new("apollo", GameName.apollo, "Apollo Justice: Ace Attorney Trilogy", "AJ_AAT_STM_Release.list"),
        new("drdr", GameName.drdr, "Dead Rising Deluxe Remaster", "DRDR_STM_Release.list"),
        new("kunitsu", GameName.kunitsu, "Kunitsu-Gami: Path of the Goddess", "KGPG_STM_Release.list"),
        new("oni2", GameName.oni2, "Onimusha 2", "O2_SD_STM_Release.list"),
        new("mhwilds", GameName.mhwilds, "Monster Hunter Wilds", "MHWs_STM_Release.list"),
        new("pragmata", GameName.pragmata, "Pragmata", "P_STM_Release.list"),
        new("mhsto3", GameName.mhsto3, "Monster Hunter Stories 3", "MHS3_TR_STM_Demo.list"),
    ];
}

sealed record GameListEntry(string RelativePath)
{
    public string FileName { get; } = Path.GetFileName(RelativePath.Replace('/', Path.DirectorySeparatorChar));
    public string RelativeDirectory { get; } = Path.GetDirectoryName(RelativePath.Replace('/', Path.DirectorySeparatorChar))?.Replace('\\', '/') ?? "";
}

sealed record ResolvedAsset(string Path, string? IndexedRelativePath);

sealed record WizardMeshInspection(int BoneCount, int MaterialCount, int LodCount, bool RequiresStreaming);

sealed record WizardExportJob(
    int RowNumber,
    string MeshQuery,
    string MeshPath,
    string OutputFolderName,
    string? StreamingPath,
    WizardMeshInspection Inspection,
    WizardAnimationSelection Animation)
{
    public bool IsSkeletal => Inspection.BoneCount > 0;
}

sealed record WizardBatchSkippedRow(int RowNumber, string MeshQuery, string Reason);

enum WizardLanguage
{
    English,
    Korean,
}

enum WizardMode
{
    SingleMesh,
    BatchCsv,
}

enum WizardBatchSkeletalMode
{
    PromptForAnimations,
    SkipAnimationPrompts,
}

enum WizardBatchExistingExportScanMode
{
    Auto,
    Designated,
}

sealed record WizardBatchExistingExportScan(WizardBatchExistingExportScanMode Mode, string? DirectoryPath)
{
    public static WizardBatchExistingExportScan Auto { get; } = new(WizardBatchExistingExportScanMode.Auto, null);
    public static WizardBatchExistingExportScan Designated(string directoryPath) => new(WizardBatchExistingExportScanMode.Designated, directoryPath);
}

enum AssetKind
{
    Mesh,
    Motlist,
    Mot,
}

enum WizardAnimationMode
{
    None,
    MotlistDirectory,
    Motlists,
    MotFiles,
    MotlistDirectoryAndMotFiles,
}

sealed class WizardAnimationSelection
{
    public static WizardAnimationSelection None { get; } = new(WizardAnimationMode.None, null, [], []);

    public WizardAnimationMode Mode { get; }
    public string? MotlistDirectory { get; }
    public IReadOnlyList<string> Motlists { get; }
    public IReadOnlyList<string> MotFiles { get; }

    private WizardAnimationSelection(WizardAnimationMode mode, string? motlistDirectory, IReadOnlyList<string> motlists, IReadOnlyList<string> motFiles)
    {
        Mode = mode;
        MotlistDirectory = motlistDirectory;
        Motlists = motlists;
        MotFiles = motFiles;
    }

    public static WizardAnimationSelection FromMotlistDirectory(string path) => new(WizardAnimationMode.MotlistDirectory, path, [], []);
    public static WizardAnimationSelection FromMotlists(IReadOnlyList<string> paths) => new(WizardAnimationMode.Motlists, null, paths, []);
    public static WizardAnimationSelection FromMotFiles(IReadOnlyList<string> paths) => new(WizardAnimationMode.MotFiles, null, [], paths);
    public static WizardAnimationSelection FromMotlistDirectoryAndMotFiles(string motlistDirectory, IReadOnlyList<string> motFiles) => new(WizardAnimationMode.MotlistDirectoryAndMotFiles, motlistDirectory, [], motFiles);
}

sealed record WizardAnimationCandidates(string MeshName, string? MotlistDirectory, IReadOnlyList<string> MotFiles)
{
    public bool HasAnyCandidates => !string.IsNullOrWhiteSpace(MotlistDirectory) || MotFiles.Count > 0;
    public static WizardAnimationCandidates Empty(string meshName) => new(meshName, null, []);
}
