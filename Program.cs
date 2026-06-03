using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
static bool HasFlag(string[] args, string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

if (args.Length == 0 || HasFlag(args, "--help"))
{
    Console.WriteLine("REE-Content-Exporter - REE Content Editor pipeline wrapper");
    Console.WriteLine("Usage:");
    Console.WriteLine("  REE-Content-Exporter --mesh <mesh.path> [--streaming <meshstream.path>] [--mdf <mdf2.path>] [--motlist <motlist.path> ...|--motlist-dir <folder>|--mot <mot.path> ...] --output <file.fbx|file.glb|folder> [--animation-name <contains>] [--batch-motlist|--split-animations] [--no-animations] [--no-textures] [--texture-format png|dds] [--include-lods] [--include-occlusion] [--allow-missing-streaming]");
    return;
}

var meshPath = GetArg(args, "--mesh") ?? throw new ArgumentException("Missing --mesh");
var streamingPath = GetArg(args, "--streaming");
var mdfPath = GetArg(args, "--mdf");
var motlistPaths = GetArgs(args, "--motlist").ToList();
var motlistDir = GetArg(args, "--motlist-dir");
if (!string.IsNullOrWhiteSpace(motlistDir))
{
    motlistPaths.AddRange(Directory.GetFiles(motlistDir, "*.motlist*", SearchOption.AllDirectories));
}
motlistPaths = motlistPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
var motPaths = GetArgs(args, "--mot").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
var outputPath = GetArg(args, "--output") ?? throw new ArgumentException("Missing --output");
var animationFilter = GetArg(args, "--animation-name");
var includeAnimations = !HasFlag(args, "--no-animations");
var includeTextures = !HasFlag(args, "--no-textures");
var textureFormat = (GetArg(args, "--texture-format") ?? "png").ToLowerInvariant();
if (textureFormat is not ("png" or "dds")) throw new ArgumentException("--texture-format must be png or dds");
var batchMotlist = HasFlag(args, "--batch-motlist");
var splitAnimations = HasFlag(args, "--split-animations");
var includeLods = HasFlag(args, "--include-lods");
var includeOcc = HasFlag(args, "--include-occlusion");
var allowMissingStreaming = HasFlag(args, "--allow-missing-streaming");

Console.WriteLine("REE Content Editor native export path");
Console.WriteLine($"Mesh: {meshPath}");
Console.WriteLine($"Streaming: {streamingPath ?? "-"}");
Console.WriteLine($"MDF: {mdfPath ?? "auto"}");
Console.WriteLine($"Motlists: {(motlistPaths.Count == 0 ? "-" : string.Join("; ", motlistPaths))}");
Console.WriteLine($"Mots: {(motPaths.Count == 0 ? "-" : string.Join("; ", motPaths))}");
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

var motions = new List<(string Source, MotFileBase Motion)>();
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

if (exportSeparateAnimationFiles)
{
    if (motions.Count == 0) throw new ArgumentException("--batch-motlist requires --motlist with at least one selected motion");
    var ext = Path.GetExtension(outputPath);
    var outDir = string.IsNullOrEmpty(ext) ? outputPath : (Path.GetDirectoryName(outputPath) ?? ".");
    var outExt = string.IsNullOrEmpty(ext) ? ".glb" : ext;
    Directory.CreateDirectory(outDir);
    if (includeTextures && materialWrapper != null) ExportMaterialTextures(materialWrapper, meshPath, Path.Combine(outDir, "textures"), textureFormat);
    Console.WriteLine($"Batch exporting {motions.Count} motions to {outDir} (*{outExt})");
    var index = 0;
    var includeSourceInName = motlistPaths.Count + motPaths.Count > 1;
    foreach (var (source, motion) in motions)
    {
        var safe = SanitizeFileName(string.IsNullOrWhiteSpace(motion.Name) ? $"motion_{index:0000}" : motion.Name);
        var sourcePrefix = includeSourceInName ? SanitizeFileName(source) + "_" : "";
        var target = Path.Combine(outDir, $"{index:0000}_{sourcePrefix}{safe}{outExt}");
        ExportOne(resource, target, includeLods, includeOcc, [motion], materialWrapper, meshPath, includeTextures: false);
        Console.WriteLine($"[{index + 1}/{motions.Count}] {target}");
        index++;
    }
}
else
{
    var singleOutputPath = ResolveSingleOutputPath(outputPath, name);
    ExportOne(resource, singleOutputPath, includeLods, includeOcc, motions.Select(m => m.Motion), materialWrapper, meshPath, includeTextures);
}

Console.WriteLine("DONE");

static void ExportOne(CommonMeshResource resource, string target, bool includeLods, bool includeOcc, IEnumerable<MotFileBase> motions, MaterialGroupWrapper? materials, string meshPath, bool includeTextures)
{
    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
    if (includeTextures && materials != null) ExportMaterialTextures(materials, meshPath, Path.Combine(Path.GetDirectoryName(target) ?? ".", "textures"), resource.ExportTextureFormat);
    resource.ExportToFile(target, includeLods, includeOcc, null, motions, null);
    NormalizeGlbNames(target);
    Console.WriteLine($"Exported {target} bytes={new FileInfo(target).Length}");
}

static string ResolveSingleOutputPath(string outputPath, string meshName)
{
    if (!string.IsNullOrEmpty(Path.GetExtension(outputPath))) return outputPath;

    Directory.CreateDirectory(outputPath);
    return Path.Combine(outputPath, $"{SanitizeFileName(meshName)}_all_animations.glb");
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
