// TaskbarTimer - timers, clock-time countdowns, pomodoro and alarms that live in the Windows taskbar.
// Build: run build.cmd (uses the C# compiler that ships with Windows / .NET Framework 4.x).
// Written by Claude (Anthropic AI) in Claude Code. Provided as is, without warranty - see README.md.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarTimer
{
    // ================================================================== Win32
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int CX, CY; public SIZE(int x, int y) { CX = x; CY = y; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT dst, ref SIZE size,
            IntPtr hdcSrc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("winmm.dll")] public static extern int waveOutSetVolume(IntPtr h, uint vol);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;

        public static void RoundCorners(IntPtr h) { try { int v = 2; DwmSetWindowAttribute(h, 33, ref v, 4); } catch { } }
        public static void DarkTitleBar(IntPtr h, bool dark) { try { int v = dark ? 1 : 0; DwmSetWindowAttribute(h, 20, ref v, 4); } catch { } }
    }

    // ================================================================== Theme & helpers
    static class Theme
    {
        public static float Scale = 1f;
        public static int S(float v) { return (int)Math.Round(v * Scale); }

        static int ReadDword(string key, string name, int fallback)
        {
            try { object o = Registry.GetValue(key, name, null); if (o is int) return (int)o; } catch { }
            return fallback;
        }
        const string Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        public static bool TaskbarLight { get { return ReadDword(Personalize, "SystemUsesLightTheme", 0) == 1; } }
        public static bool AppsLight { get { return ReadDword(Personalize, "AppsUseLightTheme", 0) == 1; } }
        public static Color Accent
        {
            get
            {
                int abgr = ReadDword(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", unchecked((int)0xFFD77800));
                Color c = Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                if (!AppsLight) c = Mix(c, Color.White, 0.25f);
                return c;
            }
        }
        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
        public static bool IsLightColor(Color c) { return (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) > 160; }
        public static readonly Color Alarm = Color.FromArgb(232, 72, 85);
        public static readonly Color Warn = Color.FromArgb(255, 185, 0);

        // Ten easy-to-tell-apart timer colours.
        public static readonly Color[] Palette =
        {
            Color.FromArgb(76, 166, 255),   // blue
            Color.FromArgb(255, 140, 66),   // orange
            Color.FromArgb(94, 209, 122),   // green
            Color.FromArgb(232, 107, 216),  // magenta
            Color.FromArgb(255, 214, 72),   // yellow
            Color.FromArgb(155, 123, 255),  // purple
            Color.FromArgb(63, 212, 212),   // teal
            Color.FromArgb(255, 122, 156),  // rose
            Color.FromArgb(168, 217, 63),   // lime
            Color.FromArgb(200, 155, 109),  // tan
        };
        public static bool SimilarColor(Color a, Color b)
        {
            return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 60;
        }

        public static Font UiFont(float px, bool semibold)
        {
            return new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", S(px), FontStyle.Regular, GraphicsUnit.Pixel);
        }
        static string iconFamily;
        public static Font IconFont(float px)
        {
            if (iconFamily == null)
            {
                iconFamily = "Segoe MDL2 Assets";
                foreach (var f in FontFamily.Families) if (f.Name == "Segoe Fluent Icons") { iconFamily = f.Name; break; }
            }
            return new Font(iconFamily, S(px), FontStyle.Regular, GraphicsUnit.Pixel);
        }
        public const string IcoClose = "", IcoAdd = "",
            IcoRefresh = "", IcoSettings = "", IcoDelete = "";

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            d = Math.Min(d, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
        public static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            using (var p = RoundRect(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }
        public static void Stroke(Graphics g, RectangleF r, float rad, Color c, float w)
        {
            using (var p = RoundRect(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        public static string Format(int secs)
        {
            if (secs < 0) secs = 0;
            if (secs >= 3600)
                return (secs / 3600).ToString() + ":" + (secs / 60 % 60).ToString("00") + ":" + (secs % 60).ToString("00");
            return (secs / 60).ToString() + ":" + (secs % 60).ToString("00");
        }
        public static bool Clock24 { get { return CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H"); } }
        public static string ClockTime(DateTime t)
        {
            if (Clock24) return t.ToString("H:mm");
            return t.ToString("h:mm") + (t.Hour < 12 ? " am" : " pm");
        }
        public static string DayAndTime(DateTime t)
        {
            if (t == DateTime.MaxValue) return "never";
            if (t.Date == DateTime.Today) return "today " + ClockTime(t);
            if (t.Date == DateTime.Today.AddDays(1)) return "tomorrow " + ClockTime(t);
            return t.ToString("ddd") + " " + ClockTime(t);
        }
        public static DateTime NextOccurrence(int minuteOfDay)
        {
            DateTime now = DateTime.Now;
            DateTime t = now.Date.AddMinutes(((minuteOfDay % 1440) + 1440) % 1440);
            if (t <= now) t = t.AddDays(1);
            return t;
        }
        public static int MinuteOfDay(DateTime t) { return (t.Hour * 60 + t.Minute + (t.Second > 0 || t.Millisecond > 0 ? 1 : 0)) % 1440; }
        public static string Span(double secs)
        {
            int s = (int)Math.Ceiling(secs);
            if (s >= 3600) return (s / 3600) + "h " + (s / 60 % 60).ToString("00") + "m";
            if (s >= 60) return (s / 60) + "m " + (s % 60).ToString("00") + "s";
            return s + "s";
        }
        public static string ShortDuration(int secs)
        {
            if (secs > 0 && secs % 3600 == 0) return (secs / 3600) + "h";
            if (secs % 60 == 0) return (secs / 60) + "m";
            if (secs < 60) return secs + "s";
            return (secs / 60) + "m" + (secs % 60) + "s";
        }
        // "90" "90s" "5m" "1h30m" "1:30" -> seconds. Plain numbers are minutes when plainIsMinutes.
        public static int ParseDuration(string a, bool plainIsMinutes)
        {
            a = (a ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            if (a.Length == 0) return 0;
            if (a.Contains(":"))
            {
                var p = a.Split(':'); int x, y, z;
                if (p.Length == 2 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y)) return x * 60 + y;
                if (p.Length == 3 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y) && int.TryParse(p[2], out z)) return x * 3600 + y * 60 + z;
                return 0;
            }
            int plain;
            if (int.TryParse(a, out plain)) return plainIsMinutes ? plain * 60 : plain;
            int total = 0, num = 0; bool any = false;
            foreach (char c in a)
            {
                if (char.IsDigit(c)) { num = num * 10 + (c - '0'); any = true; }
                else if (c == 'h') { total += num * 3600; num = 0; }
                else if (c == 'm') { total += num * 60; num = 0; }
                else if (c == 's') { total += num; num = 0; }
                else return 0;
            }
            return any ? total + num : 0;
        }
        public static readonly string[] DayShort = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        public static int DayBit(DateTime t) { return ((int)t.DayOfWeek + 6) % 7; } // Monday = 0
    }

    // ================================================================== Repeating alarms
    class AlarmItem
    {
        public int Minute = 7 * 60;   // minute of the day
        public int Days;              // bit 0 = Monday .. bit 6 = Sunday; 0 = once
        public bool Enabled = true;
        public string Label = "";
        public DateTime NextFire = DateTime.MaxValue;

        public void Recompute(DateTime after)
        {
            NextFire = DateTime.MaxValue;
            for (int d = 0; d <= 8; d++)
            {
                DateTime t = after.Date.AddDays(d).AddMinutes(Minute);
                if (t <= after) continue;
                if (Days == 0 || (Days & (1 << Theme.DayBit(t))) != 0) { NextFire = t; return; }
            }
        }
        public static string DaysText(int days)
        {
            if (days == 0) return "Once";
            if (days == 127) return "Every day";
            if (days == 31) return "Weekdays";
            if (days == 96) return "Weekends";
            var parts = new List<string>();
            for (int i = 0; i < 7; i++) if ((days & (1 << i)) != 0) parts.Add(Theme.DayShort[i]);
            return string.Join(", ", parts.ToArray());
        }
        public string Serialize() { return Minute + "|" + Days + "|" + (Enabled ? 1 : 0) + "|" + Label; }
        public static AlarmItem Parse(string v)
        {
            var p = v.Split(new[] { '|' }, 4);
            int m, d, e;
            if (p.Length < 3 || !int.TryParse(p[0], out m) || !int.TryParse(p[1], out d) || !int.TryParse(p[2], out e)) return null;
            return new AlarmItem { Minute = ((m % 1440) + 1440) % 1440, Days = d & 127, Enabled = e == 1, Label = p.Length > 3 ? p[3] : "" };
        }
    }

    // ================================================================== Settings
    class Settings
    {
        public int LastSeconds = 300, LastTarget = -1, LastMode = 0, LastAlarm = 7 * 60, LastAlarmDays = 31;
        public Dictionary<string, int> Offsets = new Dictionary<string, int>();
        public int HotkeyOpen = (int)(Keys.Control | Keys.Alt | Keys.T);
        public int HotkeyPause = (int)(Keys.Control | Keys.Alt | Keys.P);
        public string SoundName = "Chime", SoundPath = "";
        public int Volume = 80, RingSeconds = 60;
        public bool PlaySound = true;
        public bool WarnEnabled = true, WarnSound = true;
        public int WarnSeconds = 60;
        public bool ShowAll = true, ShowLabels = false, AllMonitors = true, OverFullscreen = false;
        public int BarSize = 1;
        public List<int> TimerPresets = new List<int> { 60, 300, 600, 900, 1800 };
        public List<int> UntilPresets = new List<int> { 900, 1800, 3600, 7200, 14400 };
        public int PomoWork = 25, PomoShort = 5, PomoLong = 15, PomoRounds = 4;
        public List<AlarmItem> Alarms = new List<AlarmItem>();

        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarTimer"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        static List<int> IntList(string v)
        {
            var list = new List<int>();
            foreach (var part in v.Split(',')) { int x; if (int.TryParse(part.Trim(), out x) && x > 0) list.Add(x); }
            return list;
        }
        static string Join(List<int> l)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(','); sb.Append(l[i]); }
            return sb.ToString();
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('='); if (eq < 0) continue;
                        string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1);
                        int iv; bool isInt = int.TryParse(v.Trim(), out iv);
                        bool b = isInt && iv != 0;
                        switch (k)
                        {
                            case "LastSeconds": if (isInt) s.LastSeconds = iv; break;
                            case "LastTarget": if (isInt) s.LastTarget = iv; break;
                            case "LastMode": if (isInt) s.LastMode = Math.Max(0, Math.Min(3, iv)); break;
                            case "LastAlarm": if (isInt) s.LastAlarm = iv; break;
                            case "LastAlarmDays": if (isInt) s.LastAlarmDays = iv & 127; break;
                            case "OverlayOffset": if (isInt && iv >= 0) s.Offsets[Screen.PrimaryScreen.DeviceName] = iv; break;
                            case "HotkeyOpen": if (isInt) s.HotkeyOpen = iv; break;
                            case "HotkeyPause": if (isInt) s.HotkeyPause = iv; break;
                            case "SoundName": s.SoundName = v.Trim(); break;
                            case "SoundPath": s.SoundPath = v.Trim(); break;
                            case "Volume": if (isInt) s.Volume = Math.Max(0, Math.Min(100, iv)); break;
                            case "RingSeconds": if (isInt) s.RingSeconds = Math.Max(0, iv); break;
                            case "PlaySound": if (isInt) s.PlaySound = b; break;
                            case "WarnEnabled": if (isInt) s.WarnEnabled = b; break;
                            case "WarnSound": if (isInt) s.WarnSound = b; break;
                            case "WarnSeconds": if (isInt) s.WarnSeconds = Math.Max(5, iv); break;
                            case "ShowAll": if (isInt) s.ShowAll = b; break;
                            case "ShowLabels": if (isInt) s.ShowLabels = b; break;
                            case "AllMonitors": if (isInt) s.AllMonitors = b; break;
                            case "OverFullscreen": if (isInt) s.OverFullscreen = b; break;
                            case "BarSize": if (isInt) s.BarSize = Math.Max(0, Math.Min(2, iv)); break;
                            case "TimerPresets": { var l = IntList(v); if (l.Count > 0) s.TimerPresets = l; } break;
                            case "UntilPresets": { var l = IntList(v); if (l.Count > 0) s.UntilPresets = l; } break;
                            case "PomoWork": if (isInt) s.PomoWork = iv; break;
                            case "PomoShort": if (isInt) s.PomoShort = iv; break;
                            case "PomoLong": if (isInt) s.PomoLong = iv; break;
                            case "PomoRounds": if (isInt) s.PomoRounds = iv; break;
                            case "Alarm": { var a = AlarmItem.Parse(v); if (a != null) s.Alarms.Add(a); } break;
                            default: if (k.StartsWith("Offset.") && isInt) s.Offsets[k.Substring(7)] = iv; break;
                        }
                    }
                }
            }
            catch { }
            foreach (var a in s.Alarms) a.Recompute(DateTime.Now);
            return s;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                Action<string, object> w = (k, v) => sb.Append(k).Append('=').Append(v).Append("\r\n");
                Func<bool, int> bi = x => x ? 1 : 0;
                w("LastSeconds", LastSeconds); w("LastTarget", LastTarget); w("LastMode", LastMode);
                w("LastAlarm", LastAlarm); w("LastAlarmDays", LastAlarmDays);
                w("HotkeyOpen", HotkeyOpen); w("HotkeyPause", HotkeyPause);
                w("SoundName", SoundName); w("SoundPath", SoundPath); w("Volume", Volume);
                w("RingSeconds", RingSeconds); w("PlaySound", bi(PlaySound));
                w("WarnEnabled", bi(WarnEnabled)); w("WarnSound", bi(WarnSound)); w("WarnSeconds", WarnSeconds);
                w("ShowAll", bi(ShowAll)); w("ShowLabels", bi(ShowLabels)); w("AllMonitors", bi(AllMonitors));
                w("OverFullscreen", bi(OverFullscreen)); w("BarSize", BarSize);
                w("TimerPresets", Join(TimerPresets)); w("UntilPresets", Join(UntilPresets));
                w("PomoWork", PomoWork); w("PomoShort", PomoShort); w("PomoLong", PomoLong); w("PomoRounds", PomoRounds);
                foreach (var kv in Offsets) w("Offset." + kv.Key, kv.Value);
                foreach (var a in Alarms) w("Alarm", a.Serialize());
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }
    }

    // ================================================================== Timers
    enum TimerKind { Duration, Until, Pomodoro, Alarm }
    enum TimerState { Running, Paused, Finished }

    class TimerItem
    {
        public int Id;
        public TimerKind Kind;
        public string Label = "";
        public Color Color;
        public TimerState State;
        public int TotalSeconds, OriginalSeconds;
        public DateTime EndUtc, TargetLocal, FinishedAt;
        public double PausedRemaining;
        public bool Warned, Ringing;
        public int PomoRound = 1, PomoWork = 25, PomoShort = 5, PomoLongMin = 15, PomoRounds = 4;
        public bool PomoBreak, PomoLong;

        public double Remaining
        {
            get
            {
                if (State == TimerState.Running) return Math.Max(0, (EndUtc - DateTime.UtcNow).TotalSeconds);
                if (State == TimerState.Paused) return PausedRemaining;
                return 0;
            }
        }
        public int DisplaySeconds { get { return (int)Math.Ceiling(Remaining - 0.0001); } }
        public float Fraction { get { return TotalSeconds <= 0 ? 0f : (float)Math.Min(1.0, Remaining / TotalSeconds); } }
        public bool Pausable { get { return Kind == TimerKind.Duration || Kind == TimerKind.Pomodoro; } }

        public void Run(int secs)
        {
            secs = Math.Max(1, secs);
            TotalSeconds = secs;
            EndUtc = DateTime.UtcNow.AddSeconds(secs);
            State = TimerState.Running;
            Warned = false; Ringing = false;
        }
        public void RunUntil(DateTime t)
        {
            TargetLocal = t;
            TotalSeconds = (int)Math.Ceiling(Math.Max(1, (t - DateTime.Now).TotalSeconds));
            EndUtc = t.ToUniversalTime();
            State = TimerState.Running;
            Warned = false; Ringing = false;
        }
        public void Pause() { if (State == TimerState.Running && Pausable) { PausedRemaining = Remaining; State = TimerState.Paused; } }
        public void Resume() { if (State == TimerState.Paused) { EndUtc = DateTime.UtcNow.AddSeconds(PausedRemaining); State = TimerState.Running; } }
        public void Adjust(int delta)
        {
            if (State == TimerState.Finished) return;
            double rem = Math.Max(1, Remaining + delta);
            if (State == TimerState.Running) EndUtc = DateTime.UtcNow.AddSeconds(rem); else PausedRemaining = rem;
            if (Kind == TimerKind.Until || Kind == TimerKind.Alarm) TargetLocal = DateTime.Now.AddSeconds(rem);
            TotalSeconds = Math.Max(TotalSeconds, (int)Math.Ceiling(rem));
            if (delta > 0) Warned = false;
        }

        public string Title
        {
            get
            {
                if (Label.Length > 0) return Label;
                switch (Kind)
                {
                    case TimerKind.Duration: return Theme.Format(OriginalSeconds > 0 ? OriginalSeconds : TotalSeconds) + " timer";
                    case TimerKind.Until: return "Until " + Theme.ClockTime(TargetLocal);
                    case TimerKind.Pomodoro: return "Pomodoro";
                    default: return "Alarm " + Theme.ClockTime(TargetLocal);
                }
            }
        }
        public string Detail
        {
            get
            {
                if (State == TimerState.Finished)
                {
                    if (Kind == TimerKind.Pomodoro) return "Cycle complete";
                    if (Kind == TimerKind.Duration) return "Time's up";
                    return "It's " + Theme.ClockTime(TargetLocal);
                }
                string paused = State == TimerState.Paused ? " · paused" : "";
                if (Kind == TimerKind.Pomodoro)
                    return (PomoBreak ? (PomoLong ? "Long break" : "Break") : "Focus") + " · round " + PomoRound + " of " + PomoRounds + paused;
                if (Kind == TimerKind.Until || Kind == TimerKind.Alarm) return "Rings at " + Theme.ClockTime(TargetLocal);
                if (State == TimerState.Paused) return "Paused";
                return "Ends at " + Theme.ClockTime(DateTime.Now.AddSeconds(Remaining));
            }
        }
    }

    class TimerManager
    {
        public const int MaxTimers = 10;
        public readonly List<TimerItem> Items = new List<TimerItem>();
        readonly Settings settings;
        readonly Random rng = new Random();
        int nextId = 1;

        public event EventHandler Changed;
        public event Action<TimerItem> ItemFinished, ItemWarning, PhaseChanged;

        public TimerManager(Settings s) { settings = s; }
        public void RaiseChanged() { if (Changed != null) Changed(this, EventArgs.Empty); }
        public bool CanAdd { get { return Items.Count < MaxTimers; } }

        public bool ColorInUse(Color c, TimerItem except)
        {
            foreach (var it in Items) if (it != except && Theme.SimilarColor(it.Color, c)) return true;
            return false;
        }
        // A random colour that no running timer is using.
        public Color PickColor()
        {
            var free = new List<Color>();
            foreach (var c in Theme.Palette) if (!ColorInUse(c, null)) free.Add(c);
            if (free.Count > 0) return free[rng.Next(free.Count)];
            for (int i = 0; i < 200; i++)
            {
                var c = Color.FromArgb(60 + rng.Next(196), 60 + rng.Next(196), 60 + rng.Next(196));
                if (!ColorInUse(c, null)) return c;
            }
            return Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256));
        }

        public TimerItem Add(TimerKind kind, string label, Color color)
        {
            if (!CanAdd) return null;
            if (color.IsEmpty || ColorInUse(color, null)) color = PickColor();
            var it = new TimerItem { Id = nextId++, Kind = kind, Label = (label ?? "").Trim(), Color = color };
            Items.Add(it);
            return it;
        }
        public void Remove(TimerItem it) { Items.Remove(it); RaiseChanged(); }

        // Finished first (oldest first), then by time left.
        public List<TimerItem> Ordered()
        {
            var list = new List<TimerItem>(Items);
            list.Sort((a, b) =>
            {
                bool fa = a.State == TimerState.Finished, fb = b.State == TimerState.Finished;
                if (fa != fb) return fa ? -1 : 1;
                if (fa) return a.FinishedAt.CompareTo(b.FinishedAt);
                int c = a.Remaining.CompareTo(b.Remaining);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            return list;
        }

        public bool InWarning(TimerItem it)
        {
            return settings.WarnEnabled && it.Kind != TimerKind.Pomodoro && it.State != TimerState.Finished
                && it.TotalSeconds > settings.WarnSeconds + 5 && it.Remaining <= settings.WarnSeconds;
        }

        public void Tick()
        {
            bool changed = false;
            DateTime now = DateTime.Now;
            foreach (var it in Items.ToArray())
            {
                if (it.State != TimerState.Running) continue;
                if (!it.Warned && InWarning(it))
                {
                    it.Warned = true;
                    if (ItemWarning != null) ItemWarning(it);
                }
                if (it.Remaining > 0) continue;
                if (it.Kind == TimerKind.Pomodoro && AdvancePomodoro(it))
                {
                    if (PhaseChanged != null) PhaseChanged(it);
                }
                else
                {
                    it.State = TimerState.Finished; it.FinishedAt = now; it.Ringing = true;
                    if (ItemFinished != null) ItemFinished(it);
                }
                changed = true;
            }

            bool alarmsChanged = false;
            foreach (var a in settings.Alarms)
            {
                if (!a.Enabled || now < a.NextFire) continue;
                DateTime fired = a.NextFire;
                if (a.Days == 0) a.Enabled = false;
                a.Recompute(now);
                alarmsChanged = true;
                if ((now - fired).TotalMinutes > 5) continue;   // computer was asleep: don't ring hours late
                if (!CanAdd)
                {   // make room by clearing the oldest finished timer
                    foreach (var old in Ordered()) if (old.State == TimerState.Finished) { Items.Remove(old); break; }
                }
                var it = Add(TimerKind.Alarm, a.Label, Color.Empty);
                if (it == null) continue;
                it.TargetLocal = fired; it.TotalSeconds = 1;
                it.State = TimerState.Finished; it.FinishedAt = now; it.Ringing = true;
                if (ItemFinished != null) ItemFinished(it);
                changed = true;
            }
            if (alarmsChanged) settings.Save();
            if (changed) RaiseChanged();
        }

        static bool AdvancePomodoro(TimerItem it)
        {
            if (!it.PomoBreak)
            {
                it.PomoBreak = true;
                it.PomoLong = it.PomoRound >= it.PomoRounds;
                it.Run((it.PomoLong ? it.PomoLongMin : it.PomoShort) * 60);
                return true;
            }
            if (it.PomoLong) return false;    // long break over: cycle complete
            it.PomoBreak = false;
            it.PomoRound++;
            it.Run(it.PomoWork * 60);
            return true;
        }
    }

    // ================================================================== Sounds (generated, or a custom .wav)
    class WavBuilder
    {
        const int Rate = 44100;
        readonly List<short> data = new List<short>();
        public void Tone(double freq, double dur, double amp, double decay, params double[] harmonics)
        {
            int n = (int)(Rate * dur), fade = (int)(Rate * 0.008);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double env = Math.Min(1.0, Math.Min(i / (double)fade, (n - i) / (double)fade));
                if (decay > 0) env *= Math.Exp(-decay * t);
                double v = Math.Sin(2 * Math.PI * freq * t);
                for (int h = 0; h + 1 < harmonics.Length; h += 2) v += harmonics[h + 1] * Math.Sin(2 * Math.PI * freq * harmonics[h] * t);
                data.Add((short)Math.Max(short.MinValue, Math.Min(short.MaxValue, v * env * amp * short.MaxValue)));
            }
        }
        public void Rest(double dur) { int n = (int)(Rate * dur); for (int i = 0; i < n; i++) data.Add(0); }
        public MemoryStream Build()
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            int len = data.Count * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + len);
            w.Write(new[] { 'W', 'A', 'V', 'E' }); w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(len);
            foreach (var s in data) w.Write(s);
            w.Flush(); ms.Position = 0;
            return ms;
        }
    }

    class SoundManager
    {
        public static readonly string[] Names = { "Chime", "Digital beeps", "Bell", "Gentle rise", "Custom file" };
        readonly Settings settings;
        SoundPlayer player, warnPlayer;
        string loadedKey;
        public bool Looping { get; private set; }

        public SoundManager(Settings s) { settings = s; }

        static MemoryStream Make(string name)
        {
            var w = new WavBuilder();
            switch (name)
            {
                case "Digital beeps":
                    for (int i = 0; i < 4; i++) { w.Tone(2093, 0.08, 0.32, 0, 3, 0.25, 5, 0.1); w.Rest(0.07); }
                    w.Rest(0.6); break;
                case "Bell":
                    w.Tone(880, 1.6, 0.42, 2.6, 2.0, 0.5, 3.01, 0.25, 4.2, 0.15); w.Rest(0.3); break;
                case "Gentle rise":
                    w.Tone(523.25, 0.28, 0.3, 1.5, 2, 0.1); w.Tone(659.25, 0.28, 0.3, 1.5, 2, 0.1);
                    w.Tone(783.99, 0.5, 0.3, 1.2, 2, 0.1); w.Rest(0.9); break;
                default: // Chime
                    for (int k = 0; k < 2; k++) { w.Tone(1046.5, 0.11, 0.3, 0, 2, 0.25); w.Rest(0.07); w.Tone(1318.5, 0.11, 0.3, 0, 2, 0.25); w.Rest(0.07); }
                    w.Tone(1568, 0.22, 0.3, 0, 2, 0.25); w.Rest(0.9); break;
            }
            return w.Build();
        }
        static MemoryStream MakeWarning()
        {
            var w = new WavBuilder();
            w.Tone(783.99, 0.13, 0.22, 4, 2, 0.15); w.Tone(1046.5, 0.22, 0.22, 4, 2, 0.15);
            return w.Build();
        }

        SoundPlayer Get(string name, string path)
        {
            string key = name + "|" + path;
            if (player == null || key != loadedKey)
            {
                Stop();
                if (player != null) player.Dispose();
                if (name == "Custom file" && File.Exists(path)) player = new SoundPlayer(path);
                else player = new SoundPlayer(Make(name == "Custom file" ? "Chime" : name));
                try { player.Load(); } catch { player = new SoundPlayer(Make("Chime")); }
                loadedKey = key;
            }
            return player;
        }
        static void ApplyVolume(int vol)
        {
            uint v = (uint)(Math.Max(0, Math.Min(100, vol)) * 0xFFFF / 100);
            try { Native.waveOutSetVolume(IntPtr.Zero, v | (v << 16)); } catch { }
        }

        public void StartAlarm()
        {
            if (!settings.PlaySound) return;
            ApplyVolume(settings.Volume);
            try { Get(settings.SoundName, settings.SoundPath).PlayLooping(); Looping = true; }
            catch { SystemSounds.Exclamation.Play(); }
        }
        public void PlayOnce() { PlayOnce(settings.SoundName, settings.SoundPath, settings.Volume, false); }
        public void PlayOnce(string name, string path, int volume, bool force)
        {
            if (!force && !settings.PlaySound) return;
            if (Looping) return;
            ApplyVolume(volume);
            try { Get(name, path).Play(); } catch { }
        }
        public void PlayWarning()
        {
            if (Looping || !settings.WarnSound) return;
            ApplyVolume(settings.Volume);
            try { if (warnPlayer == null) { warnPlayer = new SoundPlayer(MakeWarning()); warnPlayer.Load(); } warnPlayer.Play(); } catch { }
        }
        public void Stop()
        {
            if (player != null) { try { player.Stop(); } catch { } }
            Looping = false;
        }
    }

    // ================================================================== Global hotkeys
    class HotkeyWindow : NativeWindow, IDisposable
    {
        public event Action<int> Pressed;
        readonly List<int> ids = new List<int>();
        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public bool Register(int id, int keys)
        {
            if (keys == 0) return true;
            Keys k = (Keys)keys;
            uint mods = 0x4000; // MOD_NOREPEAT
            if ((k & Keys.Alt) != 0) mods |= 1;
            if ((k & Keys.Control) != 0) mods |= 2;
            if ((k & Keys.Shift) != 0) mods |= 4;
            bool ok = Native.RegisterHotKey(Handle, id, mods, (uint)(k & Keys.KeyCode));
            if (ok) ids.Add(id);
            return ok;
        }
        public void UnregisterAll() { foreach (var id in ids) Native.UnregisterHotKey(Handle, id); ids.Clear(); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && Pressed != null) Pressed((int)m.WParam);
            base.WndProc(ref m);
        }
        public void Dispose() { UnregisterAll(); DestroyHandle(); }

        public static string Text(int keys)
        {
            if (keys == 0) return "None";
            Keys k = (Keys)keys;
            var parts = new List<string>();
            if ((k & Keys.Control) != 0) parts.Add("Ctrl");
            if ((k & Keys.Alt) != 0) parts.Add("Alt");
            if ((k & Keys.Shift) != 0) parts.Add("Shift");
            Keys code = k & Keys.KeyCode;
            string name = code.ToString();
            if (code >= Keys.D0 && code <= Keys.D9) name = ((int)(code - Keys.D0)).ToString();
            parts.Add(name);
            return string.Join("+", parts.ToArray());
        }
    }

    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "TaskbarTimer";
        public static bool Enabled
        {
            get { try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunName) != null; } catch { return false; } }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    {
                        if (value) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                        else k.DeleteValue(RunName, false);
                    }
                }
                catch { }
            }
        }
    }

    // ================================================================== Taskbar overlay: a row of countdown "pills" on one taskbar
    class TaskbarOverlay : Form
    {
        readonly TimerManager mgr;
        readonly Settings settings;
        public readonly IntPtr Taskbar;
        readonly bool primary;
        public event Action<TimerItem> PillClicked;
        public event EventHandler RightClicked;

        readonly List<KeyValuePair<TimerItem, Rectangle>> pills = new List<KeyValuePair<TimerItem, Rectangle>>();
        Rectangle bounds;
        bool flashOn, dragging, mouseDown;
        Point downScreen; int downRight, dragRight;
        TimerItem hoverItem;
        readonly ToolTip tip = new ToolTip();
        static readonly Bitmap measureBmp = new Bitmap(1, 1);

        static int S(float v) { return Theme.S(v); }

        public TaskbarOverlay(TimerManager m, Settings s, IntPtr taskbar, bool isPrimary)
        {
            mgr = m; settings = s; Taskbar = taskbar; primary = isPrimary;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Cursor = Cursors.Hand;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        Rectangle TaskbarRect()
        {
            Native.RECT r;
            if (Native.GetWindowRect(Taskbar, out r)) return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return Rectangle.Empty;
        }
        string ScreenKey { get { return Screen.FromHandle(Taskbar).DeviceName; } }
        int AutoRight(Rectangle tb)
        {
            if (primary)
            {
                IntPtr n = Native.FindWindowEx(Taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                Native.RECT r;
                if (n != IntPtr.Zero && Native.GetWindowRect(n, out r) && r.Right > r.Left && r.Left > tb.Left && r.Left < tb.Right)
                    return r.Left - S(6);
                return tb.Right - S(330);
            }
            return tb.Right - S(130);   // secondary taskbars: leave room for the clock
        }
        static bool FullscreenOn(Rectangle tb)
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            var sb = new StringBuilder(64);
            Native.GetClassName(fg, sb, 64);
            string cls = sb.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return false;
            Native.RECT r;
            if (!Native.GetWindowRect(fg, out r)) return false;
            Rectangle scr = Screen.FromRectangle(tb).Bounds;
            return r.Left <= scr.Left && r.Top <= scr.Top && r.Right >= scr.Right && r.Bottom >= scr.Bottom;
        }

        float TextPx { get { return settings.BarSize == 0 ? 13 : settings.BarSize == 2 ? 17.5f : 15; } }
        bool ShowLabel(TimerItem it) { return settings.ShowLabels && it.Label.Length > 0; }
        static string LabelText(TimerItem it) { var l = it.Label; return l.Length > 14 ? l.Substring(0, 13) + "…" : l; }
        static string TimeText(TimerItem it) { return it.State == TimerState.Finished ? "Done" : Theme.Format(it.DisplaySeconds); }
        static string Template(string s)
        {   // same width as s, so pills don't jiggle as digits change
            var c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++) if (char.IsDigit(c[i])) c[i] = '0';
            return new string(c);
        }
        static StringFormat TextFormat()
        {
            var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
            sf.LineAlignment = StringAlignment.Center;
            sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            return sf;
        }
        static bool HasGlyph(TimerItem it) { return it.State == TimerState.Paused || it.Kind == TimerKind.Pomodoro || it.Kind == TimerKind.Alarm; }

        int PillWidth(TimerItem it, Font f, Font lf, Graphics g, StringFormat sf)
        {
            float w = g.MeasureString(Template(TimeText(it)), f, PointF.Empty, sf).Width;
            if (ShowLabel(it)) w += g.MeasureString(LabelText(it), lf, PointF.Empty, sf).Width + S(5);
            if (HasGlyph(it)) w += S(11);
            return Math.Max(S(54 + settings.BarSize * 6), (int)Math.Ceiling(w) + S(22));
        }

        List<TimerItem> ShownItems()
        {
            var all = mgr.Ordered();
            if (!settings.ShowAll && all.Count > 1) all = all.GetRange(0, 1);
            return all;
        }

        public void UpdateView(bool flash)
        {
            flashOn = flash;
            var items = ShownItems();
            Rectangle tb = TaskbarRect();
            bool show = items.Count > 0 && tb.Width > tb.Height && (settings.OverFullscreen || !FullscreenOn(tb));
            if (!show) { if (Visible) Hide(); pills.Clear(); hoverItem = null; return; }
            DoLayout(items, tb);
            if (!Visible) Show();
            Render();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        void DoLayout(List<TimerItem> items, Rectangle tb)
        {
            int h = Math.Max(S(24), Math.Min(tb.Height - S(8), S(46)));
            int gap = S(4);
            var widths = new List<int>();
            using (var g = Graphics.FromImage(measureBmp))
            using (var f = new Font("Segoe UI Semibold", S(TextPx), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var lf = new Font("Segoe UI", S(TextPx - 2), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var sf = TextFormat())
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                foreach (var it in items) widths.Add(PillWidth(it, f, lf, g, sf));
            }
            int total = 0;
            foreach (var w in widths) total += w;
            total += gap * (items.Count - 1);

            int right, off;
            if (dragging) right = dragRight;
            else if (settings.Offsets.TryGetValue(ScreenKey, out off)) right = tb.Right - off;
            else right = AutoRight(tb);
            right = Math.Max(tb.Left + total, Math.Min(tb.Right, right));
            bounds = new Rectangle(right - total, tb.Top + (tb.Height - h) / 2, total, h);

            pills.Clear();
            int x = total;   // most urgent timer sits closest to the tray
            for (int i = 0; i < items.Count; i++)
            {
                x -= widths[i];
                pills.Add(new KeyValuePair<TimerItem, Rectangle>(items[i], new Rectangle(x, 0, widths[i], h)));
                x -= gap;
            }
        }

        void Render()
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            bool light = Theme.TaskbarLight;
            Color fg = light ? Color.FromArgb(235, 0, 0, 0) : Color.White;
            Color warnText = light ? Color.FromArgb(150, 95, 0) : Theme.Warn;

            using (var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var f = new Font("Segoe UI Semibold", S(TextPx), FontStyle.Regular, GraphicsUnit.Pixel))
                using (var lf = new Font("Segoe UI", S(TextPx - 2), FontStyle.Regular, GraphicsUnit.Pixel))
                using (var sf = TextFormat())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    foreach (var kv in pills)
                    {
                        TimerItem it = kv.Key;
                        Rectangle pr = kv.Value;
                        bool hover = it == hoverItem;
                        bool finished = it.State == TimerState.Finished;
                        bool paused = it.State == TimerState.Paused;
                        bool warn = mgr.InWarning(it);
                        var r = new RectangleF(pr.X + 0.5f, pr.Y + 0.5f, pr.Width - 1, pr.Height - 1);

                        Color bg;
                        if (finished && flashOn) bg = Color.FromArgb(230, Theme.Alarm);
                        else if (warn) bg = Color.FromArgb(hover ? 75 : 55, Theme.Warn);
                        else if (it.Kind == TimerKind.Pomodoro && it.PomoBreak) bg = Color.FromArgb(hover ? 70 : 48, it.Color);
                        else if (light) bg = Color.FromArgb(hover ? 40 : 20, 0, 0, 0);
                        else bg = Color.FromArgb(hover ? 36 : 18, 255, 255, 255);
                        Theme.Fill(g, r, S(5), bg);

                        // progress bar in the timer's colour
                        if (!finished)
                        {
                            float inset = S(9), bh = Math.Max(2, S(2.5f));
                            float by = pr.Height - S(6) - bh;
                            var track = new RectangleF(pr.X + inset, by, pr.Width - inset * 2, bh);
                            Theme.Fill(g, track, bh / 2, Color.FromArgb(light ? 30 : 40, fg));
                            float fw = track.Width * it.Fraction;
                            if (fw >= bh) Theme.Fill(g, new RectangleF(track.X, track.Y, fw, bh), bh / 2, paused ? Color.FromArgb(160, fg) : it.Color);
                        }

                        Color tc = finished ? (flashOn ? Color.White : Theme.Alarm) : warn ? warnText : paused ? Color.FromArgb(150, fg) : fg;
                        string time = TimeText(it);
                        float textH = finished ? pr.Height : pr.Height - S(5);
                        float timeW = g.MeasureString(time, f, PointF.Empty, sf).Width;
                        float labelW = ShowLabel(it) ? g.MeasureString(LabelText(it), lf, PointF.Empty, sf).Width + S(5) : 0;
                        float glyphW = HasGlyph(it) ? S(11) : 0;
                        float x = pr.X + (pr.Width - (glyphW + labelW + timeW)) / 2f;
                        float cy = textH / 2f;

                        if (glyphW > 0)
                        {
                            float gs = S(8);
                            if (paused)
                            {
                                float bw = Math.Max(2, S(2.5f));
                                using (var b = new SolidBrush(Color.FromArgb(150, fg)))
                                {
                                    g.FillRectangle(b, x, cy - gs / 2, bw, gs);
                                    g.FillRectangle(b, x + bw * 2, cy - gs / 2, bw, gs);
                                }
                            }
                            else if (it.Kind == TimerKind.Pomodoro && it.PomoBreak)
                                using (var p = new Pen(it.Color, Math.Max(1.5f, S(1.6f)))) g.DrawEllipse(p, x, cy - gs / 2, gs, gs);
                            else
                                using (var b = new SolidBrush(finished && flashOn ? Color.White : it.Color)) g.FillEllipse(b, x, cy - gs / 2, gs, gs);
                            x += glyphW;
                        }
                        if (labelW > 0)
                        {
                            using (var b = new SolidBrush(Color.FromArgb(finished ? 255 : 190, tc)))
                                g.DrawString(LabelText(it), lf, b, new RectangleF(x, 0, labelW + S(4), textH), sf);
                            x += labelW;
                        }
                        using (var b = new SolidBrush(tc)) g.DrawString(time, f, b, new RectangleF(x, 0, timeW + S(4), textH), sf);
                    }
                }
                Apply(bmp);
            }
        }

        void Apply(Bitmap bmp)
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = Native.SelectObject(memDc, hBmp);
                var size = new Native.SIZE(bmp.Width, bmp.Height);
                var src = new Native.POINT(0, 0);
                var dst = new Native.POINT(bounds.X, bounds.Y);
                var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
                if (hBmp != IntPtr.Zero) { Native.SelectObject(memDc, old); Native.DeleteObject(hBmp); }
                Native.DeleteDC(memDc);
            }
        }

        TimerItem PillAt(Point p)
        {
            foreach (var kv in pills) if (kv.Value.Contains(p)) return kv.Key;
            return null;
        }

        protected override void OnMouseLeave(EventArgs e) { hoverItem = null; tip.SetToolTip(this, null); Render(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { mouseDown = true; downScreen = Cursor.Position; downRight = bounds.Right; }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (mouseDown)
            {
                int dx = Cursor.Position.X - downScreen.X;
                if (!dragging && Math.Abs(dx) > S(5)) { dragging = true; Cursor = Cursors.SizeWE; }
                if (dragging) { dragRight = downRight + dx; UpdateView(flashOn); }
            }
            else
            {
                var it = PillAt(e.Location);
                if (it != hoverItem)
                {
                    hoverItem = it;
                    tip.SetToolTip(this, it == null ? null : it.Title + "\n" + it.Detail);
                    Render();
                }
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && mouseDown)
            {
                mouseDown = false;
                if (dragging)
                {
                    dragging = false; Cursor = Cursors.Hand;
                    settings.Offsets[ScreenKey] = Math.Max(0, TaskbarRect().Right - bounds.Right);
                    settings.Save();
                }
                else if (PillClicked != null) PillClicked(PillAt(e.Location));
            }
            else if (e.Button == MouseButtons.Right && RightClicked != null) RightClicked(this, EventArgs.Empty);
            base.OnMouseUp(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }

    // ================================================================== Pop-up panel
    class TimerPanel : Form
    {
        readonly TimerManager mgr;
        readonly Settings settings;
        public event EventHandler SettingsRequested;
        public event Action<TimerItem> DismissItem, SnoozeItem, RestartItem;
        public DateTime LastHidden = DateTime.MinValue;

        readonly Dictionary<string, Rectangle> rects = new Dictionary<string, Rectangle>();   // clickable
        readonly Dictionary<string, Rectangle> deco = new Dictionary<string, Rectangle>();    // drawn only
        string hover, pressed;
        readonly System.Windows.Forms.Timer repeat = new System.Windows.Forms.Timer();
        readonly TextBox labelBox = new TextBox();
        List<TimerItem> rowItems = new List<TimerItem>();

        int tab;   // 0 Timer, 1 Until, 2 Pomodoro, 3 Alarms
        int setSeconds, targetMinute, alarmMinute, alarmDays;
        Color pickColor = Color.Empty, customColor = Color.Empty;
        int focusField; string typeBuf = "";
        string notice = ""; DateTime noticeUntil;
        bool suppressHide;
        int anchorBottom;

        const int MaxTimer = 23 * 3600 + 59 * 60 + 59;
        static readonly string[] TabNames = { "Timer", "Until", "Pomodoro", "Alarms" };
        static int S(float v) { return Theme.S(v); }

        public TimerPanel(TimerManager m, Settings s)
        {
            mgr = m; settings = s;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            tab = settings.LastMode;
            setSeconds = Math.Max(1, Math.Min(MaxTimer, settings.LastSeconds));
            targetMinute = settings.LastTarget >= 0 ? settings.LastTarget % 1440 : DefaultTarget();
            alarmMinute = settings.LastAlarm % 1440;
            alarmDays = settings.LastAlarmDays;

            labelBox.BorderStyle = BorderStyle.None;
            labelBox.Font = new Font("Segoe UI", S(13), FontStyle.Regular, GraphicsUnit.Pixel);
            labelBox.MaxLength = 40;
            labelBox.TabStop = false;
            labelBox.GotFocus += delegate { Invalidate(); };
            labelBox.LostFocus += delegate { Invalidate(); };
            Controls.Add(labelBox);

            repeat.Tick += delegate
            {
                repeat.Interval = 60;
                if (pressed != null && pressed == hover) Step(pressed);
            };
            DoLayout();
        }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; cp.ExStyle |= Native.WS_EX_TOOLWINDOW; return cp; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
            Native.SendMessage(labelBox.Handle, 0x1501, (IntPtr)1, "Label (optional), e.g. Tea, Standup");
        }

        static int DefaultTarget()
        {
            DateTime t = DateTime.Now.AddMinutes(30);
            int m = t.Hour * 60 + t.Minute;
            return ((m + 4) / 5 * 5) % 1440;
        }

        // ---------------------------------------------------------- layout
        void DoLayout()
        {
            rects.Clear(); deco.Clear();
            int W = S(340), pad = S(16), inner = W - pad * 2;
            int[] tabW = { S(58), S(56), S(86), S(70) };
            for (int i = 0, tx = pad; i < 4; tx += tabW[i], i++) rects["tab" + i] = new Rectangle(tx, S(12), tabW[i], S(28));
            rects["gear"] = new Rectangle(W - pad - S(30), S(12), S(30), S(28));
            int y = S(50);

            if (tab != 2)
            {
                bool ampm = (tab == 1 || tab == 3) && !Theme.Clock24;
                int cx = W / 2, numW;
                int[] centers;
                if (tab == 0) { centers = new[] { cx - S(106), cx, cx + S(106) }; numW = S(86); }
                else if (ampm) { centers = new[] { cx - S(82), cx + S(18) }; numW = S(90); }
                else { centers = new[] { cx - S(72), cx + S(72) }; numW = S(110); }
                for (int i = 0; i < centers.Length; i++)
                {
                    int c = centers[i];
                    rects["up" + i] = new Rectangle(c - S(30), y, S(60), S(30));
                    rects["num" + i] = new Rectangle(c - numW / 2, y + S(32), numW, S(60));
                    rects["down" + i] = new Rectangle(c - S(30), y + S(94), S(60), S(30));
                }
                if (ampm) rects["ampm"] = new Rectangle(cx + S(76), y + S(46), S(52), S(32));
                y += S(146);
            }
            else
            {
                int gap = S(10), cw = (inner - gap) / 2;
                for (int i = 0; i < 4; i++)
                {
                    int col = i % 2, row = i / 2;
                    var cell = new Rectangle(pad + col * (cw + gap), y + row * S(60), cw, S(54));
                    deco["cell" + i] = cell;
                    var box = new Rectangle(cell.X, cell.Y + S(18), cw, S(36));
                    rects["pm" + i] = new Rectangle(box.X, box.Y, S(38), box.Height);
                    rects["pp" + i] = new Rectangle(box.Right - S(38), box.Y, S(38), box.Height);
                    rects["num" + i] = new Rectangle(box.X + S(38), box.Y, box.Width - S(76), box.Height);
                }
                y += S(124);
            }

            if (tab == 3)
            {
                int g6 = S(6), dw = (inner - g6 * 6) / 7;
                for (int d = 0; d < 7; d++) rects["day" + d] = new Rectangle(pad + d * (dw + g6), y, dw, S(30));
                y += S(38);
            }
            if (tab != 0) { deco["info"] = new Rectangle(pad, y, inner, S(18)); y += S(26); }

            deco["label"] = new Rectangle(pad, y, inner, S(34));
            y += S(44);

            if (tab != 3)
            {
                int n = 12, sw = S(20), sgap = (inner - n * sw) / (n - 1);
                for (int i = 0; i < n; i++) rects["sw" + i] = new Rectangle(pad + i * (sw + sgap), y, sw, sw);
                y += sw + S(14);
            }
            if (tab == 0 || tab == 1)
            {
                var list = tab == 0 ? settings.TimerPresets : settings.UntilPresets;
                int n = Math.Min(6, list.Count);
                if (n > 0)
                {
                    int g8 = S(8), chipW = (inner - g8 * (n - 1)) / n;
                    for (int i = 0; i < n; i++) rects["pre" + i] = new Rectangle(pad + i * (chipW + g8), y, chipW, S(30));
                    y += S(40);
                }
            }
            rects["start"] = new Rectangle(pad, y, inner, S(38));
            y += S(38) + S(14);

            deco["sep"] = new Rectangle(0, y, W, 1);
            y += S(10);
            deco["head"] = new Rectangle(pad, y, inner, S(18));
            y += S(24);

            if (tab != 3)
            {
                rowItems = mgr.Ordered();
                if (rowItems.Count == 0) { deco["empty"] = new Rectangle(pad, y, inner, S(30)); y += S(34); }
                int bw = S(30);
                for (int i = 0; i < rowItems.Count; i++)
                {
                    var row = new Rectangle(pad, y, inner, S(44));
                    deco["row" + i] = row;
                    TimerItem it = rowItems[i];
                    string a1 = null, a2 = null;
                    if (it.State == TimerState.Finished)
                        a2 = (it.Kind == TimerKind.Duration || it.Kind == TimerKind.Pomodoro) ? "restart" : "snooze";
                    else
                    {
                        a1 = "plus";
                        if (it.Pausable) a2 = it.State == TimerState.Paused ? "resume" : "pause";
                    }
                    int by = row.Y + (row.Height - bw) / 2;
                    rects["r" + i + ":close"] = new Rectangle(row.Right - bw - S(4), by, bw, bw);
                    if (a2 != null) rects["r" + i + ":" + a2] = new Rectangle(row.Right - bw * 2 - S(6), by, bw, bw);
                    if (a1 != null) rects["r" + i + ":" + a1] = new Rectangle(row.Right - bw * 3 - S(8), by, bw, bw);
                    y += S(48);
                }
            }
            else
            {
                var alarms = settings.Alarms;
                if (alarms.Count == 0) { deco["empty"] = new Rectangle(pad, y, inner, S(30)); y += S(34); }
                for (int i = 0; i < alarms.Count; i++)
                {
                    var row = new Rectangle(pad, y, inner, S(44));
                    deco["arow" + i] = row;
                    rects["ad" + i] = new Rectangle(row.Right - S(34), row.Y + S(7), S(30), S(30));
                    rects["at" + i] = new Rectangle(row.Right - S(34) - S(50), row.Y + S(11), S(42), S(22));
                    y += S(48);
                }
            }
            y += S(6);

            int oldH = ClientSize.Height;
            ClientSize = new Size(W, y);
            if (Visible && oldH != y) Top = anchorBottom - Height;

            var lr = deco["label"];
            labelBox.Location = new Point(lr.X + S(10), lr.Y + (lr.Height - labelBox.Height) / 2);
            labelBox.Width = lr.Width - S(20);
            bool light = Theme.AppsLight;
            labelBox.BackColor = light ? Color.White : Color.FromArgb(55, 55, 55);
            labelBox.ForeColor = light ? Color.FromArgb(26, 26, 26) : Color.White;
        }

        public void RefreshView()
        {
            if (!Visible) return;
            DoLayout();
            Invalidate();
        }

        public void ShowNear(Point anchor)
        {
            tab = settings.LastMode;
            if (tab == 1 && (Theme.NextOccurrence(targetMinute) - DateTime.Now).TotalHours > 12) targetMinute = DefaultTarget();
            focusField = 0; typeBuf = "";
            DoLayout();
            Rectangle wa = Screen.FromPoint(anchor).WorkingArea;
            anchorBottom = wa.Bottom - S(12);
            int x = Math.Max(wa.Left + S(12), Math.Min(wa.Right - Width - S(12), anchor.X - Width / 2));
            Location = new Point(x, Math.Max(wa.Top, anchorBottom - Height));
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            ActiveControl = null;
            Invalidate();
        }
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (suppressHide) return;
            pressed = null; repeat.Stop();
            LastHidden = DateTime.Now;
            Hide();
        }
        void HidePanel() { LastHidden = DateTime.Now; Hide(); }
        void Notice(string text) { notice = text; noticeUntil = DateTime.Now.AddSeconds(4); Invalidate(); }

        // ---------------------------------------------------------- editable fields
        int ClockMinute
        {
            get { return tab == 3 ? alarmMinute : targetMinute; }
            set
            {
                value = ((value % 1440) + 1440) % 1440;
                if (tab == 3) { alarmMinute = value; settings.LastAlarm = value; }
                else { targetMinute = value; settings.LastTarget = value; }
            }
        }
        int FieldCount { get { return tab == 2 ? 4 : tab == 0 ? 3 : 2; } }
        int FieldMin(int f) { if (tab == 0) return 0; if (tab == 2) return 1; return f == 0 && !Theme.Clock24 ? 1 : 0; }
        int FieldMax(int f)
        {
            if (tab == 0) return f == 0 ? 23 : 59;
            if (tab == 2) return f == 3 ? 10 : f == 1 ? 60 : 99;
            if (f == 0) return Theme.Clock24 ? 23 : 12;
            return 59;
        }
        int GetField(int f)
        {
            if (tab == 0) return f == 0 ? setSeconds / 3600 : f == 1 ? setSeconds / 60 % 60 : setSeconds % 60;
            if (tab == 2) return f == 0 ? settings.PomoWork : f == 1 ? settings.PomoShort : f == 2 ? settings.PomoLong : settings.PomoRounds;
            int m = ClockMinute;
            if (f == 1) return m % 60;
            int h = m / 60;
            return Theme.Clock24 ? h : (h % 12 == 0 ? 12 : h % 12);
        }
        void SetField(int f, int v)
        {
            v = Math.Max(FieldMin(f), Math.Min(FieldMax(f), v));
            if (tab == 0)
            {
                int hh = setSeconds / 3600, mm = setSeconds / 60 % 60, ss = setSeconds % 60;
                if (f == 0) hh = v; else if (f == 1) mm = v; else ss = v;
                setSeconds = hh * 3600 + mm * 60 + ss;
                settings.LastSeconds = setSeconds;
            }
            else if (tab == 2)
            {
                if (f == 0) settings.PomoWork = v; else if (f == 1) settings.PomoShort = v;
                else if (f == 2) settings.PomoLong = v; else settings.PomoRounds = v;
            }
            else
            {
                int h = ClockMinute / 60, mi = ClockMinute % 60;
                if (f == 1) mi = v;
                else if (Theme.Clock24) h = v;
                else h = (v % 12) + (h >= 12 ? 12 : 0);
                ClockMinute = h * 60 + mi;
            }
            Invalidate();
        }
        void StepField(int f, int dir)
        {
            typeBuf = ""; focusField = f;
            if (tab == 0) { setSeconds = Math.Max(0, Math.Min(MaxTimer, setSeconds + (f == 0 ? 3600 : f == 1 ? 60 : 1) * dir)); settings.LastSeconds = setSeconds; }
            else if (tab == 2) SetField(f, GetField(f) + dir);
            else ClockMinute = ClockMinute + (f == 0 ? 60 : 1) * dir;
            Invalidate();
        }
        void Step(string key)
        {
            if (key.StartsWith("up")) StepField(key[2] - '0', 1);
            else if (key.StartsWith("down")) StepField(key[4] - '0', -1);
            else if (key.StartsWith("pp")) StepField(key[2] - '0', 1);
            else if (key.StartsWith("pm")) StepField(key[2] - '0', -1);
        }
        static bool IsStepper(string key)
        {
            return key != null && (key.StartsWith("up") || key.StartsWith("down") || key.StartsWith("pp") || key.StartsWith("pm"));
        }
        void TypeDigit(int d)
        {
            if (focusField < 0 || focusField >= FieldCount) focusField = 0;
            string nb = typeBuf + d;
            int max = FieldMax(focusField);
            int v = int.Parse(nb);
            if (v > max || nb.Length > 2) { nb = d.ToString(); v = d; }
            typeBuf = nb;
            if (v >= FieldMin(focusField)) SetField(focusField, v);
            if (typeBuf.Length >= 2 || v * 10 > max)
            {
                typeBuf = "";
                if (focusField < FieldCount - 1) focusField++;
            }
            Invalidate();
        }
        void Backspace()
        {
            if (typeBuf.Length > 0)
            {
                typeBuf = typeBuf.Substring(0, typeBuf.Length - 1);
                SetField(focusField, typeBuf.Length == 0 ? FieldMin(focusField) : int.Parse(typeBuf));
            }
            else if (focusField > 0) focusField--;
            Invalidate();
        }
        void MoveField(int dir) { typeBuf = ""; focusField = Math.Max(0, Math.Min(FieldCount - 1, focusField + dir)); Invalidate(); }

        // ---------------------------------------------------------- input
        string HitAt(Point p)
        {
            foreach (var kv in rects) if (kv.Value.Contains(p)) return kv.Key;
            return null;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            var h = HitAt(e.Location);
            if (h != hover) { hover = h; Cursor = h == null ? Cursors.Default : Cursors.Hand; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = null; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (deco["label"].Contains(e.Location)) { labelBox.Focus(); return; }
            ActiveControl = null;
            pressed = HitAt(e.Location);
            if (IsStepper(pressed)) { Step(pressed); repeat.Interval = 400; repeat.Start(); }
            else if (pressed != null && pressed.StartsWith("num")) { focusField = pressed[3] - '0'; typeBuf = ""; }
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            repeat.Stop();
            var h = HitAt(e.Location);
            if (e.Button == MouseButtons.Left && h != null && h == pressed && !IsStepper(h)) Press(h);
            pressed = null;
            Invalidate();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var h = HitAt(e.Location);
            int dir = e.Delta > 0 ? 1 : -1;
            if (h != null)
            {
                if (h.StartsWith("num") || IsStepper(h))
                {
                    char c = h.StartsWith("down") ? h[4] : h.StartsWith("num") ? h[3] : h[2];
                    StepField(c - '0', dir);
                }
                else if (h == "ampm") ClockMinute = ClockMinute + 720;
            }
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool inLabel = labelBox.Focused;
            Keys code = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) != 0;
            if (code == Keys.Escape) { HidePanel(); return true; }
            if (code == Keys.Enter) { StartFromEditor(); return true; }
            if (code == Keys.Tab)
            {
                if (ctrl) { Press("tab" + ((tab + ((keyData & Keys.Shift) != 0 ? 3 : 1)) % 4)); return true; }
                if (inLabel) ActiveControl = null; else labelBox.Focus();
                Invalidate();
                return true;
            }
            if (inLabel) return base.ProcessCmdKey(ref msg, keyData);
            if (ctrl || (keyData & Keys.Alt) != 0) return base.ProcessCmdKey(ref msg, keyData);

            if (code >= Keys.D0 && code <= Keys.D9 && (keyData & Keys.Shift) == 0) { TypeDigit(code - Keys.D0); return true; }
            if (code >= Keys.NumPad0 && code <= Keys.NumPad9) { TypeDigit(code - Keys.NumPad0); return true; }
            switch (code)
            {
                case Keys.OemSemicolon: case Keys.OemPeriod: case Keys.Decimal: case Keys.Space: MoveField(1); return true;
                case Keys.Back: Backspace(); return true;
                case Keys.Up: StepField(focusField, 1); return true;
                case Keys.Down: StepField(focusField, -1); return true;
                case Keys.Left: MoveField(-1); return true;
                case Keys.Right: MoveField(1); return true;
                case Keys.A: if ((tab == 1 || tab == 3) && ClockMinute >= 720) ClockMinute = ClockMinute - 720; Invalidate(); return true;
                case Keys.P: if ((tab == 1 || tab == 3) && ClockMinute < 720) ClockMinute = ClockMinute + 720; Invalidate(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void Press(string key)
        {
            if (key.StartsWith("tab"))
            {
                tab = key[3] - '0';
                settings.LastMode = tab; settings.Save();
                focusField = 0; typeBuf = "";
                DoLayout(); Invalidate();
                return;
            }
            if (key == "gear") { HidePanel(); if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); return; }
            if (key == "ampm") { ClockMinute = ClockMinute + 720; Invalidate(); return; }
            if (key == "start") { StartFromEditor(); return; }
            if (key.StartsWith("day"))
            {
                alarmDays ^= 1 << (key[3] - '0');
                settings.LastAlarmDays = alarmDays;
                Invalidate(); return;
            }
            if (key.StartsWith("sw")) { PickSwatch(int.Parse(key.Substring(2))); return; }
            if (key.StartsWith("pre"))
            {
                int i = int.Parse(key.Substring(3));
                if (tab == 0) { setSeconds = Math.Min(MaxTimer, settings.TimerPresets[i]); settings.LastSeconds = setSeconds; }
                else ClockMinute = Theme.MinuteOfDay(DateTime.Now.AddSeconds(settings.UntilPresets[i]));
                typeBuf = "";
                Invalidate(); return;
            }
            if (key.StartsWith("at") || key.StartsWith("ad"))
            {
                int i = int.Parse(key.Substring(2));
                if (i >= settings.Alarms.Count) return;
                var a = settings.Alarms[i];
                if (key[1] == 't') { a.Enabled = !a.Enabled; a.Recompute(DateTime.Now); }
                else settings.Alarms.RemoveAt(i);
                settings.Save();
                DoLayout(); Invalidate(); return;
            }
            if (key.StartsWith("r") && key.Contains(":"))
            {
                int colon = key.IndexOf(':');
                int i = int.Parse(key.Substring(1, colon - 1));
                if (i >= rowItems.Count) return;
                var it = rowItems[i];
                switch (key.Substring(colon + 1))
                {
                    case "close":
                        if (it.State == TimerState.Finished) { if (DismissItem != null) DismissItem(it); }
                        else mgr.Remove(it);
                        break;
                    case "pause": it.Pause(); mgr.RaiseChanged(); break;
                    case "resume": it.Resume(); mgr.RaiseChanged(); break;
                    case "plus": it.Adjust(60); mgr.RaiseChanged(); break;
                    case "restart": if (RestartItem != null) RestartItem(it); break;
                    case "snooze": if (SnoozeItem != null) SnoozeItem(it); break;
                }
                DoLayout(); Invalidate();
            }
        }

        void PickSwatch(int i)
        {
            if (i == 0) { pickColor = Color.Empty; Invalidate(); return; }
            if (i >= 1 && i <= 10)
            {
                Color c = Theme.Palette[i - 1];
                if (mgr.ColorInUse(c, null)) { Notice("That colour is already in use"); return; }
                pickColor = c; Invalidate(); return;
            }
            suppressHide = true;
            try
            {
                using (var cd = new ColorDialog { FullOpen = true, AnyColor = true })
                {
                    if (!customColor.IsEmpty) cd.Color = customColor;
                    var cc = new int[Theme.Palette.Length];
                    for (int k = 0; k < cc.Length; k++) cc[k] = ColorTranslator.ToWin32(Theme.Palette[k]);
                    cd.CustomColors = cc;
                    if (cd.ShowDialog(this) == DialogResult.OK)
                    {
                        if (mgr.ColorInUse(cd.Color, null)) Notice("Too close to a colour in use");
                        else { customColor = cd.Color; pickColor = cd.Color; }
                    }
                }
            }
            finally { suppressHide = false; }
            Activate();
            Invalidate();
        }

        void StartFromEditor()
        {
            string label = labelBox.Text.Trim();
            if (tab == 3)
            {
                if (settings.Alarms.Count >= 10) { Notice("You can have up to 10 alarms"); return; }
                var a = new AlarmItem { Minute = alarmMinute, Days = alarmDays, Label = label };
                a.Recompute(DateTime.Now);
                settings.Alarms.Add(a);
                settings.Alarms.Sort((x, y) => x.Minute.CompareTo(y.Minute));
                settings.Save();
                labelBox.Text = ""; ActiveControl = null;
                Notice("Alarm set: " + Theme.DayAndTime(a.NextFire));
                DoLayout(); Invalidate();
                return;
            }
            if (!mgr.CanAdd) { Notice("Up to " + TimerManager.MaxTimers + " timers at once"); return; }
            if (tab == 0 && setSeconds <= 0) { Notice("Set a time first"); return; }
            Color c = pickColor;
            if (!c.IsEmpty && mgr.ColorInUse(c, null)) c = Color.Empty;
            TimerItem it = mgr.Add(tab == 0 ? TimerKind.Duration : tab == 1 ? TimerKind.Until : TimerKind.Pomodoro, label, c);
            if (tab == 0) { it.OriginalSeconds = setSeconds; it.Run(setSeconds); }
            else if (tab == 1) it.RunUntil(Theme.NextOccurrence(targetMinute));
            else
            {
                it.PomoWork = settings.PomoWork; it.PomoShort = settings.PomoShort;
                it.PomoLongMin = settings.PomoLong; it.PomoRounds = settings.PomoRounds;
                it.PomoRound = 1; it.PomoBreak = false; it.PomoLong = false;
                it.Run(it.PomoWork * 60);
            }
            settings.Save();
            labelBox.Text = ""; ActiveControl = null;
            pickColor = Color.Empty;
            mgr.RaiseChanged();
            HidePanel();
        }

        // ---------------------------------------------------------- painting
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            bool light = Theme.AppsLight;
            Color bg = light ? Color.FromArgb(249, 249, 249) : Color.FromArgb(40, 40, 40);
            Color text = light ? Color.FromArgb(26, 26, 26) : Color.White;
            Color sub = light ? Color.FromArgb(96, 96, 96) : Color.FromArgb(170, 170, 170);
            Color btn = light ? Color.White : Color.FromArgb(55, 55, 55);
            Color btnHover = light ? Color.FromArgb(240, 240, 240) : Color.FromArgb(66, 66, 66);
            Color btnPress = light ? Color.FromArgb(230, 230, 230) : Color.FromArgb(48, 48, 48);
            Color border = light ? Color.FromArgb(220, 220, 220) : Color.FromArgb(62, 62, 62);
            Color accent = Theme.Accent;
            Color onAccent = Theme.IsLightColor(accent) ? Color.Black : Color.White;
            g.Clear(bg);

            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var nearC = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var farC = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            using (var f11 = Theme.UiFont(11, false))
            using (var f12 = Theme.UiFont(12, false))
            using (var f12s = Theme.UiFont(12.5f, true))
            using (var f13 = Theme.UiFont(13, false))
            using (var f13s = Theme.UiFont(13.5f, true))
            using (var f16s = Theme.UiFont(16, true))
            using (var f46 = Theme.UiFont(46, true))
            using (var ico = Theme.IconFont(13))
            using (var bText = new SolidBrush(text))
            using (var bSub = new SolidBrush(sub))
            {
                Func<string, Color, Color, Color> stateColor = (key, normal, hov) =>
                    pressed == key && hover == key ? btnPress : hover == key ? hov : normal;

                // Tabs + settings
                Rectangle t0 = rects["tab0"], t3 = rects["tab3"];
                var whole = new RectangleF(t0.X + 0.5f, t0.Y + 0.5f, t3.Right - t0.X - 1, t0.Height - 1);
                Theme.Fill(g, whole, S(5), btn);
                Theme.Stroke(g, whole, S(5), border, 1);
                for (int i = 0; i < 4; i++)
                {
                    var r = rects["tab" + i];
                    var innerR = new RectangleF(r.X + S(3), r.Y + S(3), r.Width - S(6), r.Height - S(6));
                    bool sel = i == tab;
                    if (sel) Theme.Fill(g, innerR, S(4), accent);
                    else if (hover == "tab" + i) Theme.Fill(g, innerR, S(4), btnHover);
                    using (var b = new SolidBrush(sel ? onAccent : sub)) g.DrawString(TabNames[i], f12s, b, r, center);
                }
                {
                    var r = rects["gear"];
                    if (hover == "gear") Theme.Fill(g, r, S(5), btnHover);
                    using (var gi = Theme.IconFont(15)) using (var b = new SolidBrush(hover == "gear" ? text : sub)) g.DrawString(Theme.IcoSettings, gi, b, r, center);
                }

                bool fieldsFocused = !labelBox.Focused;
                if (tab != 2)
                {
                    int nf = tab == 0 ? 3 : 2;
                    string[] vals = new string[3];
                    string[] labels;
                    if (tab == 0)
                    {
                        vals[0] = (setSeconds / 3600).ToString();
                        vals[1] = (setSeconds / 60 % 60).ToString("00");
                        vals[2] = (setSeconds % 60).ToString("00");
                        labels = new[] { "hr", "min", "sec" };
                    }
                    else
                    {
                        int h = ClockMinute / 60;
                        vals[0] = Theme.Clock24 ? h.ToString("00") : (h % 12 == 0 ? 12 : h % 12).ToString();
                        vals[1] = (ClockMinute % 60).ToString("00");
                        labels = new[] { "hour", "min" };
                    }
                    for (int i = 0; i < nf; i++)
                    {
                        var r = rects["num" + i];
                        if (hover == "num" + i) Theme.Fill(g, r, S(6), Color.FromArgb(light ? 10 : 14, text));
                        g.DrawString(vals[i], f46, bText, r, center);
                        if (focusField == i && fieldsFocused)
                        {
                            float uw = S(44), ux = r.X + (r.Width - uw) / 2f;
                            Theme.Fill(g, new RectangleF(ux, r.Bottom - S(4), uw, S(3)), S(1.5f), typeBuf.Length > 0 ? accent : Color.FromArgb(140, accent));
                        }
                    }
                    for (int i = 0; i + 1 < nf; i++)
                    {
                        Rectangle ln = rects["num" + i], rn = rects["num" + (i + 1)];
                        float colonX = (ln.Right + rn.Left) / 2f;
                        g.DrawString(":", f46, bText, new RectangleF(colonX - S(20), ln.Y - S(4), S(40), ln.Height), center);
                    }
                    for (int i = 0; i < nf; i++)
                    {
                        var d = rects["down" + i];
                        g.DrawString(labels[i], f11, bSub, new RectangleF(d.X - S(20), d.Bottom - S(2), d.Width + S(40), S(16)), center);
                    }
                    var arrowKeys = new List<string>();
                    for (int i = 0; i < nf; i++) { arrowKeys.Add("up" + i); arrowKeys.Add("down" + i); }
                    foreach (var k in arrowKeys)
                    {
                        var r = rects[k];
                        Color fill = stateColor(k, Color.Transparent, btnHover);
                        if (fill != Color.Transparent) Theme.Fill(g, r, S(5), fill);
                        bool up = k.StartsWith("up");
                        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, aw = S(7), ah = S(4);
                        using (var pen = new Pen(hover == k ? text : sub, Math.Max(1.5f, S(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        {
                            if (up) g.DrawLines(pen, new[] { new PointF(cx - aw, cy + ah), new PointF(cx, cy - ah), new PointF(cx + aw, cy + ah) });
                            else g.DrawLines(pen, new[] { new PointF(cx - aw, cy - ah), new PointF(cx, cy + ah), new PointF(cx + aw, cy - ah) });
                        }
                    }
                    if (rects.ContainsKey("ampm"))
                    {
                        var r = rects["ampm"];
                        var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
                        Theme.Fill(g, rf, S(5), stateColor("ampm", btn, btnHover));
                        Theme.Stroke(g, rf, S(5), border, 1);
                        g.DrawString(ClockMinute < 720 ? "AM" : "PM", f13s, bText, r, center);
                    }
                }
                else
                {
                    string[] titles = { "Focus (min)", "Short break (min)", "Long break (min)", "Rounds" };
                    for (int i = 0; i < 4; i++)
                    {
                        var cell = deco["cell" + i];
                        g.DrawString(titles[i], f11, bSub, new RectangleF(cell.X, cell.Y, cell.Width, S(16)), nearC);
                        Rectangle m = rects["pm" + i], p = rects["pp" + i], n = rects["num" + i];
                        var box = new RectangleF(m.X + 0.5f, m.Y + 0.5f, p.Right - m.X - 1, m.Height - 1);
                        bool foc = focusField == i && fieldsFocused;
                        Theme.Fill(g, box, S(5), btn);
                        Theme.Stroke(g, box, S(5), foc ? accent : border, foc ? Math.Max(1.5f, S(1.5f)) : 1);
                        if (hover == "pm" + i) Theme.Fill(g, new RectangleF(m.X + S(3), m.Y + S(3), m.Width - S(6), m.Height - S(6)), S(4), btnHover);
                        if (hover == "pp" + i) Theme.Fill(g, new RectangleF(p.X + S(3), p.Y + S(3), p.Width - S(6), p.Height - S(6)), S(4), btnHover);
                        using (var ic = Theme.IconFont(11))
                        {
                            g.DrawString("", ic, hover == "pm" + i ? bText : bSub, m, center);   // minus
                            g.DrawString(Theme.IcoAdd, ic, hover == "pp" + i ? bText : bSub, p, center);
                        }
                        g.DrawString(GetField(i).ToString(), f16s, bText, n, center);
                    }
                }

                // Day chips (alarms)
                if (tab == 3)
                {
                    string[] letters = { "M", "T", "W", "T", "F", "S", "S" };
                    for (int d = 0; d < 7; d++)
                    {
                        var r = rects["day" + d];
                        bool on = (alarmDays & (1 << d)) != 0;
                        var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
                        Theme.Fill(g, rf, r.Height / 2f, on ? accent : stateColor("day" + d, btn, btnHover));
                        if (!on) Theme.Stroke(g, rf, r.Height / 2f, border, 1);
                        using (var b = new SolidBrush(on ? onAccent : text)) g.DrawString(letters[d], f12s, b, r, center);
                    }
                }

                // Info line
                if (deco.ContainsKey("info"))
                {
                    string info = "";
                    if (tab == 1)
                    {
                        DateTime t = Theme.NextOccurrence(targetMinute);
                        info = "Rings " + Theme.DayAndTime(t) + "  ·  in " + Theme.Span((t - DateTime.Now).TotalSeconds);
                    }
                    else if (tab == 2)
                    {
                        int total = settings.PomoRounds * settings.PomoWork + (settings.PomoRounds - 1) * settings.PomoShort + settings.PomoLong;
                        info = settings.PomoRounds + " × " + settings.PomoWork + "m focus, " + settings.PomoShort + "m breaks  ·  ends ~" + Theme.ClockTime(DateTime.Now.AddMinutes(total));
                    }
                    else if (tab == 3)
                    {
                        var a = new AlarmItem { Minute = alarmMinute, Days = alarmDays };
                        a.Recompute(DateTime.Now);
                        info = (alarmDays == 0 ? "Rings once  ·  " : AlarmItem.DaysText(alarmDays) + "  ·  next: ") + Theme.DayAndTime(a.NextFire);
                    }
                    g.DrawString(info, f12, bSub, deco["info"], center);
                }

                // Label box
                {
                    var lr = deco["label"];
                    var rf = new RectangleF(lr.X + 0.5f, lr.Y + 0.5f, lr.Width - 1, lr.Height - 1);
                    Theme.Fill(g, rf, S(5), labelBox.BackColor);
                    Theme.Stroke(g, rf, S(5), labelBox.Focused ? accent : border, labelBox.Focused ? Math.Max(1.5f, S(1.5f)) : 1);
                }

                // Colour swatches
                if (rects.ContainsKey("sw0"))
                {
                    for (int i = 0; i < 12; i++)
                    {
                        var r = rects["sw" + i];
                        var rf = new RectangleF(r.X + S(2), r.Y + S(2), r.Width - S(4), r.Height - S(4));
                        bool selected = i == 0 ? pickColor.IsEmpty
                            : i <= 10 ? (!pickColor.IsEmpty && pickColor.ToArgb() == Theme.Palette[i - 1].ToArgb())
                            : (!customColor.IsEmpty && !pickColor.IsEmpty && pickColor.ToArgb() == customColor.ToArgb());
                        if (i == 0)
                        {   // "random": a pie of palette colours
                            for (int q = 0; q < 4; q++)
                                using (var b = new SolidBrush(Theme.Palette[q * 2])) g.FillPie(b, rf.X, rf.Y, rf.Width, rf.Height, -90 + q * 90, 90);
                        }
                        else if (i <= 10)
                        {
                            Color c = Theme.Palette[i - 1];
                            bool used = mgr.ColorInUse(c, null);
                            using (var b = new SolidBrush(used ? Color.FromArgb(55, c) : c)) g.FillEllipse(b, rf);
                            if (used) using (var p = new Pen(Color.FromArgb(150, sub), Math.Max(1, S(1.2f)))) g.DrawLine(p, rf.Left + S(3), rf.Bottom - S(3), rf.Right - S(3), rf.Top + S(3));
                        }
                        else
                        {
                            if (!customColor.IsEmpty) using (var b = new SolidBrush(customColor)) g.FillEllipse(b, rf);
                            else using (var p = new Pen(sub, 1) { DashStyle = DashStyle.Dot }) g.DrawEllipse(p, rf);
                            using (var ic = Theme.IconFont(9))
                            using (var b = new SolidBrush(customColor.IsEmpty ? sub : (Theme.IsLightColor(customColor) ? Color.Black : Color.White)))
                                g.DrawString(Theme.IcoAdd, ic, b, r, center);
                        }
                        if (selected || hover == "sw" + i)
                            using (var p = new Pen(selected ? text : sub, Math.Max(1.5f, S(1.6f)))) g.DrawEllipse(p, r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
                    }
                }

                // Presets
                for (int i = 0; rects.ContainsKey("pre" + i); i++)
                {
                    var r = rects["pre" + i];
                    int secs = tab == 0 ? settings.TimerPresets[i] : settings.UntilPresets[i];
                    bool selected = tab == 0 && setSeconds == secs;
                    float ins = selected ? S(1.5f) : 0.5f;
                    var rf = new RectangleF(r.X + ins, r.Y + ins, r.Width - ins * 2, r.Height - ins * 2);
                    Theme.Fill(g, rf, rf.Height / 2, stateColor("pre" + i, btn, btnHover));
                    Theme.Stroke(g, rf, rf.Height / 2, selected ? accent : border, selected ? Math.Max(1.5f, S(1.5f)) : 1);
                    g.DrawString((tab == 1 ? "+" : "") + Theme.ShortDuration(secs), f13, bText, r, center);
                }

                // Start button
                {
                    var r = rects["start"];
                    Color fill = pressed == "start" && hover == "start" ? Theme.Mix(accent, bg, 0.2f)
                        : hover == "start" ? Theme.Mix(accent, light ? Color.Black : Color.White, 0.1f) : accent;
                    Theme.Fill(g, r, S(5), fill);
                    string[] names = { "Start timer", "Start countdown", "Start focus", "Add alarm" };
                    using (var b = new SolidBrush(onAccent)) g.DrawString(names[tab], f13s, b, r, center);
                }

                // List
                using (var p = new Pen(border)) g.DrawLine(p, 0, deco["sep"].Y, ClientSize.Width, deco["sep"].Y);
                {
                    var hr = deco["head"];
                    string head = tab == 3 ? "Alarms (" + settings.Alarms.Count + ")" : "Running (" + mgr.Items.Count + ")";
                    g.DrawString(head, f12s, bSub, hr, nearC);
                    if (notice.Length > 0 && DateTime.Now < noticeUntil)
                        using (var b = new SolidBrush(accent)) g.DrawString(notice, f11, b, new RectangleF(hr.X + S(90), hr.Y, hr.Width - S(90), hr.Height), farC);
                }
                if (deco.ContainsKey("empty"))
                    g.DrawString(tab == 3 ? "No alarms yet. Pick a time and days above." : "Nothing running. Type a time and press Enter.", f12, bSub, deco["empty"], center);

                if (tab != 3)
                {
                    for (int i = 0; i < rowItems.Count; i++)
                    {
                        var it = rowItems[i];
                        var row = deco["row" + i];
                        bool finished = it.State == TimerState.Finished;
                        Theme.Fill(g, row, S(6), Color.FromArgb(light ? 8 : 12, text));
                        Theme.Fill(g, new RectangleF(row.X + S(6), row.Y + S(8), S(4), row.Height - S(16)), S(2), it.Color);
                        int btnLeft = row.Right;
                        foreach (var kv in rects) if (kv.Key.StartsWith("r" + i + ":") && kv.Value.X < btnLeft) btnLeft = kv.Value.X;
                        float timeW = S(70);
                        float tx = row.X + S(18), tw = btnLeft - S(6) - timeW - tx;
                        using (var b = new SolidBrush(finished ? Theme.Alarm : text))
                            g.DrawString(it.Title, f13s, b, new RectangleF(tx, row.Y + S(4), tw, S(20)), nearC);
                        g.DrawString(it.Detail, f11, bSub, new RectangleF(tx, row.Y + S(22), tw, S(16)), nearC);
                        var timeR = new RectangleF(btnLeft - S(6) - timeW, row.Y, timeW, row.Height - S(4));
                        Color tc = finished ? Theme.Alarm : mgr.InWarning(it) ? (light ? Color.FromArgb(150, 95, 0) : Theme.Warn) : it.State == TimerState.Paused ? sub : text;
                        using (var b = new SolidBrush(tc)) g.DrawString(finished ? "Done" : Theme.Format(it.DisplaySeconds), f16s, b, timeR, farC);
                        if (!finished)
                        {
                            var bar = new RectangleF(tx, row.Bottom - S(6), timeR.Right - tx, S(3));
                            Theme.Fill(g, bar, S(1.5f), Color.FromArgb(light ? 25 : 35, text));
                            float fw = bar.Width * it.Fraction;
                            if (fw > S(3)) Theme.Fill(g, new RectangleF(bar.X, bar.Y, fw, bar.Height), S(1.5f), it.State == TimerState.Paused ? sub : it.Color);
                        }
                        foreach (var kv in rects)
                        {
                            if (!kv.Key.StartsWith("r" + i + ":")) continue;
                            string act = kv.Key.Substring(kv.Key.IndexOf(':') + 1);
                            var r = kv.Value;
                            if (hover == kv.Key) Theme.Fill(g, r, S(5), btnHover);
                            Brush b = hover == kv.Key ? bText : bSub;
                            if (act == "plus") g.DrawString("+1m", f11, b, r, center);
                            else if (act == "snooze") g.DrawString("+5m", f11, b, r, center);
                            else if (act == "pause" || act == "resume")
                            {
                                Color gc = hover == kv.Key ? text : sub;
                                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, gh = S(11);
                                using (var gb = new SolidBrush(gc))
                                {
                                    if (act == "pause")
                                    {
                                        float bw = Math.Max(2, S(3));
                                        g.FillRectangle(gb, cx - bw - S(1.5f), cy - gh / 2, bw, gh);
                                        g.FillRectangle(gb, cx + S(1.5f), cy - gh / 2, bw, gh);
                                    }
                                    else g.FillPolygon(gb, new[] { new PointF(cx - S(4), cy - gh / 2), new PointF(cx + S(6), cy), new PointF(cx - S(4), cy + gh / 2) });
                                }
                            }
                            else g.DrawString(act == "restart" ? Theme.IcoRefresh : Theme.IcoClose, ico, b, r, center);
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < settings.Alarms.Count; i++)
                    {
                        var a = settings.Alarms[i];
                        var row = deco["arow" + i];
                        Theme.Fill(g, row, S(6), Color.FromArgb(light ? 8 : 12, text));
                        Brush main = a.Enabled ? bText : bSub;
                        var tr = rects["at" + i];
                        float tx = row.X + S(12), tw = tr.X - S(8) - tx;
                        DateTime t = DateTime.Today.AddMinutes(a.Minute);
                        g.DrawString(Theme.ClockTime(t), f16s, main, new RectangleF(tx, row.Y + S(2), tw, S(24)), nearC);
                        string sub2 = AlarmItem.DaysText(a.Days) + (a.Label.Length > 0 ? "  ·  " + a.Label : "")
                            + (a.Enabled && a.NextFire != DateTime.MaxValue ? "  ·  next " + Theme.DayAndTime(a.NextFire) : a.Enabled ? "" : "  ·  off");
                        g.DrawString(sub2, f11, bSub, new RectangleF(tx, row.Y + S(24), tw, S(16)), nearC);
                        var swr = new RectangleF(tr.X + 0.5f, tr.Y + 0.5f, tr.Width - 1, tr.Height - 1);
                        if (a.Enabled) Theme.Fill(g, swr, swr.Height / 2, accent);
                        else Theme.Stroke(g, swr, swr.Height / 2, sub, Math.Max(1, S(1.2f)));
                        float knob = swr.Height - S(8);
                        float kx = a.Enabled ? swr.Right - S(4) - knob : swr.X + S(4);
                        using (var b = new SolidBrush(a.Enabled ? onAccent : sub)) g.FillEllipse(b, kx, swr.Y + S(4), knob, knob);
                        var dr = rects["ad" + i];
                        if (hover == "ad" + i) Theme.Fill(g, dr, S(5), btnHover);
                        g.DrawString(Theme.IcoDelete, ico, hover == "ad" + i ? bText : bSub, dr, center);
                    }
                }
            }
            using (var pen = new Pen(border)) g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    // ================================================================== Settings window
    class SettingsForm : Form
    {
        readonly Settings s;
        readonly SoundManager sound;
        public event EventHandler Saved;
        public event EventHandler ResetPositionRequested;

        TextBox hkOpen, hkPause, tbPath, tbTimerPresets, tbUntilPresets;
        int hkOpenVal, hkPauseVal;
        ComboBox cbSound, cbSize;
        TrackBar trVolume;
        Label lblVolume;
        NumericUpDown numRing, numWarn;
        CheckBox chkPlay, chkWarn, chkWarnSound, chkShowAll, chkLabels, chkMonitors, chkFull, chkStartup;
        int y, x0, colX;
        readonly int W;
        readonly Color fieldBack, text, sub;

        static int S(float v) { return Theme.S(v); }

        public SettingsForm(Settings settings, SoundManager sm)
        {
            s = settings; sound = sm;
            Text = "Taskbar Timer settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            Font = new Font("Segoe UI", S(12.5f), FontStyle.Regular, GraphicsUnit.Pixel);
            bool light = Theme.AppsLight;
            BackColor = light ? Color.FromArgb(249, 249, 249) : Color.FromArgb(32, 32, 32);
            ForeColor = text = light ? Color.FromArgb(26, 26, 26) : Color.White;
            sub = light ? Color.FromArgb(96, 96, 96) : Color.FromArgb(170, 170, 170);
            fieldBack = light ? Color.White : Color.FromArgb(45, 45, 45);
            W = S(1000); x0 = 0; colX = S(175);
            y = S(4);

            Section("Keyboard shortcuts");
            hkOpenVal = s.HotkeyOpen; hkPauseVal = s.HotkeyPause;
            hkOpen = HotkeyBox(() => hkOpenVal, v => hkOpenVal = v); Row("Open the timer panel", hkOpen, S(180), null);
            hkPause = HotkeyBox(() => hkPauseVal, v => hkPauseVal = v); Row("Pause / resume / dismiss", hkPause, S(180), null);
            Note("Click a box and press the keys. Backspace clears it.");

            Section("Alarm sound");
            chkPlay = Check("Play a sound when a timer ends (off = flash only)", s.PlaySound);
            cbSound = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = fieldBack, ForeColor = text };
            cbSound.Items.AddRange(SoundManager.Names);
            cbSound.SelectedItem = Array.IndexOf(SoundManager.Names, s.SoundName) >= 0 ? s.SoundName : "Chime";
            var btnTest = MakeButton("Test", false);
            Row("Sound", cbSound, S(170), btnTest);
            tbPath = new TextBox { Text = s.SoundPath, BorderStyle = BorderStyle.FixedSingle, BackColor = fieldBack, ForeColor = text };
            var btnBrowse = MakeButton("Browse…", false);
            btnBrowse.Click += delegate
            {
                using (var ofd = new OpenFileDialog { Filter = "WAV sound (*.wav)|*.wav", Title = "Choose an alarm sound" })
                    if (ofd.ShowDialog(this) == DialogResult.OK) { tbPath.Text = ofd.FileName; cbSound.SelectedItem = "Custom file"; }
            };
            Row("Custom .wav file", tbPath, S(170), btnBrowse);
            trVolume = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, Value = s.Volume, AutoSize = false, Height = S(30), BackColor = BackColor };
            lblVolume = new Label { AutoSize = true, ForeColor = sub, Text = s.Volume + "%" };
            trVolume.ValueChanged += delegate { lblVolume.Text = trVolume.Value + "%"; };
            Row("Volume", trVolume, S(190), lblVolume);
            btnTest.Click += delegate { sound.Stop(); sound.PlayOnce((string)cbSound.SelectedItem, tbPath.Text, trVolume.Value, true); };
            numRing = Num(0, 3600, s.RingSeconds);
            Row("Ring for", numRing, S(80), new Label { AutoSize = true, ForeColor = sub, Text = "sec (0 = until dismissed)" });

            Section("Warning before the end");
            chkWarn = Check("Warn me before a timer ends (it turns amber)", s.WarnEnabled);
            numWarn = Num(5, 3600, s.WarnSeconds);
            Row("Warn when", numWarn, S(80), new Label { AutoSize = true, ForeColor = sub, Text = "seconds are left" });
            chkWarnSound = Check("Play a soft ping when the warning starts", s.WarnSound);

            int leftBottom = y;
            y = S(4); x0 = S(500); colX = x0 + S(175);   // second column
            Section("Taskbar");
            chkShowAll = Check("Show every running timer (off = only the next to finish)", s.ShowAll);
            chkLabels = Check("Show timer labels on the taskbar", s.ShowLabels);
            cbSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = fieldBack, ForeColor = text };
            cbSize.Items.AddRange(new object[] { "Small", "Normal", "Large" });
            cbSize.SelectedIndex = s.BarSize;
            var btnReset = MakeButton("Reset position", false);
            btnReset.Click += delegate { if (ResetPositionRequested != null) ResetPositionRequested(this, EventArgs.Empty); };
            Row("Text size", cbSize, S(120), btnReset);
            chkMonitors = Check("Show on every monitor's taskbar", s.AllMonitors);
            chkFull = Check("Keep showing over full-screen apps", s.OverFullscreen);

            Section("Presets");
            tbTimerPresets = new TextBox { Text = PresetText(s.TimerPresets), BorderStyle = BorderStyle.FixedSingle, BackColor = fieldBack, ForeColor = text };
            Row("Timer buttons", tbTimerPresets, S(280), null);
            tbUntilPresets = new TextBox { Text = PresetText(s.UntilPresets), BorderStyle = BorderStyle.FixedSingle, BackColor = fieldBack, ForeColor = text };
            Row("Until buttons", tbUntilPresets, S(280), null);
            Note("Up to 6, comma-separated, e.g. 90s, 5m, 1h30m.\nUntil buttons count from now.");
            y += S(16);

            Section("General");
            chkStartup = Check("Start with Windows", Startup.Enabled);

            y = Math.Max(y, leftBottom) + S(14);
            var ok = MakeButton("Save", true);
            var cancel = MakeButton("Cancel", false);
            ok.AutoSize = cancel.AutoSize = false;
            ok.Size = cancel.Size = new Size(S(100), S(34));
            cancel.Location = new Point(W - S(20) - cancel.Width, y);
            ok.Location = new Point(cancel.Left - S(10) - ok.Width, y);
            ok.Click += delegate { SaveAll(); };
            cancel.Click += delegate { Close(); };
            Controls.Add(ok); Controls.Add(cancel);
            CancelButton = cancel;
            ClientSize = new Size(W, y + ok.Height + S(18));
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkTitleBar(Handle, !Theme.AppsLight); }

        static string PresetText(List<int> l)
        {
            var parts = new List<string>();
            foreach (var x in l) parts.Add(Theme.ShortDuration(x));
            return string.Join(", ", parts.ToArray());
        }
        static List<int> ParsePresets(string text, List<int> fallback)
        {
            var l = new List<int>();
            foreach (var part in text.Split(','))
            {
                int secs = Theme.ParseDuration(part, true);
                if (secs > 0 && l.Count < 6) l.Add(secs);
            }
            return l.Count > 0 ? l : fallback;
        }

        void Section(string title)
        {
            y += S(14);
            var l = new Label { Text = title, AutoSize = true, Font = Theme.UiFont(14, true), Location = new Point(x0 + S(20), y) };
            Controls.Add(l);
            y += S(28);
        }
        void Note(string note)
        {
            var l = new Label { Text = note, AutoSize = true, ForeColor = sub, Location = new Point(colX, y - S(2)) };
            Controls.Add(l);
            y += S(22);
        }
        CheckBox Check(string label, bool value)
        {
            var c = new CheckBox { Text = label, Checked = value, AutoSize = true, Location = new Point(x0 + S(24), y), ForeColor = text };
            Controls.Add(c);
            y += S(28);
            return c;
        }
        NumericUpDown Num(int min, int max, int value)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), BackColor = fieldBack, ForeColor = text, BorderStyle = BorderStyle.FixedSingle };
        }
        Button MakeButton(string label, bool primary)
        {
            var b = new Button { Text = label, FlatStyle = FlatStyle.Flat, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(S(8), S(2), S(8), S(2)) };
            Color accent = Theme.Accent;
            b.BackColor = primary ? accent : fieldBack;
            b.ForeColor = primary ? (Theme.IsLightColor(accent) ? Color.Black : Color.White) : text;
            b.FlatAppearance.BorderColor = primary ? accent : (Theme.AppsLight ? Color.FromArgb(210, 210, 210) : Color.FromArgb(70, 70, 70));
            return b;
        }
        void Row(string label, Control c, int width, Control extra)
        {
            var l = new Label { Text = label, AutoSize = true, Location = new Point(x0 + S(24), y + S(4)) };
            Controls.Add(l);
            c.Location = new Point(colX, y);
            c.Width = width;
            Controls.Add(c);
            if (extra != null)
            {
                extra.Location = new Point(colX + width + S(10), y + (extra is Label ? S(4) : -S(2)));
                Controls.Add(extra);
            }
            y += Math.Max(S(34), c.Height + S(8));
        }
        TextBox HotkeyBox(Func<int> get, Action<int> set)
        {
            var tb = new TextBox { ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = fieldBack, ForeColor = text, Text = HotkeyWindow.Text(get()), Cursor = Cursors.Hand, ShortcutsEnabled = false };
            tb.KeyDown += (o, e) =>
            {
                e.SuppressKeyPress = true; e.Handled = true;
                if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) { set(0); tb.Text = "None"; return; }
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) return;
                if ((e.Modifiers & (Keys.Control | Keys.Alt)) == 0) { tb.Text = "Use Ctrl and/or Alt + a key"; return; }
                int v = (int)(e.KeyCode | e.Modifiers);
                set(v); tb.Text = HotkeyWindow.Text(v);
            };
            tb.PreviewKeyDown += (o, e) => { e.IsInputKey = true; };
            tb.Leave += delegate { tb.Text = HotkeyWindow.Text(get()); };
            return tb;
        }

        void SaveAll()
        {
            s.HotkeyOpen = hkOpenVal; s.HotkeyPause = hkPauseVal;
            s.PlaySound = chkPlay.Checked;
            s.SoundName = (string)cbSound.SelectedItem;
            s.SoundPath = tbPath.Text.Trim();
            s.Volume = trVolume.Value;
            s.RingSeconds = (int)numRing.Value;
            s.WarnEnabled = chkWarn.Checked;
            s.WarnSeconds = (int)numWarn.Value;
            s.WarnSound = chkWarnSound.Checked;
            s.ShowAll = chkShowAll.Checked;
            s.ShowLabels = chkLabels.Checked;
            s.BarSize = Math.Max(0, cbSize.SelectedIndex);
            s.AllMonitors = chkMonitors.Checked;
            s.OverFullscreen = chkFull.Checked;
            s.TimerPresets = ParsePresets(tbTimerPresets.Text, s.TimerPresets);
            s.UntilPresets = ParsePresets(tbUntilPresets.Text, s.UntilPresets);
            if (chkStartup.Checked != Startup.Enabled) Startup.Enabled = chkStartup.Checked;
            s.Save();
            if (Saved != null) Saved(this, EventArgs.Empty);
            Close();
        }
    }

    // ================================================================== App
    class TimerApp : ApplicationContext
    {
        readonly Settings settings = Settings.Load();
        readonly TimerManager mgr;
        readonly SoundManager sound;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly TimerPanel panel;
        readonly Dictionary<IntPtr, TaskbarOverlay> overlays = new Dictionary<IntPtr, TaskbarOverlay>();
        readonly HotkeyWindow hotkeys = new HotkeyWindow();
        readonly System.Windows.Forms.Timer ticker = new System.Windows.Forms.Timer();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        SettingsForm settingsForm;
        DateTime ringStarted;
        IntPtr lastIcon = IntPtr.Zero;
        string lastIconKey = "";
        const int HkOpen = 1, HkPause = 2;

        public TimerApp(string[] args)
        {
            mgr = new TimerManager(settings);
            sound = new SoundManager(settings);
            panel = new TimerPanel(mgr, settings);
            var h = panel.Handle;

            menu.Items.Add("Open timer", null, delegate { OpenPanel(Cursor.Position); });
            menu.Items.Add("Settings…", null, delegate { OpenSettings(); });
            menu.Items.Add("Pause / resume all", null, delegate { PauseResumeAll(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });

            tray.ContextMenuStrip = menu;
            tray.Text = "Taskbar Timer";
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) TogglePanel(Cursor.Position); };
            tray.BalloonTipClicked += delegate { DismissAllFinished(); };
            UpdateTrayIcon();
            tray.Visible = true;

            panel.SettingsRequested += delegate { OpenSettings(); };
            panel.DismissItem += Dismiss;
            panel.SnoozeItem += Snooze;
            panel.RestartItem += Restart;

            mgr.Changed += delegate { RefreshAll(); };
            mgr.ItemFinished += OnFinished;
            mgr.ItemWarning += it => sound.PlayWarning();
            mgr.PhaseChanged += OnPhase;

            hotkeys.Pressed += id =>
            {
                if (id == HkOpen) TogglePanel(Cursor.Position);
                else if (id == HkPause) PauseResumeAll();
            };
            string failed = RegisterHotkeys();
            if (failed.Length > 0)
                tray.ShowBalloonTip(6000, "Taskbar Timer", "The shortcut " + failed + " is already used by another app. You can change it in Settings.", ToolTipIcon.Info);

            ticker.Interval = 200;
            ticker.Tick += delegate { mgr.Tick(); RefreshAll(); };
            ticker.Start();

            foreach (var a in args) StartFromArg(a);
        }

        void StartFromArg(string arg)
        {
            // "5m", "1h30m", "90" (seconds), "@19:30" (count down to a time); add "=Label" to name it
            if (string.IsNullOrEmpty(arg)) return;
            string label = "";
            int eq = arg.IndexOf('=');
            if (eq > 0) { label = arg.Substring(eq + 1); arg = arg.Substring(0, eq); }
            arg = arg.Trim();
            if (arg.StartsWith("@"))
            {
                DateTime t;
                if (DateTime.TryParseExact(arg.Substring(1), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                {
                    var it = mgr.Add(TimerKind.Until, label, Color.Empty);
                    if (it != null) it.RunUntil(Theme.NextOccurrence(t.Hour * 60 + t.Minute));
                }
            }
            else
            {
                int secs = Theme.ParseDuration(arg, false);
                var it = secs > 0 ? mgr.Add(TimerKind.Duration, label, Color.Empty) : null;
                if (it != null) { it.OriginalSeconds = secs; it.Run(secs); }
            }
            mgr.RaiseChanged();
        }

        string RegisterHotkeys()
        {
            hotkeys.UnregisterAll();
            var failed = new List<string>();
            if (!hotkeys.Register(HkOpen, settings.HotkeyOpen)) failed.Add(HotkeyWindow.Text(settings.HotkeyOpen));
            if (!hotkeys.Register(HkPause, settings.HotkeyPause)) failed.Add(HotkeyWindow.Text(settings.HotkeyPause));
            return string.Join(" and ", failed.ToArray());
        }

        void TogglePanel(Point anchor)
        {
            if (panel.Visible) { panel.Hide(); return; }
            if ((DateTime.Now - panel.LastHidden).TotalMilliseconds < 250) return;
            OpenPanel(anchor);
        }
        void OpenPanel(Point anchor) { panel.ShowNear(anchor); }

        void OpenSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Activate(); return; }
            settingsForm = new SettingsForm(settings, sound);
            settingsForm.Saved += delegate
            {
                string failed = RegisterHotkeys();
                if (failed.Length > 0)
                    MessageBox.Show("The shortcut " + failed + " is already used by another app. Pick a different one.", "Taskbar Timer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshAll();
            };
            settingsForm.ResetPositionRequested += delegate { settings.Offsets.Clear(); settings.Save(); RefreshAll(); };
            settingsForm.FormClosed += delegate { settingsForm = null; };
            settingsForm.Show();
            settingsForm.Activate();
        }

        // ---- timer events
        void OnFinished(TimerItem it)
        {
            ringStarted = DateTime.Now;
            if (!sound.Looping) sound.StartAlarm();
            string title = it.Kind == TimerKind.Alarm ? "Alarm" : "Time's up!";
            string body;
            switch (it.Kind)
            {
                case TimerKind.Duration:
                    body = it.Label.Length > 0 ? it.Label + " (" + Theme.Format(it.OriginalSeconds) + ") is done." : "Your " + Theme.Format(it.OriginalSeconds) + " timer has finished.";
                    break;
                case TimerKind.Pomodoro: body = (it.Label.Length > 0 ? it.Label + ": pomodoro" : "Pomodoro") + " cycle complete. Nice work!"; break;
                default: body = "It's " + Theme.ClockTime(it.TargetLocal) + "." + (it.Label.Length > 0 ? " " + it.Label : ""); break;
            }
            tray.ShowBalloonTip(10000, title, body, ToolTipIcon.Info);
        }
        void OnPhase(TimerItem it)
        {
            sound.PlayOnce();
            string body = it.PomoBreak
                ? "Focus round " + it.PomoRound + " done. Take a " + (it.PomoLong ? it.PomoLongMin : it.PomoShort) + " min " + (it.PomoLong ? "long " : "") + "break."
                : "Break over. Focus round " + it.PomoRound + " of " + it.PomoRounds + ".";
            tray.ShowBalloonTip(6000, it.Label.Length > 0 ? it.Label : "Pomodoro", body, ToolTipIcon.Info);
        }
        void StopSoundIfQuiet()
        {
            foreach (var it in mgr.Items) if (it.Ringing) return;
            sound.Stop();
        }
        void Dismiss(TimerItem it) { mgr.Items.Remove(it); StopSoundIfQuiet(); mgr.RaiseChanged(); }
        void DismissAllFinished()
        {
            mgr.Items.RemoveAll(x => x.State == TimerState.Finished);
            sound.Stop();
            mgr.RaiseChanged();
        }
        void Snooze(TimerItem it)
        {
            if (it.Kind == TimerKind.Alarm) it.Kind = TimerKind.Until;
            it.RunUntil(DateTime.Now.AddMinutes(5));
            StopSoundIfQuiet(); mgr.RaiseChanged();
        }
        void Restart(TimerItem it)
        {
            if (it.Kind == TimerKind.Pomodoro) { it.PomoRound = 1; it.PomoBreak = false; it.PomoLong = false; it.Run(it.PomoWork * 60); }
            else it.Run(it.OriginalSeconds > 0 ? it.OriginalSeconds : it.TotalSeconds);
            StopSoundIfQuiet(); mgr.RaiseChanged();
        }
        void PauseResumeAll()
        {
            if (mgr.Items.Exists(x => x.State == TimerState.Finished)) { DismissAllFinished(); return; }
            bool anyRunning = mgr.Items.Exists(x => x.State == TimerState.Running && x.Pausable);
            foreach (var it in mgr.Items) { if (anyRunning) it.Pause(); else it.Resume(); }
            mgr.RaiseChanged();
        }
        void OnPillClicked(TimerItem it)
        {
            if (it != null && it.State == TimerState.Finished) { Dismiss(it); return; }
            TogglePanel(Cursor.Position);
        }

        // ---- refresh
        void RefreshAll()
        {
            if (sound.Looping && settings.RingSeconds > 0 && (DateTime.Now - ringStarted).TotalSeconds > settings.RingSeconds)
            {
                sound.Stop();
                foreach (var it in mgr.Items) it.Ringing = false;
            }
            bool anyFinished = mgr.Items.Exists(x => x.State == TimerState.Finished);
            bool flash = anyFinished && (DateTime.Now.Millisecond / 500) == 0;
            UpdateOverlays(flash);
            panel.RefreshView();
            UpdateTrayIcon();
            var first = mgr.Ordered();
            string tip;
            if (first.Count == 0) tip = "Taskbar Timer";
            else
            {
                var it = first[0];
                tip = it.Title + ": " + (it.State == TimerState.Finished ? "time's up!" : Theme.Format(it.DisplaySeconds) + " left");
                if (first.Count > 1) tip += " (+" + (first.Count - 1) + " more)";
            }
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (tray.Text != tip) tray.Text = tip;
        }

        void UpdateOverlays(bool flash)
        {
            var bars = new List<IntPtr>();
            IntPtr primaryBar = Native.FindWindow("Shell_TrayWnd", null);
            if (primaryBar != IntPtr.Zero) bars.Add(primaryBar);
            if (settings.AllMonitors)
            {
                IntPtr s = IntPtr.Zero;
                while ((s = Native.FindWindowEx(IntPtr.Zero, s, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) bars.Add(s);
            }
            foreach (var key in new List<IntPtr>(overlays.Keys))
                if (!bars.Contains(key)) { overlays[key].Close(); overlays[key].Dispose(); overlays.Remove(key); }
            foreach (var bar in bars)
            {
                TaskbarOverlay o;
                if (!overlays.TryGetValue(bar, out o))
                {
                    o = new TaskbarOverlay(mgr, settings, bar, bar == primaryBar);
                    var hh = o.Handle;
                    o.PillClicked += OnPillClicked;
                    o.RightClicked += delegate { menu.Show(Cursor.Position); };
                    overlays[bar] = o;
                }
                o.UpdateView(flash);
            }
        }

        void UpdateTrayIcon()
        {
            var list = mgr.Ordered();
            TimerItem it = list.Count > 0 ? list[0] : null;
            int steps = 48;
            int frac = it == null ? -1 : (int)Math.Round(it.Fraction * steps);
            bool light = Theme.TaskbarLight;
            bool blink = DateTime.Now.Millisecond < 500;
            string key = (it == null ? "idle" : it.Id + ":" + it.State + ":" + frac + ":" + it.Color.ToArgb()) + ":" + light + ":" + (it != null && it.State == TimerState.Finished && blink);
            if (key == lastIconKey) return;
            lastIconKey = key;

            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            Color fg = light ? Color.FromArgb(30, 30, 30) : Color.White;
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float pw = size / 7f;
                    var r = new RectangleF(pw / 2 + 0.5f, pw / 2 + size * 0.08f, size - pw - 1, size - pw - 1 - size * 0.08f);
                    float d = Math.Min(r.Width, r.Height);
                    r = new RectangleF(r.X + (r.Width - d) / 2, r.Bottom - d, d, d);
                    if (it != null && it.State == TimerState.Finished)
                    {
                        using (var b = new SolidBrush(blink ? Theme.Alarm : fg)) g.FillEllipse(b, r);
                    }
                    else
                    {
                        using (var track = new Pen(Color.FromArgb(it != null ? 90 : 255, fg), pw)) g.DrawEllipse(track, r);
                        if (it != null)
                        {
                            float sweep = 360f * frac / steps;
                            if (sweep > 0) using (var p = new Pen(it.State == TimerState.Paused ? fg : it.Color, pw) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, r, -90, sweep);
                        }
                        using (var p = new Pen(fg, pw * 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                            g.DrawLine(p, cx, r.Y - pw * 0.9f, cx, r.Y - pw * 0.2f);
                            if (it == null) g.DrawLine(p, cx, cy, cx, cy - r.Height * 0.28f);
                        }
                    }
                }
                IntPtr hIcon = bmp.GetHicon();
                var old = tray.Icon;
                tray.Icon = Icon.FromHandle(hIcon);
                if (old != null) old.Dispose();
                if (lastIcon != IntPtr.Zero) Native.DestroyIcon(lastIcon);
                lastIcon = hIcon;
            }
        }

        protected override void ExitThreadCore()
        {
            ticker.Stop();
            sound.Stop();
            hotkeys.Dispose();
            settings.Save();
            tray.Visible = false;
            tray.Dispose();
            foreach (var o in overlays.Values) o.Close();
            panel.Close();
            base.ExitThreadCore();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "TaskbarTimer_SingleInstance_7f3a", out created))
            {
                if (!created) return;
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = g.DpiX / 96f;
                Application.Run(new TimerApp(args));
            }
        }
    }
}
