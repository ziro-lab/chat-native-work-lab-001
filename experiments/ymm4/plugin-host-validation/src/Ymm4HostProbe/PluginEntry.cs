using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using YukkuriMovieMaker.Plugin;

namespace Ymm4HostProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — YMM4 Host Probe";

    public void SetCulture(CultureInfo cultureInfo)
    {
        var markerPath = Environment.GetEnvironmentVariable("CNWL_YMM4_PROBE_MARKER");
        if (string.IsNullOrWhiteSpace(markerPath))
            return;

        var assembly = typeof(PluginEntry).Assembly;
        var assemblyPath = assembly.Location;
        var sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))).ToLowerInvariant();
        var fullPath = Path.GetFullPath(markerPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var body = new StringBuilder()
            .AppendLine("Chat Native Work Lab — YMM4 Host Probe")
            .Append("culture=").AppendLine(cultureInfo.Name)
            .Append("assembly=").AppendLine(assembly.FullName)
            .Append("assembly_path=").AppendLine(assemblyPath)
            .Append("sha256=").AppendLine(sha256)
            .ToString();

        File.WriteAllText(fullPath, body, new UTF8Encoding(false));
    }
}
