using ContentEditor.App.FileLoaders;
using ReeLib;

internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Length == 0 || HasFlag(args, "--help"))
        {
            Console.WriteLine("REE-Content-Exporter - REE Content Editor pipeline wrapper");
            Console.WriteLine("Usage:");
            Console.WriteLine("  REE-Content-Exporter --mesh <mesh.path> [--streaming <meshstreaming.path>] [--motlist <motlist.path>|--mot <mot.path>] --output <file.fbx|file.glb> [--animation-name <contains>] [--no-animations] [--include-lods] [--include-occlusion] [--allow-missing-streaming]");
            return;
        }

        string meshPath = GetArg(args, "--mesh") ?? throw new ArgumentException("Missing --mesh");
        string? streamingPath = GetArg(args, "--streaming");
        string? motlistPath = GetArg(args, "--motlist");
        string? motPath = GetArg(args, "--mot");
        string outputPath = GetArg(args, "--output") ?? throw new ArgumentException("Missing --output");
        string? animationFilter = GetArg(args, "--animation-name");
        bool includeAnimations = !HasFlag(args, "--no-animations");
        bool includeLods = HasFlag(args, "--include-lods");
        bool includeOcc = HasFlag(args, "--include-occlusion");
        bool allowMissingStreaming = HasFlag(args, "--allow-missing-streaming");

        Console.WriteLine("REE Content Editor native export path");
        Console.WriteLine("Mesh: " + meshPath);
        Console.WriteLine("Streaming: " + (streamingPath ?? "-"));
        Console.WriteLine("Motlist: " + (motlistPath ?? "-"));
        Console.WriteLine("Mot: " + (motPath ?? "-"));
        Console.WriteLine("Output: " + outputPath);

        using FileHandler meshHandler = new FileHandler(meshPath);
        MeshFile mesh = new MeshFile(meshHandler);
        if (!mesh.Read()) throw new Exception("REE-Lib failed to read mesh");
        Console.WriteLine($"Loaded mesh version={mesh.Header.version} requiresStreaming={mesh.RequiresStreamingData} materials={mesh.MaterialNames.Count} bones={mesh.BoneData?.Bones.Count ?? 0} lods={mesh.MeshData?.LODs.Count ?? 0}");

        if (!string.IsNullOrWhiteSpace(streamingPath))
        {
            using FileHandler handler = new FileHandler(streamingPath);
            mesh.LoadStreamingData(handler);
            Console.WriteLine("Loaded explicit streaming buffer");
        }
        else if (mesh.RequiresStreamingData)
        {
            string? candidate = FindStreamingCandidate(meshPath);
            if (candidate != null)
            {
                using FileHandler handler = new FileHandler(candidate);
                mesh.LoadStreamingData(handler);
                Console.WriteLine("Loaded auto streaming buffer: " + candidate);
            }
            else if (!allowMissingStreaming)
            {
                throw new FileNotFoundException("Mesh requires streaming data, but no streaming buffer was found. Pass --streaming or extract the natives/STM/streaming sibling path.");
            }
            else
            {
                Console.WriteLine("WARNING: mesh requires streaming data, but no candidate was found. Output may be invalid.");
            }
        }

        List<MotFileBase> motions = new();
        if (includeAnimations)
        {
            if (!string.IsNullOrWhiteSpace(motlistPath))
            {
                using FileHandler motlistHandler = new FileHandler(motlistPath);
                MotlistFile motlist = new MotlistFile(motlistHandler);
                if (!motlist.Read()) throw new Exception("REE-Lib failed to read motlist");
                IEnumerable<MotFileBase> selected = motlist.MotFiles;
                if (!string.IsNullOrWhiteSpace(animationFilter))
                {
                    selected = selected.Where(m => m.Name.Contains(animationFilter, StringComparison.OrdinalIgnoreCase));
                }
                motions.AddRange(selected);
                Console.WriteLine($"Loaded motlist {motlist.Name}: total={motlist.MotFiles.Count} selected={motions.Count}");
            }

            if (!string.IsNullOrWhiteSpace(motPath))
            {
                using FileHandler motHandler = new FileHandler(motPath);
                MotFile mot = new MotFile(motHandler);
                if (!mot.Read()) throw new Exception("REE-Lib failed to read mot");
                mot.ReadBones(null);
                if (string.IsNullOrWhiteSpace(animationFilter) || mot.Name.Contains(animationFilter, StringComparison.OrdinalIgnoreCase)) motions.Add(mot);
                Console.WriteLine("Loaded mot " + mot.Name);
            }
        }

        var resource = new CommonMeshResource(PathUtils.GetFilepathWithoutExtensionOrVersion(meshPath).ToString(), null!)
        {
            NativeMesh = mesh,
            GameVersion = GameName.pragmata
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        resource.ExportToFile(outputPath, includeLods, includeOcc, null, motions);
        Console.WriteLine($"DONE bytes={new FileInfo(outputPath).Length}");
    }

    private static string? FindStreamingCandidate(string meshPath)
    {
        string dir = Path.GetDirectoryName(meshPath) ?? ".";
        string baseNoVersion = Path.GetFileNameWithoutExtension(meshPath);
        string normalized = meshPath.Replace('/', Path.DirectorySeparatorChar).Replace("\\", Path.DirectorySeparatorChar.ToString());
        string stmMarker = Path.DirectorySeparatorChar + "natives" + Path.DirectorySeparatorChar + "STM" + Path.DirectorySeparatorChar;
        string? stmStreaming = null;
        int stmIndex = normalized.IndexOf(stmMarker, StringComparison.OrdinalIgnoreCase);
        if (stmIndex >= 0)
        {
            string root = normalized[..(stmIndex + stmMarker.Length)];
            string relative = normalized[(stmIndex + stmMarker.Length)..];
            stmStreaming = Path.Combine(root, "streaming", relative);
        }

        string first = Path.Combine(dir, baseNoVersion + "streaming");
        string?[] candidates = [stmStreaming, meshPath + ".streaming", meshPath + ".meshstreaming", first, first + ".meshstreaming", Path.ChangeExtension(meshPath, ".meshstreaming")];
        return candidates.Where(c => !string.IsNullOrWhiteSpace(c)).FirstOrDefault(File.Exists);
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
}
