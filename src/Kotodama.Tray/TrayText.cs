using System.Resources;

namespace Kotodama.Tray;

internal static class TrayText
{
    private static readonly ResourceManager Resources = new("Kotodama.Tray.TrayStrings", typeof(TrayText).Assembly);

    internal static string Get(string name) => Resources.GetString(name) ?? name;
}
