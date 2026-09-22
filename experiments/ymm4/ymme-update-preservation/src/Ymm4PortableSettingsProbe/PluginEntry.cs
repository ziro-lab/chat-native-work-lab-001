using System.Globalization;
using YukkuriMovieMaker.Plugin;

namespace Ymm4PortableSettingsProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — Portable Settings Probe";
    public void SetCulture(CultureInfo cultureInfo) { }
}
