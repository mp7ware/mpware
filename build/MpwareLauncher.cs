using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace mpwareLauncher
{
    internal static class Program
    {
        private const string ResourceName = "mpwareRuntimeZip";

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args != null && args.Length > 0 && String.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
                {
                    return SelfTest();
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                return app.Run(new DashboardWindow());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "mpware", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        internal static string ResolveScriptPath()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string sidecar = IOPath.Combine(exeDir, "_FOLDERMUSTBEONCDRIVE", "mpware.ps1");

            if (File.Exists(sidecar))
            {
                return sidecar;
            }

            return ExtractEmbeddedRuntime();
        }

        private static int SelfTest()
        {
            string script = ResolveScriptPath();
            if (String.IsNullOrWhiteSpace(script) || !File.Exists(script))
            {
                return 2;
            }

            string root = IOPath.GetDirectoryName(script);
            if (!File.Exists(IOPath.Combine(root, "zFunctions.psm1")))
            {
                return 3;
            }

            if (!File.Exists(IOPath.Combine(root, "winfetch.psm1")))
            {
                return 4;
            }

            return 0;
        }

        private static string ExtractEmbeddedRuntime()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream resource = assembly.GetManifestResourceStream(ResourceName))
            {
                if (resource == null)
                {
                    return null;
                }

                byte[] zipBytes;
                using (MemoryStream ms = new MemoryStream())
                {
                    resource.CopyTo(ms);
                    zipBytes = ms.ToArray();
                }

                string hash = Sha256(zipBytes).Substring(0, 16);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (String.IsNullOrWhiteSpace(localAppData))
                {
                    localAppData = IOPath.GetTempPath();
                }

                string cacheRoot = IOPath.Combine(localAppData, "mpware", "runtime");
                string target = IOPath.Combine(cacheRoot, hash);
                string script = IOPath.Combine(target, "mpware.ps1");

                if (File.Exists(script))
                {
                    return script;
                }

                Directory.CreateDirectory(cacheRoot);
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }
                Directory.CreateDirectory(target);

                string zipPath = IOPath.Combine(cacheRoot, hash + ".zip");
                File.WriteAllBytes(zipPath, zipBytes);
                ZipFile.ExtractToDirectory(zipPath, target);
                File.Delete(zipPath);

                return File.Exists(script) ? script : null;
            }
        }

        private static string Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }

    internal sealed class DashboardWindow : Window
    {
        private readonly Brush _appBackground = BrushFromRgb(6, 8, 13);
        private readonly Brush _panel = BrushFromRgb(11, 14, 20);
        private readonly Brush _panelSoft = BrushFromRgb(16, 20, 28);
        private readonly Brush _panelHover = BrushFromRgb(25, 31, 42);
        private readonly Brush _text = BrushFromRgb(238, 242, 249);
        private readonly Brush _muted = BrushFromRgb(148, 163, 184);
        private readonly Brush _accent = BrushFromRgb(59, 130, 246);
        private readonly Brush _track = BrushFromRgb(38, 45, 58);

        private Grid _content;
        private StackPanel _logPanel;
        private TextBlock _title;
        private TextBlock _subtitle;
        private TextBlock _crumb;
        private TextBlock _healthScore;
        private Rectangle _healthFill;
        private readonly Dictionary<string, MetricCard> _metrics = new Dictionary<string, MetricCard>();
        private readonly List<string> _logs = new List<string>();
        private readonly PerformanceCounter _cpuCounter;
        private readonly string _scriptPath;
        private readonly string _runtimeRoot;
        private string _activePage = "Home";

        public DashboardWindow()
        {
            _scriptPath = Program.ResolveScriptPath();
            _runtimeRoot = String.IsNullOrWhiteSpace(_scriptPath) ? null : IOPath.GetDirectoryName(_scriptPath);

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            }
            catch
            {
                _cpuCounter = null;
            }

            Title = "mpware";
            Width = 1160;
            Height = 740;
            MinWidth = 980;
            MinHeight = 640;
            Background = _appBackground;
            Foreground = _text;
            FontFamily = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            BuildShell();
            ShowHome();

            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(2);
            timer.Tick += delegate { RefreshMetrics(); };
            timer.Start();

            RefreshMetrics();
            AddLog("Dashboard loaded");
            AddLog("Runtime: " + (_runtimeRoot ?? "not found"));
        }

        private void BuildShell()
        {
            Grid root = new Grid();
            root.Background = _appBackground;
            root.SnapsToDevicePixels = true;
            root.UseLayoutRounding = true;
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(204) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Content = root;

            Border sidebar = new Border();
            sidebar.Background = BrushFromRgb(8, 10, 16);
            sidebar.BorderBrush = BrushFromRgb(27, 34, 47);
            sidebar.BorderThickness = new Thickness(0, 0, 1, 0);
            Grid.SetColumn(sidebar, 0);
            root.Children.Add(sidebar);

            StackPanel nav = new StackPanel { Margin = new Thickness(18, 24, 18, 18) };
            sidebar.Child = nav;

            TextBlock logo = TextBlock("mpware", 27, FontWeights.Bold, _text);
            logo.Margin = new Thickness(8, 0, 0, 2);
            nav.Children.Add(logo);
            TextBlock tagline = TextBlock("windows tuning", 12, FontWeights.Normal, _muted);
            tagline.Margin = new Thickness(10, 0, 0, 24);
            nav.Children.Add(tagline);

            nav.Children.Add(NavButton("Home", ShowHome));
            nav.Children.Add(NavButton("Performance", ShowPerformance));
            nav.Children.Add(NavButton("Debloat", ShowDebloat));
            nav.Children.Add(NavButton("Privacy", ShowPrivacy));
            nav.Children.Add(NavButton("Apps", ShowApps));
            nav.Children.Add(NavButton("Restore", ShowRestore));
            nav.Children.Add(NavButton("Logs", ShowLogs));

            Border runtimeCard = Card();
            runtimeCard.Margin = new Thickness(0, 28, 0, 0);
            StackPanel runtimeStack = new StackPanel { Margin = new Thickness(14) };
            runtimeCard.Child = runtimeStack;
            runtimeStack.Children.Add(TextBlock("Runtime", 12, FontWeights.SemiBold, _muted));
            runtimeStack.Children.Add(TextBlock(_runtimeRoot == null ? "missing" : "embedded", 18, FontWeights.Bold, _text));
            Button launch = ActionButton("Open full tools", delegate { LaunchFullRuntime(); });
            launch.Margin = new Thickness(0, 12, 0, 0);
            runtimeStack.Children.Add(launch);
            nav.Children.Add(runtimeCard);

            Grid main = new Grid { Margin = new Thickness(26, 24, 26, 22) };
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(main, 1);
            root.Children.Add(main);

            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(header, 0);
            main.Children.Add(header);

            StackPanel heading = new StackPanel();
            _crumb = TextBlock("Home", 12, FontWeights.SemiBold, _muted);
            _title = TextBlock("Welcome back!", 31, FontWeights.SemiBold, _text);
            _subtitle = TextBlock("How's your PC running today?", 13, FontWeights.Normal, _muted);
            heading.Children.Add(_crumb);
            heading.Children.Add(_title);
            heading.Children.Add(_subtitle);
            header.Children.Add(heading);

            StackPanel headerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(headerActions, 1);
            headerActions.Children.Add(PillButton("Settings", delegate { RunWindowsSettings(); }));
            headerActions.Children.Add(PillButton("My PC", delegate { OpenExplorer(); }));
            header.Children.Add(headerActions);

            _content = new Grid();
            Grid.SetRow(_content, 1);
            main.Children.Add(_content);
        }

        private Button NavButton(string label, Action action)
        {
            Button button = new Button();
            button.Content = label;
            button.Height = 42;
            button.Margin = new Thickness(0, 2, 0, 6);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(16, 0, 0, 0);
            button.BorderThickness = new Thickness(0);
            button.Foreground = _text;
            button.Background = String.Equals(label, _activePage, StringComparison.OrdinalIgnoreCase) ? _panelHover : Brushes.Transparent;
            button.FontWeight = FontWeights.SemiBold;
            button.Cursor = Cursors.Hand;
            button.Template = ButtonTemplate(new CornerRadius(8));
            button.SnapsToDevicePixels = true;
            button.Click += delegate { _activePage = label; action(); };
            return button;
        }

        private Button PillButton(string label, RoutedEventHandler handler)
        {
            Button b = ActionButton(label, handler);
            b.Height = 34;
            b.MinWidth = 86;
            b.Margin = new Thickness(8, 0, 0, 0);
            return b;
        }

        private void SetHeader(string crumb, string title, string subtitle)
        {
            _crumb.Text = crumb;
            _title.Text = title;
            _subtitle.Text = subtitle;
        }

        private void ShowHome()
        {
            SetHeader("Home", "Welcome back!", "Live dashboard and quick actions for mpware.");
            _content.Children.Clear();
            _content.RowDefinitions.Clear();
            _content.ColumnDefinitions.Clear();
            _content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(385) });
            _content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border health = Card();
            health.Margin = new Thickness(0, 0, 14, 14);
            Grid.SetColumn(health, 0);
            Grid.SetRow(health, 0);
            _content.Children.Add(health);

            Grid healthGrid = new Grid { Margin = new Thickness(16) };
            healthGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            healthGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            healthGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            health.Child = healthGrid;
            healthGrid.Children.Add(TextBlock("Health", 13, FontWeights.SemiBold, _muted));
            Border rail = new Border { Width = 18, Height = 270, CornerRadius = new CornerRadius(9), Background = BrushFromRgb(19, 24, 34), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(rail, 1);
            Grid fillGrid = new Grid();
            rail.Child = fillGrid;
            _healthFill = new Rectangle { Fill = _accent, RadiusX = 9, RadiusY = 9, VerticalAlignment = VerticalAlignment.Bottom, Height = 120 };
            fillGrid.Children.Add(_healthFill);
            healthGrid.Children.Add(rail);
            _healthScore = TextBlock("Good", 14, FontWeights.Bold, _text);
            _healthScore.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(_healthScore, 2);
            healthGrid.Children.Add(_healthScore);

            Grid metricGrid = new Grid();
            metricGrid.RowDefinitions.Add(new RowDefinition());
            metricGrid.RowDefinitions.Add(new RowDefinition());
            metricGrid.ColumnDefinitions.Add(new ColumnDefinition());
            metricGrid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(metricGrid, 1);
            Grid.SetRow(metricGrid, 0);
            _content.Children.Add(metricGrid);

            AddMetric(metricGrid, "CPU", "Detecting CPU", "0", "%", "Utilization", 0, 0);
            AddMetric(metricGrid, "GPU", "Detecting GPU", "N/A", "", "Telemetry", 0, 1);
            AddMetric(metricGrid, "RAM", "Detecting memory", "0", "%", "Utilization", 1, 0);
            AddMetric(metricGrid, "Storage", "System drive", "0", "%", "Utilization", 1, 1);

            Grid actions = new Grid();
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.RowDefinitions.Add(new RowDefinition());
            actions.RowDefinitions.Add(new RowDefinition());
            Grid.SetColumnSpan(actions, 2);
            Grid.SetRow(actions, 1);
            _content.Children.Add(actions);

            AddActionCard(actions, "Optimize my PC", "Open performance and cleanup tools", 0, 0, delegate { LaunchFullRuntime(); });
            AddActionCard(actions, "Debloat tools", "Choose debloat presets safely", 0, 1, delegate { RunFunction("debloat"); });
            AddActionCard(actions, "Config profiles", "Save or load tweak profiles", 1, 0, delegate { RunScript("configUI.ps1"); });
            AddActionCard(actions, "Restore center", "Undo or repair changes", 1, 1, delegate { RunScript("Restore.ps1"); });
        }

        private void ShowPerformance()
        {
            SetHeader("Performance", "Performance", "Power plans, services, tasks, and Windows 11 tuning.");
            ShowActionPage(new ActionSpec[] {
                new ActionSpec("Power Tweaks", "Open power plan optimizer.", delegate { RunFunction("import-powerplan"); }),
                new ActionSpec("Disable Services", "Review service startup cleanup.", delegate { RunFunction("disable-services"); }),
                new ActionSpec("Remove Tasks", "Review scheduled task cleanup.", delegate { RunFunction("remove-tasks"); }),
                new ActionSpec("Windows 11 Tweaks", "Open shell and Windows 11 options.", delegate { RunFunction("W11Tweaks"); }),
                new ActionSpec("Optional Tweaks", "Open optional desktop and context tweaks.", delegate { RunFunction("OptionalTweaks"); }),
                new ActionSpec("Full Tools", "Open the complete runtime UI.", delegate { LaunchFullRuntime(); })
            });
        }

        private void ShowDebloat()
        {
            SetHeader("Debloat", "Debloat", "Remove apps and tune the base Windows install.");
            ShowActionPage(new ActionSpec[] {
                new ActionSpec("Debloat Presets", "Choose which app groups to remove.", delegate { RunFunction("debloat"); }),
                new ActionSpec("Registry Tweaks", "Apply registry performance and usability tweaks.", delegate { RunFunction("import-reg"); }),
                new ActionSpec("Edge Remove", "Open the Edge removal helper.", delegate { RunScript("EdgeRemove.ps1"); }),
                new ActionSpec("Unpin Start", "Open Start menu cleanup helper.", delegate { RunScript("Unpin.ps1"); })
            });
        }

        private void ShowPrivacy()
        {
            SetHeader("Privacy", "Privacy", "Telemetry and policy controls without removed security bypass items.");
            ShowActionPage(new ActionSpec[] {
                new ActionSpec("Group Policy Tweaks", "Updates and telemetry policy options.", delegate { RunFunction("gpTweaks"); }),
                new ActionSpec("Disable App Actions", "Trim suggested app actions.", delegate { RunScript("Disable-AppActions.ps1"); }),
                new ActionSpec("Store Settings", "Open Microsoft Store settings helper.", delegate { RunScript("Set-StoreSettings.ps1"); }),
                new ActionSpec("Optional Tweaks", "More shell and privacy-adjacent options.", delegate { RunFunction("OptionalTweaks"); })
            });
        }

        private void ShowApps()
        {
            SetHeader("Apps", "Apps", "Install runtimes, browsers, drivers, and helper scripts.");
            ShowActionPage(new ActionSpec[] {
                new ActionSpec("Install Packages", "DirectX, C++ runtimes, and .NET 3.5.", delegate { RunFunction("install-packs"); }),
                new ActionSpec("Install Browsers", "Choose a browser installer.", delegate { RunFunction("install-browsers"); }),
                new ActionSpec("NVIDIA Driver", "Open NVIDIA driver helper.", delegate { RunScript("NvidiaAutoinstall.ps1"); }),
                new ActionSpec("Network Driver", "Open network driver helper.", delegate { RunScript("LocalNetworkInstaller.ps1"); }),
                new ActionSpec("Other Scripts", "Open curated helper script installer.", delegate { RunScript("Install-OtherScripts.ps1"); })
            });
        }

        private void ShowRestore()
        {
            SetHeader("Restore", "Restore", "Undo, repair, cleanup, and recovery helpers.");
            ShowActionPage(new ActionSpec[] {
                new ActionSpec("Restore Tweaks", "Open restore options.", delegate { RunScript("Restore.ps1"); }),
                new ActionSpec("Repair Windows", "Run Windows repair helper.", delegate { RunFunction("Repair-Windows"); }),
                new ActionSpec("Ultimate Cleanup", "Open cleanup workflow.", delegate { RunFunction("UltimateCleanup"); }),
                new ActionSpec("Restart Explorer", "Restart explorer.exe.", delegate { RunFunction("Restart-Explorer"); }),
                new ActionSpec("Restart To BIOS", "Open firmware restart confirmation.", delegate { RunFunction("Restart-Bios"); })
            });
        }

        private void ShowLogs()
        {
            SetHeader("Logs", "Activity Log", "What mpware has launched in this session.");
            _content.Children.Clear();
            Border logCard = Card();
            logCard.Margin = new Thickness(0, 0, 0, 0);
            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _logPanel = new StackPanel { Margin = new Thickness(18) };
            scroll.Content = _logPanel;
            logCard.Child = scroll;
            _content.Children.Add(logCard);
            RenderLogs();
            AddLog("Log view opened");
        }

        private void ShowActionPage(ActionSpec[] specs)
        {
            _content.Children.Clear();
            _content.RowDefinitions.Clear();
            _content.ColumnDefinitions.Clear();
            _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            WrapPanel wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            Grid.SetRow(wrap, 0);
            _content.Children.Add(wrap);

            foreach (ActionSpec spec in specs)
            {
                Border item = ActionTile(spec.Title, spec.Description, spec.Handler);
                item.Width = 278;
                item.Height = 126;
                item.Margin = new Thickness(0, 0, 14, 14);
                wrap.Children.Add(item);
            }

            Border logCard = Card();
            Grid.SetRow(logCard, 1);
            StackPanel stack = new StackPanel { Margin = new Thickness(18) };
            stack.Children.Add(TextBlock("Activity", 16, FontWeights.Bold, _text));
            _logPanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            stack.Children.Add(_logPanel);
            logCard.Child = stack;
            _content.Children.Add(logCard);
            RenderLogs();
            AddLog("Ready");
        }

        private void AddMetric(Grid parent, string key, string subtitle, string value, string unit, string caption, int row, int col)
        {
            MetricCard metric = new MetricCard();
            Border card = Card();
            card.Margin = new Thickness(col == 0 ? 0 : 7, row == 0 ? 0 : 7, col == 0 ? 7 : 0, row == 0 ? 7 : 0);
            Grid.SetRow(card, row);
            Grid.SetColumn(card, col);
            parent.Children.Add(card);

            Grid grid = new Grid { Margin = new Thickness(18) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            card.Child = grid;

            StackPanel left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(IconBox());
            left.Children.Add(TextBlock(key, 21, FontWeights.Bold, _text));
            metric.Subtitle = TextBlock(subtitle, 12, FontWeights.Normal, _muted);
            left.Children.Add(metric.Subtitle);
            Button view = ActionButton("View more", delegate { ShowPerformance(); });
            view.Width = 92;
            view.Margin = new Thickness(0, 20, 0, 0);
            left.Children.Add(view);
            grid.Children.Add(left);

            Grid gaugeWrap = new Grid();
            Grid.SetColumn(gaugeWrap, 1);
            metric.Gauge = new Canvas { Width = 220, Height = 132, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            gaugeWrap.Children.Add(metric.Gauge);
            StackPanel center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
            StackPanel valueLine = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            metric.Value = TextBlock(value, 35, FontWeights.Bold, _text);
            metric.Unit = TextBlock(unit, 15, FontWeights.SemiBold, _text);
            metric.Unit.Margin = new Thickness(2, 10, 0, 0);
            valueLine.Children.Add(metric.Value);
            valueLine.Children.Add(metric.Unit);
            center.Children.Add(valueLine);
            metric.Caption = TextBlock(caption, 12, FontWeights.Normal, _muted);
            metric.Caption.HorizontalAlignment = HorizontalAlignment.Center;
            center.Children.Add(metric.Caption);
            gaugeWrap.Children.Add(center);
            grid.Children.Add(gaugeWrap);
            _metrics[key] = metric;
            DrawGauge(metric.Gauge, 0);
        }

        private void AddActionCard(Grid grid, string title, string description, int row, int col, RoutedEventHandler handler)
        {
            Border card = ActionTile(title, description, handler);
            card.Margin = new Thickness(col == 0 ? 0 : 7, row == 0 ? 0 : 7, col == 0 ? 7 : 0, row == 0 ? 7 : 0);
            Grid.SetRow(card, row);
            Grid.SetColumn(card, col);
            grid.Children.Add(card);
        }

        private Border ActionTile(string title, string description, RoutedEventHandler handler)
        {
            Border card = Card();
            card.Height = 92;
            Grid grid = new Grid { Margin = new Thickness(18, 14, 18, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            card.Child = grid;

            StackPanel stack = new StackPanel();
            stack.Children.Add(TextBlock(title, 16, FontWeights.Bold, _text));
            stack.Children.Add(TextBlock(description, 12, FontWeights.Normal, _muted));
            grid.Children.Add(stack);

            Button button = ActionButton(">", handler);
            button.Width = 36;
            button.Height = 36;
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
            return card;
        }

        private void RefreshMetrics()
        {
            double cpu = GetCpuUsage();
            string ramText;
            string storageText;
            string gpuName;
            double ram = GetRamUsage(out ramText);
            double storage = GetStorageUsage(out storageText);
            double gpu = GetGpuUsage(out gpuName);

            SetMetric("CPU", GetCpuName(), cpu < 0 ? "N/A" : Math.Round(cpu).ToString("0"), cpu < 0 ? "" : "%", "Utilization", cpu < 0 ? 0 : cpu);
            SetMetric("GPU", gpuName, gpu < 0 ? "N/A" : Math.Round(gpu).ToString("0"), gpu < 0 ? "" : "%", gpu < 0 ? "Telemetry unavailable" : "Utilization", gpu < 0 ? 0 : gpu);
            SetMetric("RAM", ramText, Math.Round(ram).ToString("0"), "%", "Utilization", ram);
            SetMetric("Storage", storageText, Math.Round(storage).ToString("0"), "%", "Utilization", storage);

            double health = 100 - ((Math.Max(cpu, 0) * 0.25) + (ram * 0.35) + (storage * 0.25) + (Math.Max(gpu, 0) * 0.15));
            if (health < 0) health = 0;
            if (health > 100) health = 100;
            if (_healthFill != null)
            {
                _healthFill.Height = 270 * (health / 100.0);
            }
            if (_healthScore != null)
            {
                _healthScore.Text = health >= 75 ? "Good" : health >= 50 ? "Okay" : "Busy";
            }
        }

        private void SetMetric(string key, string subtitle, string value, string unit, string caption, double gaugeValue)
        {
            MetricCard metric;
            if (!_metrics.TryGetValue(key, out metric))
            {
                return;
            }
            metric.Subtitle.Text = subtitle;
            metric.Value.Text = value;
            metric.Unit.Text = unit;
            metric.Caption.Text = caption;
            DrawGauge(metric.Gauge, gaugeValue);
        }

        private void DrawGauge(Canvas canvas, double value)
        {
            canvas.Children.Clear();
            if (value < 0) value = 0;
            if (value > 100) value = 100;

            double cx = 110;
            double cy = 112;
            for (int i = 0; i < 48; i++)
            {
                double pct = i / 47.0;
                double angle = (210 + pct * 120) * Math.PI / 180.0;
                double r1 = 72;
                double r2 = 96;
                Line line = new Line();
                line.X1 = cx + Math.Cos(angle) * r1;
                line.Y1 = cy + Math.Sin(angle) * r1;
                line.X2 = cx + Math.Cos(angle) * r2;
                line.Y2 = cy + Math.Sin(angle) * r2;
                line.StrokeThickness = 3;
                line.SnapsToDevicePixels = true;
                line.StrokeStartLineCap = PenLineCap.Round;
                line.StrokeEndLineCap = PenLineCap.Round;
                line.Stroke = (pct * 100 <= value) ? _accent : _track;
                line.Opacity = (pct * 100 <= value) ? 1.0 : 0.65;
                canvas.Children.Add(line);
            }
        }

        private void LaunchFullRuntime()
        {
            if (!EnsureRuntime())
            {
                return;
            }

            AddLog("Opening full mpware tools");
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + _scriptPath + "\"";
            psi.WorkingDirectory = _runtimeRoot;
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            Process.Start(psi);
        }

        private void RunScript(string relativeScript)
        {
            if (!EnsureRuntime())
            {
                return;
            }

            string script = IOPath.Combine(_runtimeRoot, relativeScript);
            if (!File.Exists(script))
            {
                AddLog("Missing script: " + relativeScript);
                MessageBox.Show("Missing runtime script: " + relativeScript, "mpware", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AddLog("Launching " + relativeScript);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"";
            psi.WorkingDirectory = _runtimeRoot;
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            Process.Start(psi);
        }

        private void RunFunction(string functionCall)
        {
            if (!EnsureRuntime())
            {
                return;
            }

            AddLog("Launching " + functionCall);
            string escapedRoot = _runtimeRoot.Replace("'", "''");
            string command =
                "$ErrorActionPreference='Continue';" +
                "Set-Location -LiteralPath '" + escapedRoot + "';" +
                "$Global:folder='" + escapedRoot + "';" +
                "$Global:sysDrive=$env:SystemDrive.TrimEnd('\\')+'\\';" +
                "$Global:tempDir=([System.IO.Path]::GetTempPath()).TrimEnd('\\');" +
                "$Global:iconDir=Join-Path $Global:folder 'mpwareIcons';" +
                "$Global:customIcon=Join-Path $Global:iconDir 'Powershell_black.ico';" +
                "Import-Module (Join-Path $Global:folder 'zFunctions.psm1') -Force -Global;" +
                "Import-Module (Join-Path $Global:folder 'winfetch.psm1') -Force;" +
                functionCall;

            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded;
            psi.WorkingDirectory = _runtimeRoot;
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            Process.Start(psi);
        }

        private bool EnsureRuntime()
        {
            if (!String.IsNullOrWhiteSpace(_scriptPath) && File.Exists(_scriptPath) && Directory.Exists(_runtimeRoot))
            {
                return true;
            }

            MessageBox.Show("mpware runtime was not found. Rebuild mpware.exe or extract the release zip.", "mpware", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        private void RunWindowsSettings()
        {
            AddLog("Opening Windows Settings");
            Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true });
        }

        private void OpenExplorer()
        {
            AddLog("Opening This PC");
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:MyComputerFolder") { UseShellExecute = true });
        }

        private void AddLog(string message)
        {
            _logs.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            if (_logs.Count > 80)
            {
                _logs.RemoveAt(_logs.Count - 1);
            }

            if (_logPanel == null)
            {
                return;
            }
            TextBlock line = TextBlock(_logs[0], 12, FontWeights.Normal, _muted);
            line.Margin = new Thickness(0, 0, 0, 6);
            _logPanel.Children.Insert(0, line);
        }

        private void RenderLogs()
        {
            if (_logPanel == null)
            {
                return;
            }

            _logPanel.Children.Clear();
            foreach (string log in _logs)
            {
                TextBlock line = TextBlock(log, 12, FontWeights.Normal, _muted);
                line.Margin = new Thickness(0, 0, 0, 6);
                _logPanel.Children.Add(line);
            }
        }

        private double GetCpuUsage()
        {
            try
            {
                return _cpuCounter == null ? -1 : _cpuCounter.NextValue();
            }
            catch
            {
                return -1;
            }
        }

        private string GetCpuName()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        return Convert.ToString(item["Name"]).Trim();
                    }
                }
            }
            catch
            {
            }
            return "Processor";
        }

        private double GetRamUsage(out string subtitle)
        {
            subtitle = "System memory";
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        double total = Convert.ToDouble(item["TotalVisibleMemorySize"]);
                        double free = Convert.ToDouble(item["FreePhysicalMemory"]);
                        double used = total - free;
                        subtitle = String.Format("{0:0.0} / {1:0.0} GB", used / 1024 / 1024, total / 1024 / 1024);
                        return (used / total) * 100.0;
                    }
                }
            }
            catch
            {
            }
            return 0;
        }

        private double GetStorageUsage(out string subtitle)
        {
            subtitle = "System drive";
            try
            {
                string root = IOPath.GetPathRoot(Environment.SystemDirectory);
                DriveInfo drive = new DriveInfo(root);
                double used = drive.TotalSize - drive.AvailableFreeSpace;
                subtitle = drive.Name + " " + String.Format("{0:0} / {1:0} GB", used / 1024 / 1024 / 1024, drive.TotalSize / 1024 / 1024 / 1024);
                return (used / drive.TotalSize) * 100.0;
            }
            catch
            {
                return 0;
            }
        }

        private double GetGpuUsage(out string gpuName)
        {
            gpuName = "Graphics";
            try
            {
                using (ManagementObjectSearcher video = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                {
                    foreach (ManagementObject item in video.Get())
                    {
                        string name = Convert.ToString(item["Name"]);
                        if (!String.IsNullOrWhiteSpace(name))
                        {
                            gpuName = name.Trim();
                            break;
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                double total = 0;
                int count = 0;
                using (ManagementObjectSearcher gpu = new ManagementObjectSearcher("root\\CIMV2", "SELECT UtilizationPercentage,Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine"))
                {
                    foreach (ManagementObject item in gpu.Get())
                    {
                        string name = Convert.ToString(item["Name"]);
                        if (name != null && name.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            total += Convert.ToDouble(item["UtilizationPercentage"]);
                            count++;
                        }
                    }
                }
                if (count > 0)
                {
                    return Math.Min(100, total);
                }
            }
            catch
            {
            }

            return -1;
        }

        private Border Card()
        {
            Border border = new Border();
            border.CornerRadius = new CornerRadius(10);
            border.Background = _panel;
            border.BorderBrush = BrushFromRgb(31, 39, 54);
            border.BorderThickness = new Thickness(1);
            border.SnapsToDevicePixels = true;
            return border;
        }

        private Border IconBox()
        {
            Border box = new Border();
            box.Width = 38;
            box.Height = 38;
            box.Margin = new Thickness(0, 0, 0, 16);
            box.CornerRadius = new CornerRadius(8);
            box.Background = _panelSoft;
            TextBlock glyph = TextBlock("◆", 16, FontWeights.Bold, _accent);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            box.Child = glyph;
            return box;
        }

        private Button ActionButton(string label, RoutedEventHandler handler)
        {
            Button b = new Button();
            b.Content = label;
            b.Height = 34;
            b.MinWidth = 92;
            b.Padding = new Thickness(14, 0, 14, 0);
            b.Background = _panelSoft;
            b.Foreground = _text;
            b.BorderBrush = BrushFromRgb(28, 37, 55);
            b.BorderThickness = new Thickness(1);
            b.FontWeight = FontWeights.SemiBold;
            b.Cursor = Cursors.Hand;
            b.Template = ButtonTemplate(new CornerRadius(8));
            b.SnapsToDevicePixels = true;
            b.Click += handler;
            return b;
        }

        private ControlTemplate ButtonTemplate(CornerRadius radius)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, radius);
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.SnapsToDevicePixelsProperty, true);
            presenter.SetBinding(ContentPresenter.MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });

            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        private TextBlock TextBlock(string text, double size, FontWeight weight, Brush brush)
        {
            TextBlock block = new TextBlock();
            block.Text = text;
            block.FontSize = size;
            block.FontWeight = weight;
            block.Foreground = brush;
            block.TextWrapping = TextWrapping.Wrap;
            return block;
        }

        private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private static Color ColorFromRgb(byte r, byte g, byte b)
        {
            return Color.FromRgb(r, g, b);
        }

        private static Color ColorFromArgb(byte a, byte r, byte g, byte b)
        {
            return Color.FromArgb(a, r, g, b);
        }

        private sealed class MetricCard
        {
            public TextBlock Subtitle;
            public TextBlock Value;
            public TextBlock Unit;
            public TextBlock Caption;
            public Canvas Gauge;
        }

        private sealed class ActionSpec
        {
            public readonly string Title;
            public readonly string Description;
            public readonly RoutedEventHandler Handler;

            public ActionSpec(string title, string description, RoutedEventHandler handler)
            {
                Title = title;
                Description = description;
                Handler = handler;
            }
        }
    }
}
