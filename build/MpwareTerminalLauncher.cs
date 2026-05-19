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
        private readonly HashSet<string> _registryEntryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _scriptPath;
        private readonly string _runtimeRoot;
        private Grid _content;
        private TextBlock _selectedCount;
        private TextBlock _statusLine;
        private string _activePage = "Registry Tweaks";

        public TerminalDashboardWindow()
        {
            _scriptPath = Program.ResolveScriptPath();
            _runtimeRoot = String.IsNullOrWhiteSpace(_scriptPath) ? null : IOPath.GetDirectoryName(_scriptPath);
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
            nav.Children.Add(NavButton("Debloater", ShowDebloater));
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
            StackPanel page = BeginPage("REGISTRY TWEAKS", "0 tweaks selected - for .REG / .PS1 export or direct apply.", 1040);

            Border promptBox = Box(_borderDim);
            promptBox.Margin = new Thickness(0, 0, 0, 24);
            StackPanel promptStack = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            promptBox.Child = promptStack;
            promptStack.Children.Add(Text("> mpware.exe is a standalone Windows app - double-click it, UAC prompts for admin, and selected tweaks apply directly.", 11, FontWeights.Bold, _accent));
            promptStack.Children.Add(Text("  Loaded " + _tweaks.Count + " deduplicated registry groups from the bundled tweak files. Select tweaks first, then apply or export.", 11, FontWeights.Normal, _muted));
            page.Children.Add(promptBox);

            Grid actions = new Grid();
            actions.Margin = new Thickness(0, 0, 0, 24);
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
            buttons.Children.Add(ActionButton("COPY .PS1", CopySelectedPs1, false));
            buttons.Children.Add(ActionButton("SAVE .PS1", SaveSelectedPs1, false));
            buttons.Children.Add(ActionButton("FULL TOOLS", delegate { RunFunction("import-reg"); }, false));

            string currentCategory = null;
            foreach (TweakItem tweak in _tweaks)
            {
                if (!String.Equals(currentCategory, tweak.Category, StringComparison.Ordinal))
                {
                    currentCategory = tweak.Category;
                    page.Children.Add(CategoryHeader(currentCategory));
                }
                page.Children.Add(TweakCard(tweak));
            }

            UpdateSelectedCount();
            RefreshNav();
        }

        private UIElement CategoryHeader(string category)
        {
            Grid grid = new Grid();
            grid.Margin = new Thickness(0, 18, 0, 10);

            TextBlock heading = Text(category, 16, FontWeights.Bold, _accent);
            grid.Children.Add(heading);
            return grid;
        }

        private Border TweakCard(TweakItem tweak)
        {
            Border card = Box(_border);
            card.Margin = new Thickness(0, 0, 0, 12);

            Grid grid = new Grid();
            grid.Margin = new Thickness(14, 12, 14, 12);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            card.Child = grid;

            CheckBox selector = new CheckBox();
            selector.Margin = new Thickness(0, 3, 12, 0);
            selector.VerticalAlignment = VerticalAlignment.Top;
            selector.Checked += delegate { UpdateSelectedCount(); };
            selector.Unchecked += delegate { UpdateSelectedCount(); };
            tweak.Selector = selector;
            grid.Children.Add(selector);

            StackPanel body = new StackPanel();
            Grid.SetColumn(body, 1);
            grid.Children.Add(body);

            StackPanel titleLine = new StackPanel { Orientation = Orientation.Horizontal };
            titleLine.Children.Add(Text(tweak.Name, 13, FontWeights.Bold, _text));
            titleLine.Children.Add(RiskPill(tweak.Risk));
            body.Children.Add(titleLine);

            TextBlock description = Text(tweak.Description, 11, FontWeights.Normal, _text);
            description.Margin = new Thickness(0, 5, 0, 10);
            body.Children.Add(description);

            Button path = FlatButton(">  REGISTRY PATCH", false);
            path.Height = 24;
            path.MinWidth = 142;
            path.Padding = new Thickness(8, 0, 8, 0);
            path.HorizontalAlignment = HorizontalAlignment.Left;
            path.ToolTip = "Click to show full registry paths, values, and descriptions.";
            path.Click += delegate { ShowRegistryPatch(tweak); };
            body.Children.Add(path);

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
            stack.Children.Add(SectionTitle("LATEST NVIDIA DRIVER", "Downloads and opens the bundled clean-install helper."));
            stack.Children.Add(InfoLine("Requires administrator approval."));
            stack.Children.Add(InfoLine("The helper checks the current driver list and starts the install workflow."));

            Button install = ActionButton("DOWNLOAD AND INSTALL LATEST DRIVER", delegate { RunNvidiaDriverInstaller(); }, true);
            install.Height = 42;
            install.Margin = new Thickness(0, 24, 0, 0);
            install.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(install);

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
            warningStack.Children.Add(Text("Removed Store apps usually need to be reinstalled from Microsoft Store or winget. Create a restore point first.", 11, FontWeights.Bold, _text));
            page.Children.Add(warning);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            page.Children.Add(grid);

            AddDebloatTile(grid, 0, 0, "RECOMMENDED", "Keeps Store, Xbox and Edge. Best first-pass cleanup.", "debloat -Autorun 1 -debloatSXE 1");
            AddDebloatTile(grid, 0, 1, "KEEP STORE", "Removes more apps but keeps Microsoft Store.", "debloat -Autorun 1 -debloatS 1");
            AddDebloatTile(grid, 1, 0, "FULL DEBLOAT", "Aggressive preset. Removes the most bundled apps.", "debloat -Autorun 1 -debloatAll 1");
            AddDebloatTile(grid, 1, 1, "ADVANCED PRESETS", "Open the full debloat UI for manual choices.", "debloat");

            RefreshNav();
        }

        private void ShowRestoreTweaks(object sender, RoutedEventArgs e)
        {
            StackPanel page = BeginPage("RESTORE TWEAKS", "Undo and repair helpers live separately from registry patching.", 760);

            Border box = Box(_border);
            page.Children.Add(box);

            StackPanel stack = new StackPanel { Margin = new Thickness(24) };
            box.Child = stack;
            stack.Children.Add(SectionTitle("RESTORE CENTER", "Open the bundled restore tool for registry, app, service, and shell rollback options."));
            stack.Children.Add(InfoLine("Use this after testing tweaks or before trying a different preset."));
            stack.Children.Add(InfoLine("Some removals, especially app debloat, may still require reinstalling from Store or winget."));

            Button open = ActionButton("OPEN RESTORE CENTER", delegate { RunScript("Restore.ps1"); }, true);
            open.Height = 40;
            open.Margin = new Thickness(0, 24, 0, 0);
            open.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(open);

            Button full = ActionButton("OPEN FULL TOOLS", delegate { RunFunction("import-reg"); }, false);
            full.Height = 36;
            full.Margin = new Thickness(0, 12, 0, 0);
            full.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(full);

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
            warningStack.Children.Add(Paragraph("This tool directly modifies the Windows Registry and system configuration. Know what you are doing before applying anything."));
            warningStack.Children.Add(Bullet("Create a System Restore point before applying any tweaks."));
            warningStack.Children.Add(Bullet("Run mpware.exe as Administrator - it will auto-prompt UAC on launch actions."));
            warningStack.Children.Add(Bullet("Tweaks labeled Advanced may cause instability or break software."));
            warningStack.Children.Add(Bullet("Not responsible for any damage or data loss from using these scripts."));
            warningStack.Children.Add(Bullet("Debloat removal is permanent - removed apps must be reinstalled from the Store."));
            page.Children.Add(warnings);

            Grid two = new Grid();
            two.ColumnDefinitions.Add(new ColumnDefinition());
            two.ColumnDefinitions.Add(new ColumnDefinition());
            two.Margin = new Thickness(0, 0, 0, 28);
            page.Children.Add(two);

            Border how = Box(_border);
            how.Margin = new Thickness(0, 0, 12, 0);
            how.Child = AboutPanel("HOW TO USE MPWARE.EXE", new string[] {
                "1. Download and run mpware.exe. Keep it in the extracted release folder or use the bundled standalone exe.",
                "2. Windows may show UAC when you apply tweaks, debloat, or install NVIDIA drivers. Click Yes only when you trust the action.",
                "3. Registry Tweaks: select individual groups or press SELECT ALL, then press APPLY SELECTED to import them.",
                "4. NVIDIA Driver: press DOWNLOAD AND INSTALL LATEST DRIVER. The PowerShell window stays open if the helper reports an error.",
                "5. Debloater: start with RECOMMENDED, then restart your PC after registry or app-removal changes."
            }, "");
            two.Children.Add(how);

            Border risk = Box(_border);
            risk.Margin = new Thickness(12, 0, 0, 0);
            risk.Child = RiskPanel();
            Grid.SetColumn(risk, 1);
            two.Children.Add(risk);

            Border included = Box(_border);
            included.Child = IncludedPanel();
            page.Children.Add(included);

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
            TextBlock note = Text("Debloat items use PowerShell Remove-AppxPackage - removal is permanent.", 11, FontWeights.Normal, _muted);
            note.Margin = new Thickness(0, 18, 0, 0);
            stack.Children.Add(note);
            return stack;
        }

        private StackPanel IncludedPanel()
        {
            StackPanel outer = new StackPanel { Margin = new Thickness(22) };
            outer.Children.Add(Text("() WHAT'S INCLUDED", 15, FontWeights.Bold, _accent));
            Grid grid = new Grid();
            grid.Margin = new Thickness(0, 22, 0, 0);
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            outer.Children.Add(grid);

            AddIncludedColumn(grid, 0, "GAMING", new string[] { "Disable Game DVR", "Enable HAGS", "Disable Mouse Accel", "Disable FSO", "Boost Foreground Priority", "System Profile for Games" });
            AddIncludedColumn(grid, 1, "NETWORK", new string[] { "Disable Net Throttling", "Disable Nagle", "QoS reservation", "MMCSS responsiveness", "Power throttling" });
            AddIncludedColumn(grid, 2, "CPU & POWER", new string[] { "High Performance Plan", "Timer Resolution", "Core Parking hooks", "Spectre mitigation toggle", "Kernel RAM preference" });
            return outer;
        }

        private void AddIncludedColumn(Grid grid, int column, string title, string[] lines)
        {
            StackPanel stack = new StackPanel();
            stack.Margin = new Thickness(column == 0 ? 0 : 22, 0, 0, 0);
            Grid.SetColumn(stack, column);
            grid.Children.Add(stack);
            stack.Children.Add(Text(title, 12, FontWeights.Bold, _accent));
            foreach (string line in lines)
            {
                stack.Children.Add(Text("> " + line, 11, FontWeights.Normal, _muted));
            }
        }

        private TextBlock RiskLine(string label, Brush brush, string copy)
        {
            TextBlock line = Text(label + "   " + copy, 11, FontWeights.Normal, _text);
            line.Margin = new Thickness(0, 18, 0, 0);
            line.Foreground = _text;
            line.TextDecorations = null;
            return line;
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

        private void SetCategory(string category, bool selected)
        {
            foreach (TweakItem tweak in _tweaks)
            {
                if (String.Equals(tweak.Category, category, StringComparison.Ordinal) && tweak.Selector != null)
                {
                    tweak.Selector.IsChecked = selected;
                }
            }
            UpdateSelectedCount();
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

        private void ApplyAllTweaks(object sender, RoutedEventArgs e)
        {
            SelectAllTweaks();
            ApplyRegistryTweaks(new List<TweakItem>(_tweaks), "all registry tweak groups");
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
            string regPath = IOPath.Combine(IOPath.GetTempPath(), "mpware-selected-" + Guid.NewGuid().ToString("N") + ".reg");
            File.WriteAllText(regPath, BuildRegFile(selected), Encoding.Unicode);
            string script =
                "$ErrorActionPreference='Stop';" +
                "$reg='" + PsEscape(regPath) + "';" +
                "Write-Host 'mpware: importing selected registry tweaks...' -ForegroundColor Cyan;" +
                "& reg.exe import $reg;" +
                "if ($LASTEXITCODE -ne 0) { throw 'reg.exe import failed with exit code ' + $LASTEXITCODE };" +
                "try { Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Start-Process explorer.exe } catch {};" +
                "Write-Host 'mpware: registry tweaks applied. Restart recommended.' -ForegroundColor Green;" +
                "Pause";
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
            File.WriteAllText(path, BuildRegFile(selected), Encoding.Unicode);
            SetStatus("exported " + path);
            MessageBox.Show("Saved .REG export to Desktop.", "mpware", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CopySelectedPs1(object sender, RoutedEventArgs e)
        {
            List<TweakItem> selected = GetSelectedTweaks();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select at least one registry tweak first.", "mpware", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Clipboard.SetText(BuildRegistryScript(selected));
            SetStatus("copied selected tweak script");
        }

        private void SaveSelectedPs1(object sender, RoutedEventArgs e)
        {
            List<TweakItem> selected = GetSelectedTweaks();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select at least one registry tweak first.", "mpware", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string path = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "mpware-selected-tweaks.ps1");
            File.WriteAllText(path, BuildRegistryScript(selected), Encoding.UTF8);
            SetStatus("saved " + path);
            MessageBox.Show("Saved .PS1 export to Desktop.", "mpware", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private string BuildRegistryScript(List<TweakItem> selected)
        {
            string regBase64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildRegFile(selected)));
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# mpware - selected registry tweaks");
            sb.AppendLine("# Run as Administrator");
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine("$regPath = Join-Path $env:TEMP ('mpware-selected-' + [guid]::NewGuid().ToString('N') + '.reg')");
            sb.AppendLine("$regBytes = [Convert]::FromBase64String('" + regBase64 + "')");
            sb.AppendLine("[IO.File]::WriteAllBytes($regPath, $regBytes)");
            sb.AppendLine("& reg.exe import $regPath");
            sb.AppendLine("if ($LASTEXITCODE -ne 0) { throw 'reg.exe import failed with exit code ' + $LASTEXITCODE }");
            sb.AppendLine("try { Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Start-Process explorer.exe } catch {}");
            sb.AppendLine("Write-Host 'mpware registry tweaks applied. Restart recommended.' -ForegroundColor Cyan");
            sb.AppendLine("Pause");
            return sb.ToString();
        }

        private string BuildRegFile(List<TweakItem> selected)
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
                        sb.AppendLine("[" + entry.Section + "]");
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

        private string BuildNvidiaLauncherScript()
        {
            string root = _runtimeRoot ?? "$PSScriptRoot";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# mpware - NVIDIA Driver Setup");
            sb.AppendLine("# Run as Administrator");
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine("$runtime = '" + PsEscape(root) + "'");
            sb.AppendLine("$script = Join-Path $runtime 'NvidiaAutoinstall.ps1'");
            sb.AppendLine("if (-not (Test-Path -LiteralPath $script)) { throw 'NVIDIA helper was not found.' }");
            sb.AppendLine("Write-Host 'Opening mpware NVIDIA driver helper...' -ForegroundColor Cyan");
            sb.AppendLine("Start-Process powershell.exe -Verb RunAs -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File \"' + $script + '\"')");
            return sb.ToString();
        }

        private void RunNvidiaDriverInstaller()
        {
            if (!EnsureRuntime())
            {
                return;
            }

            string script = IOPath.Combine(_runtimeRoot, "NvidiaAutoinstall.ps1");
            if (!File.Exists(script))
            {
                MessageBox.Show("Missing runtime script: NvidiaAutoinstall.ps1", "mpware", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string command =
                "$ErrorActionPreference='Stop';" +
                "try {" +
                "  Set-Location -LiteralPath '" + PsEscape(_runtimeRoot) + "';" +
                "  $Global:folder='" + PsEscape(_runtimeRoot) + "';" +
                "  $Global:sysDrive=$env:SystemDrive.TrimEnd('\\')+'\\';" +
                "  $Global:tempDir=([System.IO.Path]::GetTempPath()).TrimEnd('\\');" +
                "  $Global:iconDir=Join-Path $Global:folder 'mpwareIcons';" +
                "  $Global:customIcon=Join-Path $Global:iconDir 'Powershell_black.ico';" +
                "  Import-Module (Join-Path $Global:folder 'zFunctions.psm1') -Force -Global;" +
                "  Import-Module (Join-Path $Global:folder 'winfetch.psm1') -Force;" +
                "  Write-Host 'mpware: starting NVIDIA driver helper...' -ForegroundColor Cyan;" +
                "  & '" + PsEscape(script) + "';" +
                "  Write-Host 'mpware: NVIDIA helper finished.' -ForegroundColor Green;" +
                "} catch {" +
                "  Write-Host ''; Write-Host 'mpware: NVIDIA helper failed:' -ForegroundColor Red;" +
                "  Write-Host $_.Exception.Message -ForegroundColor Red;" +
                "  Write-Host ''; Read-Host 'Press Enter to close';" +
                "}";

            RunElevatedPowerShellNoExit(command, "launching NVIDIA driver helper");
        }

        private void CopyNvidiaPs1(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(BuildNvidiaLauncherScript());
            SetStatus("copied nvidia launcher script");
        }

        private void SaveNvidiaPs1(object sender, RoutedEventArgs e)
        {
            string path = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "mpware-nvidia-driver.ps1");
            File.WriteAllText(path, BuildNvidiaLauncherScript(), Encoding.UTF8);
            SetStatus("saved " + path);
            MessageBox.Show("Saved NVIDIA .PS1 launcher to Desktop.", "mpware", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveNvidiaBat(object sender, RoutedEventArgs e)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string ps1 = IOPath.Combine(desktop, "mpware-nvidia-driver.ps1");
            string bat = IOPath.Combine(desktop, "mpware-nvidia-driver.bat");
            File.WriteAllText(ps1, BuildNvidiaLauncherScript(), Encoding.UTF8);
            File.WriteAllText(bat, "@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%~dp0mpware-nvidia-driver.ps1\"\r\n", Encoding.ASCII);
            SetStatus("saved one-click nvidia launcher");
            MessageBox.Show("Saved NVIDIA .BAT and .PS1 launchers to Desktop.", "mpware", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RunElevatedPowerShell(string script, string log)
        {
            SetStatus(log);
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded;
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            Process.Start(psi);
        }

        private void RunElevatedPowerShellNoExit(string script, string log)
        {
            SetStatus(log);
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoExit -NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded;
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
                "$Global:iconDir=Join-Path $Global:folder 'mpwareIcons';" +
                "$Global:customIcon=Join-Path $Global:iconDir 'Powershell_black.ico';" +
                "Import-Module (Join-Path $Global:folder 'zFunctions.psm1') -Force -Global;" +
                "Import-Module (Join-Path $Global:folder 'winfetch.psm1') -Force;" +
                "& '" + PsEscape(script) + "';";
            RunElevatedPowerShellNoExit(command, "launching " + relativeScript);
        }

        private void RunFunction(string functionCall)
        {
            if (!EnsureRuntime())
            {
                return;
            }

            SetStatus("launching " + functionCall);
            string escapedRoot = PsEscape(_runtimeRoot);
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
            RunElevatedPowerShell(command, "launching " + functionCall);
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
                "try {" +
                "  Set-Location -LiteralPath '" + escapedRoot + "';" +
                "  $Global:folder='" + escapedRoot + "';" +
                "  $Global:sysDrive=$env:SystemDrive.TrimEnd('\\')+'\\';" +
                "  $Global:tempDir=([System.IO.Path]::GetTempPath()).TrimEnd('\\');" +
                "  $Global:iconDir=Join-Path $Global:folder 'mpwareIcons';" +
                "  $Global:customIcon=Join-Path $Global:iconDir 'Powershell_black.ico';" +
                "  Import-Module (Join-Path $Global:folder 'zFunctions.psm1') -Force -Global;" +
                "  Import-Module (Join-Path $Global:folder 'winfetch.psm1') -Force;" +
                "  " + functionCall + ";" +
                "  Write-Host 'mpware: command finished.' -ForegroundColor Green;" +
                "} catch {" +
                "  Write-Host ''; Write-Host 'mpware: command failed:' -ForegroundColor Red;" +
                "  Write-Host $_.Exception.Message -ForegroundColor Red;" +
                "};" +
                "Write-Host ''; Read-Host 'Press Enter to close'";
            RunElevatedPowerShellNoExit(command, "launching " + functionCall);
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

        private void BuildTweaks()
        {
            LoadBundledRegistryTweaks();
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
            item.Category = CategoryForTitle(source, title);
            item.Risk = RiskForTitle(title);
            item.Name = title;
            item.Description = "Review the registry patch before applying.";
            item.Entries = new List<RegEntry>();
            _tweaks.Add(item);
            return item;
        }

        private void AddRegEntry(TweakItem tweak, RegEntry entry)
        {
            string key = entry.DeleteSection ? "DELETE::" + entry.Section : entry.Section + "::" + entry.ValueName;
            if (_registryEntryKeys.Contains(key))
            {
                return;
            }

            _registryEntryKeys.Add(key);
            tweak.Entries.Add(entry);
        }

        private string CategoryForTitle(string source, string title)
        {
            string text = (source + " " + title).ToLowerInvariant();
            if (text.IndexOf("context", StringComparison.Ordinal) >= 0 || text.IndexOf("menu", StringComparison.Ordinal) >= 0) return "CONTEXT MENU";
            if (ContainsAny(text, "game", "xbox", "mouse", "gpu", "hags", "mmcss", "fullscreen", "foreground")) return "GAMING PERFORMANCE";
            if (ContainsAny(text, "network", "tcp", "qos", "nagle", "throttling", "dns")) return "NETWORK & LATENCY";
            if (ContainsAny(text, "privacy", "telemetry", "cortana", "advertising", "location", "camera", "contacts", "calendar", "diagnostic", "activity", "ai", "copilot")) return "PRIVACY & TELEMETRY";
            if (ContainsAny(text, "dark", "theme", "taskbar", "start", "explorer", "snap", "search", "widgets", "transparency", "dpi", "sound", "visual", "desktop", "gallery", "home shortcut")) return "VISUAL & SHELL";
            if (ContainsAny(text, "power", "hibernate", "sleep", "timer", "spectre", "meltdown", "memory", "core", "hpet", "priority")) return "CPU & POWER";
            return "WINDOWS TWEAKS";
        }

        private string RiskForTitle(string title)
        {
            string text = title.ToLowerInvariant();
            if (ContainsAny(text, "uac", "user account control", "core isolation", "deviceguard", "credential", "spectre", "meltdown", "system requirements", "labconfig", "hpet")) return "Advanced";
            if (ContainsAny(text, "network", "power", "hibernate", "timer", "driver", "privacy deny", "camera", "file system", "memory", "service")) return "Moderate";
            return "Safe";
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
            if (ContainsAny(title, "core isolation", "vbs"))
                return "Turns off virtualization-based security and memory integrity policy values. Can improve compatibility/performance but lowers hardening.";
            if (ContainsAny(title, "system requirements", "labconfig", "unsupported"))
                return "Sets setup compatibility bypass values for unsupported Windows 11 hardware checks.";
            if (ContainsAny(title, "user account control", "uac"))
                return "Changes User Account Control policy. This reduces Windows consent prompts and should be treated as advanced.";
            if (ContainsAny(title, "classic context menu"))
                return "Restores the older full right-click context menu by adding the Explorer CLSID override.";
            if (ContainsAny(title, "storage sense"))
                return "Disables the Storage Sense policy so Windows will not automatically clean selected files.";
            if (ContainsAny(title, "dark theme"))
                return "Sets Windows personalization values so apps and the system use dark mode.";
            if (ContainsAny(title, "transparency"))
                return "Turns off Windows transparency effects for a simpler shell and slightly less visual overhead.";
            if (ContainsAny(title, "hardware accelerated gpu", "hags"))
                return "Enables Hardware-Accelerated GPU Scheduling when the GPU and driver support it.";
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
            if (ContainsAny(title, "memory compression"))
                return "Changes Memory Management values used by Windows memory compression behavior.";
            if (ContainsAny(title, "remote assistance"))
                return "Disables Remote Assistance policy values so unsolicited assistance offers are blocked.";
            if (ContainsAny(title, "long paths"))
                return "Enables Win32 long path support for applications that opt in to long paths.";
            if (ContainsAny(title, "last access time"))
                return "Disables NTFS last-access timestamp updates to reduce file-system metadata writes.";
            if (ContainsAny(title, "privacy deny"))
                return "Sets Windows app privacy consent values to deny that permission for Store/UWP apps.";
            if (ContainsAny(title, "telemetry", "data collection", "diagnostic"))
                return "Limits Windows diagnostic data and related telemetry policy values where supported by the edition.";
            if (ContainsAny(title, "copilot", "windows ai", "ai insights"))
                return "Disables Copilot/Windows AI feature policy or visibility values.";
            if (ContainsAny(title, "search web results", "web search", "cloud content search", "safe search"))
                return "Adjusts Windows Search policy values for web results, cloud search, or SafeSearch behavior.";
            if (ContainsAny(title, "taskbar", "start menu", "recently", "recommend", "widgets", "news and interests", "meet now", "chat", "task view"))
                return "Changes Explorer, Start, and taskbar registry values for a cleaner Windows shell.";
            if (ContainsAny(title, "snap"))
                return "Disables Windows snap layout, snap assist, and snap group shell behavior.";
            if (ContainsAny(title, "file explorer", "quick access", "hidden files", "file name extensions", "folder type"))
                return "Changes File Explorer registry values for visibility, navigation, or default folder behavior.";
            if (ContainsAny(title, "animations", "animate", "peek", "thumbnails", "visual", "best performance", "drop shadows", "smooth edges"))
                return "Changes Windows visual-effects values used for animations, previews, shadows, thumbnails, and font smoothing.";
            if (ContainsAny(title, "sleep", "hibernate", "lock", "power modes", "power"))
                return "Changes power, lock, sleep, or hibernate policy values.";
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
            if (ContainsAny(text, "enabletransparency"))
                return "Controls Windows transparency effects.";
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
            if (ContainsAny(text, "windowscopilot", "showcopilotbutton", "windowsai", "allowrecallenablement", "disableaidataanalysis"))
                return "Controls Copilot, Recall, or Windows AI feature availability.";
            if (ContainsAny(text, "disablewebsearch", "bingsearchenabled", "cloudcontent", "safesearch"))
                return "Controls Windows Search web, cloud, and filtering behavior.";
            if (ContainsAny(text, "explorer\\advanced", "taskbar", "start", "searchbox", "hidefileext", "showhidden"))
                return "Controls Explorer, Start, taskbar, search, or file visibility behavior.";
            if (ContainsAny(text, "visualfxsetting", "userpreferencesmask", "minanimate", "taskbaranimations", "enableaeropeek"))
                return "Controls Windows visual effects and animation behavior.";
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
            public string Category;
            public string Risk;
            public string Name;
            public string Description;
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
