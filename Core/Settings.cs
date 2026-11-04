using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GhostReader
{
    /// <summary>持久化配置。存放于 %APPDATA%\GhostReader\settings.json</summary>
    internal sealed class Settings
    {
        public const int BgKeyTransparent = 0;   // 完全无背景（色键透明）
        public const int BgSolid = 1;           // 半透明纯色底
        public const int BgGhost = 2;           // 近乎无痕（近黑底 + 极低不透明度）

        public double Opacity = 1.0;          // 0.05 ~ 1.0
        public string FontFamily = "Microsoft YaHei UI";
        public double FontSize = 14;
        public int TextColor = unchecked((int)0xFFE8E8E8);
        public int BackColor = unchecked((int)0xFF141414);
        public int BackgroundMode = BgSolid;
        public double LineSpacing = 1.55;
        public int Padding = 14;

        public bool AlwaysOnTop = true;
        public bool WheelWhenClickThrough = true;
        public bool ClickThrough = false;       // 鼠标穿透
        public bool ShowInTaskbar = false;
        public bool TaskbarMode = true;         // 常驻底部任务栏
        public bool TaskbarEmbed = false;        // true=真正嵌入任务栏 HWND；false=浮层覆盖
        public int TaskbarWidth = 420;
        public int TaskbarHeight = 40;
        public bool TaskbarCustomSize = false;
        public bool AutoHideOnLeave = false;    // 鼠标移出自动隐藏
        public bool HideTrayIcon = false;       // 完全无痕：连托盘图标都不显示
        public bool StartHidden = false;

        public string HotkeyToggle = "Ctrl+Alt+M";
        public string HotkeyNext = "Ctrl+Alt+Down";
        public string HotkeyPrev = "Ctrl+Alt+Up";
        public string HotkeyExit = "Ctrl+Alt+Shift+X";

        public double BackgroundOpacity = 0.35;
        public double TextOpacity = 0.85;
        public double TaskbarBackgroundOpacity = 0.0;
        public double TaskbarTextOpacity = 0.65;
        public string TaskbarFontFamily = "Microsoft YaHei UI";
        public double TaskbarFontSize = 11;
        public int TaskbarTextColor = unchecked((int)0xFFE8E8E8);
        public int TaskbarBackColor = unchecked((int)0xFF141414);
        public int TaskbarOffset = 80;
        public bool TaskbarAutoDock = true;       // 自动停到任务栏空闲段，不压开始按钮/图标
        public bool TaskbarFreePosition = false;
        public int TaskbarX = 80, TaskbarY = 120;
        public bool AutoScrollEnabled = false;
        public double AutoScrollPixelsPerSecond = 12;
        // +1: text moves up (read forward); -1: text moves down (read backward).
        public int AutoScrollDirection = 1;
        public string HotkeyAutoScroll = "Ctrl+Alt+A";
        public bool DesktopMode = true;
        public bool ShowStatus = false;
        public string HotkeySettings = "Ctrl+Alt+S";
        public string HotkeyThrough = "Ctrl+Alt+T";
        public string LastBook = "";
        public int WinX = 120, WinY = 120, WinW = 560, WinH = 440;

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string ConfigDir
        {
            get
            {
                string dir = Environment.GetEnvironmentVariable("GHOSTREADER_PROFILE");
                if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GhostReader");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SettingsPath { get { return Path.Combine(ConfigDir, "settings.json"); } }
        public static string ProgressPath { get { return Path.Combine(ConfigDir, "progress.json"); } }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), JsonOpts);
                    if (s != null) { s.Normalize(); return s; }
                }
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Normalize();
                AtomicWrite(SettingsPath, JsonSerializer.Serialize(this, JsonOpts));
            }
            catch { }
        }

        public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, JsonOpts), JsonOpts);
        public void CopyFrom(Settings source)
        {
            foreach (var f in typeof(Settings).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                f.SetValue(this, f.GetValue(source));
            Normalize();
        }
        public void Normalize()
        {
            Opacity = Clamp(Opacity, 0, 1, 1);
            BackgroundOpacity = Clamp(BackgroundOpacity, 0, 1, .35);
            TextOpacity = Clamp(TextOpacity, 0, 1, .85);
            TaskbarBackgroundOpacity = Clamp(TaskbarBackgroundOpacity, 0, 1, 0);
            TaskbarTextOpacity = Clamp(TaskbarTextOpacity, 0, 1, .65);
            FontSize = Clamp(FontSize, 6, 48, 14);
            TaskbarFontSize = Clamp(TaskbarFontSize, 6, 32, 11);
            LineSpacing = Clamp(LineSpacing, 1, 2.5, 1.55);
            Padding = Math.Clamp(Padding, 0, 60);
            var display=System.Windows.Forms.SystemInformation.VirtualScreen;
            int maximumWidth=Math.Max(160,Math.Min(10000,display.Width)),maximumHeight=Math.Max(60,Math.Min(10000,display.Height));
            TaskbarWidth = Math.Clamp(TaskbarWidth, 100, maximumWidth);
            TaskbarHeight = Math.Clamp(TaskbarHeight, 20, maximumHeight);
            TaskbarOffset = Math.Clamp(TaskbarOffset, 0, 10000);
            WinW = Math.Clamp(WinW, 160, maximumWidth); WinH = Math.Clamp(WinH, 60, maximumHeight);
            if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = "Microsoft YaHei UI";
            if (string.IsNullOrWhiteSpace(TaskbarFontFamily)) TaskbarFontFamily = FontFamily;
            AutoScrollPixelsPerSecond = Clamp(AutoScrollPixelsPerSecond, 1, 120, 12);
            AutoScrollDirection = AutoScrollDirection < 0 ? -1 : 1;
            TaskbarX = Math.Clamp(TaskbarX, -100000, 100000);
            TaskbarY = Math.Clamp(TaskbarY, -100000, 100000);
            TaskbarEmbed = false;
        }
        private static double Clamp(double x, double min, double max, double fallback)
            => double.IsFinite(x) ? Math.Clamp(x, min, max) : fallback;

        public static void AtomicWrite(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, content, new System.Text.UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        public ReaderAppearance Appearance(bool compact)
        {
            return new ReaderAppearance
            {
                FontFamily = compact ? TaskbarFontFamily : FontFamily,
                FontSize = compact ? TaskbarFontSize : FontSize,
                TextColor = Color.FromArgb(compact ? TaskbarTextColor : TextColor),
                BackgroundColor = Color.FromArgb(compact ? TaskbarBackColor : BackColor),
                TextOpacity = compact ? TaskbarTextOpacity : TextOpacity,
                BackgroundOpacity = compact ? TaskbarBackgroundOpacity : BackgroundOpacity,
                Padding = compact ? 6 : Padding,
                LineSpacing = compact ? 1 : LineSpacing
            };
        }

        [JsonIgnore]
        public Color TextCol { get { return Color.FromArgb(TextColor); } }
        [JsonIgnore]
        public Color BackCol { get { return Color.FromArgb(BackColor); } }
    }
    internal sealed class ReaderAppearance
    {
        public string FontFamily;
        public double FontSize, TextOpacity, BackgroundOpacity, LineSpacing;
        [JsonIgnore]
        public Color TextColor, BackgroundColor;
        public int Padding;
        public Font CreateFont()
        {
            try { return new Font(FontFamily, (float)FontSize, GraphicsUnit.Point); }
            catch { return new Font("Microsoft YaHei UI", (float)FontSize, GraphicsUnit.Point); }
        }
        public Color TextBrushColor => Color.FromArgb((int)Math.Round(TextOpacity * 255), TextColor);
        public Color BackgroundBrushColor => Color.FromArgb((int)Math.Round(BackgroundOpacity * 255), BackgroundColor);
    }
}
