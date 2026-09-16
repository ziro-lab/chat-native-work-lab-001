using System.Globalization;
using System.Text;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.FileSource.FFmpeg;

namespace FfmpegHostProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — FFmpeg Locator Host Probe";

    public void SetCulture(CultureInfo cultureInfo)
    {
        var marker = Environment.GetEnvironmentVariable("CNWL_FFMPEG_LOCATOR_MARKER");
        if (string.IsNullOrWhiteSpace(marker)) return;

        var ffmpegDirectory = FFmpegResourceLocator.GetFFmpegDirectory();
        var ffmpegDllDirectory = FFmpegResourceLocator.GetFFmpegDllDirectory();
        var ffmpegExe = FFmpegResourceLocator.GetFFmpegExePath();
        var userDirectory = FFmpegResourceLocator.GetUserFFmpegDirectory();
        var ffprobe = Path.Combine(ffmpegDirectory, "ffprobe.exe");

        var body = new StringBuilder()
            .AppendLine("status=PASS_HOST_LOCATOR")
            .Append("base_directory=").AppendLine(AppContext.BaseDirectory)
            .Append("ffmpeg_directory=").AppendLine(ffmpegDirectory)
            .Append("ffmpeg_directory_exists=").AppendLine(Directory.Exists(ffmpegDirectory).ToString())
            .Append("ffmpeg_dll_directory=").AppendLine(ffmpegDllDirectory)
            .Append("ffmpeg_dll_directory_exists=").AppendLine(Directory.Exists(ffmpegDllDirectory).ToString())
            .Append("ffmpeg_exe=").AppendLine(ffmpegExe)
            .Append("ffmpeg_exe_exists=").AppendLine(File.Exists(ffmpegExe).ToString())
            .Append("user_ffmpeg_directory=").AppendLine(userDirectory)
            .Append("user_ffmpeg_directory_exists=").AppendLine(Directory.Exists(userDirectory).ToString())
            .Append("derived_ffprobe=").AppendLine(ffprobe)
            .Append("derived_ffprobe_exists=").AppendLine(File.Exists(ffprobe).ToString())
            .ToString();

        var fullPath = Path.GetFullPath(marker);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, body, new UTF8Encoding(false));
    }
}
