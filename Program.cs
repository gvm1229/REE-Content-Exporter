using System.Diagnostics;
using System.Text.Json;
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
static bool HasFlag(string[] args, string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

if (args.Length == 0 || HasFlag(args, "--help"))
{
    Console.WriteLine("REE-Content-Exporter - REE Content Editor pipeline wrapper");
    Console.WriteLine("Usage:");
    Console.WriteLine("  REE-Content-Exporter --mesh <mesh.path> [--streaming <meshstream.path>] [--mdf <mdf2.path>] [--motlist <motlist.path>|--mot <mot.path>] --output <file.fbx|file.glb|folder> [--animation-name <contains>] [--batch-motlist] [--no-animations] [--no-textures] [--texture-format png|dds] [--include-lods] [--include-occlusion] [--allow-missing-streaming]");
    return;
}

var meshPath = GetArg(args, "--mesh") ?? throw new ArgumentException("Missing --mesh");
var streamingPath = GetArg(args, "--streaming");
var mdfPath = GetArg(args, "--mdf");
var motlistPath = GetArg(args, "--motlist");
var motPath = GetArg(args, "--mot");
var outputPath = GetArg(args, "--output") ?? throw new ArgumentException("Missing --output");
var animationFilter = GetArg(args, "--animation-name");
var includeAnimations = !HasFlag(args, "--no-animations");
var includeTextures = !HasFlag(args, "--no-textures");
var textureFormat = (GetArg(args, "--texture-format") ?? "png").ToLowerInvariant();
if (textureFormat is not ("png" or "dds")) throw new ArgumentException("--texture-format must be png or dds");
var batchMotlist = HasFlag(args, "--batch-motlist");
var includeLods = HasFlag(args, "--include-lods");
var includeOcc = HasFlag(args, "--include-occlusion");
var allowMissingStreaming = HasFlag(args, "--allow-missing-streaming");

Console.WriteLine("REE Content Editor native export path");
Console.WriteLine($"Mesh: {meshPath}");
Console.WriteLine($"Streaming: {streamingPath ?? "-"}");
Console.WriteLine($"MDF: {mdfPath ?? "auto"}");
Console.WriteLine($"Motlist: {motlistPath ?? "-"}");
Console.WriteLine($"Mot: {motPath ?? "-"}");
Console.WriteLine($"Output: {outputPath}");

using var meshHandler = new FileHandler(meshPath);
var mesh = new MeshFile(meshHandler);
if (!mesh.Read()) throw new Exception("REE-Lib failed to read mesh");
Console.WriteLine($"Loaded mesh version={mesh.Header.version} requiresStreaming={mesh.RequiresStreamingData} materials={mesh.MaterialNames.Count} bones={mesh.BoneData?.Bones.Count ?? 0} lods={mesh.MeshData?.LODs.Count ?? 0}");

if (!string.IsNullOrWhiteSpace(streamingPath))
{
    using var streamingHandler = new FileHandler(streamingPath);
    mesh.LoadStreamingData(streamingHandler);
    Console.WriteLine("Loaded explicit streaming buffer");
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
        Console.WriteLine("WARNING: mesh requires streaming data, but no candidate was found. Output may be invalid.");
    }
    else
    {
        throw new FileNotFoundException("Mesh requires streaming data, but no streaming buffer was found. Pass --streaming or extract the natives/STM/streaming sibling path.");
    }
}

var motions = new List<MotFileBase>();
if (includeAnimations)
{
    if (!string.IsNullOrWhiteSpace(motlistPath))
    {
        using var mlHandler = new FileHandler(motlistPath);
        var motlist = new MotlistFile(mlHandler);
        if (!motlist.Read()) throw new Exception("REE-Lib failed to read motlist");
        IEnumerable<MotFileBase> files = motlist.MotFiles;
        if (!string.IsNullOrWhiteSpace(animationFilter))
            files = files.Where(m => m.Name.Contains(animationFilter, StringComparison.OrdinalIgnoreCase));
        motions.AddRange(files);
        Console.WriteLine($"Loaded motlist {motlist.Name}: total={motlist.MotFiles.Count} selected={motions.Count}");
    }
    if (!string.IsNullOrWhiteSpace(motPath))
    {
        using var motHandler = new FileHandler(motPath);
        var mot = new MotFile(motHandler);
        if (!mot.Read()) throw new Exception("REE-Lib failed to read mot");
        mot.ReadBones(null);
        if (string.IsNullOrWhiteSpace(animationFilter) || mot.Name.Contains(animationFilter, StringComparison.OrdinalIgnoreCase))
            motions.Add(mot);
        Console.WriteLine($"Loaded mot {mot.Name}");
    }
}

var name = PathUtils.GetFilepathWithoutExtensionOrVersion(meshPath).ToString();
var resource = new CommonMeshResource(name, null!)
{
    NativeMesh = mesh,
    GameVersion = GameName.pragmata,
    ExportTextureFormat = textureFormat,
};

MaterialGroupWrapper? materialWrapper = null;
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
}

if (batchMotlist)
{
    if (motions.Count == 0) throw new ArgumentException("--batch-motlist requires --motlist with at least one selected motion");
    var ext = Path.GetExtension(outputPath);
    var outDir = string.IsNullOrEmpty(ext) ? outputPath : (Path.GetDirectoryName(outputPath) ?? ".");
    var outExt = string.IsNullOrEmpty(ext) ? ".glb" : ext;
    Directory.CreateDirectory(outDir);
    if (includeTextures && materialWrapper != null) ExportMaterialTextures(materialWrapper, meshPath, Path.Combine(outDir, "textures"), textureFormat);
    Console.WriteLine($"Batch exporting {motions.Count} motions to {outDir} (*{outExt})");
    var index = 0;
    foreach (var motion in motions)
    {
        var safe = SanitizeFileName(string.IsNullOrWhiteSpace(motion.Name) ? $"motion_{index:0000}" : motion.Name);
        var target = Path.Combine(outDir, $"{index:0000}_{safe}{outExt}");
        ExportOne(resource, target, includeLods, includeOcc, [motion], materialWrapper, meshPath, includeTextures: false);
        Console.WriteLine($"[{index + 1}/{motions.Count}] {target}");
        index++;
    }
}
else
{
    ExportOne(resource, outputPath, includeLods, includeOcc, motions, materialWrapper, meshPath, includeTextures);
}

Console.WriteLine("DONE");

static void ExportOne(CommonMeshResource resource, string target, bool includeLods, bool includeOcc, IEnumerable<MotFileBase> motions, MaterialGroupWrapper? materials, string meshPath, bool includeTextures)
{
    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
    if (includeTextures && materials != null) ExportMaterialTextures(materials, meshPath, Path.Combine(Path.GetDirectoryName(target) ?? ".", "textures"), resource.ExportTextureFormat);
    resource.ExportToFile(target, includeLods, includeOcc, null, motions, null);
    Console.WriteLine($"Exported {target} bytes={new FileInfo(target).Length}");
}

static void ExportMaterialTextures(MaterialGroupWrapper materials, string meshPath, string outputDir, string textureFormat)
{
    Directory.CreateDirectory(outputDir);
    var exported = new Dictionary<string, object>();
    foreach (var mat in materials.Materials)
    {
        var matEntries = new List<object>();
        foreach (var tex in mat.Textures)
        {
            if (string.IsNullOrWhiteSpace(tex.texPath) || tex.texPath.Contains("/null", StringComparison.OrdinalIgnoreCase)) continue;
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
                Console.WriteLine($"WARNING: texture export failed {tex.texPath}: {ex.Message}");
            }
            finally
            {
                streamHandler?.Dispose();
                texHandler?.Dispose();
            }
        }
        exported[mat.Name] = matEntries;
    }
    var manifest = Path.Combine(outputDir, "materials.textures.json");
    File.WriteAllText(manifest, JsonSerializer.Serialize(exported, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Exported material texture manifest: {manifest}");
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

    var first = Path.Combine(dir, baseNoVersion + "streaming");
    var candidates = new[] { stmStreaming, meshPath + ".streaming", meshPath + ".meshstreaming", first, first + ".meshstreaming", Path.ChangeExtension(meshPath, ".meshstreaming") };
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
    var root = GetLooseRoot(meshPath);
    if (root == null) return null;
    var rel = gamePath.Replace('/', Path.DirectorySeparatorChar).Replace("\\", Path.DirectorySeparatorChar.ToString()).TrimStart(Path.DirectorySeparatorChar);
    if (!rel.StartsWith("natives" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        rel = Path.Combine("natives", "STM", rel);
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

static string? GetLooseRoot(string path)
{
    var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace("\\", Path.DirectorySeparatorChar.ToString());
    var marker = Path.DirectorySeparatorChar + "natives" + Path.DirectorySeparatorChar;
    var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    return idx < 0 ? null : normalized[..idx];
}

static string SanitizeFileName(string name)
{
    foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
    return string.IsNullOrWhiteSpace(name) ? "unnamed" : name;
}

