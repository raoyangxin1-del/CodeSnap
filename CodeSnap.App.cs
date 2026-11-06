// CodeSnap - screen code recognition floating tool
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Windows.Media.Ocr;

namespace CodeSnap
{
    public class Settings
    {
        public string Mode { get; set; } = "full";
        public int RegionX { get; set; }
        public int RegionY { get; set; }
        public int RegionW { get; set; } = 520;
        public int RegionH { get; set; } = 170;
        public int CursorW { get; set; } = 520;
        public int CursorH { get; set; } = 170;
        public string Regex { get; set; } = "^[A-Z0-9]{4,12}$";
        public bool StrictShape { get; set; } = false;   // true=仅“字母开头+数字结尾”风格
        public int Zoom { get; set; } = 2;   // 小区域送 OCR 前放大倍数(1-4)
        public string OverrideCode { get; set; } = "";  // 调试用：直接以该串作为识别结果
        public Dictionary<string, string> Corrections { get; set; } = new Dictionary<string, string>(); // OCR 错误词 → 正确码
        public int MinLetters { get; set; } = 2;
        public int MinDigits { get; set; } = 2;
        public int StableFrames { get; set; } = 2;
        public int MissToClear { get; set; } = 5;
        public bool OutputType { get; set; } = true;
        public bool AutoSend { get; set; } = false;
        public bool AutoEnter { get; set; } = false;
        public int ClickDelayMs { get; set; } = 130;
        public int TypeDelayMs { get; set; } = 12;
        public int ScanIntervalMs { get; set; } = 10;
        public bool DetectChange { get; set; } = true;
        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public bool HasTarget { get; set; }
        public int OverlayX { get; set; } = -1;
        public int OverlayY { get; set; } = -1;

        public static Settings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var s = JsonSerializer.Deserialize<Settings>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });
                    if (s != null) return s;
                }
            }
            catch { }
            return new Settings();
        }
        public void Save(string path)
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(path, JsonSerializer.Serialize(this, opts));
            }
            catch { }
        }
    }

    public class ScanState
    {
        public readonly object Gate = new object();
        public bool Running = true;
        public string LastError = "";
        public int ScanMs;
        public int FrameCount;
        public string Candidate = "";
        public string Code = "";
        public DateTime CodeTime = DateTime.MinValue;
        public string Status = "启动中";
        public Rectangle OverlayRect = Rectangle.Empty;
        public string Mode = "full";
        public bool DebugRequested;
    }

    public class Candidate
    {
        public string Text = "";
        public int Score;
    }

    public static class InputSim
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern short VkKeyScanW(char ch);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }
        [StructLayout(LayoutKind.Explicit)]
        public struct INPUT { [FieldOffset(0)] public int type; [FieldOffset(8)] public MOUSEINPUT mi; [FieldOffset(8)] public KEYBDINPUT ki; [FieldOffset(8)] public HARDWAREINPUT hi; }

        const int INPUT_MOUSE = 0;
        const int INPUT_KEYBOARD = 1;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_SCANCODE = 0x0008;
        const ushort VK_SHIFT = 0x10;
        const ushort VK_RETURN = 0x0D;
        const ushort VK_CONTROL = 0x11;
        const ushort VK_V = 0x56;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        static void Fire(INPUT input)
        {
            SendInput(1, new INPUT[] { input }, Marshal.SizeOf<INPUT>());
        }

        public static void Click(Point p)
        {
            SetCursorPos(p.X, p.Y);
            Thread.Sleep(12);
            var down = new INPUT { type = INPUT_MOUSE };
            down.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
            Fire(down);
            Thread.Sleep(8);
            var up = new INPUT { type = INPUT_MOUSE };
            up.mi.dwFlags = MOUSEEVENTF_LEFTUP;
            Fire(up);
        }
        static void ScanEvent(ushort scan, bool up)
        {
            var inp = new INPUT { type = INPUT_KEYBOARD };
            inp.ki.wVk = 0;
            inp.ki.wScan = scan;
            inp.ki.dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0);
            Fire(inp);
        }

        static void KeyTap(ushort vk, bool shift)
        {
            ushort scan = (ushort)MapVirtualKey(vk, 0);
            if (scan == 0)
            {
                var kd = new INPUT { type = INPUT_KEYBOARD };
                kd.ki.wVk = vk;
                Fire(kd);
                Thread.Sleep(2);
                var ku = new INPUT { type = INPUT_KEYBOARD };
                ku.ki.wVk = vk;
                ku.ki.dwFlags = KEYEVENTF_KEYUP;
                Fire(ku);
                return;
            }
            ushort shiftScan = (ushort)MapVirtualKey(VK_SHIFT, 0);
            if (shift) ScanEvent(shiftScan, false);
            ScanEvent(scan, false);
            Thread.Sleep(2);
            ScanEvent(scan, true);
            if (shift) ScanEvent(shiftScan, true);
        }

        public static void TypeText(string text, int charDelayMs)
        {
            foreach (char ch in text)
            {
                short r = VkKeyScanW(ch);
                ushort vk = (ushort)(r & 0xff);
                bool shift = (r >> 8 & 0x01) != 0;
                if (vk != 0) KeyTap(vk, shift);
                if (charDelayMs > 0) Thread.Sleep(charDelayMs);
            }
        }

        public static void CtrlV()
        {
            var cd = new INPUT { type = INPUT_KEYBOARD };
            cd.ki.wVk = VK_CONTROL;
            Fire(cd);
            var vd = new INPUT { type = INPUT_KEYBOARD };
            vd.ki.wVk = VK_V;
            Fire(vd);
            Thread.Sleep(2);
            var vu = new INPUT { type = INPUT_KEYBOARD };
            vu.ki.wVk = VK_V;
            vu.ki.dwFlags = KEYEVENTF_KEYUP;
            Fire(vu);
            var cu = new INPUT { type = INPUT_KEYBOARD };
            cu.ki.wVk = VK_CONTROL;
            cu.ki.dwFlags = KEYEVENTF_KEYUP;
            Fire(cu);
        }

        public static void PressEnter()
        {
            var kd = new INPUT { type = INPUT_KEYBOARD };
            kd.ki.wVk = VK_RETURN;
            Fire(kd);
            Thread.Sleep(2);
            var ku = new INPUT { type = INPUT_KEYBOARD };
            ku.ki.wVk = VK_RETURN;
            ku.ki.dwFlags = KEYEVENTF_KEYUP;
            Fire(ku);
        }
    }

    public static class Ocr
    {
        public static OcrEngine CreateEngine()
        {
            OcrEngine e = null;
            try { e = OcrEngine.TryCreateFromUserProfileLanguages(); } catch { }
            if (e == null)
            {
                try
                {
                    foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
                    {
                        if (lang.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                        {
                            try { e = OcrEngine.TryCreateFromLanguage(lang); } catch { }
                            if (e != null) break;
                        }
                    }
                }
                catch { }
            }
            if (e == null)
            {
                try
                {
                    foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
                    {
                        try { e = OcrEngine.TryCreateFromLanguage(lang); if (e != null) break; } catch { }
                    }
                }
                catch { }
            }
            if (e == null) throw new Exception("无法创建 Windows OCR 引擎（请检查 OCR 语言包）");
            return e;
        }
    }

    public class Scanner
    {
        public Settings Cfg;
        public ScanState State;
        OcrEngine _engine;
        List<string> _recent = new List<string>();
        string _committed = "";
        int _missCount;
        const int MaxRecent = 12;
        byte[] _lastThumb;
        DateTime _lastFullScan = DateTime.MinValue;

        public string DebugDir = "";

        public Scanner(Settings cfg, ScanState state)
        {
            Cfg = cfg; State = state;
        }

        public void Stop()
        {
            lock (State.Gate) State.Running = false;
        }

        public void DoOutput(string code)
        {
            if (Cfg.OutputType)
            {
                InputSim.TypeText(code, Cfg.TypeDelayMs);
            }
            else
            {
                try
                {
                    Clipboard.SetText(code);
                    Thread.Sleep(25);
                    InputSim.CtrlV();
                }
                catch { }
            }
            if (Cfg.AutoEnter) InputSim.PressEnter();
        }

        Rectangle VirtualBounds()
        {
            int x = int.MaxValue, y = int.MaxValue, r = int.MinValue, b = int.MinValue;
            foreach (var s in Screen.AllScreens)
            {
                x = Math.Min(x, s.Bounds.X); y = Math.Min(y, s.Bounds.Y);
                r = Math.Max(r, s.Bounds.Right); b = Math.Max(b, s.Bounds.Bottom);
            }
            if (r == int.MinValue) return Screen.PrimaryScreen.Bounds;
            return new Rectangle(x, y, r - x, b - y);
        }

        Rectangle Clamp(Rectangle r, Rectangle vb)
        {
            int x = Math.Max(r.X, vb.X), y = Math.Max(r.Y, vb.Y);
            int x2 = Math.Min(r.Right, vb.Right), y2 = Math.Min(r.Bottom, vb.Bottom);
            if (x2 <= x || y2 <= y) return Rectangle.Empty;
            return new Rectangle(x, y, x2 - x, y2 - y);
        }

        Rectangle ScanRect()
        {
            Rectangle vb = VirtualBounds();
            string mode;
            lock (State.Gate) mode = Cfg.Mode;
            State.Mode = mode;
            if (mode == "region")
            {
                return Clamp(new Rectangle(Cfg.RegionX, Cfg.RegionY, Cfg.RegionW, Cfg.RegionH), vb);
            }
            if (mode == "cursor")
            {
                InputSim.POINT pt;
                InputSim.GetCursorPos(out pt);
                return Clamp(new Rectangle(pt.X - Cfg.CursorW / 2, pt.Y - Cfg.CursorH / 2, Cfg.CursorW, Cfg.CursorH), vb);
            }
            return vb;
        }
        public void Run()
        {
            try
            {
                _engine = Ocr.CreateEngine();
                Debug("scanner started mode=" + Cfg.Mode);
                lock (State.Gate) State.Status = "扫描中";
            }
            catch (Exception ex)
            {
                lock (State.Gate) { State.LastError = ex.Message; State.Status = "OCR 初始化失败"; }
                return;
            }

            while (true)
            {
                bool running;
                bool dbg;
                lock (State.Gate) { running = State.Running; dbg = State.DebugRequested; State.DebugRequested = false; }
                if (!running && !dbg) return;

                Rectangle rect = ScanRect();
                if (rect.IsEmpty)
                {
                    if (!running) return;
                    Thread.Sleep(120);
                    continue;
                }
                if (dbg)
                {
                    DoDebug(rect);
                    if (!running) return;
                }
                string mode;
                lock (State.Gate) mode = Cfg.Mode;
                bool full = mode == "full";

                try
                {
                    var sw = Stopwatch.StartNew();
                    using (Bitmap bmp = Capture(rect))
                    {
                        if (full && Cfg.DetectChange && !HasChanged(bmp))
                        {
                            if ((DateTime.Now - _lastFullScan).TotalMilliseconds < 900)
                            {
                                Thread.Sleep(25);
                                continue;
                            }
                        }
                        string found = FindCode(bmp, rect);
                        found = ApplyCorrections(found);
                        if (!string.IsNullOrEmpty(Cfg.OverrideCode)) found = Cfg.OverrideCode.Trim().ToUpperInvariant();
                        _lastFullScan = DateTime.Now;
                        Track(found);
                    }
                    sw.Stop();
                    lock (State.Gate)
                    {
                        State.ScanMs = (int)sw.ElapsedMilliseconds;
                        State.FrameCount++;
                        State.LastError = "";
                    }
                    int idle;
                    lock (State.Gate) idle = Cfg.ScanIntervalMs;
                    if (idle > 0) Thread.Sleep(idle);
                }
                catch (Exception ex)
                {
                    Debug("ERROR " + ex.Message);
                    lock (State.Gate) State.LastError = ex.Message;
                    Thread.Sleep(300);
                }
            }
        }

        Bitmap Capture(Rectangle rect)
        {
            string testImg = Environment.GetEnvironmentVariable("CODESNAP_TEST_IMAGE");
            if (!string.IsNullOrEmpty(testImg) && File.Exists(testImg))
            {
                using (var src = new Bitmap(testImg))
                {
                    return new Bitmap(src, new Size(rect.Width, rect.Height));
                }
            }
            Bitmap bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(rect.X, rect.Y, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        Bitmap Prepare(Bitmap src, out int zoom)
        {
            int cfgZ = Math.Max(1, Math.Min(4, Cfg.Zoom));
            bool big = src.Width > 1000 || src.Height > 700;
            zoom = big ? 1 : cfgZ;
            if (zoom == 1) return (Bitmap)src.Clone();
            int w = src.Width * zoom, h = src.Height * zoom;
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, w, h);
            }
            return bmp;
        }

        void DoDebug(Rectangle rect)
        {
            try
            {
                string dir = DebugDir;
                if (dir == "") dir = Path.Combine(Path.GetTempPath(), "codesnap_debug");
                Directory.CreateDirectory(dir);
                string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                using (Bitmap bmp = Capture(rect))
                {
                    int z;
                    using (Bitmap prep = Prepare(bmp, out z))
                    {
                        string png = Path.Combine(dir, "frame_" + ts + ".png");
                        prep.Save(png, ImageFormat.Png);
                        byte[] bytes;
                        using (var ms = new MemoryStream())
                        {
                            prep.Save(ms, ImageFormat.Png);
                            bytes = ms.ToArray();
                        }
                        var res = OcrHelper.Recognize(bytes, _engine);
                        string txt = Path.Combine(dir, "ocr_" + ts + ".txt");
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("region=" + rect.X + "," + rect.Y + "," + rect.Width + "x" + rect.Height + " zoom=" + z);
                        sb.AppendLine("---OCR 原文---");
                        if (res != null && res.Lines != null)
                        {
                            foreach (var line in res.Lines)
                            {
                                if (line != null) sb.AppendLine(line.Text);
                            }
                        }
                        System.IO.File.WriteAllText(txt, sb.ToString());
                        lock (State.Gate)
                            State.Status = "诊断已保存: frame_" + ts + ".png 与 ocr_" + ts + ".txt → " + dir;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (State.Gate) State.Status = "诊断失败: " + ex.Message;
            }
        }

        void Debug(string msg)
        {
            string log = Environment.GetEnvironmentVariable("CODESNAP_LOGFILE");
            if (!string.IsNullOrEmpty(log))
            {
                try { File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\n"); } catch { }
            }
        }

        bool HasChanged(Bitmap bmp)
        {
            int tw = 44, th = 26;
            using (Bitmap small = new Bitmap(bmp, tw, th))
            {
                byte[] cur = new byte[tw * th];
                int i = 0;
                for (int y = 0; y < th; y++)
                    for (int x = 0; x < tw; x++)
                    {
                        Color c = small.GetPixel(x, y);
                        cur[i++] = (byte)((c.R + c.G + c.B) / 3);
                    }
                bool changed = _lastThumb == null;
                if (!changed)
                {
                    int diff = 0;
                    for (int j = 0; j < cur.Length; j++)
                        if (Math.Abs(cur[j] - _lastThumb[j]) > 8) diff++;
                    changed = diff >= 2;
                }
                _lastThumb = cur;
                return changed;
            }
        }

        string FindCode(Bitmap bmp, Rectangle origin)
        {
            int z;
            byte[] bytes;
            using (Bitmap prep = Prepare(bmp, out z))
            using (var ms = new MemoryStream())
            {
                prep.Save(ms, ImageFormat.Png);
                bytes = ms.ToArray();
            }
            var result = OcrHelper.Recognize(bytes, _engine);
            if (result == null || result.Lines == null) return "";

            var best = new Candidate();
            Rectangle overlay;
            lock (State.Gate) overlay = State.OverlayRect;
            System.Text.RegularExpressions.Regex re = null;
            try { re = new System.Text.RegularExpressions.Regex(Cfg.Regex); } catch { }

            foreach (var line in result.Lines)
            {
                if (line.Words == null) continue;
                foreach (var word in line.Words)
                {
                    string raw = word.Text == null ? "" : word.Text.Trim();
                    if (raw.Length < 3) continue;
                    string up = raw.ToUpperInvariant();
                    int letters = 0, digits = 0;
                    foreach (char ch in up)
                    {
                        if (ch >= 'A' && ch <= 'Z') letters++;
                        else if (ch >= '0' && ch <= '9') digits++;
                    }
                    if (letters < Cfg.MinLetters || digits < Cfg.MinDigits) continue;
                    if (Cfg.StrictShape && !System.Text.RegularExpressions.Regex.IsMatch(up, "^[A-Z]{2,}[0-9]{2,}$")) continue;
                    if (re != null && !re.IsMatch(up)) continue;
                    var wr = word.BoundingRect;
                    Rectangle r = new Rectangle((int)(wr.X / z) + origin.X, (int)(wr.Y / z) + origin.Y,
                                                Math.Max(1, (int)(wr.Width / z)), Math.Max(1, (int)(wr.Height / z)));
                    if (!overlay.IsEmpty && r.IntersectsWith(overlay)) continue;
                    int score = up.Length * 10 + letters * 5 + digits * 3;
                    if (score > best.Score)
                    {
                        best = new Candidate { Text = up, Score = score };
                    }
                }
            }
            if (best.Score == 0) return "";
            return best.Text;
        }

        string ApplyCorrections(string found)
        {
            try
            {
                if (string.IsNullOrEmpty(found)) return found;
                string key = found.ToUpperInvariant();
                string v;
                if (Cfg.Corrections != null && Cfg.Corrections.TryGetValue(key, out v) && !string.IsNullOrWhiteSpace(v))
                {
                    v = v.Trim().ToUpperInvariant();
                    Debug("CORR " + key + " -> " + v);
                    return v;
                }
            }
            catch { }
            return found;
        }

        void Track(string code)
        {
            if (code == "")
            {
                _missCount++;
                if (_missCount >= Cfg.MissToClear)
                {
                    _recent.Clear();
                    _committed = "";
                    lock (State.Gate)
                    {
                        State.Candidate = "";
                        State.Code = "";
                    }
                }
                return;
            }
            _missCount = 0;
            _recent.Add(code);
            if (_recent.Count > MaxRecent) _recent.RemoveAt(0);

            var counts = new Dictionary<string, int>();
            foreach (var s in _recent)
            {
                int v;
                counts.TryGetValue(s, out v);
                v++;
                counts[s] = v;
            }
            int max = 0;
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                int c = counts[_recent[i]];
                if (c > max) max = c;
            }
            string top = _recent[_recent.Count - 1];
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                if (counts[_recent[i]] == max) { top = _recent[i]; break; }
            }

            bool announce = false;
            lock (State.Gate)
            {
                State.Candidate = top;
                if (max >= Cfg.StableFrames && top != _committed)
                {
                    _committed = top;
                    announce = true;
                    State.Code = top;
                    State.CodeTime = DateTime.Now;
                    State.Status = "已识别 " + top;
                    Debug("COMMIT " + top);
                }
            }
            if (announce && Cfg.AutoSend && Cfg.HasTarget)
            {
                try
                {
                    InputSim.SetCursorPos(Cfg.TargetX, Cfg.TargetY);
                    Thread.Sleep(25);
                    InputSim.Click(new Point(Cfg.TargetX, Cfg.TargetY));
                    Thread.Sleep(Cfg.ClickDelayMs);
                    InputSim.TypeText(code, Cfg.TypeDelayMs);
                    if (Cfg.AutoEnter) InputSim.PressEnter();
                    lock (State.Gate) State.Status = "已自动发送 " + code;
                }
                catch { }
            }
        }
    }

    public static class OcrHelper
    {
        public static Windows.Media.Ocr.OcrResult Recognize(byte[] png, OcrEngine engine)
        {
            var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var dw = new Windows.Storage.Streams.DataWriter(ms);
            dw.WriteBytes(png);
            WaitOp(dw.StoreAsync());
            ms.Seek(0);
            var decoder = WaitOp(Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms));
            var sbmp = WaitOp(decoder.GetSoftwareBitmapAsync());
            return WaitOp(engine.RecognizeAsync(sbmp));
        }

        static TResult WaitOp<TResult>(Windows.Foundation.IAsyncOperation<TResult> op)
        {
            var sw = Stopwatch.StartNew();
            while (op.Status == Windows.Foundation.AsyncStatus.Started)
            {
                if (sw.ElapsedMilliseconds > 6000) throw new TimeoutException("OCR 超时");
                Thread.Sleep(1);
            }
            if (op.Status == Windows.Foundation.AsyncStatus.Error)
                throw new Exception(op.ErrorCode == null ? "OCR 错误" : op.ErrorCode.ToString());
            if (op.Status == Windows.Foundation.AsyncStatus.Canceled) throw new Exception("OCR 已取消");
            return op.GetResults();
        }
    }
    public class MainForm : Form
    {
        Settings _cfg;
        ScanState _state;
        Scanner _scanner;
        Thread _worker;
        Label _lblStatus, _lblCode, _lblHint;
        Button _btnPause, _btnRegion, _btnTarget, _btnSend, _btnExit;
        System.Windows.Forms.Timer _timer;
        bool _dragging;
        Point _dragOff;
        string _appDir = ".";
        string _logPath = "";
        int _exitAfterMs;
        bool _hookOk;
        Mutex _single;
        bool _ownsSingle;
        long _lastSendTick;
        const int WM_HOTKEY = 0x0312;
        const int WM_CLOSE = 0x0010;

        public MainForm(string appDir, int exitAfterMs)
        {
            _appDir = appDir;
            _logPath = Path.Combine(appDir, "codesnap.log");
            try { File.AppendAllText(_logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + "APP START\n"); } catch { }
            _exitAfterMs = exitAfterMs;
            _cfg = Settings.Load(Path.Combine(appDir, "config.json"));
            _state = new ScanState();
            _state.Mode = _cfg.Mode;
            _scanner = new Scanner(_cfg, _state);
            _scanner.DebugDir = Path.Combine(appDir, "debug");

            Text = "CodeSnap";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(23, 24, 30);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            MinimumSize = new Size(430, 140);

            int w = 470;
            if (_cfg.OverlayX >= 0) Location = new Point(_cfg.OverlayX, _cfg.OverlayY);
            else
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - w - 14, wa.Top + 10);
            }

            _lblStatus = new Label();
            _lblStatus.Location = new Point(12, 8);
            _lblStatus.Size = new Size(w - 24, 20);
            _lblStatus.ForeColor = Color.FromArgb(150, 160, 175);
            _lblStatus.Font = new Font("Microsoft YaHei UI", 9f);
            Controls.Add(_lblStatus);

            _lblCode = new Label();
            _lblCode.Location = new Point(12, 30);
            _lblCode.Size = new Size(w - 24, 38);
            _lblCode.ForeColor = Color.FromArgb(240, 242, 248);
            _lblCode.Font = new Font("Consolas", 24f, FontStyle.Bold);
            _lblCode.Text = "—";
            Controls.Add(_lblCode);

            _lblHint = new Label();
            _lblHint.Location = new Point(12, 72);
            _lblHint.Size = new Size(w - 24, 18);
            _lblHint.ForeColor = Color.FromArgb(110, 120, 140);
            _lblHint.Font = new Font("Microsoft YaHei UI", 8f);
            _lblHint.Text = "F6 诊断抓图 · F7 范围 · F8 发送 · F9 目标 · F10 选区 · F11 暂停 · F12 退出（均 Ctrl+Alt）";
            Controls.Add(_lblHint);

            int bx = 12, by = 94, bw = 82, gap = 6;
            _btnPause = MakeButton("暂停", bx, by, bw); bx += bw + gap;
            _btnRegion = MakeButton("选区域", bx, by, bw); bx += bw + gap;
            _btnTarget = MakeButton("标记目标", bx, by, bw); bx += bw + gap;
            _btnSend = MakeButton("发送", bx, by, bw); bx += bw + gap;
            _btnExit = MakeButton("退出", bx, by, bw);
            Size = new Size(w, 126);

            _btnPause.Click += (s, e) => TogglePause();
            _btnRegion.Click += (s, e) => PickRegion();
            _btnTarget.Click += (s, e) => MarkTarget();
            _btnSend.Click += (s, e) => SendNow();
            _btnExit.Click += (s, e) => Close();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 90;
            _timer.Tick += (s, e) => RefreshUi();
            _timer.Start();
        }

        Button MakeButton(string text, int x, int y, int w)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 76, 96);
            b.BackColor = Color.FromArgb(38, 40, 52);
            b.ForeColor = Color.FromArgb(220, 224, 235);
            b.Font = new Font("Microsoft YaHei UI", 9f);
            b.Location = new Point(x, y);
            b.Size = new Size(w, 26);
            Controls.Add(b);
            return b;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!AcquireSingleInstance())
            {
                Log("另一实例已在运行，本实例自动退出");
                MessageBox.Show("CodeSnap 已在运行，已保留现有悬浮窗。\n若旧悬浮窗无响应，请先按 Ctrl+Alt+F12 退出旧窗口再启动。",
                    "CodeSnap", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
                return;
            }
            KillOtherInstances();
            if (_exitAfterMs > 0)
            {
                var t = new System.Windows.Forms.Timer();
                t.Interval = _exitAfterMs;
                t.Tick += (s, ev) => { t.Stop(); Close(); };
                t.Start();
            }
            _hookOk = KeyHook.Start(OnGlobalHotkey);
            Log("low-level hook installed=" + _hookOk);
            RegisterHotkeys();
            Log("热键通道: hook=" + _hookOk + " + RegisterHotKey(双保险)");
            _worker = new Thread(_scanner.Run);
            _worker.IsBackground = true;
            _worker.Start();
            RefreshUi();
            Log("APP READY mode=" + _cfg.Mode + " override=" + _cfg.OverrideCode);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UnregisterHotkeys();
            KeyHook.Stop();
            _scanner.Stop();
            _timer.Stop();
            ReleaseSingleInstance();
            Log("APP EXIT");
            base.OnFormClosed(e);
        }

        bool AcquireSingleInstance()
        {
            try
            {
                _single = new Mutex(true, "CodeSnap_MainWindow", out _ownsSingle);
                return _ownsSingle;
            }
            catch { return true; }
        }

        void ReleaseSingleInstance()
        {
            try { if (_single != null && _ownsSingle) _single.ReleaseMutex(); } catch { }
        }

        void KillOtherInstances()
        {
            try
            {
                int me = Process.GetCurrentProcess().Id;
                int closed = 0;
                Native.EnumWindows(delegate (IntPtr h, IntPtr lp)
                {
                    uint pid;
                    Native.GetWindowThreadProcessId(h, out pid);
                    if (pid != (uint)me && Native.IsWindowVisible(h))
                    {
                        if (Native.WinTitle(h).StartsWith("CodeSnap", StringComparison.Ordinal))
                        {
                            Native.PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                            closed++;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
                if (closed > 0)
                {
                    Log("检测到旧实例窗口 " + closed + " 个，已自动关闭");
                    Thread.Sleep(400);
                }
            }
            catch { }
        }

        void Log(string m)
        {
            try { if (_logPath != "") File.AppendAllText(_logPath, DateTime.Now.ToString("HH:mm:ss.fff ") + m + "\n"); } catch { }
        }

        void TogglePause()
        {
            bool running;
            lock (_state.Gate) { running = _state.Running; _state.Running = !running; }
            lock (_state.Gate) _state.Status = running ? "已暂停" : "扫描中";
        }

        void RegisterHotkeys()
        {
            const uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
            Log("hotkey reg F8=" + Native.RegisterHotKey(Handle, 1, mods, Native.VK_F8));
            Log("hotkey reg F9=" + Native.RegisterHotKey(Handle, 2, mods, Native.VK_F9));
            Log("hotkey reg F10=" + Native.RegisterHotKey(Handle, 3, mods, Native.VK_F10));
            Log("hotkey reg F11=" + Native.RegisterHotKey(Handle, 4, mods, Native.VK_F11));
            Log("hotkey reg F12=" + Native.RegisterHotKey(Handle, 5, mods, Native.VK_F12));
            Log("hotkey reg F7=" + Native.RegisterHotKey(Handle, 6, mods, Native.VK_F7));
            Log("hotkey reg F6=" + Native.RegisterHotKey(Handle, 7, mods, Native.VK_F6));
        }
        void UnregisterHotkeys()
        {
            for (int i = 1; i <= 7; i++) Native.UnregisterHotKey(Handle, i);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                switch (m.WParam.ToInt32())
                {
                    case 1: Log("WM_HOTKEY F8"); SendNow(); break;
                    case 2: MarkTarget(); break;
                    case 3: PickRegion(); break;
                    case 4: TogglePause(); break;
                    case 5: Log("WM_HOTKEY F12"); Close(); break;
                    case 6: CycleMode(); break;
                    case 7:
                        lock (_state.Gate) _state.DebugRequested = true;
                        lock (_state.Gate) _state.Status = "正在抓取诊断图…";
                        break;
                }
            }
            base.WndProc(ref m);
        }

        void OnGlobalHotkey(ushort vk)
        {
            try
            {
                if (vk == Native.VK_F6)
                {
                    Log("HOTKEY F6");
                    lock (_state.Gate) _state.DebugRequested = true;
                    lock (_state.Gate) _state.Status = "正在抓取诊断图…";
                }
                else if (vk == Native.VK_F7) { Log("HOTKEY F7"); CycleMode(); }
                else if (vk == Native.VK_F8) { Log("HOTKEY F8"); SendNow(); }
                else if (vk == Native.VK_F9) { Log("HOTKEY F9"); MarkTarget(); }
                else if (vk == Native.VK_F10) { Log("HOTKEY F10"); PickRegion(); }
                else if (vk == Native.VK_F11) { Log("HOTKEY F11"); TogglePause(); }
                else if (vk == Native.VK_F12) { Log("HOTKEY F12"); Close(); }
            }
            catch (Exception ex) { Log("HOTKEY error " + ex.Message); }
        }

        void CycleMode()
        {
            string[] order = { "region", "cursor", "full" };
            int idx = Array.IndexOf(order, _cfg.Mode);
            string next = order[(idx + 1) % order.Length];
            _cfg.Mode = next;
            _state.Mode = next;
            SaveCfg();
            lock (_state.Gate) _state.Status = "扫描范围: " + ModeName(next);
        }

        string ConfigPath() { return Path.Combine(_appDir, "config.json"); }
        void SaveCfg() { _cfg.Save(ConfigPath()); }

        static string ModeName(string m)
        {
            if (m == "region") return "选区";
            if (m == "cursor") return "鼠标周围";
            return "全屏";
        }

        void PickRegion()
        {
            bool wasRunning = true;
            try
            {
                lock (_state.Gate) { wasRunning = _state.Running; _state.Running = false; }
                Visible = false;
                using (var rf = new RegionForm())
                {
                    if (rf.ShowDialog(this) == DialogResult.OK)
                    {
                        _cfg.RegionX = rf.Sel.X;
                        _cfg.RegionY = rf.Sel.Y;
                        _cfg.RegionW = rf.Sel.Width;
                        _cfg.RegionH = rf.Sel.Height;
                        _cfg.Mode = "region";
                        _state.Mode = "region";
                        SaveCfg();
                        lock (_state.Gate) _state.Status = "选区已更新 " + rf.Sel.Width + "x" + rf.Sel.Height;
                    }
                }
                Visible = true;
                lock (_state.Gate) _state.Running = wasRunning;
            }
            catch { Visible = true; lock (_state.Gate) _state.Running = wasRunning; }
        }

        void MarkTarget()
        {
            InputSim.POINT pt;
            InputSim.GetCursorPos(out pt);
            bool had = _cfg.HasTarget;
            _cfg.HasTarget = !had;
            if (_cfg.HasTarget) { _cfg.TargetX = pt.X; _cfg.TargetY = pt.Y; }
            SaveCfg();
            lock (_state.Gate)
                _state.Status = _cfg.HasTarget ? "输出目标已标记 (" + pt.X + "," + pt.Y + ")" : "输出目标已清除";
        }

        void SendNow()
        {
            string code;
            lock (_state.Gate) code = _state.Code;
            if (code == "")
            {
                Log("SEND skip: 未识别到代码");
                lock (_state.Gate) _state.Status = "尚未识别到代码";
                return;
            }
            long now = Environment.TickCount64;
            if (now - _lastSendTick < 250)
            {
                Log("SEND dup ignored");
                return;
            }
            _lastSendTick = now;
            try
            {
                InputSim.POINT pt;
                InputSim.GetCursorPos(out pt);
                Log("SEND trigger code=" + code + " cursor=" + pt.X + "," + pt.Y + " overlay=" + Bounds.X + "," + Bounds.Y + "," + Bounds.Width + "x" + Bounds.Height);
                bool hidden = false;
                if (Bounds.Contains(pt.X, pt.Y))
                {
                    Log("SEND 光标在悬浮窗内，临时隐藏后发送");
                    hidden = true;
                    Visible = false;
                    Application.DoEvents();
                    Thread.Sleep(60);
                }
                try
                {
                    InputSim.Click(new Point(pt.X, pt.Y));
                    Thread.Sleep(_cfg.ClickDelayMs);
                    LogForeground("SEND focus");
                    _scanner.DoOutput(code);
                    Log("SEND done");
                    lock (_state.Gate) _state.Status = "已发送 " + code;
                }
                finally
                {
                    if (hidden)
                    {
                        Visible = true;
                        TopMost = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("SEND error " + ex.Message);
                lock (_state.Gate) _state.Status = "发送失败 " + ex.Message;
            }
        }

        void LogForeground(string tag)
        {
            try
            {
                IntPtr h = Native.GetForegroundWindow();
                if (h == IntPtr.Zero) return;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                Log(tag + "=" + Native.WinTitle(h) + " pid=" + pid + " cls=" + Native.WinClass(h));
            }
            catch { }
        }

        void RefreshUi()
        {
            lock (_state.Gate)
            {
                _lblStatus.Text = _state.Status;
                if (_state.LastError != "") _lblStatus.Text += " · 错误:" + _state.LastError;
                _lblStatus.Text += " · " + ModeName(_state.Mode) + " · " + _state.FrameCount + "帧";
                if (_state.ScanMs > 0) _lblStatus.Text += " · " + _state.ScanMs + "ms/帧";

                if (_state.Code != "")
                {
                    bool fresh = (DateTime.Now - _state.CodeTime).TotalSeconds < 2.5;
                    _lblCode.Text = _state.Code;
                    _lblCode.ForeColor = fresh ? Color.FromArgb(110, 231, 150) : Color.FromArgb(240, 242, 248);
                }
                else if (_state.Candidate != "")
                {
                    _lblCode.Text = _state.Candidate;
                    _lblCode.ForeColor = Color.FromArgb(230, 190, 90);
                }
                else
                {
                    _lblCode.Text = "—";
                    _lblCode.ForeColor = Color.FromArgb(240, 242, 248);
                }
                bool running = _state.Running;
                _btnPause.Text = running ? "暂停" : "继续";
                _state.OverlayRect = Bounds;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragOff = e.Location;
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                Point p = PointToScreen(e.Location);
                Location = new Point(p.X - _dragOff.X, p.Y - _dragOff.Y);
                _cfg.OverlayX = Location.X;
                _cfg.OverlayY = Location.Y;
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                SaveCfg();
            }
            base.OnMouseUp(e);
        }
    }
    public class RegionForm : Form
    {
        public Rectangle Sel;
        Point _start, _cur;
        bool _selecting;

        public RegionForm()
        {
            Text = "选择识别区域";
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            var vb = SystemInformation.VirtualScreen;
            SetBounds(vb.X, vb.Y, vb.Width, vb.Height);
            BackColor = Color.Black;
            Opacity = 0.35;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _selecting = true;
                _start = e.Location;
                _cur = _start;
                Invalidate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_selecting) { _cur = e.Location; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_selecting && e.Button == MouseButtons.Left)
            {
                _selecting = false;
                _cur = e.Location;
                int x = Math.Min(_start.X, _cur.X), y = Math.Min(_start.Y, _cur.Y);
                int w = Math.Abs(_start.X - _cur.X), h = Math.Abs(_start.Y - _cur.Y);
                if (w > 12 && h > 8)
                {
                    Point origin = PointToScreen(Point.Empty);
                    Sel = new Rectangle(origin.X + x, origin.Y + y, w, h);
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
            base.OnMouseUp(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (_selecting)
            {
                int x = Math.Min(_start.X, _cur.X), y = Math.Min(_start.Y, _cur.Y);
                int w = Math.Abs(_start.X - _cur.X), h = Math.Abs(_start.Y - _cur.Y);
                e.Graphics.DrawRectangle(new Pen(Color.Yellow, 2f), x, y, w, h);
                using (var f = new Font("Microsoft YaHei UI", 11f))
                using (var br = new SolidBrush(Color.Yellow))
                {
                    e.Graphics.DrawString("松开鼠标确认选区 · Esc 取消", f, br, x + 4, y - 26);
                }
            }
            else
            {
                using (var f = new Font("Microsoft YaHei UI", 13f))
                using (var br = new SolidBrush(Color.White))
                {
                    e.Graphics.DrawString("按住左键拖动，框住包含代码（例如 HJK874）的区域", f, br, 60, 50);
                }
            }
        }
    }

    public static class KeyHook
    {
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        static HookProc _proc;
        static IntPtr _hook = IntPtr.Zero;
        static Action<ushort> _callback;
        static bool _ctrl, _alt, _shift;

        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x100;
        const int WM_KEYUP = 0x101;
        const int WM_SYSKEYDOWN = 0x104;
        const int WM_SYSKEYUP = 0x105;
        const uint LLKHF_INJECTED = 0x10;

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        public static bool Start(Action<ushort> onHotkey)
        {
            try
            {
                Stop();
                _callback = onHotkey;
                _proc = HookCallback;
                IntPtr hMod = GetModuleHandle(System.Diagnostics.Process.GetCurrentProcess().MainModule.ModuleName);
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
                return _hook != IntPtr.Zero;
            }
            catch { return false; }
        }

        public static void Stop()
        {
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            _proc = null;
            _callback = null;
            _ctrl = _alt = _shift = false;
        }

        static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _callback != null)
            {
                try
                {
                    var d = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    bool injected = (d.flags & LLKHF_INJECTED) != 0;
                    int msg = wParam.ToInt32();
                    if (!injected)
                    {
                        uint vk = d.vkCode;
                        if (vk == 0x10) _shift = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
                        else if (vk == 0x11) _ctrl = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
                        else if (vk == 0x12) _alt = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
                        else if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && _ctrl && _alt && !_shift)
                        {
                            _callback((ushort)vk);
                            return (IntPtr)1; // 吞掉按键，避免再次被热键/前台处理
                        }
                    }
                }
                catch { }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    public static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public const uint MOD_ALT = 0x1;
        public const uint MOD_CONTROL = 0x2;
        public const uint MOD_NOREPEAT = 0x4000;
        public const uint VK_F6 = 0x75;
        public const uint VK_F7 = 0x76;
        public const uint VK_F8 = 0x77;
        public const uint VK_F9 = 0x78;
        public const uint VK_F10 = 0x79;
        public const uint VK_F11 = 0x7A;
        public const uint VK_F12 = 0x7B;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int maxCount);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int maxCount);

        [DllImport("shcore.dll")]
        static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        public static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwareness(2) != 0) SetProcessDPIAware(); }
            catch { try { SetProcessDPIAware(); } catch { } }
        }

        public static string WinTitle(IntPtr h)
        {
            try { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }
            catch { return ""; }
        }

        public static string WinClass(IntPtr h)
        {
            try { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
            catch { return ""; }
        }
    }

    public static class AppHost
    {
        public static int Start(string[] args)
        {
            string appDir = ".";
            bool testOcr = false;
            int exitAfterMs = 0;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=")) appDir = a.Substring(6);
                if (a.StartsWith("--scan-test=")) Environment.SetEnvironmentVariable("CODESNAP_TEST_IMAGE", a.Substring(12));
                if (a.StartsWith("--logfile=")) Environment.SetEnvironmentVariable("CODESNAP_LOGFILE", a.Substring(10));
                if (a.StartsWith("--exit-after=")) int.TryParse(a.Substring(13), out exitAfterMs);
                if (a == "--test-ocr") testOcr = true;
            }
            if (testOcr) return TestOcr(appDir);

            try { Native.EnableDpiAwareness(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                using (var f = new MainForm(appDir, exitAfterMs)) Application.Run(f);
                return 0;
            }
            int rc = -9;
            var t = new Thread(() =>
            {
                try { using (var f = new MainForm(appDir, exitAfterMs)) Application.Run(f); rc = 0; }
                catch (Exception ex)
                {
                    MessageBox.Show("启动失败: " + ex.Message, "CodeSnap", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            return rc;
        }

        static int TestOcr(string appDir)
        {
            Console.WriteLine("CodeSnap OCR 自检…");
            try
            {
                var e = Ocr.CreateEngine();
                Console.WriteLine("OCR 引擎创建成功");
                string dir = Path.Combine(Path.GetTempPath(), "codesnap_test");
                Directory.CreateDirectory(dir);
                string png = Path.Combine(dir, "sample.png");
                using (Bitmap bmp = new Bitmap(520, 170))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.FromArgb(248, 248, 248));
                        using (var f = new Font("Arial Black", 56f, FontStyle.Bold))
                        using (var br = new SolidBrush(Color.FromArgb(15, 15, 15)))
                        {
                            g.DrawString("HJK874", f, br, 20f, 30f);
                        }
                    }
                    bmp.Save(png, ImageFormat.Png);
                }
                byte[] bytes = File.ReadAllBytes(png);
                var res = OcrHelper.Recognize(bytes, e);
                Console.WriteLine("识别结果: [" + res.Text.Trim() + "]");
                Console.WriteLine("提示: 请把鼠标/选区对准屏幕上的代码（字母+数字，例如 HJK874）");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("自检失败: " + ex.Message);
                return 1;
            }
        }
    }
    public static class MtaProbe
    {
        public static string Run()
        {
            string result = "not-run";
            var t = new Thread(() =>
            {
                try
                {
                    var e = Ocr.CreateEngine();
                    string dir = Path.Combine(Path.GetTempPath(), "codesnap_test");
                    Directory.CreateDirectory(dir);
                    string png = Path.Combine(dir, "sample.png");
                    using (Bitmap bmp = new Bitmap(520, 170))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.FromArgb(248, 248, 248));
                            using (var f = new Font("Arial Black", 56f, FontStyle.Bold))
                            using (var br = new SolidBrush(Color.FromArgb(15, 15, 15)))
                            {
                                g.DrawString("HJK874", f, br, 20f, 30f);
                            }
                        }
                        bmp.Save(png, ImageFormat.Png);
                    }
                    byte[] bytes = File.ReadAllBytes(png);
                    var res = OcrHelper.Recognize(bytes, e);
                    result = "MTA-OCR-OK: [" + res.Text.Trim() + "]";
                }
                catch (Exception ex) { result = "MTA-FAIL: " + ex.Message; }
            });
            t.IsBackground = true;
            t.Start();
            t.Join();
            return result;
        }
    }
}
