// Claude usage widget: a small always-on-top window with the limits shown on
// claude.ai > Settings > Usage, read from the same account API the page uses.
// The user signs in to claude.ai once inside the app (WebView2); the app never
// sees or stores the password, only WebView2's own cookies in its profile folder.
//
// Built by build.ps1 with the .NET Framework C# compiler (C# 5).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace ClaudeUsageWidget
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, @"Local\ClaudeUsageWidgetApp", out created))
            {
                if (!created) return;
                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Widget widget = null;
                // Create the widget once the dispatcher runs, so awaits resume on the UI thread.
                app.Startup += delegate { widget = new Widget(app); };
                app.Run();
                GC.KeepAlive(widget);
            }
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }

    internal class LimitRow
    {
        public TextBlock Label, Percent, Reset;
        public Border Track, Fill;
    }

    internal class Settings
    {
        public double Left = double.NaN, Top = double.NaN;
        public bool AlwaysShow;

        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeUsageWidget"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }
        public static string WebViewDir { get { return Path.Combine(Dir, "WebView2"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(FilePath));
                if (d.ContainsKey("left")) s.Left = Convert.ToDouble(d["left"]);
                if (d.ContainsKey("top")) s.Top = Convert.ToDouble(d["top"]);
                if (d.ContainsKey("alwaysShow")) s.AlwaysShow = Convert.ToBoolean(d["alwaysShow"]);
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var d = new Dictionary<string, object>();
                d["left"] = Left; d["top"] = Top; d["alwaysShow"] = AlwaysShow;
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(d));
            }
            catch { }
        }
    }

    public class Widget
    {
        // Colors of the meters on claude.ai > Settings > Usage
        const string Blue = "#257DE0", Amber = "#FBB315", Red = "#DC3C3D";
        const double BarWidth = 120;
        static readonly TimeSpan FetchEvery = TimeSpan.FromSeconds(30);

        // Runs on https://claude.ai in the hidden WebView2 and posts the usage back.
        const string FetchScript = @"(async () => {
  const post = o => chrome.webview.postMessage(JSON.stringify(o));
  try {
    const orgsRes = await fetch('/api/organizations', { credentials: 'include' });
    if (orgsRes.status === 401 || orgsRes.status === 403) return post({ auth: false });
    const orgs = await orgsRes.json();
    if (!Array.isArray(orgs) || orgs.length === 0) return post({ auth: false });
    const org = orgs.find(o => (o.capabilities || []).indexOf('chat') >= 0) || orgs[0];
    const res = await fetch('/api/organizations/' + org.uuid + '/usage', { credentials: 'include' });
    if (res.status === 401 || res.status === 403) return post({ auth: false });
    const u = await res.json();
    post({ auth: true, limits: u.limits || null, five_hour: u.five_hour || null, seven_day: u.seven_day || null });
  } catch (e) {
    post({ error: String(e) });
  }
})();";

        readonly Application app;
        readonly Settings settings = Settings.Load();
        readonly Window win = new Window();
        readonly StackPanel rows = new StackPanel();
        readonly TextBlock note = new TextBlock();
        readonly List<LimitRow> rowCache = new List<LimitRow>();
        readonly BrushConverter brushes = new BrushConverter();

        Window loginWin;
        CoreWebView2Controller controller;
        CoreWebView2 web;
        bool loggingIn, ready;
        ArrayList lastLimits;
        DateTime lastOk = DateTime.MinValue;
        string status = "Подключение…";

        public Widget(Application app)
        {
            this.app = app;
            BuildWidget();
            BuildLoginWindow();

            var top = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            top.Tick += delegate { KeepOnTop(); };
            top.Start();

            var ui = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            ui.Tick += delegate { UpdateVisibility(); Render(); };
            ui.Start();

            var fetch = new DispatcherTimer { Interval = FetchEvery };
            fetch.Tick += delegate { Fetch(); };
            fetch.Start();

            UpdateVisibility();
            Render();
            InitWebView();
        }

        // ---------- widget window ----------

        Brush B(string hex) { return (Brush)brushes.ConvertFromString(hex); }

        void BuildWidget()
        {
            win.WindowStyle = WindowStyle.None;
            win.AllowsTransparency = true;
            win.Background = Brushes.Transparent;
            win.Topmost = true;
            win.ShowInTaskbar = false;
            win.ResizeMode = ResizeMode.NoResize;
            win.SizeToContent = SizeToContent.WidthAndHeight;
            win.Title = "Claude limits";
            win.FontFamily = new FontFamily("Segoe UI");
            win.FontSize = 12;

            note.Foreground = B("#9C9A92");
            note.FontSize = 10;
            note.Margin = new Thickness(0, 4, 0, 0);

            var panel = new StackPanel();
            panel.Children.Add(rows);
            panel.Children.Add(note);
            win.Content = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = B("#E6262624"),
                BorderBrush = B("#40FFFFFF"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 7, 10, 7),
                Child = panel
            };

            var wa = SystemParameters.WorkArea;
            win.Left = double.IsNaN(settings.Left) ? wa.Right - 400 : settings.Left;
            win.Top = double.IsNaN(settings.Top) ? wa.Bottom - 90 : settings.Top;

            win.MouseLeftButtonDown += delegate
            {
                try { win.DragMove(); } catch { }
                settings.Left = win.Left; settings.Top = win.Top; settings.Save();
            };

            var menu = new ContextMenu();
            menu.Items.Add(Item("Обновить сейчас", delegate { Fetch(); }));
            menu.Items.Add(Item("Открыть «Использование» на claude.ai", delegate { Process.Start("https://claude.ai/settings/usage"); }));
            var always = Item("Показывать, даже когда Claude закрыт", null);
            always.IsCheckable = true;
            always.IsChecked = settings.AlwaysShow;
            always.Click += delegate { settings.AlwaysShow = always.IsChecked; settings.Save(); UpdateVisibility(); };
            menu.Items.Add(always);
            menu.Items.Add(Item("Войти в другой аккаунт", delegate { ShowLogin(true); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Закрыть виджет", delegate { Quit(); }));
            win.ContextMenu = menu;
        }

        static MenuItem Item(string header, RoutedEventHandler click)
        {
            var m = new MenuItem { Header = header };
            if (click != null) m.Click += click;
            return m;
        }

        LimitRow Row(int i)
        {
            while (rowCache.Count <= i)
            {
                var r = new LimitRow();
                var g = new Grid { Margin = new Thickness(0, rowCache.Count == 0 ? 0 : 4, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarWidth) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                r.Label = new TextBlock { Foreground = B("#F0EEE6"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                r.Fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, CornerRadius = new CornerRadius(2), Background = B(Blue) };
                r.Track = new Border { Width = BarWidth, Height = 4, CornerRadius = new CornerRadius(2), Background = B("#33257DE0"), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true, Child = r.Fill };
                r.Percent = new TextBlock { Foreground = B("#F0EEE6"), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                r.Reset = new TextBlock { Foreground = B("#9C9A92"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

                Grid.SetColumn(r.Label, 0); Grid.SetColumn(r.Track, 1); Grid.SetColumn(r.Percent, 2); Grid.SetColumn(r.Reset, 3);
                g.Children.Add(r.Label); g.Children.Add(r.Track); g.Children.Add(r.Percent); g.Children.Add(r.Reset);
                rows.Children.Add(g);
                rowCache.Add(r);
            }
            return rowCache[i];
        }

        static string LabelFor(string kind, string group)
        {
            if (kind == "session" || group == "session" || kind == "five_hour") return "Сессия";
            if (kind == "weekly_all" || kind == "seven_day") return "Неделя";
            if (kind != null && kind.StartsWith("weekly_")) return "Неделя " + kind.Substring(7);
            return kind ?? "?";
        }

        static string FillFor(string severity, double pct)
        {
            var s = (severity ?? "").ToLowerInvariant();
            if (s == "critical" || s == "exceeded" || s == "limited" || s == "error" || s == "danger") return Red;
            if (s == "warning" || s == "high" || s == "approaching" || s == "caution") return Amber;
            if (s == "normal" || s == "low" || s == "ok") return Blue;
            return pct >= 95 ? Red : pct >= 80 ? Amber : Blue;
        }

        static string ResetText(object resetsAt)
        {
            DateTimeOffset t;
            if (resetsAt == null || !DateTimeOffset.TryParse(resetsAt.ToString(), out t)) return "";
            var mins = (int)Math.Round((t - DateTimeOffset.Now).TotalMinutes);
            if (mins <= 0) return "сброшен";
            int d = mins / 1440, h = (mins % 1440) / 60, m = mins % 60;
            if (d > 0) return string.Format("сброс через {0}д {1}ч", d, h);
            if (h > 0) return string.Format("сброс через {0}ч {1}м", h, m);
            return string.Format("сброс через {0}м", m);
        }

        void Render()
        {
            var items = lastLimits ?? new ArrayList();
            for (int i = 0; i < Math.Max(items.Count, 2); i++)
            {
                var r = Row(i);
                var l = i < items.Count ? items[i] as Dictionary<string, object> : null;
                ((FrameworkElement)r.Label.Parent).Visibility = (l == null && i >= 2) ? Visibility.Collapsed : Visibility.Visible;
                if (l == null)
                {
                    r.Label.Text = i == 0 ? "Сессия" : "Неделя";
                    r.Fill.Width = 0; r.Percent.Text = "—"; r.Reset.Text = "";
                    continue;
                }
                double pct = 0;
                try { pct = Convert.ToDouble(Get(l, "percent")); } catch { }
                var fill = FillFor(Get(l, "severity") as string, pct);
                r.Label.Text = LabelFor(Get(l, "kind") as string, Get(l, "group") as string);
                r.Fill.Width = Math.Max(4, BarWidth * Math.Min(100, Math.Max(0, pct)) / 100);
                r.Fill.Background = B(fill);
                r.Track.Background = B("#33" + fill.Substring(1));
                r.Percent.Text = Math.Round(pct) + "%";
                r.Reset.Text = ResetText(Get(l, "resets_at"));
            }
            for (int i = Math.Max(items.Count, 2); i < rowCache.Count; i++)
                ((FrameworkElement)rowCache[i].Label.Parent).Visibility = Visibility.Collapsed;

            if (lastOk == DateTime.MinValue) note.Text = status;
            else
            {
                var age = (int)(DateTime.Now - lastOk).TotalMinutes;
                var when = age < 1 ? "обновлено только что" : age < 60 ? "обновлено " + age + " мин назад" : "обновлено " + (age / 60) + " ч назад";
                note.Text = status == null ? when : when + " · " + status;
            }
        }

        static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        static bool ClaudeOpen()
        {
            foreach (var p in Process.GetProcessesByName("claude"))
            {
                try { if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle)) return true; }
                catch { }
            }
            return false;
        }

        void UpdateVisibility()
        {
            var show = settings.AlwaysShow || loggingIn || ClaudeOpen();
            if (show && !win.IsVisible) win.Show();
            else if (!show && win.IsVisible) win.Hide();
        }

        void KeepOnTop()
        {
            // The taskbar puts itself above topmost windows when clicked: take the top back.
            if (!win.IsVisible) return;
            var hwnd = new WindowInteropHelper(win).Handle;
            Native.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // HWND_TOPMOST; NOSIZE|NOMOVE|NOACTIVATE
        }

        void Quit()
        {
            try { if (controller != null) controller.Close(); } catch { }
            app.Shutdown();
        }

        // ---------- claude.ai session (WebView2) ----------

        void BuildLoginWindow()
        {
            loginWin = new Window
            {
                Title = "Claude Usage Widget — вход в claude.ai",
                Width = 520, Height = 720,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = true
            };
            loginWin.Closing += (s, e) => { e.Cancel = true; HideLogin(); };
            loginWin.SizeChanged += delegate { ResizeWebView(); };
            new WindowInteropHelper(loginWin).EnsureHandle();
        }

        void ResizeWebView()
        {
            if (controller == null) return;
            var src = PresentationSource.FromVisual(loginWin);
            double sx = 1, sy = 1;
            if (src != null && src.CompositionTarget != null) { sx = src.CompositionTarget.TransformToDevice.M11; sy = src.CompositionTarget.TransformToDevice.M22; }
            var content = loginWin.Content as FrameworkElement;
            double w = loginWin.ActualWidth, h = loginWin.ActualHeight;
            if (content != null) { w = content.ActualWidth; h = content.ActualHeight; }
            controller.Bounds = new System.Drawing.Rectangle(0, 0, (int)(w * sx), (int)(h * sy));
        }

        async void InitWebView()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, Settings.WebViewDir, null);
                controller = await env.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(loginWin).Handle);
                controller.IsVisible = false;
                web = controller.CoreWebView2;
                web.Settings.AreDevToolsEnabled = false;
                web.WebMessageReceived += OnMessage;
                web.NavigationCompleted += OnNavigated;
                web.NewWindowRequested += (s, e) => { e.Handled = true; web.Navigate(e.Uri); };
                web.Navigate("https://claude.ai/robots.txt");
            }
            catch (Exception ex)
            {
                status = "Нет WebView2: " + ex.Message;
                Render();
            }
        }

        void OnNavigated(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            var url = web.Source ?? "";
            if (!url.StartsWith("https://claude.ai")) return;
            if (!loggingIn) { ready = true; Fetch(); return; }
            // While signing in: once the page leaves /login, check whether the session works.
            if (url.IndexOf("/login", StringComparison.OrdinalIgnoreCase) < 0 && url.IndexOf("/magic-link", StringComparison.OrdinalIgnoreCase) < 0)
            {
                ready = true;
                Fetch();
            }
        }

        void Fetch()
        {
            if (web == null || !ready) return;
            try { web.ExecuteScriptAsync(FetchScript); } catch { }
        }

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> msg;
            try { msg = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(e.TryGetWebMessageAsString()); }
            catch { return; }

            if (msg.ContainsKey("error"))
            {
                status = "ошибка связи с claude.ai";
                Render();
                return;
            }
            if (!(Get(msg, "auth") is bool) || !(bool)Get(msg, "auth"))
            {
                status = "нужно войти в claude.ai";
                Render();
                if (!loggingIn) ShowLogin(false);
                return;
            }

            var limits = Get(msg, "limits") as ArrayList;
            if (limits == null || limits.Count == 0)
            {
                // Older shape: five_hour / seven_day with utilization
                limits = new ArrayList();
                foreach (var key in new[] { "five_hour", "seven_day" })
                {
                    var w = Get(msg, key) as Dictionary<string, object>;
                    if (w == null) continue;
                    var l = new Dictionary<string, object>();
                    l["kind"] = key; l["percent"] = Get(w, "utilization"); l["resets_at"] = Get(w, "resets_at");
                    limits.Add(l);
                }
            }
            lastLimits = limits;
            lastOk = DateTime.Now;
            status = null;
            if (loggingIn) HideLogin();
            Render();
        }

        void ShowLogin(bool switchAccount)
        {
            if (web == null) return;
            loggingIn = true;
            UpdateVisibility();
            loginWin.Show();
            loginWin.Activate();
            ResizeWebView();
            controller.IsVisible = true;
            web.Navigate(switchAccount ? "https://claude.ai/logout" : "https://claude.ai/login");
        }

        void HideLogin()
        {
            loggingIn = false;
            if (controller != null) controller.IsVisible = false;
            loginWin.Hide();
            if (web != null && !(web.Source ?? "").EndsWith("/robots.txt")) web.Navigate("https://claude.ai/robots.txt");
            UpdateVisibility();
        }
    }
}
