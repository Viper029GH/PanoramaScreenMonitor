using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace PanoramaScreenMonitor;

internal enum InterfaceTheme { System, Dark, Light }

internal static class WindowTheme
{
    private static readonly (string Name, string Dark, string Light)[] Colors =
    [
        ("Window", "#14161A", "#ECF1F7"),
        ("Title", "#1A1D22", "#F7FAFE"),
        ("Card", "#1C1F25", "#FFFFFF"),
        ("Line", "#2C303A", "#D0DBE7"),
        ("Input", "#12141A", "#F6F9FD"),
        ("Foreground", "#E7E9EE", "#202936"),
        ("Muted", "#A3ABBA", "#516379"),
        ("Accent", "#8CCBFF", "#174E7D"),
        ("Danger", "#FFB397", "#9E3320"),
        ("Tile", "#262D37", "#E8EFF7"),
        ("Badge", "#253646", "#DBECFB"),
        ("Button", "#252A33", "#EEF3F9"),
        ("Border", "#3A424F", "#AABBCD"),
        ("Hover", "#374450", "#DFEAF7"),
        ("Pressed", "#202630", "#CCDFF0"),
        ("Primary", "#2A4052", "#DCECFB"),
        ("PrimaryLine", "#405B72", "#8BABCA"),
        ("InputLine", "#687687", "#8598AB")
    ];

    internal static bool Apply(ResourceDictionary resources, InterfaceTheme theme)
    {
        bool light = theme == InterfaceTheme.Light || (theme == InterfaceTheme.System && SystemPrefersLight());
        foreach (var (name, darkValue, lightValue) in Colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? lightValue : darkValue));
            brush.Freeze();
            resources["Theme" + name] = brush;
        }
        return light;
    }

    private static bool SystemPrefersLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme", 1) is not 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    internal static Forms.ToolStripRenderer TrayRenderer(bool light) =>
        new Forms.ToolStripProfessionalRenderer(new TrayColors(light)) { RoundedEdges = false };

    private sealed class TrayColors(bool light) : Forms.ProfessionalColorTable
    {
        private Drawing.Color Background => Drawing.ColorTranslator.FromHtml(light ? "#FFFFFF" : "#1C1F25");
        private Drawing.Color Highlight => Drawing.ColorTranslator.FromHtml(light ? "#DFEAF7" : "#374450");
        private Drawing.Color Line => Drawing.ColorTranslator.FromHtml(light ? "#D0DBE7" : "#3A424F");
        public override Drawing.Color ToolStripDropDownBackground => Background;
        public override Drawing.Color ImageMarginGradientBegin => Background;
        public override Drawing.Color ImageMarginGradientMiddle => Background;
        public override Drawing.Color ImageMarginGradientEnd => Background;
        public override Drawing.Color MenuBorder => Line;
        public override Drawing.Color MenuItemBorder => Highlight;
        public override Drawing.Color MenuItemSelected => Highlight;
        public override Drawing.Color SeparatorDark => Line;
        public override Drawing.Color SeparatorLight => Background;
    }
}
