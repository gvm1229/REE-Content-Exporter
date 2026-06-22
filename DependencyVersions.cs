internal static class DependencyVersions
{
    public const string RecordedAtUtc = "2026-06-22T08:05:00Z";

    public const string ExporterCommit = "f0e65cf23598b3e3eaebda3a7d6870d6ccae54e6";

    public const string ReeContentEditorCommit = "711c509affb0362c7af1e5343e8c57d32a1ad27d";
    public const string ReeContentEditorStatus = "origin/master plus exporter bridge patch";
    public const string ReeContentEditorBridgePatchSha256 = "b04c38ed8c9dccb93feb9c894c929821089735e790d32ac7720480cae007376d";

    public const string ReEngineLibCommit = "867daf1b0361a67e24bc82ef1391e01cc33d524a";
    public const string ReEngineLibStatus = "origin/master clean";

    public static void Print(TextWriter writer)
    {
        writer.WriteLine("REE-Content-Exporter dependency versions");
        writer.WriteLine($"Recorded UTC: {RecordedAtUtc}");
        writer.WriteLine($"REE-Content-Exporter: {ExporterCommit}");
        writer.WriteLine($"REE-Content-Editor: {ReeContentEditorCommit}");
        writer.WriteLine($"REE-Content-Editor status: {ReeContentEditorStatus}");
        writer.WriteLine($"REE-Content-Editor bridge patch SHA-256: {ReeContentEditorBridgePatchSha256}");
        writer.WriteLine($"RE-Engine-Lib: {ReEngineLibCommit}");
        writer.WriteLine($"RE-Engine-Lib status: {ReEngineLibStatus}");
    }
}
