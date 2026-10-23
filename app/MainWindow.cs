// The window. Design rules, in order: one headline that answers "am I up to date?", one primary action, everything else
// in quiet cards. Scans and installs run on background threads so the window never freezes, and nothing is installed
// until Install is clicked and confirmed.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Mdu
{
    // Follows Windows' light/dark setting and accent color.
    class Theme
    {
        public bool Dark;
        public string WindowBg, Card, Border, Text, Text2, Hover, Accent, Ok, Warn, Bad, Muted;

        public static Theme Load()
        {
            bool dark = false;
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                dark = v != null && Convert.ToInt32(v) == 0;
            }
            catch { }
            string accent = "#0A84FF";
            try
            {
                object raw = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
                if (raw != null)
                {
                    byte[] b = BitConverter.GetBytes(Convert.ToInt32(raw));   // stored as ABGR: byte 0 = R, 1 = G, 2 = B
                    accent = string.Format("#{0:X2}{1:X2}{2:X2}", b[0], b[1], b[2]);
                }
            }
            catch { }
            Theme t = new Theme();
            t.Dark = dark; t.Accent = accent;
            if (dark)
            {
                t.WindowBg = "#1C1C1E"; t.Card = "#2C2C2E"; t.Border = "#3A3A3C"; t.Text = "#F5F5F7"; t.Text2 = "#98989D"; t.Hover = "#3A3A3C";
                t.Ok = "#30D158"; t.Warn = "#FFD60A"; t.Bad = "#FF453A"; t.Muted = "#98989D";
            }
            else
            {
                t.WindowBg = "#F5F5F7"; t.Card = "#FFFFFF"; t.Border = "#E5E5EA"; t.Text = "#1D1D1F"; t.Text2 = "#6E6E73"; t.Hover = "#F2F2F7";
                t.Ok = "#248A3D"; t.Warn = "#C25E00"; t.Bad = "#D70015"; t.Muted = "#6E6E73";
            }
            return t;
        }
    }

    class Row
    {
        public Border Element;
        public Grid Grid;
        public CheckBox CheckBox;
        public ProgressBar Bar;
        public TextBlock BarText;
        public TextBlock Secondary;
        public TextBlock CardCount;   // the count pill of the card this row is counted in (lowered when it is hidden)
        public string Name, Kind, HardwareId, Version;
    }

    // A small text link on a row's second line.
    class RowLink
    {
        public string Text, Tip;
        public RoutedEventHandler Click;
        public RowLink(string text, string tip, RoutedEventHandler click) { Text = text; Tip = tip; Click = click; }
    }

    class Card
    {
        public Border Border;
        public StackPanel Body;
        public Button Action;
        public TextBlock CountText;   // the number in the header pill
    }

    class MainWindow
    {
        public readonly Window Window;
        readonly Theme _theme;
        readonly Dictionary<string, Brush> _brush = new Dictionary<string, Brush>();
        readonly TextBlock _headline, _subline, _status, _version;
        readonly Button _primary, _rescan, _report, _later, _restart, _help;
        readonly ProgressBar _progress;
        readonly StackPanel _sections;
        readonly Border _rebootBanner, _updateBanner;
        readonly TextBlock _updateText;
        readonly Button _updateButton, _updateNotes;
        UpdateInfo _update;
        bool _updating;
        readonly CheckBox _weekly;
        readonly ScrollViewer _scroller;
        readonly DispatcherTimer _timer;

        ScanResult _scan;
        ScanProgress _scanProg;
        InstallJob _job;
        bool _scanning, _installing, _busy, _suppressWeekly;
        string _primaryMode = "scan";

        readonly Dictionary<string, Row> _driverRows = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> _driverOrder = new List<string>();
        readonly Dictionary<string, WuDriver> _driverByTitle = new Dictionary<string, WuDriver>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Row> _appRows = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> _appOrder = new List<string>();
        readonly HashSet<Row> _finished = new HashSet<Row>();
        readonly Dictionary<string, Button> _toggle = new Dictionary<string, Button>();
        Row _nvRow;
        Card _hiddenCard, _historyCard;
        Border _allDevicesBorder;
        NvidiaDriver _nvInfo;

        public MainWindow()
        {
            _theme = Theme.Load();
            _brush["WindowBg"] = MakeBrush(_theme.WindowBg);
            _brush["Card"] = MakeBrush(_theme.Card);
            _brush["Border"] = MakeBrush(_theme.Border);
            _brush["Text"] = MakeBrush(_theme.Text);
            _brush["Text2"] = MakeBrush(_theme.Text2);
            _brush["Accent"] = MakeBrush(_theme.Accent);
            _brush["Hover"] = MakeBrush(_theme.Hover);
            _brush["AccentTint"] = MakeBrush("#22" + _theme.Accent.Substring(1));

            using (Stream s = typeof(MainWindow).Assembly.GetManifestResourceStream("Mdu.MainWindow.xaml"))
                Window = (Window)XamlReader.Load(s);
            foreach (KeyValuePair<string, Brush> kv in _brush) Window.Resources[kv.Key] = kv.Value;
            try
            {
                using (Stream ico = typeof(MainWindow).Assembly.GetManifestResourceStream("Mdu.mdu.ico"))
                    if (ico != null)
                    {
                        IconBitmapDecoder dec = new IconBitmapDecoder(ico, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        BitmapFrame big = dec.Frames[0];
                        foreach (BitmapFrame f in dec.Frames) if (f.PixelWidth > big.PixelWidth) big = f;
                        Window.Icon = big;
                        ((Image)Window.FindName("Logo")).Source = big;
                    }
            }
            catch { }
            Window.TaskbarItemInfo = new TaskbarItemInfo();

            _headline = (TextBlock)Window.FindName("Headline");
            _subline = (TextBlock)Window.FindName("Subline");
            _status = (TextBlock)Window.FindName("Status");
            _version = (TextBlock)Window.FindName("VersionText");
            _primary = (Button)Window.FindName("PrimaryButton");
            _rescan = (Button)Window.FindName("RescanButton");
            _report = (Button)Window.FindName("ReportButton");
            _later = (Button)Window.FindName("LaterButton");
            _restart = (Button)Window.FindName("RestartButton");
            _help = (Button)Window.FindName("HelpButton");
            _progress = (ProgressBar)Window.FindName("Progress");
            _sections = (StackPanel)Window.FindName("Sections");
            _rebootBanner = (Border)Window.FindName("RebootBanner");
            _updateBanner = (Border)Window.FindName("UpdateBanner");
            _updateText = (TextBlock)Window.FindName("UpdateText");
            _updateButton = (Button)Window.FindName("UpdateButton");
            _updateNotes = (Button)Window.FindName("UpdateNotesButton");
            _weekly = (CheckBox)Window.FindName("WeeklyCheck");
            _scroller = (ScrollViewer)Window.FindName("Scroller");
            Hyperlink about = new Hyperlink(new Run("v" + AppInfo.Version));
            about.Foreground = _brush["Text2"];
            about.TextDecorations = null;
            about.Cursor = Cursors.Hand;
            about.Click += (s, e) => Open(Updater.RepoUrl);
            _version.Inlines.Add(about);

            _timer = new DispatcherTimer(DispatcherPriority.Normal, Window.Dispatcher);
            _timer.Interval = TimeSpan.FromMilliseconds(250);
            _timer.Tick += OnTick;

            _primary.Click += (s, e) => { if (_primaryMode == "install") StartInstall(false, null); else StartScan(); };
            _rescan.Click += (s, e) => StartScan();
            _report.Click += (s, e) => { if (_scan != null && _scan.ReportHtml != null) Open(_scan.ReportHtml); };
            _later.Click += (s, e) => _rebootBanner.Visibility = Visibility.Collapsed;
            _restart.Click += (s, e) =>
            {
                if (Ask("Restart now? Save your work first; the restart starts in 30 seconds.", MessageBoxImage.Question))
                    Util.Run(Util.SystemTool("shutdown.exe"), "/r /t 30 /c \"Miller Driver Updater: restarting to finish driver installation.\"", 15000);
            };
            _help.Click += (s, e) => ReportProblem();
            _updateNotes.Click += (s, e) => { if (_update != null && !string.IsNullOrEmpty(_update.PageUrl)) Open(_update.PageUrl); };
            _updateButton.Click += (s, e) => StartUpdate();
            _weekly.Checked += (s, e) => SetWeekly(true);
            _weekly.Unchecked += (s, e) => SetWeekly(false);

            Window.SourceInitialized += (s, e) => NativeUi.DarkTitleBar(new WindowInteropHelper(Window).Handle, _theme.Dark);
            Window.Loaded += (s, e) => { StartScan(); LoadWeeklyState(); CheckForUpdate(); };
            Window.Closing += OnClosing;
            Window.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F5 && !_scanning && !_installing && !_busy) { e.Handled = true; StartScan(); }
            };
        }

        // ---------------------------------------------------------------- building blocks

        static SolidColorBrush MakeBrush(string hex)
        {
            SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        TextBlock NewText(string text, double size = 13, FontWeight? weight = null, Brush brush = null)
        {
            TextBlock t = new TextBlock();
            t.Text = text ?? "";
            t.FontSize = size;
            t.FontWeight = weight ?? FontWeights.Normal;
            t.TextWrapping = TextWrapping.Wrap;
            if (brush != null) t.Foreground = brush;
            return t;
        }

        Border NewPill(string text, string kind)
        {
            string fg = kind == "ok" ? _theme.Ok : kind == "warn" ? _theme.Warn : kind == "bad" ? _theme.Bad : kind == "info" ? _theme.Accent : _theme.Muted;
            Border b = new Border();
            b.CornerRadius = new CornerRadius(10);
            b.Padding = new Thickness(9, 2, 9, 2);
            b.Margin = new Thickness(12, 0, 0, 0);
            b.VerticalAlignment = VerticalAlignment.Center;
            b.Background = MakeBrush("#1F" + fg.Substring(1));
            TextBlock t = NewText(text, 11, FontWeights.SemiBold, MakeBrush(fg));
            t.TextWrapping = TextWrapping.NoWrap;
            b.Child = t;
            return b;
        }

        Button NewButton(string text, RoutedEventHandler onClick)
        {
            Button b = new Button();
            b.Content = text;
            b.Style = (Style)Window.FindResource("Secondary");
            b.VerticalAlignment = VerticalAlignment.Center;
            if (onClick != null) b.Click += onClick;
            return b;
        }

        Card NewCard(string title, string subtitle, string count, string countKind = "info", string actionText = null, RoutedEventHandler onAction = null)
        {
            Card c = new Card();
            c.Border = new Border();
            c.Border.Background = _brush["Card"];
            c.Border.BorderBrush = _brush["Border"];
            c.Border.BorderThickness = new Thickness(1);
            c.Border.CornerRadius = new CornerRadius(12);
            c.Border.Padding = new Thickness(20, 16, 20, 10);
            c.Border.Margin = new Thickness(0, 0, 0, 14);
            StackPanel stack = new StackPanel();
            // Header: title + count on the left, an optional action button (e.g. "Check all") on the right.
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(NewText(title, 15, FontWeights.SemiBold));
            if (!string.IsNullOrEmpty(count))
            {
                Border pill = NewPill(count, countKind);
                c.CountText = pill.Child as TextBlock;
                left.Children.Add(pill);
            }
            Grid.SetColumn(left, 0);
            head.Children.Add(left);
            if (actionText != null)
            {
                c.Action = NewButton(actionText, onAction);
                c.Action.Padding = new Thickness(12, 4, 12, 4);
                c.Action.FontSize = 12;
                Grid.SetColumn(c.Action, 1);
                head.Children.Add(c.Action);
            }
            stack.Children.Add(head);
            if (!string.IsNullOrEmpty(subtitle))
            {
                TextBlock s = NewText(subtitle, 12, null, _brush["Text2"]);
                s.Margin = new Thickness(0, 3, 0, 0);
                stack.Children.Add(s);
            }
            c.Body = new StackPanel();
            c.Body.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(c.Body);
            c.Border.Child = stack;
            _sections.Children.Add(c.Border);
            return c;
        }

        // A click on a button or checkbox inside the row belongs to that control, not to the row.
        static bool FromControl(DependencyObject d, DependencyObject stop)
        {
            while (d != null && d != stop)
            {
                if (d is ButtonBase || d is Hyperlink) return true;   // CheckBox is a ButtonBase too
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        Row NewRow(string primary, string secondary = null, string right = null, string tag = null, string tagKind = "muted",
                   bool checkable = false, bool isChecked = false, string buttonText = null, RoutedEventHandler onClick = null, bool last = false,
                   RowLink[] links = null)
        {
            Row row = new Row();
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (checkable)
            {
                row.CheckBox = new CheckBox();
                row.CheckBox.IsChecked = isChecked;
                row.CheckBox.Margin = new Thickness(0, 0, 14, 0);
                row.CheckBox.Checked += (s, e) => UpdatePrimaryButton();
                row.CheckBox.Unchecked += (s, e) => UpdatePrimaryButton();
                AutomationNameFor(row.CheckBox, primary);
                Grid.SetColumn(row.CheckBox, 0);
                grid.Children.Add(row.CheckBox);
            }
            StackPanel text = new StackPanel();
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Children.Add(NewText(primary, 13, FontWeights.SemiBold));
            if (!string.IsNullOrEmpty(secondary) || links != null)
            {
                TextBlock sec = NewText(secondary, 12, null, _brush["Text2"]);
                // Small text links at the end of the second line (What's New, Hide): quiet, no extra buttons.
                if (links != null)
                {
                    foreach (RowLink l in links)
                    {
                        if (sec.Inlines.Count > 0) sec.Inlines.Add(new Run("   "));
                        Hyperlink h = new Hyperlink(new Run(l.Text.Replace(' ', ' ')));   // a link wraps as one piece
                        h.Foreground = _brush["Accent"];
                        h.TextDecorations = null;
                        h.Cursor = Cursors.Hand;
                        if (l.Tip != null) h.ToolTip = l.Tip;
                        h.Click += l.Click;
                        sec.Inlines.Add(h);
                    }
                }
                row.Secondary = sec;
                text.Children.Add(sec);
            }
            if (checkable)
            {
                // Per-item progress, hidden until this row is being downloaded/installed.
                row.Bar = new ProgressBar();
                row.Bar.Height = 4;
                row.Bar.Minimum = 0;
                row.Bar.Maximum = 100;
                row.Bar.BorderThickness = new Thickness(0);
                row.Bar.Margin = new Thickness(0, 8, 0, 0);
                row.Bar.Background = _brush["Border"];
                row.Bar.Foreground = _brush["Accent"];
                row.Bar.Visibility = Visibility.Collapsed;
                text.Children.Add(row.Bar);
                row.BarText = NewText("", 12, null, _brush["Text2"]);
                row.BarText.Margin = new Thickness(0, 4, 0, 0);
                row.BarText.Visibility = Visibility.Collapsed;
                text.Children.Add(row.BarText);
            }
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (!string.IsNullOrEmpty(right))
            {
                TextBlock r = NewText(right, 12, null, _brush["Text2"]);
                r.VerticalAlignment = VerticalAlignment.Center;
                r.Margin = new Thickness(16, 0, 0, 0);
                r.TextWrapping = TextWrapping.NoWrap;
                Grid.SetColumn(r, 2);
                grid.Children.Add(r);
            }
            if (!string.IsNullOrEmpty(tag))
            {
                Border pill = NewPill(tag, tagKind);
                Grid.SetColumn(pill, 3);
                grid.Children.Add(pill);
            }
            else if (buttonText != null)
            {
                Button btn = NewButton(buttonText, onClick);
                btn.Margin = new Thickness(14, 0, 0, 0);
                Grid.SetColumn(btn, 3);
                grid.Children.Add(btn);
            }
            Border wrap = new Border();
            wrap.Padding = new Thickness(8, 10, 8, 10);
            wrap.Margin = new Thickness(-8, 0, -8, 0);
            wrap.BorderBrush = _brush["Border"];
            wrap.BorderThickness = last ? new Thickness(0) : new Thickness(0, 0, 0, 1);
            wrap.Child = grid;
            if (checkable)
            {
                // The whole row is the click target, not just the little box.
                CheckBox cb = row.CheckBox;
                wrap.Background = Brushes.Transparent;
                wrap.Cursor = Cursors.Hand;
                wrap.MouseLeftButtonUp += (s, e) =>
                {
                    if (!cb.IsEnabled || FromControl(e.OriginalSource as DependencyObject, wrap)) return;
                    cb.IsChecked = cb.IsChecked != true;
                };
                wrap.MouseEnter += (s, e) => { if (cb.IsEnabled) wrap.Background = _brush["Hover"]; };
                wrap.MouseLeave += (s, e) => wrap.Background = Brushes.Transparent;
            }
            row.Element = wrap;
            row.Grid = grid;
            return row;
        }

        static void AutomationNameFor(UIElement e, string name) { System.Windows.Automation.AutomationProperties.SetName(e, name ?? ""); }

        void AddNote(StackPanel body, string text)
        {
            TextBlock t = NewText(text, 12, null, _brush["Text2"]);
            t.Margin = new Thickness(0, 8, 0, 4);
            body.Children.Add(t);
        }

        void SetRowTag(Row row, string text, string kind, UIElement extra = null)
        {
            List<UIElement> old = new List<UIElement>();
            foreach (UIElement c in row.Grid.Children) if (Grid.GetColumn(c) == 3) old.Add(c);
            foreach (UIElement c in old) row.Grid.Children.Remove(c);
            StackPanel wrap = new StackPanel();
            wrap.Orientation = Orientation.Horizontal;
            wrap.VerticalAlignment = VerticalAlignment.Center;
            wrap.Children.Add(NewPill(text, kind));
            if (extra != null) wrap.Children.Add(extra);
            Grid.SetColumn(wrap, 3);
            row.Grid.Children.Add(wrap);
        }

        Row RowByKey(string key)
        {
            if (key == "nvidia") return _nvRow;
            Row r;
            if (_driverRows.TryGetValue(key, out r)) return r;
            if (_appRows.TryGetValue(key, out r)) return r;
            return null;
        }

        // Applies the worker's per-item state to a row's bar and caption.
        void UpdateRowProgress(string key, ItemState item)
        {
            Row row = RowByKey(key);
            if (row == null || row.Bar == null || item.State == "queued") return;
            if (item.State == "active")
            {
                if (row.Bar.Visibility != Visibility.Visible) { row.Bar.Visibility = Visibility.Visible; row.BarText.Visibility = Visibility.Visible; }
                if (item.Percent >= 0) { if (row.Bar.IsIndeterminate) row.Bar.IsIndeterminate = false; row.Bar.Value = item.Percent; }
                else if (!row.Bar.IsIndeterminate) row.Bar.IsIndeterminate = true;
            }
            else
            {
                // done / failed: keep the caption, drop the bar
                if (row.Bar.Visibility != Visibility.Collapsed) { row.Bar.IsIndeterminate = false; row.Bar.Visibility = Visibility.Collapsed; }
                row.BarText.Visibility = Visibility.Visible;
            }
            if (row.BarText.Text != item.Text) row.BarText.Text = item.Text;
        }

        // ---------------------------------------------------------------- selection

        List<CheckBox> GroupBoxes(string group)
        {
            List<CheckBox> list = new List<CheckBox>();
            if (group == "Apps")
            {
                foreach (string id in _appOrder) { Row r = _appRows[id]; if (r.Kind != "unknown" && r.CheckBox != null && r.CheckBox.IsEnabled) list.Add(r.CheckBox); }   // "version unknown" rows need a deliberate tick
            }
            else
            {
                if (_nvRow != null && _nvRow.CheckBox.IsEnabled) list.Add(_nvRow.CheckBox);
                foreach (string t in _driverOrder) { Row r = _driverRows[t]; if (r.CheckBox.IsEnabled) list.Add(r.CheckBox); }
            }
            return list;
        }

        IEnumerable<Row> CheckableRows()
        {
            if (_nvRow != null) yield return _nvRow;
            foreach (string t in _driverOrder) yield return _driverRows[t];
            foreach (string id in _appOrder) yield return _appRows[id];
        }

        void ToggleAll(string group)
        {
            List<CheckBox> boxes = GroupBoxes(group);
            if (boxes.Count == 0) return;
            bool allChecked = boxes.TrueForAll(b => b.IsChecked == true);
            foreach (CheckBox b in boxes) b.IsChecked = !allChecked;
            UpdatePrimaryButton();
        }

        void UpdateToggleLabel(string group)
        {
            Button btn;
            if (!_toggle.TryGetValue(group, out btn) || btn == null) return;
            List<CheckBox> boxes = GroupBoxes(group);
            btn.IsEnabled = boxes.Count > 0 && !_installing && !_busy;
            if (boxes.Count == 0) return;
            string label = boxes.TrueForAll(b => b.IsChecked == true) ? "Uncheck all" : "Check all";
            if ((string)btn.Content != label) btn.Content = label;
        }

        void UpdatePrimaryButton()
        {
            UpdateToggleLabel("Drivers");
            UpdateToggleLabel("Apps");
            if (_scanning || _installing || _busy || _updating) return;
            int n = 0, open = 0;
            foreach (Row r in CheckableRows())
            {
                if (!r.CheckBox.IsEnabled) continue;
                open++;
                if (r.CheckBox.IsChecked == true) n++;
            }
            if (n > 0) { _primary.Content = "Install " + n + " Selected"; _primary.IsEnabled = true; _primaryMode = "install"; }
            else if (open == 0) { _primary.Content = "Check Again"; _primary.IsEnabled = true; _primaryMode = "scan"; }
            else { _primary.Content = "Nothing Selected"; _primary.IsEnabled = false; _primaryMode = "install"; }
            // Only one "Check Again" on screen at a time.
            _rescan.Visibility = _primaryMode == "scan" ? Visibility.Collapsed : Visibility.Visible;
            _rescan.IsEnabled = true;
        }

        // ---------------------------------------------------------------- progress / busy

        void ShowProgress(int pct)
        {
            _progress.Visibility = Visibility.Visible;
            TaskbarItemInfo tb = Window.TaskbarItemInfo;
            if (pct < 0)
            {
                if (!_progress.IsIndeterminate) _progress.IsIndeterminate = true;
                tb.ProgressState = TaskbarItemProgressState.Indeterminate;
                return;
            }
            if (_progress.IsIndeterminate) _progress.IsIndeterminate = false;
            _progress.Value = pct;
            tb.ProgressState = TaskbarItemProgressState.Normal;
            tb.ProgressValue = pct / 100.0;
        }

        void HideProgress()
        {
            _progress.Visibility = Visibility.Hidden;
            _progress.IsIndeterminate = false;
            Window.TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
        }

        void SetHeadline(string text) { if (_headline.Text != text) _headline.Text = text; }

        void OnTick(object sender, EventArgs e)
        {
            if (_scanning && _scanProg != null)
            {
                int pct = _scanProg.Percent();
                SetHeadline("Checking for updates... " + pct + "%");
                ShowProgress(pct);
                string st = _scanProg.Status();
                if (_status.Text != st) _status.Text = st;
            }
            else if (_installing && _job != null)
            {
                int overall = _job.Overall;
                ShowProgress(overall);
                if (overall >= 0) SetHeadline("Installing... " + overall + "%");
                string st = _job.Status;
                if (_subline.Text != st) _subline.Text = st;
                foreach (KeyValuePair<string, ItemState> kv in _job.Snapshot()) UpdateRowProgress(kv.Key, kv.Value);
            }
        }

        void Done()
        {
            HideProgress();
            if (!Window.IsActive) NativeUi.Flash(new WindowInteropHelper(Window).Handle);
        }

        bool Ask(string text, MessageBoxImage icon, MessageBoxButton buttons = MessageBoxButton.OKCancel)
        {
            MessageBoxResult r = MessageBox.Show(Window, text, AppInfo.Name, buttons, icon);
            return r == MessageBoxResult.OK || r == MessageBoxResult.Yes;
        }

        void ShowError(string text)
        {
            // Plain words first, the raw message after "Details:" for whoever reads the support bundle.
            string friendly = Errors.Friendly(text);
            if (friendly == text) friendly += "\n\nIf this keeps happening, click Report a Problem at the bottom of the window.";
            MessageBox.Show(Window, friendly, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        static void Open(string target)
        {
            try { Util.OpenShell(target); } catch (Exception ex) { Log.Diag("Open " + target + " failed: " + ex.Message); }
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (!_installing || _updating) return;
            if (!Ask("Updates are still installing. Closing now can leave a driver or app half-installed.\n\nClose anyway?", MessageBoxImage.Warning, MessageBoxButton.YesNo))
                e.Cancel = true;
        }

        // ---------------------------------------------------------------- scan

        async void StartScan()
        {
            if (_scanning || _installing || _busy) return;
            _scanning = true;
            _sections.Children.Clear();
            _driverRows.Clear(); _driverOrder.Clear(); _driverByTitle.Clear(); _appRows.Clear(); _appOrder.Clear(); _finished.Clear(); _toggle.Clear();
            _nvRow = null; _nvInfo = null;
            _hiddenCard = null; _historyCard = null; _allDevicesBorder = null;
            _rebootBanner.Visibility = Visibility.Collapsed;
            _report.Visibility = Visibility.Collapsed;
            _rescan.Visibility = Visibility.Collapsed;
            _headline.Text = "Checking for updates... 0%";
            _subline.Text = "Checking Windows Update, nvidia.com and winget. Checking never changes anything.";
            _primary.Content = "Checking...";
            _primary.IsEnabled = false;
            _status.Text = "Starting...";
            ShowProgress(0);
            ScanProgress prog = new ScanProgress();
            _scanProg = prog;
            _timer.Start();
            ScanResult r;
            try { r = await Task.Run(() => Scanner.Run(prog)); }
            catch (Exception ex) { r = new ScanResult(); r.Error = Errors.Text(ex); Log.Error("Scan crashed: " + ex); }
            _timer.Stop();
            _scanning = false;
            Done();
            try { ShowScan(r); }
            catch (Exception ex)
            {
                Log.Error("Showing the scan failed: " + ex);
                ShowError(Errors.Text(ex));
                _primaryMode = "scan"; _primary.Content = "Check Again"; _primary.IsEnabled = true;
            }
        }

        void ShowScan(ScanResult r)
        {
            _scan = r;
            if (r.Error != null)
            {
                _headline.Text = "The scan hit a problem.";
                _subline.Text = Errors.Friendly(r.Error);
                _status.Text = "Try Check Again. If it keeps happening, Report a Problem (bottom right) packs the logs for you.";
                _primaryMode = "scan";
                _primary.Content = "Check Again";
                _primary.IsEnabled = true;
                return;
            }
            NvidiaDriver nv = r.Nvidia;
            bool nvAvail = nv != null && nv.Available;
            // Apps that update themselves (Battle.net, Steam, browsers...) get an Open button, are never run and not counted;
            // "version unknown" ones are listed last and left out of Check all.
            List<WingetUpgrade> appsSelf = r.Apps.FindAll(a => a.Kind == "self");
            List<WingetUpgrade> appsUnknown = r.Apps.FindAll(a => a.Kind == "unknown");
            List<WingetUpgrade> appsNormal = r.Apps.FindAll(a => a.Kind != "self" && a.Kind != "unknown");
            int total = r.Drivers.Count + appsNormal.Count + appsUnknown.Count + (nvAvail ? 1 : 0);

            if (total == 0 && r.Problems.Count == 0) _headline.Text = "You're up to date.";
            else if (total == 0) _headline.Text = "No updates, but something needs attention.";
            else if (total == 1) _headline.Text = "1 update available";
            else _headline.Text = total + " updates available";
            _subline.Text = string.Format("Checked {0} {1} {2} devices {1} {3} {1} took {4}", r.Finished.ToString("h:mm tt"), Util.Dot, r.DeviceCount,
                r.System != null ? r.System.Computer : Environment.MachineName, Util.FormatDuration(r.Seconds));
            if (r.ReportHtml != null) _report.Visibility = Visibility.Visible;
            _status.Text = total > 0 ? "Select what you want, then click Install. Drivers get a restore point first." : "Nothing to install right now.";

            ShowPreflight(r.Preflight);
            ShowProblems(r, nv);
            ShowDrivers(r, nv, nvAvail);
            ShowApps(r, appsNormal, appsUnknown, appsSelf);
            ShowVendorApps(r, nv, nvAvail);
            ShowBackups(r.Backups);
            ShowHidden(r);
            ShowHistory(r.History);
            ShowAllDevices(r.Inventory);
            _primaryMode = "install";
            UpdatePrimaryButton();
            _scroller.ScrollToTop();
        }

        // Before you start: the things most likely to make an install fail or be unsafe, each with a one-click fix.
        void ShowPreflight(List<PreflightCheck> pre)
        {
            if (pre.Count == 0) return;
            string kind = pre.Exists(c => c.Severity == "bad") ? "bad" : "warn";
            Card card = NewCard("Before you start", "Nothing here stops you. These are the things most likely to make an install fail or be unsafe on this PC.", pre.Count.ToString(), kind);
            for (int i = 0; i < pre.Count; i++)
            {
                PreflightCheck c = pre[i];
                bool last = i == pre.Count - 1;
                Row row;
                if (c.FixText != null)
                {
                    row = NewRow(c.Title, c.Detail, buttonText: c.FixText, last: last, onClick: (s, e) =>
                    {
                        Button b = (Button)s;
                        try
                        {
                            _status.Text = Preflight.Fix(c);
                            if (c.FixKind == "service") { b.IsEnabled = false; b.Content = "Done"; }
                        }
                        catch (Exception ex) { _status.Text = "Couldn't do that: " + Errors.Friendly(Errors.Text(ex), true); }
                    });
                }
                else row = NewRow(c.Title, c.Detail, tag: c.Severity == "info" ? "Note" : "Heads up", tagKind: c.Severity, last: last);
                card.Body.Children.Add(row.Element);
            }
        }

        void ShowProblems(ScanResult r, NvidiaDriver nv)
        {
            if (r.Problems.Count == 0) return;
            Card card = NewCard("Needs attention", "Windows reports a problem with these devices.", r.Problems.Count.ToString(), "warn");
            int lookups = 0;
            for (int i = 0; i < r.Problems.Count; i++)
            {
                Device p = r.Problems[i];
                List<string> parts = new List<string>();
                if (!string.IsNullOrEmpty(p.Class)) parts.Add(p.Class);
                if (!string.IsNullOrEmpty(p.Problem)) parts.Add(p.Problem);
                string hint = Inventory.ProblemHint(p, r.Drivers, nv);
                if (hint != null) parts.Add(hint);
                string sec = string.Join(" " + Util.Dot + " ", parts.ToArray());
                bool last = i == r.Problems.Count - 1;
                Row row;
                if (hint != null && Rx.IsMatch(hint, "update.*below"))
                    row = NewRow(p.Name, sec, tag: "Fix is below", tagKind: "ok", last: last);   // an update on this screen covers it
                else if (hint != null)
                    row = NewRow(p.Name, sec, tag: "See note", tagKind: "warn", last: last);
                else if (!string.IsNullOrEmpty(p.HardwareId))
                {
                    string url = Inventory.CatalogUrl(p.HardwareId);
                    row = NewRow(p.Name, sec, right: "Error", buttonText: "Look Up Driver", last: last, onClick: (s, e) =>
                    {
                        Open(url);
                        _status.Text = "Opened the Microsoft Update Catalog for that device. If it lists a driver: download the .cab, extract it, and install with  pnputil /add-driver <file>.inf /install.  If it finds nothing, the device maker's site is the next stop.";
                    });
                    lookups++;
                }
                else row = NewRow(p.Name, sec, tag: "Error", tagKind: "bad", last: last);
                card.Body.Children.Add(row.Element);
            }
            if (lookups > 0) AddNote(card.Body, "Look Up Driver searches the Microsoft Update Catalog (official, signed) by this device's maker and model id. If that finds nothing either, the driver comes from the device maker or the PC maker's support page.");
        }

        void ShowDrivers(ScanResult r, NvidiaDriver nv, bool nvAvail)
        {
            string sub = "From Windows Update, matched to your hardware. Optional drivers start unticked.";
            if (r.WuError != null) sub = "Windows Update check failed: " + Errors.Friendly(r.WuError, true);
            Card card = NewCard("Drivers", sub, (r.Drivers.Count + (nvAvail ? 1 : 0)).ToString(), "info", "Uncheck all", (s, e) => ToggleAll("Drivers"));
            _toggle["Drivers"] = card.Action;
            if (nvAvail)
            {
                // GeForce driver straight from nvidia.com: no NVIDIA App, no account.
                string sec = "Installed " + nv.Installed + "  " + Util.Dot + "  from nvidia.com, signature verified before install  " + Util.Dot + "  driver only, no NVIDIA App or account";
                if (nv.Lookup == "generic") sec += "  " + Util.Dot + "  your GPU name was not in NVIDIA's list, so this is the current unified GeForce driver";
                string nvWhat = "NVIDIA " + nv.Name + " " + nv.Version;
                List<RowLink> nvLinks = new List<RowLink>();
                if (nv.DetailsUrl != null) nvLinks.Add(new RowLink("What's new", "NVIDIA's release notes for this driver", (s, e) => Open(nv.DetailsUrl)));
                nvLinks.Add(new RowLink("Skip this version", "Don't offer this NVIDIA driver again; a newer one will be offered",
                    (s, e) => HideUpdate(_nvRow, nvWhat, "nvidia.com", () => Skips.Set("nvidia", nv.Version), () => Skips.Set("nvidia", null))));
                _nvRow = NewRow(nvWhat, sec, right: nv.SizeBytes > 0 ? Util.FormatBytes(nv.SizeBytes) : null,
                                tag: "nvidia.com", tagKind: "ok", checkable: true, isChecked: true, last: r.Drivers.Count == 0, links: nvLinks.ToArray());
                _nvInfo = nv;
                _nvRow.CardCount = card.CountText;
                card.Body.Children.Add(_nvRow.Element);
            }
            if (r.Drivers.Count == 0 && r.WuError == null && !nvAvail) AddNote(card.Body, "No driver updates for this hardware right now.");
            for (int i = 0; i < r.Drivers.Count; i++)
            {
                WuDriver d = r.Drivers[i];
                if (_driverRows.ContainsKey(d.Title)) continue;
                List<string> parts = new List<string>();
                if (!string.IsNullOrEmpty(d.Manufacturer)) parts.Add(d.Manufacturer);
                if (!string.IsNullOrEmpty(d.Class)) parts.Add(d.Class);
                if (!string.IsNullOrEmpty(d.VersionDate)) parts.Add("version date " + d.VersionDate);
                Row row = null;
                row = NewRow(d.Title, string.Join(" " + Util.Dot + " ", parts.ToArray()), right: d.SizeMB > 0 ? d.SizeMB.ToString("0.0", Util.Inv) + " MB" : null,
                             tag: d.Optional ? "Optional" : null, checkable: true, isChecked: !d.Optional, last: i == r.Drivers.Count - 1,
                             links: new[] { new RowLink("Hide", "Hide this driver in Windows Update, so neither this app nor Windows installs it (Unhide brings it back)",
                                 (s, e) => HideUpdate(row, d.Title, "Windows Update", () => WindowsUpdate.SetHidden(d, true), () => WindowsUpdate.SetHidden(d, false))) });
                row.HardwareId = d.HardwareId;
                row.CardCount = card.CountText;
                _driverRows[d.Title] = row;
                _driverOrder.Add(d.Title);
                _driverByTitle[d.Title] = d;
                card.Body.Children.Add(row.Element);
            }
            foreach (string t in r.DeferredGpu) AddNote(card.Body, "Skipped Windows Update's '" + t + "': the newer driver comes from NVIDIA directly (above) or the vendor app.");
            if (nv != null && !nvAvail)
            {
                if (nv.Error != null) AddNote(card.Body, "Could not check nvidia.com for a newer GeForce driver (" + Errors.Friendly(nv.Error, true) + "). Windows Update or the NVIDIA App still work.");
                else if (!string.IsNullOrEmpty(nv.Version)) AddNote(card.Body, "NVIDIA driver " + nv.Installed + " is current (nvidia.com's newest is " + nv.Version + ").");
            }
        }

        void ShowApps(ScanResult r, List<WingetUpgrade> normal, List<WingetUpgrade> unknown, List<WingetUpgrade> self)
        {
            string sub = "Newer versions found by winget. Tick the ones you want, or Check all.";
            if (self.Count > 0) sub += " Apps that update themselves are listed last.";
            if (r.WingetError != null) sub = "winget check failed: " + Errors.Friendly(r.WingetError, true);
            Card card = NewCard("Apps", sub, (normal.Count + unknown.Count).ToString(), "info", "Check all", (s, e) => ToggleAll("Apps"));
            _toggle["Apps"] = card.Action;
            if (r.Apps.Count == 0 && r.WingetError == null) AddNote(card.Body, "Everything winget knows about is current.");
            List<WingetUpgrade> ordered = new List<WingetUpgrade>(normal);
            ordered.AddRange(unknown);
            for (int i = 0; i < ordered.Count; i++)
            {
                WingetUpgrade a = ordered[i];
                if (_appRows.ContainsKey(a.Id)) continue;
                bool last = i == ordered.Count - 1 && self.Count == 0;
                Row row = null;
                // Apps start unticked: most of the time people just want the drivers. "Check all" ticks them in one go.
                if (a.Kind == "unknown")
                    row = NewRow(a.Name, a.Id + "  " + Util.Dot + "  winget can't read the installed version. Tick it to reinstall the newest one; it runs last.",
                                 right: "newest is " + a.Available, tag: "Version unknown", checkable: true, last: last, links: HideAppLink(a, () => row));
                else
                    row = NewRow(a.Name, a.Id, right: a.Installed + "  " + Util.Arrow + "  " + a.Available, checkable: true, last: last, links: HideAppLink(a, () => row));
                row.Name = a.Name;
                row.Kind = a.Kind;
                row.Version = a.Available;
                row.CardCount = card.CountText;
                _appRows[a.Id] = row;
                _appOrder.Add(a.Id);
                card.Body.Children.Add(row.Element);
            }
            for (int i = 0; i < self.Count; i++)
            {
                WingetUpgrade a = self[i];
                string pattern = !string.IsNullOrEmpty(a.AppMatch) ? a.AppMatch : System.Text.RegularExpressions.Regex.Escape(a.Name);
                Row row = null;
                row = NewRow(a.Name, a.Id + "  " + Util.Dot + "  " + a.Note + ". Open it and it updates on its own.", right: "newest is " + a.Available,
                             buttonText: "Open", last: i == self.Count - 1, onClick: (s, e) => LaunchApp(pattern, null), links: HideAppLink(a, () => row));
                card.Body.Children.Add(row.Element);
            }
        }

        // ---------------------------------------------------------------- hide / unhide, history

        RowLink[] HideAppLink(WingetUpgrade a, Func<Row> row)
        {
            return new[] { new RowLink("Hide", "Pin this app in winget so its updates are left out (Unhide brings it back)",
                (s, e) => HideUpdate(row(), a.Name, "winget", () => Winget.Pin(a.Id), () => Winget.Unpin(a.Id))) };
        }

        // Hide: the source itself keeps the choice (hidden in Windows Update, pinned in winget, NVIDIA version skipped);
        // the row moves to Hidden updates, where Unhide undoes it.
        async void HideUpdate(Row row, string what, string source, Action hide, Action unhide)
        {
            if (row == null) return;
            if (_installing || _busy || _updating) { _status.Text = "Wait for the current install to finish, then hide it."; return; }
            _status.Text = "Hiding " + what + "...";
            string err = null;
            try { await Task.Run(hide); } catch (Exception ex) { err = Errors.Text(ex); }
            if (err != null) { _status.Text = "Couldn't hide that: " + Errors.Friendly(err, true); return; }
            if (row.CheckBox != null) { row.CheckBox.IsChecked = false; row.CheckBox.IsEnabled = false; _finished.Add(row); }
            row.Element.Visibility = Visibility.Collapsed;
            int count;
            if (row.CardCount != null && int.TryParse(row.CardCount.Text, out count) && count > 0) row.CardCount.Text = (count - 1).ToString();
            AddHiddenRow(what, source, unhide);
            History.Add("Hid " + what, "", source, true, "Hidden by you");
            _status.Text = what + " is hidden. Unhide it any time under Hidden updates.";
            RefreshHeadline();
            UpdatePrimaryButton();
        }

        Card EnsureHiddenCard()
        {
            if (_hiddenCard != null) return _hiddenCard;
            _hiddenCard = NewCard("Hidden updates", "Updates you chose not to install. They stay hidden until you Unhide them.", null, "muted");
            // NewCard adds it at the end; keep it above History and All devices.
            Border anchor = _historyCard != null ? _historyCard.Border : _allDevicesBorder;
            if (anchor != null)
            {
                _sections.Children.Remove(_hiddenCard.Border);
                _sections.Children.Insert(_sections.Children.IndexOf(anchor), _hiddenCard.Border);
            }
            return _hiddenCard;
        }

        void AddHiddenRow(string what, string source, Action unhide)
        {
            Card card = EnsureHiddenCard();
            string why = source == "Windows Update" ? "Hidden in Windows Update, so Windows won't install it on its own either"
                       : source == "winget" ? "Pinned in winget, so its updates are left out"
                       : "This version is skipped; a newer one will be offered";
            Row row = null;
            row = NewRow(what, why, buttonText: "Unhide", onClick: (s, e) => Unhide((Button)s, row, what, source, unhide));
            card.Body.Children.Add(row.Element);
        }

        async void Unhide(Button b, Row row, string what, string source, Action unhide)
        {
            b.IsEnabled = false;
            string err = null;
            try { await Task.Run(unhide); } catch (Exception ex) { err = Errors.Text(ex); }
            if (err != null) { b.IsEnabled = true; _status.Text = "Couldn't unhide that: " + Errors.Friendly(err, true); return; }
            b.Content = "Unhidden";
            if (row != null && row.Secondary != null) row.Secondary.Text = "Offered again from the next Check Again.";
            History.Add("Unhid " + what, "", source, true, "Offered again");
            _status.Text = what + " will be offered again. Check Again to see it in the list.";
        }

        void ShowHidden(ScanResult r)
        {
            foreach (WuDriver d in r.HiddenDrivers) AddHiddenRow(d.Title, "Windows Update", () => WindowsUpdate.SetHidden(d, false));
            foreach (WingetUpgrade p in r.PinnedApps) AddHiddenRow(p.Name, "winget", () => Winget.Unpin(p.Id));
            if (r.SkippedNvidia != null) AddHiddenRow("NVIDIA driver " + r.SkippedNvidia, "nvidia.com", () => Skips.Set("nvidia", null));
        }

        // After a Hide: the headline counts what is still on offer.
        void RefreshHeadline()
        {
            int n = 0;
            foreach (Row r in CheckableRows()) if (!_finished.Contains(r)) n++;
            int problems = _scan != null ? _scan.Problems.Count : 0;
            if (n == 0 && problems == 0) _headline.Text = "You're up to date.";
            else if (n == 0) _headline.Text = "No updates, but something needs attention.";
            else _headline.Text = n == 1 ? "1 update available" : n + " updates available";
        }

        // What this app installed, rolled back or hid, newest first; collapsed like All devices.
        void ShowHistory(List<HistoryEntry> history)
        {
            if (history.Count == 0) return;
            _historyCard = NewCard("History", "What this app installed, rolled back or hid on this PC, newest first.", history.Count.ToString(), "muted");
            StackPanel list = new StackPanel();
            list.Visibility = Visibility.Collapsed;
            bool built = false;
            Button toggle = NewButton("Show", null);
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            toggle.Margin = new Thickness(0, 4, 0, 4);
            toggle.Click += (s, e) =>
            {
                if (list.Visibility == Visibility.Collapsed)
                {
                    if (!built)
                    {
                        int shown = 0;
                        for (int i = history.Count - 1; i >= 0 && shown < 50; i--, shown++)
                        {
                            HistoryEntry h = history[i];
                            DateTime when;
                            string whenText = DateTime.TryParse(h.When, Util.Inv, System.Globalization.DateTimeStyles.None, out when) ? when.ToString("MMM d, h:mm tt") : h.When;
                            string title = h.What + (string.IsNullOrEmpty(h.Version) ? "" : " " + h.Version);
                            list.Children.Add(NewRow(title, whenText + " " + Util.Dot + " " + h.Source + " " + Util.Dot + " " + h.Result,
                                tag: h.Ok ? null : "Failed", tagKind: "bad", last: i == 0 || shown == 49).Element);
                        }
                        built = true;
                    }
                    list.Visibility = Visibility.Visible;
                    toggle.Content = "Hide";
                }
                else { list.Visibility = Visibility.Collapsed; toggle.Content = "Show"; }
            };
            _historyCard.Body.Children.Add(toggle);
            _historyCard.Body.Children.Add(list);
        }

        void LaunchApp(string pattern, string url)
        {
            string msg = InstalledApps.Start(pattern, url);
            if (msg == "Not found") msg = "Could not find that app in the Start menu; open it the way you usually do and it will update itself.";
            _status.Text = msg;
        }

        // Graphics + peripherals: the vendor apps own these updates.
        void ShowVendorApps(ScanResult r, NvidiaDriver nv, bool nvAvail)
        {
            List<Peripheral> known = r.Peripherals.FindAll(p => p.Known && p.Present);
            known.Sort((a, b) => { int c = string.Compare(a.Vendor, b.Vendor, StringComparison.OrdinalIgnoreCase); return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            int other = r.Peripherals.FindAll(p => !p.Known && p.Present).Count;
            Card card = NewCard("Graphics and peripherals", "Firmware for these comes from each maker's own app. Open it from here to check.", (r.Gpus.Count + known.Count).ToString(), "muted");
            int count = r.Gpus.Count + known.Count, i = 0;
            foreach (Gpu g in r.Gpus)
            {
                bool last = ++i == count;
                string sec = "Driver " + g.FriendlyVersion;
                if (!string.IsNullOrEmpty(g.DriverDate)) sec += " " + Util.Dot + " " + g.DriverDate;
                if (!string.IsNullOrEmpty(g.VendorApp)) sec += " " + Util.Dot + " " + g.VendorApp;
                Row row;
                if (g.Vendor == "NVIDIA" && nv != null && !string.IsNullOrEmpty(nv.Version))
                    row = NewRow(g.Name, "Driver " + g.FriendlyVersion + "  " + Util.Dot + "  checked with nvidia.com directly; the NVIDIA App is not needed",
                                 tag: nvAvail ? "Update " + nv.Version + " above" : "Up to date", tagKind: "ok", last: last);
                else row = VendorRow(g.Name, sec, g.AppMatch, g.AppInstalled, g.Url, g.WingetId, r, last);
                card.Body.Children.Add(row.Element);
            }
            foreach (Peripheral p in known)
            {
                bool last = ++i == count;
                string sec = p.Vendor;
                if (!string.IsNullOrEmpty(p.FirmwareRev)) sec += " " + Util.Dot + " firmware rev " + p.FirmwareRev;
                if (!string.IsNullOrEmpty(p.VendorApp)) sec += " " + Util.Dot + " " + p.VendorApp;
                card.Body.Children.Add(VendorRow(p.Name, sec, p.AppMatch, p.AppInstalled, p.Url, p.WingetId, r, last).Element);
            }
            if (count == 0) AddNote(card.Body, "No graphics adapter or known-vendor peripherals detected.");
            if (other > 0) AddNote(card.Body, other + " other USB/Bluetooth devices (hubs, radios, webcams) are listed in the report.");
        }

        Row VendorRow(string name, string sec, string appMatch, bool installed, string url, string wingetId, ScanResult r, bool last)
        {
            if (appMatch == "^$") return NewRow(name, sec, tag: "Phone / web", last: last);
            if (installed) return NewRow(name, sec, buttonText: "Open App", last: last, onClick: (s, e) => LaunchApp(appMatch, url));
            bool viaWinget = !string.IsNullOrEmpty(wingetId) && r.WingetHints.ContainsKey(wingetId) && r.WingetHints[wingetId];
            string hint = viaWinget ? "winget install --id " + wingetId + " --exact" : url;
            if (string.IsNullOrEmpty(url) && !viaWinget) return NewRow(name, sec, tag: "App missing", tagKind: "warn", last: last);
            return NewRow(name, sec + " " + Util.Dot + " get it: " + hint, buttonText: "Get App", last: last, onClick: (s, e) => GetVendorApp((Button)s, wingetId, viaWinget, url));
        }

        // Missing vendor app: install it with winget when winget has it, otherwise open the vendor's download page.
        async void GetVendorApp(Button b, string wingetId, bool viaWinget, string url)
        {
            if (!viaWinget) { Open(url); _status.Text = "Opened the vendor's download page."; return; }
            if (!Ask("Install " + wingetId + " with winget now?", MessageBoxImage.Question)) return;
            b.IsEnabled = false;
            b.Content = "Installing...";
            _status.Text = "Installing " + wingetId + " with winget...";
            ProcResult pr = null;
            string err = null;
            try { pr = await Task.Run(() => Winget.Invoke(new[] { "install", "--id", wingetId, "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" })); }
            catch (Exception ex) { err = Errors.Text(ex); }
            if (err == null && pr.ExitCode == 0) { b.Content = "Installed"; _status.Text = wingetId + " is installed. Open it to check for firmware updates."; }
            else
            {
                b.IsEnabled = true; b.Content = "Get App";
                _status.Text = err != null ? Errors.Friendly(err, true) : Errors.WingetExitText(pr.ExitCode, pr.Output);
            }
        }

        // Earlier backups: roll back any driver this tool replaced before.
        void ShowBackups(List<DriverBackup> backups)
        {
            if (backups.Count == 0) return;
            Card card = NewCard("Driver backups", "Drivers this tool backed up before replacing them. Roll Back reinstalls that version.", backups.Count.ToString(), "muted");
            for (int i = 0; i < backups.Count; i++)
            {
                DriverBackup b = backups[i];
                string when = b.Exported;
                DateTime dt;
                if (DateTime.TryParse(b.Exported, Util.Inv, System.Globalization.DateTimeStyles.None, out dt)) when = dt.ToString("yyyy-MM-dd HH:mm", Util.Inv);
                Row row = NewRow(b.Device, b.Inf + " " + Util.Dot + " backed up " + when, buttonText: "Roll Back", last: i == backups.Count - 1,
                                 onClick: (s, e) => StartRestore(b.Path, b.InstanceId, b.Device));
                card.Body.Children.Add(row.Element);
            }
        }

        // Every present device, collapsed by default (the hardware-information view); built on first Show.
        void ShowAllDevices(List<Device> inventory)
        {
            List<Device> present = inventory.FindAll(d => d.Present && !string.IsNullOrEmpty(d.Name));
            present.Sort((a, b) => { int c = string.Compare(a.Class, b.Class, StringComparison.OrdinalIgnoreCase); return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            Card card = NewCard("All devices", "Every present device and the driver it is using now.", present.Count.ToString(), "muted");
            _allDevicesBorder = card.Border;
            StackPanel list = new StackPanel();
            list.Visibility = Visibility.Collapsed;
            bool built = false;
            Button toggle = NewButton("Show", null);
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            toggle.Margin = new Thickness(0, 4, 0, 4);
            toggle.Click += (s, e) =>
            {
                if (list.Visibility == Visibility.Collapsed)
                {
                    if (!built)
                    {
                        string cls = null;
                        for (int k = 0; k < present.Count; k++)
                        {
                            Device dv = present[k];
                            if (!string.Equals(dv.Class, cls, StringComparison.OrdinalIgnoreCase))
                            {
                                cls = dv.Class;
                                TextBlock h = NewText(string.IsNullOrEmpty(cls) ? "Other" : cls, 12, FontWeights.SemiBold, _brush["Text2"]);
                                h.Margin = new Thickness(0, 10, 0, 2);
                                list.Children.Add(h);
                            }
                            List<string> sec = new List<string>();
                            if (!string.IsNullOrEmpty(dv.Provider)) sec.Add(dv.Provider);
                            if (!string.IsNullOrEmpty(dv.DriverVersion)) sec.Add("v" + dv.DriverVersion);
                            if (!string.IsNullOrEmpty(dv.DriverDate)) sec.Add(dv.DriverDate);
                            if (!string.IsNullOrEmpty(dv.Inf)) sec.Add(dv.Inf);
                            bool lastInClass = k == present.Count - 1 || !string.Equals(present[k + 1].Class, cls, StringComparison.OrdinalIgnoreCase);
                            list.Children.Add(NewRow(dv.Name, string.Join(" " + Util.Dot + " ", sec.ToArray()), right: dv.Vid != null ? dv.Vid + ":" + dv.Pid : null, last: lastInClass).Element);
                        }
                        built = true;
                    }
                    list.Visibility = Visibility.Visible;
                    toggle.Content = "Hide";
                }
                else { list.Visibility = Visibility.Collapsed; toggle.Content = "Show"; }
            };
            card.Body.Children.Add(toggle);
            card.Body.Children.Add(list);
        }

        // ---------------------------------------------------------------- install

        void SetRowsEnabled(bool enabled)
        {
            foreach (Row r in CheckableRows()) if (!_finished.Contains(r)) r.CheckBox.IsEnabled = enabled;
        }

        async void StartInstall(bool skipRestorePoint, InstallRequest preset)
        {
            if (_scanning || _installing || _busy || _updating) return;
            InstallRequest req = preset;
            if (req == null)
            {
                req = new InstallRequest();
                foreach (string t in _driverOrder) { Row row = _driverRows[t]; if (row.CheckBox.IsEnabled && row.CheckBox.IsChecked == true) req.Drivers.Add(_driverByTitle[t]); }
                if (_nvRow != null && _nvRow.CheckBox.IsEnabled && _nvRow.CheckBox.IsChecked == true) req.Nvidia = _nvInfo;
                List<string> normal = new List<string>(), unknown = new List<string>();
                foreach (string id in _appOrder)
                {
                    Row row = _appRows[id];
                    if (!row.CheckBox.IsEnabled || row.CheckBox.IsChecked != true) continue;
                    (row.Kind == "unknown" ? unknown : normal).Add(id);   // "version unknown" ones run last
                    req.WingetNames[id] = row.Name;
                    if (row.Version != null) req.WingetVersions[id] = row.Version;
                }
                req.WingetIds.AddRange(normal);
                req.WingetIds.AddRange(unknown);
                if (req.Drivers.Count == 0 && req.Nvidia == null && req.WingetIds.Count == 0) return;

                List<string> parts = new List<string>();
                if (req.Drivers.Count > 0) parts.Add(req.Drivers.Count == 1 ? "1 driver update from Windows Update" : req.Drivers.Count + " driver updates from Windows Update");
                if (req.Nvidia != null) parts.Add("the NVIDIA driver " + req.Nvidia.Version + " from nvidia.com");
                if (req.WingetIds.Count > 0) parts.Add(req.WingetIds.Count == 1 ? "1 app update" : req.WingetIds.Count + " app updates");
                string msg = "Install " + string.Join(", ", parts.ToArray()) + "?\n\n";
                msg += req.Drivers.Count > 0 || req.Nvidia != null ? "A system restore point is created and each replaced driver is backed up first. " : "";
                msg += "Nothing restarts automatically.";
                // Re-check the instant things (battery, disk, network, pending restart) right now; the scan may be minutes old.
                List<PreflightCheck> heads = new List<PreflightCheck>();
                try { heads = Preflight.Run(true).FindAll(c => c.Severity != "info"); } catch { }
                MessageBoxImage icon = MessageBoxImage.Question;
                if (heads.Count > 0)
                {
                    msg += "\n\nHeads up:";
                    foreach (PreflightCheck c in heads) msg += "\n - " + c.Title + ": " + c.Detail;
                    icon = MessageBoxImage.Warning;
                }
                if (!Ask(msg, icon)) return;
            }
            req.SkipRestorePoint = skipRestorePoint;

            _installing = true;
            InstallJob job = new InstallJob();
            _job = job;
            SetRowsEnabled(false);
            foreach (Button b in _toggle.Values) if (b != null) b.IsEnabled = false;
            _primary.Content = "Installing...";
            _primary.IsEnabled = false;
            _rescan.IsEnabled = false;
            _headline.Text = "Installing...";
            _subline.Text = "Preparing...";
            _status.Text = "You can keep using the PC. The screen may flicker while a display driver installs.";
            ShowProgress(-1);
            _timer.Start();
            InstallResult r;
            try { r = await Task.Run(() => job.Run(req)); }
            catch (Exception ex) { r = new InstallResult(); r.Error = Errors.Text(ex); Log.Error("Install crashed: " + ex); }
            _timer.Stop();
            _installing = false;
            Done();
            try { ShowInstall(r, req); }
            catch (Exception ex) { Log.Error("Showing the install result failed: " + ex); ShowError(Errors.Text(ex)); }
        }

        void ShowInstall(InstallResult r, InstallRequest req)
        {
            foreach (KeyValuePair<string, ItemState> kv in _job.Snapshot()) UpdateRowProgress(kv.Key, kv.Value);
            if (r.Error != null) ShowError("Installing stopped early: " + r.Error);

            foreach (DriverOutcome d in r.Drivers)
            {
                Row row;
                if (!_driverRows.TryGetValue(d.Title, out row)) continue;
                string kind = d.Code == 2 ? "ok" : (d.Code == 0 || d.Code == 3) ? "warn" : "bad";
                Button rollBack = null;
                // If a backup of the replaced driver exists, offer Roll Back right on the row.
                if (d.Code == 2 && !string.IsNullOrEmpty(row.HardwareId))
                {
                    DriverBackup b = r.Backups.Find(x => x.Success && x.Path != null && !string.IsNullOrEmpty(x.HardwareId) &&
                        (string.Equals(x.HardwareId, row.HardwareId, StringComparison.OrdinalIgnoreCase) || x.HardwareId.StartsWith(row.HardwareId, StringComparison.OrdinalIgnoreCase)));
                    if (b != null)
                    {
                        rollBack = NewButton("Roll Back", (s, e) => StartRestore(b.Path, b.InstanceId, b.Device));
                        rollBack.Margin = new Thickness(10, 0, 0, 0);
                    }
                }
                SetRowTag(row, d.Text, kind, rollBack);
                Finish(row);
            }
            if (r.Nvidia != null && _nvRow != null)
            {
                if (r.Nvidia.Success) SetRowTag(_nvRow, "Installed " + r.Nvidia.Version, "ok");
                else
                {
                    SetRowTag(_nvRow, "Failed", "bad");
                    if (!string.IsNullOrEmpty(r.Nvidia.Message)) { _nvRow.BarText.Text = Errors.Friendly(r.Nvidia.Message, true); _nvRow.BarText.Visibility = Visibility.Visible; }
                }
                Finish(_nvRow);
            }
            foreach (AppOutcome a in r.Apps)
            {
                Row row;
                if (!_appRows.TryGetValue(a.Id, out row)) continue;
                if (a.Success) SetRowTag(row, "Updated", "ok");
                else
                {
                    SetRowTag(row, "Failed", "bad");
                    // The caption under the row carries the plain-language reason.
                    if (string.IsNullOrEmpty(row.BarText.Text)) row.BarText.Text = Errors.WingetExitText(a.Code);
                    row.BarText.Visibility = Visibility.Visible;
                }
                Finish(row);
            }
            int failedBackups = r.Backups.FindAll(b => !b.Success).Count;

            if (r.RestoreFailed)
            {
                List<WuDriver> pending = req.Drivers.FindAll(d => !_finished.Contains(_driverRows[d.Title]));
                bool nvPending = req.Nvidia != null && r.Nvidia == null;
                string what = req.WingetIds.Count > 0 ? "App updates were applied. " : "";
                if ((pending.Count > 0 || nvPending) && Ask("Windows couldn't create a restore point:\n" + Errors.Friendly(r.RestorePoint, true) + "\n\n" + what +
                        "Install the selected drivers anyway? Each replaced driver is still backed up individually and can be rolled back.", MessageBoxImage.Warning, MessageBoxButton.YesNo))
                {
                    InstallRequest again = new InstallRequest();
                    again.Drivers = pending;
                    again.Nvidia = nvPending ? req.Nvidia : null;
                    StartInstall(true, again);
                    return;
                }
            }

            List<string> done = new List<string>();
            double totalBytes = 0, totalSecs = 0;
            foreach (InstallStat st in r.Stats) { totalBytes += st.Bytes; totalSecs += st.DownloadSeconds; }
            done.Add("Done.");
            if (totalBytes > 0 && totalSecs > 0) done.Add(string.Format(Util.Inv, "Downloaded {0} in {1:n0} s ({2}).", Util.FormatBytes(totalBytes), totalSecs, Util.FormatSpeed(totalBytes / totalSecs)));
            if (r.RestorePoint != null && !r.RestoreFailed) done.Add(r.RestorePoint);
            if (failedBackups > 0) done.Add(failedBackups + " driver backup(s) failed; see the log (Report a Problem).");
            _status.Text = string.Join(" ", done.ToArray());
            if (r.Reboot) { _rebootBanner.Visibility = Visibility.Visible; _headline.Text = "Restart to finish."; }
            else _headline.Text = "Done.";
            _subline.Text = "Check Again to rescan, or open the vendor apps below for firmware and GPU drivers.";
            SetRowsEnabled(true);
            UpdatePrimaryButton();
        }

        void Finish(Row row)
        {
            row.CheckBox.IsChecked = false;
            row.CheckBox.IsEnabled = false;
            row.Element.Cursor = null;
            _finished.Add(row);
        }

        async void StartRestore(string backupPath, string instanceId, string device)
        {
            if (_scanning || _installing || _busy) return;
            if (!Ask("Put the previous driver back for:\n" + device + "\n\nfrom " + backupPath + " ?", MessageBoxImage.Question)) return;
            _busy = true;
            _primary.IsEnabled = false;
            _rescan.IsEnabled = false;
            ShowProgress(-1);
            _status.Text = "Putting the previous driver back...";
            DriverRestoreResult res = null;
            string err = null;
            try { res = await Task.Run(() => Drivers.Restore(backupPath, instanceId)); }
            catch (Exception ex) { err = Errors.Text(ex); }
            _busy = false;
            Done();
            History.Add("Rolled back " + device, "", "driver backup", err == null && res.Success, err == null && res.Success ? "Previous driver restored" : "Rollback failed");
            if (err != null) ShowError("Rollback failed: " + err);
            else if (!res.Success) ShowError("Rollback failed:\n" + res.Output);
            else
            {
                _status.Text = "Previous driver restored for " + device + ". If Windows Update offers the newer driver again, click Hide on its row and Windows leaves it alone.";
                if (res.RebootRequired) _rebootBanner.Visibility = Visibility.Visible;
            }
            UpdatePrimaryButton();
        }

        // ---------------------------------------------------------------- self-update

        // Quietly asks GitHub Releases for a newer version; the banner appears only when there is one.
        async void CheckForUpdate()
        {
            if (!Updater.Configured) return;
            UpdateInfo u = null;
            try { u = await Task.Run(() => Updater.Check()); } catch { }
            if (u == null || !u.Available) return;
            _update = u;
            _updateText.Text = AppInfo.Name + " " + u.Version + " is available (you have " + AppInfo.Version + "). Updating takes a few seconds; the app closes and opens again.";
            _updateBanner.Visibility = Visibility.Visible;
        }

        // Download -> verify -> run Setup /S /UPDATE -> close. Setup opens the new version when it is done.
        async void StartUpdate()
        {
            UpdateInfo u = _update;
            if (u == null || _updating) return;
            if (_installing || _busy)
            {
                _status.Text = "Finish the current install first; then click Update.";
                return;
            }
            if (!Ask("Update " + AppInfo.Name + " to " + u.Version + " now?\n\nIt downloads the new version from GitHub, checks it against GitHub's checksum, closes this window, installs, and opens again. Nothing else on the PC changes.", MessageBoxImage.Question))
                return;
            _updating = true;
            _updateButton.IsEnabled = false;
            _updateNotes.IsEnabled = false;
            _primary.IsEnabled = false;
            _rescan.IsEnabled = false;
            ShowProgress(-1);
            _updateText.Text = "Downloading " + AppInfo.Name + " " + u.Version + "...";
            string path = null, err = null;
            try
            {
                path = await Task.Run(() => Updater.Download(u, (pct, text) =>
                    Window.Dispatcher.BeginInvoke(new Action(() => { _updateText.Text = text; if (pct >= 0) ShowProgress(pct); }))));
            }
            catch (Exception ex) { err = Errors.Text(ex); Log.Diag("Update download failed: " + err); }
            if (err != null)
            {
                _updating = false;
                HideProgress();
                _updateButton.IsEnabled = true;
                _updateNotes.IsEnabled = true;
                _updateText.Text = "The update did not download: " + Errors.Friendly(err, true);
                UpdatePrimaryButton();
                return;
            }
            try
            {
                Updater.LaunchSetup(path);
            }
            catch (Exception ex)
            {
                _updating = false;
                HideProgress();
                _updateButton.IsEnabled = true;
                _updateText.Text = "Setup could not start: " + Errors.Friendly(Errors.Text(ex), true);
                UpdatePrimaryButton();
                return;
            }
            _updateText.Text = "Installing " + u.Version + "... the app opens again in a moment.";
            Application.Current.Shutdown();
        }

        // ---------------------------------------------------------------- footer

        async void LoadWeeklyState()
        {
            bool on = false;
            string exe = AppInfo.ExePath;
            try { on = await Task.Run(() => Schedule.CheckAndRepair(exe)); } catch { }
            _suppressWeekly = true;
            _weekly.IsChecked = on;
            _suppressWeekly = false;
        }

        void SetWeekly(bool on)
        {
            if (_suppressWeekly) return;
            try
            {
                if (on) { Schedule.Register(AppInfo.ExePath); _status.Text = "Weekly check on: Mondays at 10:00, silently, with a notification when updates are found."; }
                else { Schedule.Unregister(); _status.Text = "Weekly check off."; }
            }
            catch (Exception ex)
            {
                _status.Text = "Couldn't change the weekly check: " + Errors.Text(ex);
                _suppressWeekly = true;
                _weekly.IsChecked = !on;
                _suppressWeekly = false;
            }
        }

        // Report a Problem: everything a helper needs, zipped onto the desktop, so "it doesn't work" comes with the logs.
        async void ReportProblem()
        {
            _status.Text = "Collecting the logs...";
            _help.IsEnabled = false;
            string zip = null, err = null;
            ScanResult last = _scan;
            try { zip = await Task.Run(() => Report.SupportBundle(last)); }
            catch (Exception ex) { err = Errors.Text(ex); }
            _help.IsEnabled = true;
            if (err != null) { ShowError("Couldn't collect the logs: " + err); return; }
            _status.Text = "Saved " + zip;
            try { Process.Start("explorer.exe", "/select,\"" + zip + "\""); } catch { }
            // The zip names devices and driver versions, so it goes privately to whoever helps, not onto a public issue.
            if (Ask("The app's logs and latest report are saved on your desktop:\n\n" + zip +
                    "\n\nThe zip lists your device names and driver versions (no personal files), so share it only with someone helping you." +
                    "\n\nOpen GitHub to describe the problem in a new issue?", MessageBoxImage.Information, MessageBoxButton.YesNo))
                Open(Updater.RepoUrl + "/issues/new/choose");
        }
    }
}
