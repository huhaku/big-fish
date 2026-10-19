using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMagick;

namespace PetdexPet
{
    public record Anim(int Row, int Frames, bool Loop, bool Timed = false);

    public partial class MainWindow : Window
    {
        const int FrameW = 192;
        const int FrameH = 208;
        const int Columns = 8;
        const int TickMs = 30;
        const int LongPressMs = 600;
        const int LonelyAfterSec = 60;
        const int WalkDurationMs = 3000;

        // 原版(Qoder)每帧延时,与精灵图行号一一对应
        static readonly int[][] RowDelays =
        {
            new[] { 280, 110, 110, 140, 140, 320 },
            new[] { 120, 120, 120, 120, 120, 120, 120, 220 },
            new[] { 120, 120, 120, 120, 120, 120, 120, 220 },
            new[] { 140, 140, 140, 280 },
            new[] { 140, 140, 140, 140, 280 },
            new[] { 140, 140, 140, 140, 140, 140, 140, 240 },
            new[] { 150, 150, 150, 150, 150, 260 },
            new[] { 120, 120, 120, 120, 120, 220 },
            new[] { 150, 150, 150, 150, 150, 280 },
        };

        static readonly Anim Idle = new(0, 6, true);
        public static readonly Dictionary<string, Anim> Actions = new()
        {
            ["走路(右)"] = new Anim(1, 8, true),
            ["走路(左)"] = new Anim(2, 8, true),
            ["挥手"] = new Anim(3, 4, false, true),
            ["缩小登场"] = new Anim(4, 5, false),
            ["待机变体"] = new Anim(5, 8, true, true),
            ["开心"] = new Anim(6, 6, false, true),
            ["害羞"] = new Anim(7, 6, false, true),
            ["低落"] = new Anim(8, 6, false, true),
        };
        static readonly string[] RandomActions = { "走路(右)", "走路(左)", "挥手", "待机变体" };

        readonly Random _rng = new();
        CroppedBitmap[][] _rows;
        Anim _base = Idle;
        Anim _playing = Idle;
        bool _looping = true;
        int _frame;
        double _scale = 1.0;
        double _bubbleScale = 1.0;
        double _bubbleFont = 56;
        double _bubbleSeconds = 3.5;
        double _bubbleFreq = 30;
        Dictionary<string, bool> _enabled = Actions.Keys.ToDictionary(k => k, _ => true);
        DateTime _lastInteraction = DateTime.Now;
        Point _downPoint;
        bool _dragging;
        bool _pressFired;
        int _elapsedMs;
        double _delayScale = 1.0;
        double _actionSeconds = 3.0;
        List<string> _bubbles = new()
        {
            "欧鲸鲸~",
            "肚子好饿......",
            "好想吃大白饭~",
            "帮我看看锅里的饭好了没?",
            "正在调用qwen......",
            "当前任务:欺负GLM",
            "正在确认蒸馏GPT的进度",
        };
        bool _offWorkEnabled;
        TimeSpan _offWorkTime = new(18, 0, 0);
        string _offWorkText = "距离下班还有 {time}";
        bool _offWorkPinned;
        BubbleWindow _bubble;
        readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        bool _bubblePinned;
        string _currentBubbleText = "";
        bool _countdownShowing;
        bool _bubbleFlip;
        readonly DispatcherTimer _bubbleHideTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
        readonly DispatcherTimer _bubbleTimer = new();

        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(TickMs) };
        readonly DispatcherTimer _pressTimer = new() { Interval = TimeSpan.FromMilliseconds(LongPressMs) };
        readonly DispatcherTimer _playForTimer = new() { Interval = TimeSpan.FromMilliseconds(WalkDurationMs) };
        readonly DispatcherTimer _randomTimer = new();
        readonly DispatcherTimer _lonelyTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PetdexPet", "config.json");

        public IReadOnlyList<string> ActionNames => Actions.Keys.ToList();
        public bool IsActionEnabled(string name) => _enabled.TryGetValue(name, out var on) && on;
        public IReadOnlyList<string> Bubbles => _bubbles;
        public double BubbleScale => _bubbleScale;
        public double BubbleFont => _bubbleFont;
        public double BubbleSeconds => _bubbleSeconds;
        public double BubbleFreq => _bubbleFreq;
        public double ActionSeconds => _actionSeconds;

        public void SetActionSeconds(double seconds)
        {
            _actionSeconds = Math.Clamp(seconds, 1, 10);
        }
        public bool OffWorkEnabled => _offWorkEnabled;
        public string OffWorkTimeText => $"{_offWorkTime.Hours:00}:{_offWorkTime.Minutes:00}";
        public string OffWorkText => _offWorkText;
        public bool OffWorkPinned => _offWorkPinned;

        public void SetOffWork(bool enabled, string timeText, string text)
        {
            _offWorkEnabled = enabled;
            if (TimeSpan.TryParse((timeText ?? "").Trim(), out var t))
                _offWorkTime = t;
            if (!string.IsNullOrWhiteSpace(text)) _offWorkText = text.Trim();
            if (_bubblePinned || _offWorkPinned)
            {
                if (enabled) ShowCountdown();
                else ShowNormal(false);
            }
            if (!enabled) _countdownTimer.Stop();
        }

        public void SetOffWorkPinned(bool pinned)
        {
            _offWorkPinned = pinned;
            if (pinned && _offWorkEnabled) ShowCountdown();
            else if (!pinned && !_bubblePinned) HideBubble();
        }

        string OffWorkCountdownText()
        {
            var remain = _offWorkTime - DateTime.Now.TimeOfDay;
            if (remain <= TimeSpan.Zero) remain += TimeSpan.FromDays(1);
            var time = $"{(int)remain.TotalHours:00}:{remain.Minutes:00}:{remain.Seconds:00}";
            return _offWorkText.Replace("{time}", time);
        }

        public void SetBubblePrefs(double bubbleScale, double font, double seconds, double freq)
        {
            _bubbleScale = bubbleScale;
            _bubbleFont = font;
            _bubbleSeconds = seconds;
            _bubbleFreq = Math.Clamp(freq, 30, 600);
            if (_bubblePinned) RefreshPinnedBubble();
        }

        public void RefreshPinnedBubble()
        {
            if (!_bubblePinned) return;
            if (_countdownShowing && _offWorkEnabled) ShowCountdown();
            else ShowNormal(false);
        }

        public void PreviewBubbleText(string text)
        {
            if (!_bubblePinned || string.IsNullOrWhiteSpace(text)) return;
            _currentBubbleText = text.Trim();
            ShowBubbleCore(false);
        }

        public void SetBubbles(IEnumerable<string> lines)
        {
            _bubbles = lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }

        public void SaveSettings() => SaveConfig();

        void ShowCountdown()
        {
            _currentBubbleText = OffWorkCountdownText();
            ShowBubbleCore(true);
        }

        void ShowNormal(bool random)
        {
            if (_bubbles.Count == 0)
            {
                if (_offWorkEnabled) ShowCountdown();
                return;
            }
            if (random || _currentBubbleText.Length == 0 || _countdownShowing)
                _currentBubbleText = _bubbles[_rng.Next(_bubbles.Count)];
            ShowBubbleCore(false);
        }

        void BubbleTrigger()
        {
            if (_playing.Row != 0) return;
            if (_offWorkEnabled && _offWorkPinned) { ShowNormal(true); return; }
            if (_offWorkEnabled && (_bubbleFlip = !_bubbleFlip)) { ShowCountdown(); return; }
            ShowNormal(true);
        }

        void ShowBubbleCore(bool countdown)
        {
            _countdownShowing = countdown;
            _bubble.SetScale(_scale * _bubbleScale);
            _bubble.BubbleText.Text = _currentBubbleText;
            _bubble.BubbleText.FontSize = _bubbleFont;
            _bubble.BubbleText.LineHeight = _bubbleFont * 1.15;
            _bubble.Left = -2000;
            _bubble.Top = -2000;
            _bubble.Show();
            _bubble.UpdateLayout();
            _bubble.Left = Left + (ActualWidth - _bubble.ActualWidth) / 2;
            _bubble.Top = Top - _bubble.ActualHeight + 14 * _scale;
            if (countdown) _countdownTimer.Start();
            else _countdownTimer.Stop();
            if (_bubblePinned || (_offWorkEnabled && _offWorkPinned && countdown))
            {
                _bubbleHideTimer.Stop();
            }
            else
            {
                _bubbleHideTimer.Interval = TimeSpan.FromSeconds(_bubbleSeconds);
                _bubbleHideTimer.Stop();
                _bubbleHideTimer.Start();
            }
        }

        void HideBubble()
        {
            _bubbleHideTimer.Stop();
            _countdownTimer.Stop();
            _bubble.Hide();
        }

        void ScheduleBubble()
        {
            _bubbleTimer.Interval = TimeSpan.FromSeconds(_bubbleFreq);
            _bubbleTimer.Start();
        }

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            ApplyScale(_scale);
            Left = SystemParameters.WorkArea.Width - Width - 60;
            Top = SystemParameters.WorkArea.Height - Height - 30;
            MouseLeftButtonDown += OnMouse;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseUp;
            MouseEnter += (s, e) => _lastInteraction = DateTime.Now;
            _pressTimer.Tick += (s, e) =>
            {
                _pressTimer.Stop();
                _pressFired = true;
                if (IsActionEnabled("害羞")) PlayOnce(Actions["害羞"]);
            };
            _playForTimer.Tick += (s, e) => BackToBase();
            _randomTimer.Tick += (s, e) => { _randomTimer.Stop(); PlayRandom(); ScheduleRandom(); };
            _lonelyTimer.Tick += OnLonelyTick;
            _bubbleTimer.Tick += (s, e) => { _bubbleTimer.Stop(); BubbleTrigger(); ScheduleBubble(); };
            _bubbleHideTimer.Tick += (s, e) =>
            {
                _bubbleHideTimer.Stop();
                _bubble.Hide();
                if (_offWorkEnabled && _offWorkPinned) ShowCountdown();
            };
            _countdownTimer.Tick += (s, e) =>
            {
                if (!_offWorkEnabled || !_countdownShowing || !_bubble.IsVisible)
                {
                    _countdownTimer.Stop();
                    return;
                }
                _currentBubbleText = OffWorkCountdownText();
                _bubble.BubbleText.Text = _currentBubbleText;
            };
            _bubble = new BubbleWindow();
            Loaded += (s, e) => Start();
        }

        public void SetActionEnabled(string name, bool on)
        {
            _enabled[name] = on;
        }

        void OnMouse(object sender, MouseButtonEventArgs e)
        {
            _lastInteraction = DateTime.Now;
            if (e.ClickCount == 2)
            {
                _pressTimer.Stop();
                if (IsActionEnabled("开心")) PlayOnce(Actions["开心"]);
                return;
            }
            _downPoint = e.GetPosition(this);
            _pressFired = false;
            _pressTimer.Start();
        }

        void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _pressTimer.Stop();
            if (_pressFired || _dragging || e.ClickCount > 1) return;
            _lastInteraction = DateTime.Now;
            if (_playing.Row != 0) return;
            if (_offWorkEnabled) ShowCountdown();
            else ShowNormal(true);
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging || e.LeftButton != MouseButtonState.Pressed) return;
            Vector moved = e.GetPosition(this) - _downPoint;
            if (moved.Length < 6) return;
            _pressTimer.Stop();
            _dragging = true;
            HideBubble();
            DragMove();
            _dragging = false;
            _lastInteraction = DateTime.Now;
            if (_offWorkEnabled && _offWorkPinned) ShowCountdown();
        }

        double ScaleFor(Anim anim)
        {
            var delays = RowDelays[anim.Row];
            int total = 0;
            for (int i = 0; i < anim.Frames && i < delays.Length; i++) total += delays[i];
            return anim.Timed && total > 0 ? _actionSeconds * 1000 / total : 1.0;
        }

        void PlayOnce(Anim anim)
        {
            _playForTimer.Stop();
            _playing = anim;
            _looping = false;
            _frame = 0;
            _elapsedMs = 0;
            _delayScale = ScaleFor(anim);
        }

        void PlayFor(Anim anim, int ms)
        {
            _playing = anim;
            _looping = true;
            _frame = 0;
            _elapsedMs = 0;
            _delayScale = ScaleFor(anim);
            _playForTimer.Interval = TimeSpan.FromMilliseconds(ms);
            _playForTimer.Start();
        }

        void BackToBase()
        {
            _playForTimer.Stop();
            _playing = _base;
            _looping = true;
            _frame = 0;
            _elapsedMs = 0;
            _delayScale = 1.0;
        }

        void PlayRandom()
        {
            var pool = RandomActions.Where(IsActionEnabled).ToList();
            if (pool.Count == 0) return;
            var anim = Actions[pool[_rng.Next(pool.Count)]];
            if (anim.Loop)
                PlayFor(anim, anim.Timed ? (int)(_actionSeconds * 1000)
                                         : _rng.Next(2000, WalkDurationMs + 1));
            else PlayOnce(anim);
        }

        void ScheduleRandom()
        {
            _randomTimer.Interval = TimeSpan.FromSeconds(_rng.Next(8, 21));
            _randomTimer.Start();
        }

        void OnLonelyTick(object sender, EventArgs e)
        {
            if (!IsActionEnabled("低落")) return;
            if (DateTime.Now - _lastInteraction < TimeSpan.FromSeconds(LonelyAfterSec)) return;
            _lastInteraction = DateTime.Now;
            PlayOnce(Actions["低落"]);
        }

        public void ApplyScale(double scale)
        {
            _scale = scale;
            Width = FrameW * scale;
            Height = FrameH * scale;
            PetImage.Stretch = Stretch.Fill;
        }

        void LoadConfig()
        {
            if (!File.Exists(ConfigPath)) return;
            try
            {
                var json = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                _scale = Math.Clamp(json.RootElement.GetProperty("scale").GetDouble(), 0.1, 1.3);
                if (json.RootElement.TryGetProperty("bubbleScale", out var bs))
                    _bubbleScale = Math.Clamp(bs.GetDouble(), 0.5, 2);
                if (json.RootElement.TryGetProperty("bubbleFont", out var bf))
                    _bubbleFont = Math.Clamp(bf.GetDouble(), 50, 300);
                if (json.RootElement.TryGetProperty("bubbleSeconds", out var bt))
                    _bubbleSeconds = Math.Clamp(bt.GetDouble(), 1, 15);
                if (json.RootElement.TryGetProperty("bubbleFreq", out var fq))
                    _bubbleFreq = Math.Clamp(fq.GetDouble(), 30, 600);
                if (json.RootElement.TryGetProperty("actionSeconds", out var asg))
                    _actionSeconds = Math.Clamp(asg.GetDouble(), 1, 10);
                if (json.RootElement.TryGetProperty("offWorkEnabled", out var oe))
                    _offWorkEnabled = oe.ValueKind == JsonValueKind.True;
                if (json.RootElement.TryGetProperty("offWorkTime", out var ot) &&
                    TimeSpan.TryParse(ot.GetString(), out var otv))
                    _offWorkTime = otv;
                if (json.RootElement.TryGetProperty("offWorkText", out var ox) &&
                    !string.IsNullOrWhiteSpace(ox.GetString()))
                    _offWorkText = ox.GetString();
                if (json.RootElement.TryGetProperty("offWorkPinned", out var op))
                    _offWorkPinned = op.ValueKind == JsonValueKind.True;
                if (json.RootElement.TryGetProperty("enabled", out var arr) &&
                    arr.ValueKind == JsonValueKind.Array)
                {
                    var on = arr.EnumerateArray()
                        .Where(v => v.ValueKind == JsonValueKind.String)
                        .Select(v => v.GetString());
                    foreach (var k in Actions.Keys)
                        _enabled[k] = on.Contains(k);
                }
                if (json.RootElement.TryGetProperty("bubbles", out var brr) &&
                    brr.ValueKind == JsonValueKind.Array)
                {
                    var list = brr.EnumerateArray()
                        .Where(v => v.ValueKind == JsonValueKind.String)
                        .Select(v => v.GetString())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                    if (list.Count > 0 || brr.EnumerateArray().Any())
                        _bubbles = list;
                }
            }
            catch (JsonException) { }
        }

        void SaveConfig()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new
            {
                scale = _scale,
                enabled = Actions.Keys.Where(IsActionEnabled).ToList(),
                bubbles = _bubbles,
                bubbleScale = _bubbleScale,
                bubbleFont = _bubbleFont,
                bubbleSeconds = _bubbleSeconds,
                bubbleFreq = _bubbleFreq,
                actionSeconds = _actionSeconds,
                offWorkEnabled = _offWorkEnabled,
                offWorkTime = OffWorkTimeText,
                offWorkText = _offWorkText,
                offWorkPinned = _offWorkPinned
            }));
        }

        void Start()
        {
            _bubble.Owner = this;
            var asm = GetType().Assembly;
            using var stream = asm.GetManifestResourceStream("PetdexPet.Assets.spritesheet.webp")
                ?? throw new InvalidOperationException("内置精灵图缺失");
            using var image = new MagickImage(stream);
            byte[] png = image.ToByteArray(MagickFormat.Png);
            var sheet = BitmapFrame.Create(new MemoryStream(png),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            _rows = new CroppedBitmap[9][];
            int[] frameCounts = { 6, 8, 8, 4, 5, 8, 6, 6, 6 };
            for (int r = 0; r < 9; r++)
            {
                _rows[r] = new CroppedBitmap[frameCounts[r]];
                for (int i = 0; i < frameCounts[r]; i++)
                    _rows[r][i] = new CroppedBitmap(sheet,
                        new Int32Rect(i * FrameW, r * FrameH, FrameW, FrameH));
            }

            _timer.Tick += Tick;
            PetImage.Source = _rows[_playing.Row][0];
            _timer.Start();
            _lonelyTimer.Start();
            ScheduleRandom();
            ScheduleBubble();
            if (_offWorkEnabled && _offWorkPinned) ShowCountdown();
            if (IsActionEnabled("缩小登场")) PlayOnce(Actions["缩小登场"]);
        }

        void Tick(object sender, EventArgs e)
        {
            var delays = RowDelays[_playing.Row];
            int delay = _frame < delays.Length ? delays[_frame] : 120;
            _elapsedMs += TickMs;
            if (_elapsedMs < delay * _delayScale) return;
            _elapsedMs = 0;
            PetImage.Source = _rows[_playing.Row][_frame];
            _frame++;
            if (_frame >= _playing.Frames)
            {
                if (_looping) _frame = 0;
                else BackToBase();
            }
        }

        void Exit_Click(object sender, RoutedEventArgs e) => Close();

        void Settings_Click(object sender, RoutedEventArgs e)
        {
            PinBubble(true);
            var w = new SettingsWindow(this, _scale) { Owner = this };
            w.Closed += (s, e) => PinBubble(false);
            w.ShowDialog();
        }

        void PinBubble(bool on)
        {
            _bubblePinned = on;
            if (on) RefreshPinnedBubble();
            else if (_offWorkEnabled && _offWorkPinned) ShowCountdown();
            else HideBubble();
        }
    }
}
