using ReeLib;

internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Length == 0 || HasFlag(args, "--help"))
        {
            Console.WriteLine("REE-Content-Exporter PoC");
            Console.WriteLine("Usage:");
            Console.WriteLine("  REE-Content-Exporter --mesh <mesh.path> [--motlist <motlist.path>|--mot <mot.path>] --output <file.fbx|file.glb> [--animation-name <contains>] [--no-animations] [--include-lods]");
            return;
        }

        string meshPath = GetArg(args, "--mesh") ?? throw new ArgumentException("Missing --mesh");
        string? motlistPath = GetArg(args, "--motlist");
        string? motPath = GetArg(args, "--mot");
        string outputPath = GetArg(args, "--output") ?? throw new ArgumentException("Missing --output");
        string? animationFilter = GetArg(args, "--animation-name");
        bool includeAnimations = !HasFlag(args, "--no-animations");
        bool includeLods = HasFlag(args, "--include-lods");

        Console.WriteLine("Mesh: " + meshPath);
        Console.WriteLine("Motlist: " + (motlistPath ?? "-"));
        Console.WriteLine("Mot: " + (motPath ?? "-"));
        Console.WriteLine("Output: " + outputPath);

        using FileHandler meshHandler = new FileHandler(meshPath);
        MeshFile mesh = new MeshFile(meshHandler);
        mesh.Read();
        Console.WriteLine($"Loaded mesh version={mesh.Header.version} materials={mesh.MaterialNames.Count} bones={mesh.BoneData?.Bones.Count ?? 0} lods={mesh.MeshData?.LODs.Count ?? 0}");

        List<MotFile> motions = new();
        if (includeAnimations)
        {
            if (!string.IsNullOrEmpty(motlistPath))
            {
                using FileHandler motlistHandler = new FileHandler(motlistPath);
                MotlistFile motlist = new MotlistFile(motlistHandler);
                motlist.Read();
                motions.AddRange(motlist.MotFiles.OfType<MotFile>());
                Console.WriteLine($"Loaded motlist {motlist.Name}: motFiles={motlist.MotFiles.Count} motions={motlist.Motions.Count}");
            }

            if (!string.IsNullOrEmpty(motPath))
            {
                using FileHandler motHandler = new FileHandler(motPath);
                MotFile mot = new MotFile(motHandler);
                mot.Read();
                mot.ReadBones(null);
                motions.Add(mot);
                Console.WriteLine("Loaded mot " + mot.Name);
            }

            if (!string.IsNullOrEmpty(animationFilter))
            {
                motions = motions.Where(m => m.Name.Contains(animationFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }

        Console.WriteLine($"Exporting animations={motions.Count}");
        Exporter.Export(mesh, motions, outputPath, includeLods);
        Console.WriteLine("DONE");
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
