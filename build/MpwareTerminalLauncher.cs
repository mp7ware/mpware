using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using IOPath = System.IO.Path;

namespace mpwareLauncher
{
    internal sealed class TerminalDashboardWindow : Window
    {
        private readonly Brush _background = BrushFromRgb(0, 0, 0);
        private readonly Brush _panel = BrushFromRgb(5, 5, 5);
        private readonly Brush _panelSoft = BrushFromRgb(10, 10, 10);
        private readonly Brush _border = BrushFromRgb(236, 236, 236);
        private readonly Brush _borderDim = BrushFromRgb(83, 83, 83);
        private readonly Brush _text = BrushFromRgb(238, 238, 238);
        private readonly Brush _muted = BrushFromRgb(166, 166, 166);
        private readonly Brush _accent = BrushFromRgb(0, 239, 246);
        private readonly Brush _safe = BrushFromRgb(0, 211, 126);
        private readonly Brush _moderate = BrushFromRgb(218, 168, 0);
        private readonly Brush _advanced = BrushFromRgb(238, 112, 0);
        private readonly Brush _danger = BrushFromRgb(255, 116, 116);
        private readonly FontFamily _mono = new FontFamily("Consolas");

        private readonly Dictionary<string, Button> _navButtons = new Dictionary<string, Button>();
        private readonly List<TweakItem> _tweaks = new List<TweakItem>();
        private readonly string _runtimeRoot;
        private Grid _content;
        private TextBlock _selectedCount;
        private TextBlock _statusLine;
        private string _activePage = "Registry Tweaks";

        public TerminalDashboardWindow()
        {
            _runtimeRoot = Program.ResolveRuntimeRoot();
            BuildTweaks();

            Title = "mpware";
            Width = 1220;
            Height = 780;
            MinWidth = 980;
            MinHeight = 640;
            Background = _background;
            Foreground = _text;
            FontFamily = _mono;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            BuildShell();
            ShowRegistryTweaks();
            SetStatus("ready");
        }

        private void BuildShell()
        {
            Grid root = new Grid();
            root.Background = _background;
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(256) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Content = root;

            Border sidebar = new Border();
            sidebar.Background = _panel;
            sidebar.BorderBrush = _borderDim;
            sidebar.BorderThickness = new Thickness(0, 0, 1, 0);
            Grid.SetColumn(sidebar, 0);
            root.Children.Add(sidebar);

            DockPanel dock = new DockPanel();
            sidebar.Child = dock;

            StackPanel brand = new StackPanel { Margin = new Thickness(20, 20, 20, 18) };
            DockPanel.SetDock(brand, Dock.Top);
            dock.Children.Add(brand);

            StackPanel logoLine = new StackPanel { Orientation = Orientation.Horizontal };
            logoLine.Children.Add(Text(">_", 18, FontWeights.Bold, _accent));
            TextBlock logo = Text(" mpware", 24, FontWeights.Bold, _text);
            logoLine.Children.Add(logo);
            brand.Children.Add(logoLine);

            StackPanel footer = new StackPanel { Margin = new Thickness(20) };
            DockPanel.SetDock(footer, Dock.Bottom);
            footer.Children.Add(Text("v1.0.0", 12, FontWeights.Normal, _muted));
            _statusLine = Text("status: booting", 11, FontWeights.Normal, _muted);
            _statusLine.Margin = new Thickness(0, 10, 0, 0);
            footer.Children.Add(_statusLine);
            dock.Children.Add(footer);

            StackPanel nav = new StackPanel { Margin = new Thickness(16, 8, 16, 0) };
            dock.Children.Add(nav);
            nav.Children.Add(NavButton("Registry Tweaks", ShowRegistryTweaks));
            nav.Children.Add(NavButton("NVIDIA Driver", ShowNvidiaDriver));
            nav.Children.Add(NavButton("Programs", ShowPrograms));
            nav.Children.Add(NavButton("Debloater", ShowDebloater));
            nav.Children.Add(NavButton("Cleanup", ShowCleanup));
            nav.Children.Add(NavButton("Restore Tweaks", ShowRestoreTweaks));
            nav.Children.Add(NavButton("About", ShowAbout));

            _content = new Grid();
            Grid.SetColumn(_content, 1);
            root.Children.Add(_content);
        }

        private Button NavButton(string label, RoutedEventHandler handler)
        {
            Button button = FlatButton(">  " + label, false);
            button.Height = 44;
            button.Margin = new Thickness(0, 0, 0, 8);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(14, 0, 14, 0);
            button.Click += delegate
            {
                _activePage = label;
                RefreshNav();
                handler(button, new RoutedEventArgs());
            };
            _navButtons[label] = button;
            return button;
        }

        private void RefreshNav()
        {
            foreach (KeyValuePair<string, Button> pair in _navButtons)
            {
                bool active = String.Equals(pair.Key, _activePage, StringComparison.OrdinalIgnoreCase);
                pair.Value.Foreground = active ? _accent : _muted;
                pair.Value.BorderBrush = active ? _accent : Brushes.Transparent;
                pair.Value.Background = active ? BrushFromRgb(0, 26, 28) : Brushes.Transparent;
            }
        }

        private StackPanel BeginPage(string title, string subtitle, double maxWidth)
        {
            _content.Children.Clear();
            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _content.Children.Add(scroll);

            StackPanel page = new StackPanel();
            page.Margin = new Thickness(28, 24, 28, 32);
            page.MaxWidth = maxWidth;
            page.HorizontalAlignment = HorizontalAlignment.Center;
            scroll.Content = page;

            page.Children.Add(Text(title, 24, FontWeights.Bold, _accent));
            TextBlock sub = Text(subtitle, 12, FontWeights.Normal, _muted);
            sub.Margin = new Thickness(0, 8, 0, 24);
            page.Children.Add(sub);
            return page;
        }

        private void ShowRegistryTweaks(object sender, RoutedEventArgs e)
        {
            ShowRegistryTweaks();
        }

        private void ShowRegistryTweaks()
        {
            StackPanel page = BeginPage("REGISTRY TWEAKS", "Select, inspect, export, or apply registry groups.", 1040);

            Border promptBox = Box(_borderDim);
            promptBox.Margin = new Thickness(0, 0, 0, 18);
            StackPanel promptStack = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
            promptBox.Child = promptStack;
            promptStack.Children.Add(Text("> mpware.exe launches as Administrator and opens a progress log while applying changes.", 11, FontWeights.Bold, _accent));
            promptStack.Children.Add(Text("  Selected tweaks create a Windows restore point first, apply registry values, skip already-missing delete targets, then verify the selected keys.", 11, FontWeights.Normal, _muted));
            page.Children.Add(promptBox);

            Grid actions = new Grid();
            actions.Margin = new Thickness(0, 0, 0, 18);
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            page.Children.Add(actions);

            _selectedCount = Text("0 tweaks selected", 13, FontWeights.Bold, _text);
            _selectedCount.VerticalAlignment = VerticalAlignment.Center;
            actions.Children.Add(_selectedCount);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(buttons, 1);
            actions.Children.Add(buttons);
            buttons.Children.Add(ActionButton("APPLY SELECTED", ApplySelectedTweaks, true));
            buttons.Children.Add(ActionButton("SELECT ALL", delegate { SelectAllTweaks(); }, false));
            buttons.Children.Add(ActionButton("EXPORT .REG", ExportSelectedReg, false));

            foreach (TweakItem tweak in OrderedTweaks())
            {
                page.Children.Add(TweakCard(tweak));
            }

            UpdateSelectedCount();
            RefreshNav();
        }

        private Border TweakCard(TweakItem tweak)
        {
            Border card = Box(_border);
            card.Margin = new Thickness(0, 0, 0, 12);

            Grid grid = new Grid();
            grid.Margin = new Thickness(12, 8, 12, 8);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            card.Child = grid;

            CheckBox selector = new CheckBox();
            selector.Margin = new Thickness(0, 2, 10, 0);
            selector.VerticalAlignment = VerticalAlignment.Top;
            selector.Checked += delegate { UpdateSelectedCount(); };
            selector.Unchecked += delegate { UpdateSelectedCount(); };
            tweak.Selector = selector;
            grid.Children.Add(selector);

            StackPanel body = new StackPanel();
            Grid.SetColumn(body, 1);
            body.ToolTip = tweak.Description;
            grid.Children.Add(body);

            StackPanel titleLine = new StackPanel { Orientation = Orientation.Horizontal };
            titleLine.Children.Add(Text(tweak.Name, 12, FontWeights.Bold, _text));
            titleLine.Children.Add(RiskPill(tweak.Risk));
            body.Children.Add(titleLine);

            Button path = FlatButton("> REGISTRY PATH", false);
            path.Height = 24;
            path.MinWidth = 136;
            path.Padding = new Thickness(8, 0, 8, 0);
            path.Margin = new Thickness(14, 0, 0, 0);
            path.HorizontalAlignment = HorizontalAlignment.Right;
            path.VerticalAlignment = VerticalAlignment.Center;
            path.ToolTip = "Click to show full registry paths, values, and descriptions.";
            path.Click += delegate { ShowRegistryPatch(tweak); };
            Grid.SetColumn(path, 2);
            grid.Children.Add(path);

            return card;
        }

        private Border RiskPill(string risk)
        {
            Brush fill = _safe;
            if (String.Equals(risk, "Moderate", StringComparison.OrdinalIgnoreCase)) fill = _moderate;
            if (String.Equals(risk, "Advanced", StringComparison.OrdinalIgnoreCase)) fill = _advanced;

            Border pill = new Border();
            pill.Background = fill;
            pill.Margin = new Thickness(10, 0, 0, 0);
            pill.Padding = new Thickness(6, 2, 6, 2);
            pill.Child = Text(risk.ToUpperInvariant(), 9, FontWeights.Bold, _background);
            return pill;
        }

        private void ShowRegistryPatch(TweakItem tweak)
        {
            Window dialog = new Window();
            dialog.Title = "mpware - registry patch";
            dialog.Owner = this;
            dialog.Width = 820;
            dialog.Height = 560;
            dialog.MinWidth = 640;
            dialog.MinHeight = 420;
            dialog.Background = _background;
            dialog.Foreground = _text;
            dialog.FontFamily = _mono;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.UseLayoutRounding = true;
            dialog.SnapsToDevicePixels = true;

            Grid root = new Grid();
            root.Margin = new Thickness(18);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            dialog.Content = root;

            StackPanel header = new StackPanel();
            header.Children.Add(Text(tweak.Name.ToUpperInvariant(), 16, FontWeights.Bold, _accent));
            TextBlock description = Text(tweak.Description, 11, FontWeights.Normal, _muted);
            description.Margin = new Thickness(0, 8, 0, 14);
            header.Children.Add(description);
            root.Children.Add(header);

            TextBox patch = new TextBox();
            patch.Text = RegistryPreview(tweak);
            patch.FontFamily = _mono;
            patch.FontSize = 12;
            patch.Foreground = _text;
            patch.Background = _panel;
            patch.BorderBrush = _border;
            patch.BorderThickness = new Thickness(1);
            patch.Padding = new Thickness(12);
            patch.IsReadOnly = true;
            patch.AcceptsReturn = true;
            patch.AcceptsTab = true;
            patch.TextWrapping = TextWrapping.NoWrap;
            patch.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            patch.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            Grid.SetRow(patch, 1);
            root.Children.Add(patch);

            Button close = ActionButton("CLOSE", delegate { dialog.Close(); }, true);
            close.Width = 110;
            close.Margin = new Thickness(0, 14, 0, 0);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(close, 2);
            root.Children.Add(close);

            dialog.ShowDialog();
        }

        private void ShowNvidiaDriver(object sender, RoutedEventArgs e)
        {
            StackPanel page = BeginPage("NVIDIA DRIVER TOOL", "One action for latest graphics driver installation.", 720);

            Border box = Box(_border);
            page.Children.Add(box);

            StackPanel stack = new StackPanel { Margin = new Thickness(28) };
            box.Child = stack;
            stack.Children.Add(SectionTitle("LATEST NVIDIA DRIVER", "Downloads the driver and applies the bundled NVIDIA Profile Inspector preset."));
            stack.Children.Add(InfoLine("Requires administrator approval."));
            stack.Children.Add(InfoLine("Uses NvidiaAutoInstall\\DefaultProfile.nip and nvidiaProfileInspector.exe from the runtime folder."));
            stack.Children.Add(InfoLine("The old safe-mode handoff is disabled so the driver install continues in the same run."));

            Button install = ActionButton("DOWNLOAD AND INSTALL LATEST DRIVER", delegate { RunNvidiaDriverInstaller(); }, true);
            install.Height = 42;
            install.Margin = new Thickness(0, 24, 0, 0);
            install.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(install);

            RefreshNav();
        }

        private void ShowPrograms(object sender, RoutedEventArgs e)
        {
            StackPanel page = BeginPage("PROGRAMS", "Install browsers, everyday apps, and common Windows runtimes from one place.", 860);

            Border browsers = Box(_border);
            browsers.Margin = new Thickness(0, 0, 0, 18);
            page.Children.Add(browsers);

            StackPanel browserStack = new StackPanel { Margin = new Thickness(24) };
            browsers.Child = browserStack;
            browserStack.Children.Add(SectionTitle("BROWSERS", "Installs with winget when available. If winget is missing, mpware opens the official download page instead."));
            browserStack.Children.Add(InfoLine("Brave, Firefox, and Chrome are installed individually so you can pick only what you want."));
            browserStack.Children.Add(BuildProgramsRow(new Button[]
            {
                InstallCommandButton("INSTALL BRAVE", "Install-MpwareBrowser -Name 'Brave'"),
                InstallCommandButton("INSTALL FIREFOX", "Install-MpwareBrowser -Name 'Firefox'"),
                InstallCommandButton("INSTALL CHROME", "Install-MpwareBrowser -Name 'Chrome'")
            }));

            Border apps = Box(_border);
            apps.Margin = new Thickness(0, 0, 0, 18);
            page.Children.Add(apps);

            StackPanel appStack = new StackPanel { Margin = new Thickness(24) };
            apps.Child = appStack;
            appStack.Children.Add(SectionTitle("APPS", "Install common desktop apps directly, or open the official vendor page when a direct package is not reliable."));
            appStack.Children.Add(InfoLine("NVIDIA App currently opens the official NVIDIA page. Steam and Discord use winget with official-site fallback."));
            appStack.Children.Add(BuildProgramsRow(new Button[]
            {
                InstallCommandButton("NVIDIA APP", "Install-MpwareProgram -Name 'NVIDIA App'"),
                InstallCommandButton("INSTALL STEAM", "Install-MpwareProgram -Name 'Steam'"),
                InstallCommandButton("INSTALL DISCORD", "Install-MpwareProgram -Name 'Discord'")
            }));

            Border runtimes = Box(_border);
            page.Children.Add(runtimes);

            StackPanel runtimeStack = new StackPanel { Margin = new Thickness(24) };
            runtimes.Child = runtimeStack;
            runtimeStack.Children.Add(SectionTitle("PACKAGES", "Installs the usual Windows support runtimes in one pass."));
            runtimeStack.Children.Add(InfoLine("Bundle includes VC++ 2015+ x64/x86, .NET Desktop Runtime 8, and DirectX End-User Runtime."));

            Button packageButton = InstallCommandButton("INSTALL VCREDIST + NETRUNTIME + DIRECTX", "Install-MpwarePackages");
            packageButton.Height = 42;
            packageButton.Margin = new Thickness(0, 22, 0, 0);
            packageButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            runtimeStack.Children.Add(packageButton);

            RefreshNav();
        }

        private void ShowDebloater(object sender, RoutedEventArgs e)
        {
            StackPanel page = BeginPage("WINDOWS DEBLOATER", "Simple presets for removing bundled Windows apps.", 820);

            Border warning = Box(_danger);
            warning.Margin = new Thickness(0, 0, 0, 22);
            StackPanel warningStack = new StackPanel { Margin = new Thickness(18) };
            warning.Child = warningStack;
            warningStack.Children.Add(Text("/!\\ Debloat changes are permanent", 15, FontWeights.Bold, _danger));
            warningStack.Children.Add(Text("Removed Store apps usually need to be reinstalled from Microsoft Store or winget. mpware only auto-creates restore points before registry tweak imports.", 11, FontWeights.Bold, _text));
            page.Children.Add(warning);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            page.Children.Add(grid);

            AddDebloatTile(grid, 0, 0, "RECOMMENDED", "Removes common bloat and Copilot. Keeps Microsoft Store, Xbox, and Edge.", "Invoke-MpwareDebloatPreset -Preset Recommended");
            AddDebloatTile(grid, 0, 1, "KEEP STORE", "Recommended plus Xbox and Widgets cleanup. Keeps Microsoft Store.", "Invoke-MpwareDebloatPreset -Preset KeepStore");
            AddDebloatTile(grid, 1, 0, "FULL DEBLOAT", "Aggressive preset. Removes Store, Xbox, Copilot, Widgets, and bundled apps.", "Invoke-MpwareDebloatPreset -Preset Full");

            RefreshNav();
        }

        private void ShowCleanup(object sender, RoutedEventArgs e)
        {
            StackPanel page = BeginPage("CLEANUP", "Simple cleanup picker with a visible progress log.", 760);

            Border warning = Box(_moderate);
            warning.Margin = new Thickness(0, 0, 0, 22);
            StackPanel warningStack = new StackPanel { Margin = new Thickness(18) };
            warning.Child = warningStack;
            warningStack.Children.Add(Text("/!\\ Cleanup removes selected caches, logs, and temporary files.", 15, FontWeights.Bold, _moderate));
            warningStack.Children.Add(Text("The cleanup window is simplified to CHECK ALL and CLEAN. Create a manual restore point first if you want rollback coverage.", 11, FontWeights.Bold, _text));
            page.Children.Add(warning);

            Border box = Box(_border);
            page.Children.Add(box);

            StackPanel stack = new StackPanel { Margin = new Thickness(24) };
            box.Child = stack;
            stack.Children.Add(SectionTitle("CLEANUP", "Pick cleanup targets, then run them from one compact window."));
            stack.Children.Add(InfoLine("Only two actions are shown in the cleanup window: CHECK ALL and CLEAN."));
            stack.Children.Add(InfoLine("A restart may be useful after cleanup."));

            Button run = ActionButton("OPEN CLEANUP", delegate { RunFunctionWithVisibleConsole("Show-MpwareCleanup"); }, true);
            run.Height = 40;
            run.Margin = new Thickness(0, 24, 0, 0);
            run.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(run);

            RefreshNav();
        }

        private void ShowRestoreTweaks(object sender, RoutedEventArgs e)
        {
            StackPanel page = BeginPage("RESTORE TWEAKS", "Undo and repair helpers live separately from registry patching.", 760);

            Border box = Box(_border);
            page.Children.Add(box);

            StackPanel stack = new StackPanel { Margin = new Thickness(24) };
            box.Child = stack;
            stack.Children.Add(SectionTitle("RESTORE CENTER", "Open the bundled restore tool for the current registry tweak bundle."));
            stack.Children.Add(InfoLine("This restore window now only exposes registry tweak rollback."));
            stack.Children.Add(InfoLine("Debloat removals and cleanup actions may still require reinstalling apps or restoring Windows manually."));

            Button open = ActionButton("OPEN RESTORE CENTER", delegate { RunScript("Restore.ps1"); }, true);
            open.Height = 40;
            open.Margin = new Thickness(0, 24, 0, 0);
            open.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(open);

            RefreshNav();
        }

        private void AddDebloatTile(Grid grid, int row, int col, string title, string description, string functionCall)
        {
            Border tile = Box(_border);
            tile.Margin = new Thickness(col == 0 ? 0 : 10, row == 0 ? 0 : 10, col == 0 ? 10 : 0, row == 0 ? 10 : 0);
            Grid.SetRow(tile, row);
            Grid.SetColumn(tile, col);
            grid.Children.Add(tile);

            StackPanel stack = new StackPanel { Margin = new Thickness(20) };
            tile.Child = stack;
            stack.Children.Add(Text(title, 16, FontWeights.Bold, _accent));
            TextBlock copy = Text(description, 11, FontWeights.Normal, _muted);
            copy.Margin = new Thickness(0, 10, 0, 18);
            stack.Children.Add(copy);

            Button run = ActionButton("RUN", delegate { RunFunctionWithVisibleConsole(functionCall); }, true);
            run.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(run);
        }

        private Grid BuildProgramsRow(Button[] buttons)
        {
            System.Windows.Controls.Primitives.UniformGrid grid = new System.Windows.Controls.Primitives.UniformGrid();
            grid.Columns = buttons.Length;
            grid.Margin = new Thickness(0, 20, 0, 0);

            foreach (Button button in buttons)
            {
                button.Margin = new Thickness(0, 0, 10, 0);
                button.Height = 40;
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                grid.Children.Add(button);
            }

            if (buttons.Length > 0)
            {
                buttons[buttons.Length - 1].Margin = new Thickness(0);
            }

            Grid host = new Grid();
            host.Children.Add(grid);
            return host;
        }

        private Button InstallCommandButton(string text, string functionCall)
        {
            return ActionButton(text, delegate { RunFunctionWithVisibleConsole(functionCall); }, true);
        }

        private void ShowAbout(object sender, RoutedEventArgs e)
        {
            StackPanel page = BeginPage("ABOUT MPWARE", "Documentation and important warnings.", 1600);
            page.HorizontalAlignment = HorizontalAlignment.Stretch;

            Border banner = Box(_border);
            banner.Height = 230;
            banner.Margin = new Thickness(0, 0, 0, 28);
            banner.HorizontalAlignment = HorizontalAlignment.Stretch;
            TextBlock art = Text(
@"                                                                    (////((/**,*,///
                                                                    /***/#**/**#&**/
                                            %##%###%/*                /***,*///@//**    %%%%####.
                                          (#%@@@@@@@@#%%%%%%######(##(@&&@&&&%&%%%##**//@@@@@&@@#/   ##
                                      %%%%(&%%%%%%%%%#%%######################((((((((((((((##&%###%&#(
 #####%*//(#/((/#/**%###%%@&# @@&&   ###%%######@/#((((////((//*/**,,,*////////*///(//////////(((((((&%%%%%%%%&&%%%%%%###(///****,*/(&#%#(((
###%%######%%%%%%%%%%###%%%@&&&#%%@&###&@&#&(%(%(%(#(#((%(//////*/***#######/  ##(#####((#(#(####%###&             ..,,*/(#%%&&&&&@@@%#@%///
/##%%%%%%%%%%%%%&&&&&&&%###@@@@@@@@###%@@@(@(@#@#@#@#@(@@@((((####&///**,/**********,*/**//////*//////                            *@@@(/*///
                                   %@    &%%#(&(            (       ,.#*(////(,*,////*//**/**/*/#(/(/                             *@%@/////(
                                         (#%%##/                   (,  *#/(////#%                                                 *(((/////
                                         (%&%#%/               ,,**(#/@*#(/#//((#                                                 . ((((///
                                         (%&&%%/                       ##(/#(/(/(#                                                  .(%////
                                         #%&&%&,                       %#((#(((#(#                                                   (((///
                                         (%&@@% .                      &#((((((/##                                                     ,#((
                                         #&&&&%  .                    .%%#(#(((*##
                                         %&@@                         ###&&@(@(##(
                                                   .                  *&%%%#(%%
                                            .                        , %&&&%%&&
                                                   .                 ,&@&&&&&&%
                                                                    , @@@&%@&&
                                                 .                 .#@@@@@@@&*
                                                                   *&@&&%&@&%
                                                                   .@%&&#&%%",
                6.7, FontWeights.Bold, _accent);
            art.TextWrapping = TextWrapping.NoWrap;
            art.LineHeight = 8;
            art.Margin = new Thickness(0);

            Viewbox artScaler = new Viewbox();
            artScaler.Stretch = Stretch.Uniform;
            artScaler.StretchDirection = StretchDirection.DownOnly;
            artScaler.HorizontalAlignment = HorizontalAlignment.Center;
            artScaler.VerticalAlignment = VerticalAlignment.Center;
            artScaler.Child = art;

            Grid artFrame = new Grid();
            artFrame.Margin = new Thickness(14);
            artFrame.ClipToBounds = true;
            artFrame.Children.Add(artScaler);
            banner.Child = artFrame;
            page.Children.Add(banner);

            Border warnings = Box(_danger);
            warnings.Margin = new Thickness(0, 0, 0, 26);
            StackPanel warningStack = new StackPanel { Margin = new Thickness(22) };
            warnings.Child = warningStack;
            warningStack.Children.Add(Text("/!\\ Critical Warnings", 15, FontWeights.Bold, _danger));
            warningStack.Children.Add(Paragraph("This tool directly modifies the Windows Registry and system configuration. Know what you are applying before you run it."));
            warningStack.Children.Add(Bullet("mpware creates a System Restore point before Registry Tweaks are applied only."));
            warningStack.Children.Add(Bullet("mpware.exe prompts for Administrator on launch."));
            warningStack.Children.Add(Bullet("PowerShell closes automatically after successful actions and stays open only if an error needs review."));
            warningStack.Children.Add(Bullet("Tweaks labeled Advanced may cause instability, compatibility issues, or security tradeoffs."));
            warningStack.Children.Add(Bullet("The bundled mpware powerplan is a managed tweak; it imports and activates the included .pow file."));
            warningStack.Children.Add(Bullet("Not responsible for any damage or data loss from using these scripts."));
            warningStack.Children.Add(Bullet("Debloat removal is permanent. Removed apps must be reinstalled from Store, winget, or Windows setup media."));
            page.Children.Add(warnings);

            Grid two = new Grid();
            two.ColumnDefinitions.Add(new ColumnDefinition());
            two.ColumnDefinitions.Add(new ColumnDefinition());
            two.Margin = new Thickness(0, 0, 0, 28);
            page.Children.Add(two);

            Border how = Box(_border);
            how.Margin = new Thickness(0, 0, 12, 0);
            how.Child = AboutPanel("HOW TO USE MPWARE.EXE", new string[] {
                "1. Run mpware.exe and approve the Administrator prompt.",
                "2. Registry Tweaks: select individual groups or press SELECT ALL, then press APPLY SELECTED.",
                "3. Registry apply opens a progress log, creates a Windows restore point, imports the selected patch, runs needed follow-up actions, and restarts Explorer.",
                "4. PowerShell closes automatically when an action succeeds. If it fails, the window stays open so you can read the error.",
                "5. NVIDIA, Debloater, and Cleanup are separate tools. Restart your PC after deeper changes."
            }, "");
            two.Children.Add(how);

            Border risk = Box(_border);
            risk.Margin = new Thickness(12, 0, 0, 0);
            risk.Child = RiskPanel();
            Grid.SetColumn(risk, 1);
            two.Children.Add(risk);

            RefreshNav();
        }

        private StackPanel AboutPanel(string title, string[] lines, string footer)
        {
            StackPanel stack = new StackPanel { Margin = new Thickness(22) };
            stack.Children.Add(Text(">  " + title, 15, FontWeights.Bold, _accent));
            for (int i = 0; i < lines.Length; i++)
            {
                TextBlock line = Text(lines[i], 11, FontWeights.Normal, _text);
                line.Margin = new Thickness(0, i == 0 ? 20 : 8, 0, 0);
                stack.Children.Add(line);
            }
            if (!String.IsNullOrWhiteSpace(footer))
            {
                Border keys = Box(_borderDim);
                keys.Margin = new Thickness(0, 18, 0, 0);
                keys.Child = Text(footer, 11, FontWeights.Bold, _accent);
                keys.Padding = new Thickness(8);
                stack.Children.Add(keys);
            }
            return stack;
        }

        private StackPanel RiskPanel()
        {
            StackPanel stack = new StackPanel { Margin = new Thickness(22) };
            stack.Children.Add(Text("[] RISK LEVELS", 15, FontWeights.Bold, _accent));
            stack.Children.Add(RiskLine("SAFE", _safe, "Well-tested tweaks, no realistic downside. Apply freely."));
            stack.Children.Add(RiskLine("MODERATE", _moderate, "May affect background functionality. Test after applying."));
            stack.Children.Add(RiskLine("ADVANCED", _advanced, "Can cause instability or security implications. Experienced users only."));
            return stack;
        }

        private StackPanel RiskLine(string label, Brush brush, string copy)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Margin = new Thickness(0, 18, 0, 0);

            Border pill = new Border();
            pill.Background = brush;
            pill.Padding = new Thickness(6, 2, 6, 2);
            pill.Margin = new Thickness(0, 0, 12, 0);
            pill.VerticalAlignment = VerticalAlignment.Top;
            pill.Child = Text(label, 9, FontWeights.Bold, _background);
            row.Children.Add(pill);

            TextBlock description = Text(copy, 11, FontWeights.Normal, _text);
            description.MaxWidth = 250;
            row.Children.Add(description);
            return row;
        }

        private TextBlock InfoLine(string text)
        {
            TextBlock line = Text("(i) " + text, 11, FontWeights.Bold, _text);
            line.Margin = new Thickness(0, 10, 0, 0);
            return line;
        }

        private StackPanel SectionTitle(string title, string subtitle)
        {
            StackPanel stack = new StackPanel();
            stack.Children.Add(Text("[ ] " + title, 15, FontWeights.Bold, _accent));
            if (!String.IsNullOrWhiteSpace(subtitle))
            {
                TextBlock sub = Text(subtitle, 11, FontWeights.Normal, _muted);
                sub.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(sub);
            }
            return stack;
        }

        private TextBlock Paragraph(string text)
        {
            TextBlock block = Text(text, 11, FontWeights.Bold, _text);
            block.Margin = new Thickness(0, 22, 0, 8);
            return block;
        }

        private TextBlock Bullet(string text)
        {
            TextBlock block = Text("-  " + text, 11, FontWeights.Bold, _text);
            block.Margin = new Thickness(0, 8, 0, 0);
            return block;
        }

        private void UpdateSelectedCount()
        {
            int count = GetSelectedTweaks().Count;
            if (_selectedCount != null)
            {
                _selectedCount.Text = count + " tweak" + (count == 1 ? "" : "s") + " selected";
            }
        }

        private List<TweakItem> GetSelectedTweaks()
        {
            List<TweakItem> selected = new List<TweakItem>();
            foreach (TweakItem tweak in _tweaks)
            {
                if (tweak.Selector != null && tweak.Selector.IsChecked == true)
                {
                    selected.Add(tweak);
                }
            }
            return selected;
        }

        private void SelectAllTweaks()
        {
            foreach (TweakItem tweak in _tweaks)
            {
                if (tweak.Selector != null)
                {
                    tweak.Selector.IsChecked = true;
                }
            }
            UpdateSelectedCount();
        }

        private void ApplySelectedTweaks(object sender, RoutedEventArgs e)
        {
            List<TweakItem> selected = GetSelectedTweaks();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select at least one registry tweak first.", "mpware", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ApplyRegistryTweaks(selected, selected.Count + " registry tweak groups");
        }

        private void ApplyRegistryTweaks(List<TweakItem> selected, string label)
        {
            string token = Guid.NewGuid().ToString("N");
            string regPath = IOPath.Combine(IOPath.GetTempPath(), "mpware-selected-" + token + ".reg");
            string checksPath = IOPath.Combine(IOPath.GetTempPath(), "mpware-selected-" + token + ".checks");
            File.WriteAllText(regPath, BuildRegFile(selected, false), Encoding.Unicode);
            File.WriteAllText(checksPath, BuildRegistryCheckFile(selected), Encoding.UTF8);
            bool applyBlackWallpaper = HasSelectedAction(selected, "black-wallpaper");
            bool applyBlackTaskbar = HasSelectedAction(selected, "black-taskbar") || applyBlackWallpaper;
            bool applyPowerPlan = HasSelectedAction(selected, "ultimate-power-plan");
            string escapedRoot = PsEscape(_runtimeRoot ?? "");
            string script =
                "$ErrorActionPreference='Stop';" +
                "$reg='" + PsEscape(regPath) + "';" +
                "$checks='" + PsEscape(checksPath) + "';" +
                "$Global:folder='" + escapedRoot + "';" +
                "if (-not [string]::IsNullOrWhiteSpace($Global:folder)) { Set-Location -LiteralPath $Global:folder };" +
                "$host.UI.RawUI.WindowTitle='mpware progress log';" +
                "Clear-Host;" +
                "try {" +
                "Write-Host 'mpware: progress log' -ForegroundColor Cyan;" +
                "Write-Host 'mpware: preparing to apply " + PsEscape(label) + "...' -ForegroundColor Cyan;" +
                RestorePointScript("registry tweaks") +
                RegistryDeleteScript() +
                "Write-Host 'mpware: importing selected registry tweaks with reg.exe...' -ForegroundColor Cyan;" +
                "& reg.exe import $reg;" +
                "if ($LASTEXITCODE -ne 0) { throw 'reg.exe import failed with exit code ' + $LASTEXITCODE };" +
                (applyBlackTaskbar ? ProtectedFollowUpScript("black taskbar accent", BlackTaskbarScript()) : "") +
                (applyBlackWallpaper ? ProtectedFollowUpScript("solid black desktop background", BlackWallpaperScript()) : "") +
                (applyPowerPlan ? ProtectedFollowUpScript("bundled mpware powerplan", UltimatePowerPlanScript()) : "") +
                VerifyRegistryScript() +
                "Write-Host 'mpware: restarting Explorer to refresh visible Windows settings...' -ForegroundColor Cyan;" +
                "try { Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Start-Process explorer.exe } catch { Write-Host ('mpware: explorer restart skipped: ' + $_.Exception.Message) -ForegroundColor Yellow };" +
                "Remove-Item -LiteralPath $reg,$checks -Force -ErrorAction SilentlyContinue;" +
                "Write-Host 'mpware: registry tweaks applied. Restart recommended.' -ForegroundColor Green;" +
                "} catch {" +
                "  Write-Host ''; Write-Host 'mpware: registry apply failed:' -ForegroundColor Red;" +
                "  Write-Host $_.Exception.Message -ForegroundColor Red;" +
                "  Write-Host ''; Read-Host 'Press Enter to close';" +
                "}";
            RunElevatedPowerShell(script, "applying " + label);
        }

        private void ExportSelectedReg(object sender, RoutedEventArgs e)
        {
            List<TweakItem> selected = GetSelectedTweaks();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select at least one registry tweak first.", "mpware", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string path = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "mpware-selected-tweaks.reg");
            File.WriteAllText(path, BuildRegFile(selected, true), Encoding.Unicode);
            SetStatus("exported " + path);
            MessageBox.Show("Saved .REG export to Desktop.", "mpware", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private string BuildRegFile(List<TweakItem> selected, bool includeDeleteSections)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Windows Registry Editor Version 5.00");
            foreach (TweakItem tweak in selected)
            {
                sb.AppendLine("");
                sb.AppendLine("; " + tweak.Name);
                string currentSection = null;
                foreach (RegEntry entry in tweak.Entries)
                {
                    if (entry.DeleteSection)
                    {
                        if (includeDeleteSections)
                        {
                            sb.AppendLine("[" + entry.Section + "]");
                        }
                        currentSection = null;
                        continue;
                    }
                    if (!String.Equals(currentSection, entry.Section, StringComparison.OrdinalIgnoreCase))
                    {
                        currentSection = entry.Section;
                        sb.AppendLine("[" + currentSection + "]");
                    }
                    sb.AppendLine(entry.ValueLine);
                }
                sb.AppendLine("");
            }
            return sb.ToString();
        }

        private string BuildRegistryCheckFile(List<TweakItem> selected)
        {
            StringBuilder sb = new StringBuilder();
            foreach (TweakItem tweak in selected)
            {
                foreach (RegEntry entry in tweak.Entries)
                {
                    if (entry.DeleteSection)
                    {
                        string path = entry.Section.StartsWith("-", StringComparison.Ordinal) ? entry.Section.Substring(1) : entry.Section;
                        sb.AppendLine("deletekey\t" + path + "\t");
                        continue;
                    }

                    string action = IsValueDelete(entry) ? "deletevalue" : "value";
                    sb.AppendLine(action + "\t" + entry.Section + "\t" + NormalizeRegistryValueName(entry.ValueName));
                }
            }
            return sb.ToString();
        }

        private static bool IsValueDelete(RegEntry entry)
        {
            return entry != null && !entry.DeleteSection && entry.ValueLine != null && entry.ValueLine.TrimEnd().EndsWith("=-", StringComparison.Ordinal);
        }

        private static string NormalizeRegistryValueName(string valueName)
        {
            if (String.IsNullOrWhiteSpace(valueName) || valueName.Trim() == "@")
            {
                return "";
            }

            string trimmed = valueName.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Replace("\\\"", "\"");
            }
            return trimmed;
        }

        private string RegistryDeleteScript()
        {
            return
                "$registryChecks=@(Get-Content -LiteralPath $checks -ErrorAction Stop);" +
                "$deleteRows=@($registryChecks | Where-Object { $_ -like 'deletekey*' });" +
                "if ($deleteRows.Count -gt 0) {" +
                "  Write-Host 'mpware: deleting selected registry keys when present...' -ForegroundColor Cyan;" +
                "  foreach ($row in $deleteRows) {" +
                "    $parts=$row -split \"`t\",3; $path=$parts[1];" +
                "    if ([string]::IsNullOrWhiteSpace($path)) { continue };" +
                "    $providerPath='Registry::' + $path;" +
                "    if (Test-Path -LiteralPath $providerPath) {" +
                "      Remove-Item -LiteralPath $providerPath -Recurse -Force -ErrorAction Stop;" +
                "    }" +
                "  }" +
                "};";
        }

        private string VerifyRegistryScript()
        {
            return
                "Write-Host 'mpware: verifying selected registry keys...' -ForegroundColor Cyan;" +
                "$failures=New-Object System.Collections.Generic.List[string];" +
                "$verified=0;" +
                "function Test-MpwareRegValue([string]$providerPath,[string]$name) {" +
                "  $item=Get-Item -LiteralPath $providerPath -ErrorAction SilentlyContinue;" +
                "  if (-not $item) { return $false };" +
                "  $sentinel=New-Object object;" +
                "  if ([string]::IsNullOrEmpty($name)) { $value=$item.GetValue('', $sentinel) } else { $value=$item.GetValue($name, $sentinel) };" +
                "  return -not [object]::ReferenceEquals($value,$sentinel);" +
                "};" +
                "foreach ($row in $registryChecks) {" +
                "  $parts=$row -split \"`t\",3; if ($parts.Count -lt 2) { continue };" +
                "  $action=$parts[0]; $path=$parts[1]; $name=if ($parts.Count -ge 3) { $parts[2] } else { '' };" +
                "  if ([string]::IsNullOrWhiteSpace($path)) { continue };" +
                "  $providerPath='Registry::' + $path;" +
                "  if ($action -eq 'deletekey') {" +
                "    if (Test-Path -LiteralPath $providerPath) { $failures.Add('key still exists: ' + $path) } else { $verified++ };" +
                "    continue;" +
                "  }" +
                "  if (-not (Test-Path -LiteralPath $providerPath)) { if ($action -eq 'deletevalue') { $verified++; continue } else { $failures.Add('missing key: ' + $path); continue } };" +
                "  $exists=Test-MpwareRegValue $providerPath $name;" +
                "  if ($action -eq 'deletevalue') {" +
                "    if ($exists) { $failures.Add('value still exists: ' + $path + ' :: ' + $name) } else { $verified++ };" +
                "  } else {" +
                "    if (-not $exists) { $failures.Add('missing value: ' + $path + ' :: ' + $name) } else { $verified++ };" +
                "  }" +
                "};" +
                "if ($failures.Count -gt 0) {" +
                "  $sample=($failures | Select-Object -First 12) -join '; ';" +
                "  Write-Host ('mpware: registry verification warning for ' + $failures.Count + ' optional entry(s): ' + $sample) -ForegroundColor Yellow;" +
                "} else {" +
                "  Write-Host ('mpware: verified ' + $verified + ' registry entries.') -ForegroundColor Green;" +
                "};" +
                "";
        }

        private void RunNvidiaDriverInstaller()
        {
            if (!EnsureRuntime())
            {
                return;
            }

            string nvidiaRoot = IOPath.Combine(_runtimeRoot, "NvidiaAutoInstall");
            string script = IOPath.Combine(nvidiaRoot, "NvidiaAutoinstall.ps1");
            if (!File.Exists(script))
            {
                MessageBox.Show("Missing runtime script: NvidiaAutoInstall\\NvidiaAutoinstall.ps1", "mpware", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string command =
                "$ErrorActionPreference='Stop';" +
                "try {" +
                "  Set-Location -LiteralPath '" + PsEscape(nvidiaRoot) + "';" +
                "  $Global:folder='" + PsEscape(_runtimeRoot) + "';" +
                "  $Global:nvidiaFolder='" + PsEscape(nvidiaRoot) + "';" +
                "  $Global:sysDrive=$env:SystemDrive.TrimEnd('\\')+'\\';" +
                "  $Global:tempDir=([System.IO.Path]::GetTempPath()).TrimEnd('\\');" +
                "  . (Join-Path $Global:folder 'MpwareRuntime.ps1');" +
                "  if (-not (Test-Path -LiteralPath (Join-Path $Global:nvidiaFolder 'DefaultProfile.nip'))) { throw 'DefaultProfile.nip is missing' };" +
                "  if (-not (Test-Path -LiteralPath (Join-Path $Global:nvidiaFolder 'nvidiaProfileInspector.exe'))) { throw 'nvidiaProfileInspector.exe is missing' };" +
                "  Write-Host 'mpware: starting NVIDIA driver helper with bundled Inspector profile...' -ForegroundColor Cyan;" +
                "  & '" + PsEscape(script) + "';" +
                "  Write-Host 'mpware: NVIDIA helper finished.' -ForegroundColor Green;" +
                "  Write-Host ''; Read-Host 'Press Enter to close';" +
                "} catch {" +
                "  Write-Host ''; Write-Host 'mpware: NVIDIA helper failed:' -ForegroundColor Red;" +
                "  Write-Host $_.Exception.Message -ForegroundColor Red;" +
                "  Write-Host ''; Read-Host 'Press Enter to close';" +
                "}";

            RunElevatedPowerShell(command, "launching NVIDIA driver helper");
        }

        private void RunElevatedPowerShell(string script, string log)
        {
            SetStatus(log);
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded;
            if (!String.IsNullOrWhiteSpace(_runtimeRoot))
            {
                psi.WorkingDirectory = _runtimeRoot;
            }
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
                MessageBox.Show("Missing runtime script: " + relativeScript, "mpware", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetStatus("launching " + relativeScript);
            string escapedRoot = PsEscape(_runtimeRoot);
            string command =
                "$ErrorActionPreference='Continue';" +
                "Set-Location -LiteralPath '" + escapedRoot + "';" +
                "$Global:folder='" + escapedRoot + "';" +
                "$Global:sysDrive=$env:SystemDrive.TrimEnd('\\')+'\\';" +
                "$Global:tempDir=([System.IO.Path]::GetTempPath()).TrimEnd('\\');" +
                "if (Test-Path -LiteralPath (Join-Path $Global:folder 'MpwareRuntime.ps1')) { . (Join-Path $Global:folder 'MpwareRuntime.ps1') };" +
                "& '" + PsEscape(script) + "';";
            RunElevatedPowerShell(command, "launching " + relativeScript);
        }

        private void RunFunctionWithVisibleConsole(string functionCall)
        {
            if (!EnsureRuntime())
            {
                return;
            }

            SetStatus("launching " + functionCall);
            string escapedRoot = PsEscape(_runtimeRoot);
            string command =
                "$ErrorActionPreference='Continue';" +
                "$host.UI.RawUI.WindowTitle='mpware progress log';" +
                "Clear-Host;" +
                "Write-Host 'mpware: progress log' -ForegroundColor Cyan;" +
                "try {" +
                "  Set-Location -LiteralPath '" + escapedRoot + "';" +
                "  $Global:folder='" + escapedRoot + "';" +
                "  $Global:sysDrive=$env:SystemDrive.TrimEnd('\\')+'\\';" +
                "  $Global:tempDir=([System.IO.Path]::GetTempPath()).TrimEnd('\\');" +
                "  . (Join-Path $Global:folder 'MpwareRuntime.ps1');" +
                "  Write-Host 'mpware: starting " + PsEscape(functionCall) + "...' -ForegroundColor Cyan;" +
                "  " + functionCall + ";" +
                "  Write-Host 'mpware: command finished.' -ForegroundColor Green;" +
                "} catch {" +
                "  Write-Host ''; Write-Host 'mpware: command failed:' -ForegroundColor Red;" +
                "  Write-Host $_.Exception.Message -ForegroundColor Red;" +
                "  Write-Host ''; Read-Host 'Press Enter to close';" +
                "}";
            RunElevatedPowerShell(command, "launching " + functionCall);
        }

        private bool EnsureRuntime()
        {
            if (!String.IsNullOrWhiteSpace(_runtimeRoot) && Directory.Exists(_runtimeRoot) && File.Exists(IOPath.Combine(_runtimeRoot, "RegTweaks.txt")))
            {
                return true;
            }
            MessageBox.Show("mpware runtime was not found. Rebuild mpware.exe or extract the release zip.", "mpware", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        private void SetStatus(string message)
        {
            if (_statusLine != null)
            {
                _statusLine.Text = "status: " + DateTime.Now.ToString("HH:mm:ss") + " " + message;
            }
        }

        private Button ActionButton(string text, RoutedEventHandler handler, bool primary)
        {
            Button button = FlatButton(text, primary);
            button.Height = 34;
            button.Margin = new Thickness(8, 0, 0, 0);
            button.Click += handler;
            return button;
        }

        private Button FlatButton(string text, bool primary)
        {
            Button button = new Button();
            button.Content = text;
            button.Background = primary ? _accent : _background;
            button.Foreground = primary ? _background : _text;
            button.BorderBrush = primary ? _accent : _border;
            button.BorderThickness = new Thickness(1);
            button.FontFamily = _mono;
            button.FontSize = 12;
            button.FontWeight = FontWeights.Bold;
            button.Padding = new Thickness(16, 0, 16, 0);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.Cursor = Cursors.Hand;
            button.Template = ButtonTemplate();
            return button;
        }

        private ControlTemplate ButtonTemplate()
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.VerticalAlignmentProperty, new Binding("VerticalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetBinding(ContentPresenter.MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            presenter.SetValue(ContentPresenter.SnapsToDevicePixelsProperty, true);

            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        private Border Box(Brush borderBrush)
        {
            Border box = new Border();
            box.Background = _panel;
            box.BorderBrush = borderBrush;
            box.BorderThickness = new Thickness(1);
            box.SnapsToDevicePixels = true;
            return box;
        }

        private TextBlock Text(string value, double size, FontWeight weight, Brush brush)
        {
            TextBlock block = new TextBlock();
            block.Text = value;
            block.FontFamily = _mono;
            block.FontSize = size;
            block.FontWeight = weight;
            block.Foreground = brush;
            block.TextWrapping = TextWrapping.Wrap;
            block.LineHeight = size + 6;
            return block;
        }

        private string RegistryPreview(TweakItem tweak)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TWEAK: " + tweak.Name);
            sb.AppendLine("RISK: " + tweak.Risk);
            sb.AppendLine("SUMMARY: " + tweak.Description);
            sb.AppendLine("");
            foreach (RegEntry entry in tweak.Entries)
            {
                if (entry.DeleteSection)
                {
                    sb.AppendLine("PATH: " + entry.Section);
                    sb.AppendLine("ACTION: delete this registry key and its subkeys");
                }
                else
                {
                    sb.AppendLine("PATH: " + entry.Section);
                    sb.AppendLine("VALUE: " + entry.ValueLine);
                }
                sb.AppendLine("DOES: " + DescribeRegistryEntry(entry));
                sb.AppendLine("");
            }
            return sb.ToString();
        }

        private string PsEscape(string text)
        {
            return (text ?? "").Replace("'", "''");
        }

        private string RestorePointScript(string label)
        {
            string safeLabel = PsEscape("mpware - " + label);
            return
                "  Write-Host 'mpware: creating Windows restore point...' -ForegroundColor Cyan;" +
                "  try {" +
                "    Enable-ComputerRestore -Drive $env:SystemDrive -ErrorAction SilentlyContinue;" +
                "    Checkpoint-Computer -Description '" + safeLabel + "' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop;" +
                "    Write-Host 'mpware: restore point created.' -ForegroundColor Green;" +
                "  } catch {" +
                "    Write-Host ('mpware: restore point skipped: ' + $_.Exception.Message) -ForegroundColor Yellow;" +
                "  };";
        }

        private string BlackWallpaperScript()
        {
            return
                "Write-Host 'mpware: applying solid black desktop background...' -ForegroundColor Cyan;" +
                "Set-ItemProperty -Path 'HKCU:\\Control Panel\\Colors' -Name 'Background' -Value '0 0 0' -Force;" +
                "Set-ItemProperty -Path 'HKCU:\\Control Panel\\Desktop' -Name 'Wallpaper' -Value '' -Force;" +
                "if (-not ('MpwareWallpaperRefresh' -as [type])) {" +
                "Add-Type -TypeDefinition @'\n" +
                "using System;\n" +
                "using System.Runtime.InteropServices;\n" +
                "public class MpwareWallpaperRefresh {\n" +
                "  [DllImport(\"user32.dll\", CharSet=CharSet.Auto)] public static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);\n" +
                "  [DllImport(\"user32.dll\", SetLastError=true)] public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);\n" +
                "}\n" +
                "'@;" +
                "};" +
                "[MpwareWallpaperRefresh]::SystemParametersInfo(0x0014, 0, '', 0x01 -bor 0x02) | Out-Null;" +
                "[MpwareWallpaperRefresh]::InvalidateRect([IntPtr]::Zero, [IntPtr]::Zero, $true) | Out-Null;" +
                "1..5 | ForEach-Object { & rundll32.exe user32.dll,UpdatePerUserSystemParameters 1, True; Start-Sleep -Milliseconds 25 };";
        }

        private string BlackTaskbarScript()
        {
            return
                "Write-Host 'mpware: forcing black taskbar/accent color...' -ForegroundColor Cyan;" +
                "$personalize='HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize';" +
                "$accent='HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Accent';" +
                "$dwm='HKCU:\\Software\\Microsoft\\Windows\\DWM';" +
                "New-Item -Path $personalize,$accent,$dwm -Force | Out-Null;" +
                "Set-ItemProperty -Path $personalize -Name 'AppsUseLightTheme' -Type DWord -Value 0 -Force;" +
                "Set-ItemProperty -Path $personalize -Name 'SystemUsesLightTheme' -Type DWord -Value 0 -Force;" +
                "Set-ItemProperty -Path $personalize -Name 'ColorPrevalence' -Type DWord -Value 1 -Force;" +
                "$palette=New-Object byte[] 32;" +
                "Set-ItemProperty -Path $accent -Name 'AccentPalette' -Type Binary -Value $palette -Force;" +
                "Set-ItemProperty -Path $accent -Name 'StartColorMenu' -Type DWord -Value 0 -Force;" +
                "Set-ItemProperty -Path $accent -Name 'AccentColorMenu' -Type DWord -Value 0 -Force;" +
                "Set-ItemProperty -Path $dwm -Name 'EnableWindowColorization' -Type DWord -Value 1 -Force;" +
                "Set-ItemProperty -Path $dwm -Name 'ColorPrevalence' -Type DWord -Value 1 -Force;" +
                "Set-ItemProperty -Path $dwm -Name 'AccentColor' -Type DWord -Value 0 -Force;" +
                "Set-ItemProperty -Path $dwm -Name 'ColorizationColor' -Type DWord -Value 0 -Force;" +
                "Set-ItemProperty -Path $dwm -Name 'ColorizationAfterglow' -Type DWord -Value 0 -Force;";
        }

        private string UltimatePowerPlanScript()
        {
            return
                "Write-Host 'mpware: importing bundled mpware powerplan...' -ForegroundColor Cyan;" +
                "if ([string]::IsNullOrWhiteSpace($Global:folder)) { throw 'runtime folder was not found' };" +
                "$plan=Join-Path $Global:folder 'mpware powerplan.pow';" +
                "if (-not (Test-Path -LiteralPath $plan)) { throw 'bundled mpware powerplan.pow is missing' };" +
                "$guid=$null;" +
                "$out=powercfg -import $plan 2>&1;" +
                "if ($LASTEXITCODE -ne 0) { throw ('powercfg failed to import bundled mpware powerplan: ' + ($out -join ' ')) };" +
                "foreach ($line in $out) { if ($line -match '([0-9a-fA-F-]{36})') { $guid=$matches[1]; break } };" +
                "if (-not $guid) { $list=powercfg /list 2>$null; foreach ($line in $list) { if ($line -match '([0-9a-fA-F-]{36}).*mpware') { $guid=$matches[1]; break } } };" +
                "if (-not $guid) { throw 'powercfg import did not return a plan GUID' };" +
                "powercfg /changename $guid 'mpware powerplan' | Out-Null;" +
                "powercfg /setactive $guid;" +
                "if ($LASTEXITCODE -ne 0) { throw 'powercfg failed to activate imported mpware powerplan' };";
        }

        private string ProtectedFollowUpScript(string label, string script)
        {
            return
                "try {" +
                script +
                "} catch {" +
                "  Write-Host ('mpware: " + PsEscape(label) + " skipped: ' + $_.Exception.Message) -ForegroundColor Yellow;" +
                "};";
        }

        private void BuildTweaks()
        {
            LoadBundledRegistryTweaks();
            AddManagedTweaks();
            if (_tweaks.Count == 0)
            {
                AddFallbackTweak();
            }
            RefreshTweakDescriptions();
        }

        private void LoadBundledRegistryTweaks()
        {
            if (String.IsNullOrWhiteSpace(_runtimeRoot) || !Directory.Exists(_runtimeRoot))
            {
                return;
            }

            string mainReg = IOPath.Combine(_runtimeRoot, "RegTweaks.txt");
            LoadRegFile(mainReg, "REGISTRY TWEAKS");

            string contextMenu = IOPath.Combine(_runtimeRoot, "UltimateContextMenu");
            if (Directory.Exists(contextMenu))
            {
                string[] regFiles = Directory.GetFiles(contextMenu, "*.reg");
                Array.Sort(regFiles, StringComparer.OrdinalIgnoreCase);
                foreach (string regFile in regFiles)
                {
                    LoadRegFile(regFile, "CONTEXT MENU");
                }
            }

            for (int i = _tweaks.Count - 1; i >= 0; i--)
            {
                if (_tweaks[i].Entries.Count == 0)
                {
                    _tweaks.RemoveAt(i);
                }
            }
        }

        private void LoadRegFile(string path, string source)
        {
            if (!File.Exists(path))
            {
                return;
            }

            string[] lines = File.ReadAllLines(path);
            TweakItem current = null;
            string section = null;
            bool deleteSection = false;
            string fallbackName = IOPath.GetFileNameWithoutExtension(path);

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string trimmed = raw.Trim();

                if (trimmed.Length == 0 || trimmed.StartsWith("Windows Registry Editor", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (trimmed.StartsWith(";", StringComparison.Ordinal))
                {
                    string title = trimmed.TrimStart(';').Trim();
                    if (title.Length > 0)
                    {
                        current = NewParsedTweak(source, title);
                    }
                    continue;
                }

                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    section = trimmed.Substring(1, trimmed.Length - 2);
                    deleteSection = section.StartsWith("-", StringComparison.Ordinal);
                    if (current == null)
                    {
                        current = NewParsedTweak(source, fallbackName);
                    }
                    if (deleteSection)
                    {
                        AddRegEntry(current, new RegEntry { Section = section, DeleteSection = true, ValueName = "(delete)", ValueLine = "" });
                    }
                    continue;
                }

                if (section == null || deleteSection || trimmed.IndexOf('=') < 0)
                {
                    continue;
                }

                StringBuilder valueBuilder = new StringBuilder(raw.TrimEnd());
                while (valueBuilder.ToString().TrimEnd().EndsWith("\\", StringComparison.Ordinal) && i + 1 < lines.Length)
                {
                    i++;
                    valueBuilder.AppendLine();
                    valueBuilder.Append(lines[i].TrimEnd());
                }

                string valueLine = valueBuilder.ToString();
                string valueName = valueLine.Substring(0, valueLine.IndexOf('=')).Trim();
                if (current == null)
                {
                    current = NewParsedTweak(source, fallbackName);
                }
                AddRegEntry(current, new RegEntry { Section = section, DeleteSection = false, ValueName = valueName, ValueLine = valueLine });
            }
        }

        private TweakItem NewParsedTweak(string source, string title)
        {
            TweakItem item = new TweakItem();
            item.Risk = RiskForTitle(title);
            item.Name = title;
            item.Description = "Review the registry patch before applying.";
            item.Entries = new List<RegEntry>();
            item.ActionId = ActionForTitle(title);
            _tweaks.Add(item);
            return item;
        }

        private void AddManagedTweaks()
        {
            TweakItem power = NewParsedTweak("MANAGED", "Enable mpware powerplan");
            power.Risk = "Moderate";
            power.ActionId = "ultimate-power-plan";
            power.Description = "Imports, renames, and activates the bundled mpware powerplan via powercfg.";
        }

        private bool HasSelectedAction(List<TweakItem> selected, string actionId)
        {
            foreach (TweakItem tweak in selected)
            {
                if (String.Equals(tweak.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void AddRegEntry(TweakItem tweak, RegEntry entry)
        {
            string key = entry.DeleteSection ? "DELETE::" + entry.Section : entry.Section + "::" + entry.ValueName + "::" + entry.ValueLine;
            foreach (RegEntry existing in tweak.Entries)
            {
                string existingKey = existing.DeleteSection ? "DELETE::" + existing.Section : existing.Section + "::" + existing.ValueName + "::" + existing.ValueLine;
                if (String.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            tweak.Entries.Add(entry);
        }

        private List<TweakItem> OrderedTweaks()
        {
            return new List<TweakItem>(_tweaks);
        }

        private string RiskForTitle(string title)
        {
            string text = title.ToLowerInvariant();
            if (ContainsAny(text, "uac", "user account control", "spectre", "meltdown", "system requirements", "labconfig", "hpet")) return "Advanced";
            if (ContainsAny(text, "network", "power", "hibernate", "timer", "driver", "privacy deny", "camera", "file system", "memory", "service")) return "Moderate";
            return "Safe";
        }

        private string ActionForTitle(string title)
        {
            string text = title.ToLowerInvariant();
            if (ContainsAny(text, "set background black", "remove picture wallpaper", "set wallpaper to solid color"))
            {
                return "black-wallpaper";
            }
            if (ContainsAny(text, "dark theme"))
            {
                return "black-taskbar";
            }
            return null;
        }

        private bool ContainsAny(string text, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (text.IndexOf(needle, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void RefreshTweakDescriptions()
        {
            foreach (TweakItem tweak in _tweaks)
            {
                tweak.Description = BuildTweakDescription(tweak);
            }
        }

        private string BuildTweakDescription(TweakItem tweak)
        {
            string title = tweak.Name.ToLowerInvariant();
            if (String.Equals(tweak.ActionId, "ultimate-power-plan", StringComparison.OrdinalIgnoreCase))
                return "Imports the bundled .pow file, renames it to mpware powerplan, and activates it without deleting other plans.";
            if (String.Equals(tweak.ActionId, "black-wallpaper", StringComparison.OrdinalIgnoreCase))
                return "Sets the desktop wallpaper to a blank solid black background and refreshes Explorer visuals immediately.";
            if (String.Equals(tweak.ActionId, "black-taskbar", StringComparison.OrdinalIgnoreCase))
                return "Enables dark mode and forces the Start/taskbar accent palette to solid black.";
            if (ContainsAny(title, "suggested actions"))
                return "Turns off Smart Clipboard suggested actions popups.";
            if (ContainsAny(title, "search highlights"))
                return "Disables dynamic Search highlight artwork/content in the taskbar search box.";
            if (ContainsAny(title, "system requirements", "labconfig", "unsupported"))
                return "Sets setup compatibility bypass values for unsupported Windows 11 hardware checks.";
            if (ContainsAny(title, "user account control", "uac"))
                return "Changes User Account Control policy. This reduces Windows consent prompts and should be treated as advanced.";
            if (ContainsAny(title, "storage sense"))
                return "Disables the Storage Sense policy so Windows will not automatically clean selected files.";
            if (ContainsAny(title, "action center"))
                return "Restores Action Center/notification center visibility if a policy disabled it.";
            if (ContainsAny(title, "dark theme"))
                return "Sets Windows personalization values so apps and the system use dark mode.";
            if (ContainsAny(title, "100% dpi", "dpi scaling"))
                return "Resets user DPI values to standard 100% scaling.";
            if (ContainsAny(title, "fix scaling"))
                return "Disables per-app automatic DPI scaling correction prompts.";
            if (ContainsAny(title, "transparency"))
                return "Turns off Windows transparency effects for a simpler shell and slightly less visual overhead.";
            if (ContainsAny(title, "hardware accelerated gpu", "hags"))
                return "Enables Hardware-Accelerated GPU Scheduling when the GPU and driver support it.";
            if (ContainsAny(title, "leftmost taskbar"))
                return "Aligns Windows 11 taskbar icons to the left.";
            if (ContainsAny(title, "gallery shortcut"))
                return "Removes the File Explorer Gallery namespace shortcut.";
            if (ContainsAny(title, "home shortcut"))
                return "Hides the File Explorer Home/Quick Access namespace shortcut.";
            if (ContainsAny(title, "open file explorer to this pc"))
                return "Makes File Explorer open to This PC instead of Home.";
            if (ContainsAny(title, "more pins"))
                return "Sets Start menu layout preference toward more pinned apps.";
            if (ContainsAny(title, "recently added", "recently opened", "most used", "recommended"))
                return "Reduces recent apps, recent documents, and recommendations in Start, jump lists, and Explorer.";
            if (ContainsAny(title, "ai insights", "windows ai"))
                return "Disables Windows AI/Copilot/Recall-related feature policy or visibility values.";
            if (ContainsAny(title, "pinned items in network and sound flyout"))
                return "Unpins selected quick actions from the Windows quick settings flyout.";
            if (ContainsAny(title, "share app experiences"))
                return "Turns off cross-device/shared app experience authorization values.";
            if (ContainsAny(title, "phone companion"))
                return "Hides and disables the Phone Link companion entry in Start.";
            if (ContainsAny(title, "cross device resume", "resume from taskbar"))
                return "Disables Windows cross-device resume/continue activity surfaces.";
            if (ContainsAny(title, "dynamic lighting"))
                return "Turns off Windows Dynamic Lighting control for RGB devices.";
            if (ContainsAny(title, "update apps automatically"))
                return "Disables Microsoft Store automatic app download/update policy.";
            if (ContainsAny(title, "search from taskbar"))
                return "Hides taskbar search UI.";
            if (ContainsAny(title, "chat from taskbar", "task view from taskbar", "meet now", "news and interests"))
                return "Hides selected taskbar buttons or feed surfaces.";
            if (ContainsAny(title, "game bar", "xbox capture", "game dvr"))
                return "Controls Xbox Game Bar and Game DVR capture/overlay registry values.";
            if (ContainsAny(title, "game mode"))
                return "Enables Windows Game Mode policy and GameBar values used by Windows gaming features.";
            if (ContainsAny(title, "network throttling"))
                return "Sets the Multimedia SystemProfile network throttling index used by Windows multimedia scheduling.";
            if (ContainsAny(title, "system responsiveness"))
                return "Sets the Multimedia SystemProfile CPU reservation value used by background multimedia tasks.";
            if (ContainsAny(title, "enhance pointer", "mouse"))
                return "Disables Enhanced Pointer Precision by setting Windows mouse acceleration thresholds to zero.";
            if (ContainsAny(title, "sound communications"))
                return "Sets Windows communications audio handling to do nothing instead of lowering other app volume.";
            if (ContainsAny(title, "startup sound"))
                return "Disables the Windows startup sound.";
            if (ContainsAny(title, "memory compression"))
                return "Changes Memory Management values used by Windows memory compression behavior.";
            if (ContainsAny(title, "remote assistance"))
                return "Disables Remote Assistance policy values so unsolicited assistance offers are blocked.";
            if (ContainsAny(title, "driver searching", "co installers"))
                return "Changes driver search/co-installer policy values used during device driver installation.";
            if (ContainsAny(title, "automatic maintenance"))
                return "Disables the Windows automatic maintenance scheduled maintenance policy.";
            if (ContainsAny(title, "use my sign in info"))
                return "Stops Windows from using sign-in info to finish updates/reopen apps after restart.";
            if (ContainsAny(title, "automatically update maps"))
                return "Disables automatic offline map update policy.";
            if (ContainsAny(title, "alt tab"))
                return "Restricts Alt+Tab to app windows only instead of showing Edge tabs.";
            if (ContainsAny(title, "long paths"))
                return "Enables Win32 long path support for applications that opt in to long paths.";
            if (ContainsAny(title, "last access time"))
                return "Disables NTFS last-access timestamp updates to reduce file-system metadata writes.";
            if (ContainsAny(title, "privacy deny"))
                return "Sets Windows app privacy consent values to deny that permission for Store/UWP apps.";
            if (ContainsAny(title, "background apps"))
                return "Disables background app execution policy for Store/UWP apps.";
            if (ContainsAny(title, "language list", "tracking app launches", "inking and typing", "activity history", "feedback frequency"))
                return "Turns off personalization, activity history, feedback, or language-based tracking settings.";
            if (ContainsAny(title, "telemetry", "data collection", "diagnostic"))
                return "Limits Windows diagnostic data and related telemetry policy values where supported by the edition.";
            if (ContainsAny(title, "copilot", "windows ai", "ai insights"))
                return "Disables Copilot/Windows AI feature policy or visibility values.";
            if (ContainsAny(title, "search web results", "web search", "cloud content search", "safe search"))
                return "Adjusts Windows Search policy values for web results, cloud search, or SafeSearch behavior.";
            if (ContainsAny(title, "notifications", "subscribed content", "personalized offers"))
                return "Disables selected notification, content suggestion, and tailored-experience values.";
            if (ContainsAny(title, "magnifier", "narrator"))
                return "Disables accessibility feature startup/configuration values for Magnifier or Narrator.";
            if (ContainsAny(title, "show hidden files"))
                return "Configures Explorer to show hidden files and folders.";
            if (ContainsAny(title, "show file name extensions"))
                return "Configures Explorer to show known file extensions.";
            if (ContainsAny(title, "menu show delay"))
                return "Removes the menu-open delay for classic Win32 menus.";
            if (ContainsAny(title, "language hotkey", "language bar"))
                return "Disables language switching hotkeys or hides the floating language bar.";
            if (ContainsAny(title, "lock screen image"))
                return "Disables lock-screen image/Spotlight surfaces.";
            if (ContainsAny(title, "autoplay"))
                return "Disables AutoPlay behavior for removable media.";
            if (ContainsAny(title, "task manager always on top"))
                return "Sets Task Manager to stay on top.";
            if (ContainsAny(title, "show all taskbar icons"))
                return "Changes notification-area/tray visibility values so hidden icon behavior is reduced.";
            if (ContainsAny(title, "taskbar", "start menu", "recently", "recommend", "widgets", "news and interests", "meet now", "chat", "task view"))
                return "Changes Explorer, Start, and taskbar registry values for a cleaner Windows shell.";
            if (ContainsAny(title, "snap"))
                return "Disables Windows snap layout, snap assist, and snap group shell behavior.";
            if (ContainsAny(title, "classic desktop right-click menu"))
                return "Adds the Windows 11 CLSID override that opens the classic desktop context menu by default.";
            if (ContainsAny(title, "file explorer", "quick access", "hidden files", "file name extensions", "folder type"))
                return "Changes File Explorer registry values for visibility, navigation, or default folder behavior.";
            if (ContainsAny(title, "spotlight"))
                return "Disables Windows Spotlight content/suggestion delivery values.";
            if (ContainsAny(title, "animations", "animate", "peek", "thumbnails", "visual", "best performance", "drop shadows", "smooth edges"))
                return "Changes Windows visual-effects values used for animations, previews, shadows, thumbnails, and font smoothing.";
            if (ContainsAny(title, "sleep", "hibernate", "lock", "power modes", "power"))
                return "Changes power, lock, sleep, or hibernate policy values.";
            if (ContainsAny(title, "fault tolerant heap"))
                return "Disables Fault Tolerant Heap compatibility mitigation tracking.";
            if (ContainsAny(title, "icon cache"))
                return "Increases Explorer icon cache size.";
            if (ContainsAny(title, "blue screen"))
                return "Shows more diagnostic information on crash/blue-screen screens.";
            if (ContainsAny(title, "platform binary table"))
                return "Disables Windows Platform Binary Table execution/loading behavior.";
            if (ContainsAny(title, "web services in explorer", "publish to web"))
                return "Disables legacy Explorer web service or web publishing integrations.";
            if (ContainsAny(title, "document history"))
                return "Disables Explorer document history tracking.";
            if (ContainsAny(title, "low disk space"))
                return "Disables low disk space warning checks.";
            if (ContainsAny(title, "home page in settings"))
                return "Hides the Settings app home page.";
            if (ContainsAny(title, "view and menu as guide"))
                return "Disables first-run guide hints for app view/menu buttons.";
            if (ContainsAny(title, "track my device"))
                return "Disables Find My Device/location tracking policy values.";
            if (ContainsAny(title, "explorer open in new tab"))
                return "Controls File Explorer new tab behavior.";
            if (ContainsAny(title, "sleep study"))
                return "Disables SleepStudy power diagnostics collection.";
            if (ContainsAny(title, "device usage"))
                return "Disables Settings Device Usage personalization categories.";
            if (ContainsAny(title, "store settings", "app actions", "windows backup"))
                return "Disables selected Store app settings/actions or Windows Backup account prompts.";
            if (ContainsAny(title, "black powershell console"))
                return "Sets classic console color-table values for a black PowerShell/CMD background.";
            if (ContainsAny(title, "jump list", "do not disturb", "dynamic lock", "sticky keys", "filter keys", "calendar", "big clock", "prelaunch", "share"))
                return "Adjusts the named Windows shell, notification, accessibility, or Explorer behavior.";
            if (ContainsAny(title, "context menu", "run as", "take own", "new menu", "powershell"))
                return "Adds or changes Explorer context-menu registry entries.";

            if (tweak.Entries.Count == 0)
            {
                return "No registry entries were found for this tweak.";
            }

            string first = DescribeRegistryEntry(tweak.Entries[0]);
            if (tweak.Entries.Count == 1)
            {
                return first;
            }
            return first + " Includes " + tweak.Entries.Count + " registry entries.";
        }

        private string DescribeRegistryEntry(RegEntry entry)
        {
            string text = ((entry.Section ?? "") + " " + (entry.ValueName ?? "") + " " + (entry.ValueLine ?? "")).ToLowerInvariant();
            if (entry.DeleteSection)
            {
                return "Deletes this registry key and all values/subkeys under it.";
            }
            if (ContainsAny(text, "appsuselighttheme", "systemuseslighttheme"))
                return "Controls app/system light-vs-dark theme mode; zero selects dark mode.";
            if (ContainsAny(text, "colorprevalence", "accentpalette", "startcolormenu", "accentcolormenu", "colorizationcolor"))
                return "Controls Windows accent color and whether Start/taskbar/title surfaces use that accent.";
            if (ContainsAny(text, "enabletransparency"))
                return "Controls Windows transparency effects.";
            if (ContainsAny(text, "logpixels", "win8dpiscaling", "applieddpi", "enableperprocesssystemdpi"))
                return "Controls display DPI scaling and per-app DPI correction behavior.";
            if (ContainsAny(text, "86ca1aa0-34aa-4e8b-a509-50c905bae2a2"))
                return "Controls the Windows 11 classic desktop context menu override.";
            if (ContainsAny(text, "taskbaral"))
                return "Controls Windows 11 taskbar icon alignment.";
            if (ContainsAny(text, "taskbarmn", "showtaskviewbutton", "searchboxtaskbarmode", "hidescameetnow", "taskbarda"))
                return "Controls visibility of taskbar buttons or taskbar search/feed surfaces.";
            if (ContainsAny(text, "start_trackdocs", "start_trackprogs", "showrecentlist", "hiderecentlyaddedapps", "showorhidemostusedapps", "hiderecommended"))
                return "Controls Start menu, jump list, and recommendation history surfaces.";
            if (ContainsAny(text, "hidefileext", "showfrequent", "hubmode", "openfolderinnewtab"))
                return "Controls File Explorer default view/navigation behavior.";
            if (ContainsAny(text, "unsupportedhardwarenotificationcache", "labconfig", "mosetup"))
                return "Controls Windows 11 setup/hardware compatibility warning or bypass values.";
            if (ContainsAny(text, "smartactionplatform", "smartclipboard"))
                return "Controls suggested actions generated from clipboard content.";
            if (ContainsAny(text, "storagesense"))
                return "Controls Storage Sense automatic cleanup policy.";
            if (ContainsAny(text, "disablenotificationcenter"))
                return "Controls Action Center/notification center policy.";
            if (ContainsAny(text, "snapassist", "enablesnap", "enabletaskgroups", "ditest"))
                return "Controls snap layout, snap assist flyout, snap bar, or snap group behavior.";
            if (ContainsAny(text, "windows\\currentversion\\explorer\\desktop\\namespace"))
                return "Controls File Explorer namespace shortcuts.";
            if (ContainsAny(text, "showcopilotbutton", "windowscopilot", "windowsai", "bingchat", "copilotkey", "disablecocreator", "disableimagecreator", "disableaifeatures"))
                return "Controls Copilot, Windows AI, Recall, Paint AI, Notepad AI, or Copilot key policy values.";
            if (ContainsAny(text, "taskbarendtask"))
                return "Controls the End Task option on taskbar app context menus.";
            if (ContainsAny(text, "cdp", "crossdeviceresume", "nearsdk", "romesdk"))
                return "Controls cross-device/shared experience and resume behavior.";
            if (ContainsAny(text, "lighting"))
                return "Controls Windows Dynamic Lighting device integration.";
            if (ContainsAny(text, "ucpd"))
                return "Controls the User Choice Protection Driver startup setting.";
            if (ContainsAny(text, "windowsstore", "autodownload"))
                return "Controls Microsoft Store automatic app download/update policy.";
            if (ContainsAny(text, "userduckingpreference"))
                return "Controls whether Windows lowers other app volume during communications activity.";
            if (ContainsAny(text, "disablestartupsound"))
                return "Controls Windows startup sound playback.";
            if (ContainsAny(text, "appcaptureenabled", "gamedvr_enabled", "allowgamebar", "allowgamedvr"))
                return "Controls Xbox Game Bar, Game DVR, and capture availability.";
            if (ContainsAny(text, "hwschmode"))
                return "Controls Hardware-Accelerated GPU Scheduling.";
            if (ContainsAny(text, "mousespeed", "mousethreshold1", "mousethreshold2"))
                return "Controls Enhanced Pointer Precision/mouse acceleration thresholds.";
            if (ContainsAny(text, "networkthrottlingindex"))
                return "Controls Windows multimedia network throttling behavior.";
            if (ContainsAny(text, "systemresponsiveness"))
                return "Controls the CPU percentage Windows reserves for background multimedia scheduling.";
            if (ContainsAny(text, "powerthrottlingoff"))
                return "Controls the Windows policy that turns Power Throttling off.";
            if (ContainsAny(text, "win32priorityseparation"))
                return "Controls foreground/background CPU scheduling separation.";
            if (ContainsAny(text, "allowtelemetry", "datacollection", "diagnostic"))
                return "Controls Windows diagnostic data and telemetry policy values.";
            if (ContainsAny(text, "capabilityaccessmanager", "consentstore", "appprivacy"))
                return "Controls Windows app permission consent for the named capability.";
            if (ContainsAny(text, "backgroundaccessapplications", "globaluserdisabled"))
                return "Controls whether Store/UWP apps can run in the background.";
            if (ContainsAny(text, "disablewebsearch", "bingsearchenabled", "cloudcontent", "safesearch", "isdevicesearchhistoryenabled", "isdynamicsearchboxenabled"))
                return "Controls Windows Search web, cloud, history, dynamic content, or filtering behavior.";
            if (ContainsAny(text, "explorer\\advanced", "taskbar", "start", "searchbox"))
                return "Controls Explorer, Start, taskbar, search, or file visibility behavior.";
            if (ContainsAny(text, "visualfxsetting", "userpreferencesmask", "minanimate", "taskbaranimations", "enableaeropeek"))
                return "Controls Windows visual effects and animation behavior.";
            if (ContainsAny(text, "listviewshadow", "dragfullwindows", "smoothscroll", "thumbnail", "icons only"))
                return "Controls Explorer/Desktop visual effects such as shadows, drag contents, scrolling, or thumbnails.";
            if (ContainsAny(text, "\\services\\") && ContainsAny(text, "\"start\""))
                return "Controls the startup mode for the referenced Windows service.";
            if (ContainsAny(text, "longpathsenabled"))
                return "Controls Win32 long path support.";
            if (ContainsAny(text, "ntfsdisablelastaccessupdate"))
                return "Controls NTFS last-access timestamp updates.";
            if (ContainsAny(text, "disableremoteassistance", "fallowtogethelp"))
                return "Controls Remote Assistance availability.";
            if (ContainsAny(text, "hibernat", "showsleepoption", "showlockoption"))
                return "Controls power menu, sleep, lock, or hibernate behavior.";
            if (ContainsAny(text, "autoplay", "nodrivetypeautorun"))
                return "Controls AutoPlay/AutoRun behavior.";
            if (ContainsAny(text, "multitasking", "virtualdesktops", "alttab"))
                return "Controls multitasking or Alt+Tab shell behavior.";
            if (ContainsAny(text, "languagebar", "hotkey", "input method"))
                return "Controls language bar or input switching behavior.";
            if (ContainsAny(text, "contentdeliverymanager", "subscribedcontent", "softlanding", "oempreinstalledapps"))
                return "Controls Windows suggested content, Spotlight, consumer features, or preinstalled app suggestions.";
            if (ContainsAny(text, "settingsync"))
                return "Controls Windows setting-sync categories and paid-network sync behavior.";
            if (ContainsAny(text, "findmydevice", "locationandsensors", "lfsvc"))
                return "Controls location or Find My Device policy values.";
            if (ContainsAny(text, "stickykeys", "filterkeys", "accessibility"))
                return "Controls accessibility hotkey/helper behavior.";
            if (ContainsAny(text, "faulttolerantheap"))
                return "Controls Fault Tolerant Heap compatibility mitigation settings.";
            if (ContainsAny(text, "max cached icons"))
                return "Controls Explorer icon cache capacity.";
            if (ContainsAny(text, "displayparameters", "crashcontrol"))
                return "Controls crash/blue-screen diagnostic display behavior.";
            if (ContainsAny(text, "wpbt"))
                return "Controls Windows Platform Binary Table behavior.";
            if (ContainsAny(text, "colortable", "screencolors"))
                return "Controls classic console color palette/background values.";
            if (ContainsAny(text, "traynotify", "systemtray"))
                return "Controls notification-area/tray icon visibility behavior.";
            if (ContainsAny(text, "settingspagevisibility"))
                return "Controls which Settings app pages are hidden or visible.";
            if (ContainsAny(text, "nolowdiskspacechecks"))
                return "Controls Explorer low disk space warning checks.";
            if (ContainsAny(text, "nowebservices", "nopublishingwizard"))
                return "Controls legacy Explorer web service and web publishing integrations.";
            if (ContainsAny(text, "disablelogonbackgroundimage"))
                return "Controls lock/sign-in screen background image behavior.";
            return "Writes " + entry.ValueName + " under " + entry.Section + ".";
        }

        private void AddFallbackTweak()
        {
            TweakItem item = NewParsedTweak("FALLBACK", "Dark theme");
            AddRegEntry(item, new RegEntry {
                Section = "HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
                ValueName = "\"AppsUseLightTheme\"",
                ValueLine = "\"AppsUseLightTheme\"=dword:00000000"
            });
            AddRegEntry(item, new RegEntry {
                Section = "HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
                ValueName = "\"SystemUsesLightTheme\"",
                ValueLine = "\"SystemUsesLightTheme\"=dword:00000000"
            });
        }

        private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private sealed class TweakItem
        {
            public string Risk;
            public string Name;
            public string Description;
            public string ActionId;
            public List<RegEntry> Entries;
            public CheckBox Selector;
        }

        private sealed class RegEntry
        {
            public string Section;
            public bool DeleteSection;
            public string ValueName;
            public string ValueLine;
        }
    }
}
