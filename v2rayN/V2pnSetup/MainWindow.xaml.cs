using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace V2pnSetup
{
    public partial class MainWindow : Window
    {
        private enum Flow { Install, Uninstall }

        private readonly Flow _flow;
        private Payload _payload;
        private Machine.Installed _installed;
        private string _dir;
        private bool _busy;
        private bool _finished;
        private bool _keptData;
        private bool _ignoreInstalled;
        private bool _allowDowngrade;
        private long _loggedTenth = -1;
        // Tests: V2PN_SETUP_AUTO=<folder> presses the main button by itself and saves pictures there.
        private readonly string _auto = Environment.GetEnvironmentVariable("V2PN_SETUP_AUTO");

        // Progress: the engine's per mille (target) and what is drawn, which follows it smoothly.
        private double _target;
        private double _shown;
        private DispatcherTimer _ticker;
        private Point _pin;
        private readonly List<(Ellipse dot, Point at)> _dots = new List<(Ellipse, Point)>();
        private int _linked;

        private Action _resultPrimary;
        private Action _resultSecondary;
        private DispatcherTimer _waitTimer;
        private Action _afterWait;

        private MainWindow(Flow flow)
        {
            _flow = flow;
            InitializeComponent();
            Title = flow == Flow.Install ? Text.WindowTitle : Text.UninstallTitle;
            MapCaption.Text = Text.MapCaption;
            FooterText.Text = Text.Footer;
            if (Environment.GetEnvironmentVariable("V2PN_BACKGROUND") == "1")
            {
                // Tests on the owner's computer: the window lives off screen and never takes focus.
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = -32000;
                Top = -32000;
                ShowActivated = false;
                ShowInTaskbar = false;
            }
            SourceInitialized += (_, __) => OnSourceInitialized();
            Loaded += (_, __) =>
            {
                StartMap();
                if (!string.IsNullOrEmpty(_auto))
                {
                    Dispatcher.BeginInvoke(new Action(AutoStart), DispatcherPriority.ApplicationIdle);
                }
            };
            BuildMap();
        }

        internal static MainWindow ForInstall(Payload payload, string dir, string error, bool ignoreInstalled = false)
        {
            var window = new MainWindow(Flow.Install);
            window._ignoreInstalled = ignoreInstalled;
            window.ShowInstall(payload, dir, error);
            return window;
        }

        internal static MainWindow ForUninstall(string dir)
        {
            var window = new MainWindow(Flow.Uninstall);
            window.ShowUninstall(dir);
            return window;
        }

        // ================================================================== install

        private void ShowInstall(Payload payload, string dir, string error)
        {
            _payload = payload;
            if (error != null)
            {
                ShowError(Text.ErrorTitle, error, null, retry: false);
                return;
            }
            _installed = _ignoreInstalled ? null : Machine.FindInstalled();
            _dir = _installed?.Dir ?? dir ?? Machine.DefaultDir;

            WelcomeTitle.Text = "V2pn " + payload.Version;
            FolderLabel.Text = Text.Folder;
            FolderText.Text = _dir;
            ChangeButton.Content = Text.Change;
            DesktopText.Text = Text.DesktopShortcut;
            ImportText.Text = Text.ImportV2rayN;
            ImportHint.Text = Text.ImportV2rayNHint;
            NoAdminNote.Text = Text.NoAdmin;

            if (_installed == null)
            {
                WelcomeEyebrow.Text = Text.EyebrowInstall;
                WelcomeBody.Text = Text.Tagline;
                InstallButton.Content = Text.Install;
            }
            else
            {
                var compare = Machine.CompareVersions(_installed.Version, payload.Version);
                if (compare > 0)
                {
                    ShowNewer();
                    return;
                }
                WelcomeEyebrow.Text = compare == 0 ? Text.EyebrowReinstall : Text.EyebrowUpdate;
                WelcomeBody.Text = compare == 0 ? Text.ReinstallTagline : Text.UpdateTagline(_installed.Version ?? "?");
                InstallButton.Content = compare == 0 ? Text.Reinstall : Text.Update;
                // An update goes where V2pn already is; its servers and settings live there.
                ChangeButton.Visibility = Visibility.Collapsed;
                ImportBox.Visibility = Visibility.Collapsed;
                ImportBox.IsChecked = false;
                DesktopBox.IsChecked = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "V2pn.lnk"));
            }
            Show(WelcomeScreen, animate: false);
        }

        private void ShowNewer()
        {
            ShowResult(ResultKind.Warning, Text.NewerTitle, Text.NewerBody(_installed.Version, _payload.Version), null,
                Text.Close, Close, Text.InstallOlder, () =>
                {
                    _allowDowngrade = true;
                    WelcomeEyebrow.Text = Text.EyebrowReinstall;
                    WelcomeBody.Text = Text.NewerBody(_installed.Version, _payload.Version);
                    InstallButton.Content = Text.Install;
                    ChangeButton.Visibility = Visibility.Collapsed;
                    ImportBox.Visibility = Visibility.Collapsed;
                    ImportBox.IsChecked = false;
                    Show(WelcomeScreen);
                });
        }

        private void OnChangeFolder(object sender, RoutedEventArgs e)
        {
            var picked = FolderPicker.Pick(new WindowInteropHelper(this).Handle, Text.PickFolder, Path.GetDirectoryName(_dir));
            if (picked == null)
            {
                return;
            }
            // A folder picked for programs gets V2pn's own folder inside it, as installers do.
            if (!string.Equals(Path.GetFileName(picked.TrimEnd('\\')), "V2pn", StringComparison.OrdinalIgnoreCase))
            {
                picked = Path.Combine(picked, "V2pn");
            }
            _dir = picked;
            FolderText.Text = picked;
            FolderError.Visibility = Visibility.Collapsed;
            ImportBox.Visibility = File.Exists(Path.Combine(picked, "V2pn.exe")) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnInstall(object sender, RoutedEventArgs e)
        {
            var problem = Machine.CheckFolder(_dir, _payload.EngineLength * 3);
            if (problem != null)
            {
                FolderError.Text = problem;
                FolderError.Visibility = Visibility.Visible;
                return;
            }
            FolderError.Visibility = Visibility.Collapsed;
            WhenClosed(_dir, Text.RunningBody, RunInstall, () => Show(WelcomeScreen));
        }

        private async void RunInstall()
        {
            var update = _installed != null;
            StartProgress(update ? Text.EyebrowUpdate : Text.EyebrowInstall, update ? Text.Updating : Text.Installing, determinate: true);
            Engine.Result result;
            try
            {
                result = await Engine.InstallAsync(_payload, _dir, DesktopBox.IsChecked == true,
                    ImportBox.Visibility == Visibility.Visible && ImportBox.IsChecked == true, new WindowInteropHelper(this).Handle,
                    _allowDowngrade ? new[] { "/ALLOWDOWNGRADE=1" } : null);
            }
            catch (Exception ex)
            {
                Log.Write("Install: " + ex);
                result = new Engine.Result { ExitCode = -1, Error = ex.Message };
            }
            if (!result.Ok)
            {
                StopProgress();
                ShowError(Text.ErrorTitle, Text.ErrorBody, result.Error, retry: true);
                AutoFinish("error");
                return;
            }
            await FinishProgress();
            _finished = true;
            ShowResult(ResultKind.Done, update ? Text.DoneUpdatedTitle : Text.DoneTitle, Text.DoneBody, null,
                Text.Open, () => { OpenV2pn(); Close(); }, Text.Close, Close);
            AutoFinish("installed");
        }

        private void OpenV2pn()
        {
            try
            {
                // Through Explorer: V2pn starts as if from the Start menu, not as the installer's child.
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    "\"" + Path.Combine(_dir, "V2pn.exe") + "\"") { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                Log.Write("Open V2pn: " + ex.Message);
            }
        }

        // ================================================================== uninstall

        private void ShowUninstall(string dir)
        {
            _dir = dir;
            if (!File.Exists(Path.Combine(dir, "unins000.exe")))
            {
                ShowError(Text.ErrorUninstallTitle, Text.NotInstalled, null, retry: false);
                return;
            }
            UninstallEyebrow.Text = Text.UninstallEyebrow;
            UninstallTitle.Text = Text.UninstallQuestion;
            UninstallBody.Text = Text.UninstallBody;
            UninstallVersion.Text = "V2pn " + (Machine.ExeVersion(dir) ?? string.Empty);
            UninstallFolder.Text = dir;
            RemoveDataText.Text = Text.RemoveData;
            RemoveDataHint.Text = Text.RemoveDataHint;
            RemoveButton.Content = Text.Remove;
            RemoveCancel.Content = Text.Cancel;
            Show(UninstallScreen, animate: false);
        }

        private void OnRemove(object sender, RoutedEventArgs e)
        {
            WhenClosed(_dir, Text.RunningBodyUninstall, RunUninstall, () => Show(UninstallScreen));
        }

        private async void RunUninstall()
        {
            var removeData = RemoveDataBox.IsChecked == true;
            _keptData = !removeData;
            StartProgress(Text.UninstallEyebrow, Text.Removing, determinate: false);
            Engine.Result result;
            try
            {
                result = await Engine.UninstallAsync(_dir, removeData, new WindowInteropHelper(this).Handle);
            }
            catch (Exception ex)
            {
                Log.Write("Uninstall: " + ex);
                result = new Engine.Result { ExitCode = -1, Error = ex.Message };
            }
            StopProgress();
            if (!result.Ok)
            {
                ShowError(Text.ErrorUninstallTitle, null, result.Error, retry: true);
                AutoFinish("error");
                return;
            }
            _finished = true;
            ShowResult(ResultKind.Done, Text.RemovedTitle, _keptData ? Text.RemovedKept : Text.RemovedAll, null, Text.Close, Close, null, null);
            AutoFinish("removed");
        }

        // ================================================================== waiting for V2pn to quit

        /// <summary>Goes on at once when nothing runs from the folder; else asks to quit V2pn and goes on by itself when it has.</summary>
        private void WhenClosed(string dir, string body, Action then, Action cancelled)
        {
            if (Machine.RunningFrom(dir) == null)
            {
                then();
                return;
            }
            WaitTitle.Text = Text.RunningTitle;
            WaitBody.Text = body;
            WaitText.Text = Text.Waiting;
            WaitCancel.Content = Text.Cancel;
            Show(WaitScreen);
            Spin(true);
            _afterWait = then;
            _waitTimer?.Stop();
            _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _waitTimer.Tick += async (_, __) =>
            {
                _waitTimer.Stop();
                var running = await Task.Run(() => Machine.RunningFrom(dir));
                if (_afterWait == null)
                {
                    return;
                }
                if (running == null)
                {
                    Spin(false);
                    var next = _afterWait;
                    _afterWait = null;
                    next();
                }
                else
                {
                    _waitTimer.Start();
                }
            };
            _waitTimer.Start();
            _waitCancelled = cancelled;
        }

        private Action _waitCancelled;

        private void OnWaitCancel(object sender, RoutedEventArgs e)
        {
            _waitTimer?.Stop();
            _afterWait = null;
            Spin(false);
            _waitCancelled?.Invoke();
        }

        private void Spin(bool on)
        {
            var rotate = (RotateTransform)Spinner.RenderTransform;
            if (on)
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }

        // ================================================================== progress

        private void StartProgress(string eyebrow, string title, bool determinate)
        {
            _busy = true;
            CloseButton.IsEnabled = false;
            ProgressEyebrow.Text = eyebrow;
            ProgressEyebrow.Foreground = (Brush)FindResource(_flow == Flow.Uninstall ? "Unsafe" : "Accent");
            ProgressTitle.Text = title;
            ProgressHint.Text = Text.ProgressHint;
            StepText.Text = Text.StepPrepare;
            Percent.Visibility = determinate ? Visibility.Visible : Visibility.Collapsed;
            BarFill.Visibility = determinate ? Visibility.Visible : Visibility.Collapsed;
            BarRunner.Visibility = determinate ? Visibility.Collapsed : Visibility.Visible;
            BarFill.Width = 0;
            _target = determinate ? 30 : 0; // Unpacking the engine shows as a first sliver.
            _shown = 0;
            Show(ProgressScreen);
            if (!determinate)
            {
                Bar.Margin = new Thickness(0, 46, 0, 0);
                var run = new DoubleAnimation(-90, 330, TimeSpan.FromSeconds(1.3)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                ((TranslateTransform)BarRunner.RenderTransform).BeginAnimation(TranslateTransform.XProperty, run);
            }
            _ticker = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _ticker.Tick += (_, __) => Tick();
            _ticker.Start();
        }

        private void Tick()
        {
            // Ease towards the engine's figure, never backwards.
            var step = Math.Max(0.6, (_target - _shown) * 0.12);
            _shown = Math.Min(_target, _shown + step);
            Percent.Text = $"{Math.Floor(_shown / 10)}%";
            var width = Bar.ActualWidth;
            if (width > 0)
            {
                BarFill.Width = width * _shown / 1000.0;
            }
            if (_flow == Flow.Install)
            {
                // Lines from the pin to the servers appear as the install goes on.
                while (_linked < _dots.Count && _shown >= (_linked + 1) * 1000.0 / (_dots.Count + 1))
                {
                    AddLink(_dots[_linked].at);
                    _linked++;
                }
            }
        }

        private async Task FinishProgress()
        {
            _target = 1000;
            StepText.Text = Text.StepFinish;
            while (_shown < 999)
            {
                await Task.Delay(30);
            }
            while (_linked < _dots.Count)
            {
                AddLink(_dots[_linked].at);
                _linked++;
            }
            await Task.Delay(380);
            StopProgress();
        }

        private void StopProgress()
        {
            _ticker?.Stop();
            ((TranslateTransform)BarRunner.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);
            _busy = false;
            CloseButton.IsEnabled = true;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Engine.ProgressMessage)
            {
                // The engine's files are the bulk; unpacking it took the first 3 %.
                var permille = Math.Max(0, Math.Min(1000, wParam.ToInt64()));
                if (permille / 100 != _loggedTenth)
                {
                    _loggedTenth = permille / 100;
                    Log.Write($"Engine progress {permille / 10}%");
                }
                _target = Math.Max(_target, 30 + permille * 0.94);
                StepText.Text = Text.StepFiles;
                handled = true;
            }
            else if (msg == Engine.StepMessage)
            {
                var step = wParam.ToInt32();
                if (step == Engine.StepFinish)
                {
                    _target = Math.Max(_target, 975);
                    StepText.Text = Text.StepFinish;
                }
                else if (step == Engine.StepFiles)
                {
                    StepText.Text = Text.StepFiles;
                }
                else if (step == Engine.StepUninstall || step == Engine.StepUninstallFinish)
                {
                    StepText.Text = Text.Removing + "…";
                    DimDots();
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        // ================================================================== results

        private enum ResultKind { Done, Warning, Error }

        private void ShowResult(ResultKind kind, string title, string body, string detail, string primary, Action onPrimary, string secondary, Action onSecondary)
        {
            ResultTitle.Text = title;
            ResultBody.Text = body ?? string.Empty;
            ResultBody.Visibility = string.IsNullOrEmpty(body) ? Visibility.Collapsed : Visibility.Visible;
            ResultDetail.Text = detail ?? string.Empty;
            ResultDetail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
            ResultPrimary.Content = primary;
            ResultSecondary.Content = secondary;
            ResultSecondary.Visibility = secondary == null ? Visibility.Collapsed : Visibility.Visible;
            _resultPrimary = onPrimary;
            _resultSecondary = onSecondary;

            var (fill, stroke, mark) = kind == ResultKind.Done
                ? ("SafeSoft", "Safe", "M19,31 L26.5,38.5 L41.5,22")
                : kind == ResultKind.Warning
                    ? ("AccentSoft", "Accent", "M30,19 L30,32 M30,40 L30,40.5")
                    : ("UnsafeSoft", "Unsafe", "M30,19 L30,32 M30,40 L30,40.5");
            ResultCircle.Fill = (Brush)FindResource(fill);
            ResultMark.Stroke = (Brush)FindResource(stroke);
            ResultMark.Data = Geometry.Parse(mark);
            Show(ResultScreen);
            PopIcon();
        }

        private void ShowError(string title, string body, string detail, bool retry)
        {
            if (retry)
            {
                ShowResult(ResultKind.Error, title, body, detail, Text.Retry, () =>
                {
                    if (_flow == Flow.Install)
                    {
                        OnInstall(null, null);
                    }
                    else
                    {
                        OnRemove(null, null);
                    }
                }, Text.ShowLog, OpenLog);
            }
            else
            {
                ShowResult(ResultKind.Error, title, body, detail, Text.Close, Close, null, null);
            }
        }

        private void OpenLog()
        {
            try
            {
                var log = File.Exists(Log.EnginePath) ? Log.EnginePath : Log.Path;
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + log + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Write("Open log: " + ex.Message);
            }
        }

        private void OnResultPrimary(object sender, RoutedEventArgs e) => _resultPrimary?.Invoke();

        private void OnResultSecondary(object sender, RoutedEventArgs e) => _resultSecondary?.Invoke();

        private void PopIcon()
        {
            var scale = new ScaleTransform(0.6, 0.6);
            ResultIcon.RenderTransformOrigin = new Point(0.5, 0.5);
            ResultIcon.RenderTransform = scale;
            var pop = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            // The mark draws itself in.
            ResultMark.StrokeDashArray = new DoubleCollection { 12, 12 };
            ResultMark.BeginAnimation(Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(160), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // ================================================================== screens

        private void Show(FrameworkElement screen, bool animate = true)
        {
            foreach (FrameworkElement child in Screens.Children)
            {
                child.Visibility = child == screen ? Visibility.Visible : Visibility.Collapsed;
            }
            if (!animate || SystemParameters.ClientAreaAnimation == false)
            {
                screen.Opacity = 1;
                return;
            }
            var move = new TranslateTransform(0, 10);
            screen.RenderTransform = move;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            screen.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        }

        // ================================================================== the map

        private void BuildMap()
        {
            using (var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("map.txt"))
            using (var reader = new StreamReader(stream))
            {
                reader.ReadLine(); // the view's size, 600 by 820, as the canvas has it
                var land = Geometry.Parse(reader.ReadLine());
                land.Freeze();
                Land.Data = land;
                string line;
                var dotBrush = (Brush)FindResource("MapDot");
                while ((line = reader.ReadLine()) != null)
                {
                    var parts = line.Split(' ');
                    if (parts.Length != 3)
                    {
                        continue;
                    }
                    var at = new Point(double.Parse(parts[1]), double.Parse(parts[2]));
                    if (parts[0] == "DE")
                    {
                        _pin = at;
                        Canvas.SetLeft(Pin, at.X);
                        Canvas.SetTop(Pin, at.Y);
                        continue;
                    }
                    var dot = new Ellipse { Width = 7, Height = 7, Fill = dotBrush, Opacity = 0 };
                    Canvas.SetLeft(dot, at.X - 3.5);
                    Canvas.SetTop(dot, at.Y - 3.5);
                    Dots.Children.Add(dot);
                    _dots.Add((dot, at));
                }
            }
            // Nearest servers first, so the lines spread out from the pin.
            _dots.Sort((a, b) => Distance(a.at).CompareTo(Distance(b.at)));
        }

        private double Distance(Point p) => (p - _pin).Length;

        private void StartMap()
        {
            var delay = 0;
            foreach (var (dot, _) in _dots)
            {
                dot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.75, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromMilliseconds(200 + delay) });
                delay += 45;
            }
            Pulse(PulseA, TimeSpan.Zero);
            Pulse(PulseB, TimeSpan.FromSeconds(1.2));
        }

        private static void Pulse(Ellipse ring, TimeSpan begin)
        {
            var scale = (ScaleTransform)ring.RenderTransform;
            var duration = TimeSpan.FromSeconds(2.4);
            var grow = new DoubleAnimation(0.25, 1, duration) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.38, 0, duration) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever });
        }

        private void AddLink(Point to)
        {
            // A gentle arc from the pin, bowed upwards like a flight path.
            var mid = new Point((_pin.X + to.X) / 2, (_pin.Y + to.Y) / 2 - Math.Min(60, (to - _pin).Length * 0.25));
            var figure = new PathFigure { StartPoint = _pin };
            figure.Segments.Add(new QuadraticBezierSegment(mid, to, true));
            var geometry = new PathGeometry(new[] { figure });
            var length = (to - _pin).Length * 1.1 / 1.6;
            var link = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Stroke = (Brush)FindResource("Accent"),
                StrokeThickness = 1.6,
                Opacity = 0.55,
                StrokeDashArray = new DoubleCollection { length, length },
                StrokeDashOffset = length,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            Links.Children.Add(link);
            link.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(length, 0, TimeSpan.FromMilliseconds(650)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var target = _dots.First(d => d.at == to).dot;
            target.BeginAnimation(OpacityProperty, null);
            target.Fill = (Brush)FindResource("Accent");
            target.Opacity = 1;
        }

        private void DimDots()
        {
            var delay = 0;
            foreach (var (dot, _) in _dots.AsEnumerable().Reverse())
            {
                dot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.12, TimeSpan.FromMilliseconds(400)) { BeginTime = TimeSpan.FromMilliseconds(delay) });
                delay += 60;
            }
        }

        // ================================================================== the window itself

        private void OnSourceInitialized()
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WndProc);
            // Windows 11: rounded corners, a dark frame and a border in V2pn's colour. Earlier Windows ignore them.
            var on = 1;
            DwmSetWindowAttribute(handle, 20, ref on, sizeof(int));
            var round = 2;
            DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
            var border = 0x00332A2A; // #2A2A33 as COLORREF
            DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        }

        private void OnDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // The engine is never stopped half way: it would leave a half-written folder.
            if (_busy)
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }

        public bool Finished => _finished;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // ================================================================== tests (V2PN_SETUP_AUTO)

        private async void AutoStart()
        {
            SaveShot(Path.Combine(_auto, "auto-start.png"));
            if (UninstallScreen.Visibility == Visibility.Visible)
            {
                RemoveDataBox.IsChecked = Environment.GetEnvironmentVariable("V2PN_SETUP_REMOVEDATA") == "1";
                OnRemove(null, null);
            }
            else if (WelcomeScreen.Visibility == Visibility.Visible)
            {
                OnInstall(null, null);
                await Task.Delay(2500);
                if (ProgressScreen.Visibility == Visibility.Visible)
                {
                    SaveShot(Path.Combine(_auto, "auto-progress.png"));
                }
            }
            else
            {
                AutoFinish("other");
            }
        }

        private async void AutoFinish(string outcome)
        {
            if (string.IsNullOrEmpty(_auto))
            {
                return;
            }
            await Task.Delay(1000);
            SaveShot(Path.Combine(_auto, "auto-" + outcome + ".png"));
            File.WriteAllText(Path.Combine(_auto, "auto-outcome.txt"), outcome);
            Close();
        }

        // ================================================================== pictures for review (--shot)

        internal async Task ShotAsync(string name)
        {
            switch (name)
            {
                case "welcome":
                    Show(WelcomeScreen, animate: false);
                    break;
                case "welcome-error":
                    FolderError.Text = Text.NoAccess;
                    FolderError.Visibility = Visibility.Visible;
                    Show(WelcomeScreen, animate: false);
                    break;
                case "wait":
                    WaitTitle.Text = Text.RunningTitle;
                    WaitBody.Text = Text.RunningBody;
                    WaitText.Text = Text.Waiting;
                    WaitCancel.Content = Text.Cancel;
                    Show(WaitScreen, animate: false);
                    break;
                case "progress":
                    StartProgress(Text.EyebrowInstall, Text.Installing, determinate: true);
                    _target = 640;
                    StepText.Text = Text.StepFiles;
                    await Task.Delay(1500);
                    _ticker.Stop();
                    _busy = false;
                    break;
                case "done":
                    while (_linked < _dots.Count)
                    {
                        AddLink(_dots[_linked].at);
                        _linked++;
                    }
                    ShowResult(ResultKind.Done, Text.DoneTitle, Text.DoneBody, null, Text.Open, null, Text.Close, null);
                    break;
                case "error":
                    ShowError(Text.ErrorTitle, Text.ErrorBody, "The file C:\\...\\V2pn.exe is in use.", retry: true);
                    break;
                case "newer":
                    _installed = new Machine.Installed { Version = "1.4.0" };
                    ShowNewer();
                    break;
            }
            await Task.Delay(900);
        }

        internal void ShotUninstall(string dir)
        {
            UninstallEyebrow.Text = Text.UninstallEyebrow;
            UninstallTitle.Text = Text.UninstallQuestion;
            UninstallBody.Text = Text.UninstallBody;
            UninstallVersion.Text = "V2pn 1.3.1";
            UninstallFolder.Text = dir;
            RemoveDataText.Text = Text.RemoveData;
            RemoveDataHint.Text = Text.RemoveDataHint;
            RemoveButton.Content = Text.Remove;
            RemoveCancel.Content = Text.Cancel;
            Show(UninstallScreen, animate: false);
        }

        internal void SaveShot(string file)
        {
            var dpi = 2.0;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(ActualWidth * dpi), (int)(ActualHeight * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            bitmap.Render(Root);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file))
            {
                encoder.Save(stream);
            }
        }
    }
}
