// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using NetFluss.Core;

namespace NetFluss.App;

/// <summary>
/// The speed test, running the macOS app's own HTML/JS engine.
///
/// <para><b>The engine is shared; only the host is ported.</b>
/// <c>Packaging/Resources/SpeedTest/*.html</c> renders nothing at all — it measures and posts
/// results to the host, which is why the macOS app draws the readout in SwiftUI and this one
/// draws it in WPF. Sharing the measurement code is the whole point: forking it would let the
/// two platforms quietly start reporting different numbers for the same link.</para>
///
/// <para>The page talks to its host through <c>webkit.messageHandlers.speedTestBridge</c>,
/// which exists on WKWebView and not on WebView2. A four-line shim maps it onto
/// <c>chrome.webview.postMessage</c>, and that shim is the entire port — exactly what the
/// port plan budgeted for.</para>
/// </summary>
internal sealed class SpeedTestWindow : Window
{
    /// <summary>
    /// Serves the assets over a real https origin rather than file://, which several of the
    /// APIs the providers use will not run under.
    /// </summary>
    private const string VirtualHost = "netfluss.speedtest";

    private readonly WebView2 _engine = new();
    private readonly TextBlock _phase = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _server = new() { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _download = new() { FontSize = 34, FontFamily = new FontFamily("Consolas, Segoe UI") };
    private readonly TextBlock _upload = new() { FontSize = 34, FontFamily = new FontFamily("Consolas, Segoe UI") };
    private readonly TextBlock _latency = new() { FontSize = 15 };
    private readonly TextBlock _jitter = new() { FontSize = 15 };
    private readonly ProgressBar _progress = new() { Height = 4, Minimum = 0, Maximum = 1, IsIndeterminate = false };
    private readonly Button _run = new() { Content = "Start", Padding = new Thickness(18, 6, 18, 6) };
    private readonly ComboBox _provider = new() { MinWidth = 150, Margin = new Thickness(0, 0, 10, 0) };

    private int _runId;
    private bool _ready;

    internal SpeedTestWindow(SurfacePalette surface)
    {
        Title = "NetFluss Speed Test";
        Width = 520;
        Height = 420;
        MinWidth = 420;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush(surface.Background);

        _provider.ItemsSource = new[] { "Cloudflare", "M-Lab" };
        _provider.SelectedIndex = 0;
        _run.Click += (_, _) => Start();

        Content = BuildLayout(surface);
        Reset("Choose a provider and run a speed test when you want.");

        Loaded += async (_, _) => await InitialiseAsync();
    }

    private UIElement BuildLayout(SurfacePalette surface)
    {
        var text = Brush(surface.TextPrimary);
        foreach (var block in new[] { _phase, _server, _latency, _jitter })
        {
            block.Foreground = text;
        }

        _download.Foreground = Brush(ThemeColor.FromHex("4CC2FF"));
        _upload.Foreground = Brush(ThemeColor.FromHex("6CCB5F"));

        Grid Metric(string label, TextBlock value, string unit)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var caption = new TextBlock { Text = label, FontSize = 12, Opacity = 0.7, Foreground = text };
            Grid.SetRow(caption, 0);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(value);
            row.Children.Add(new TextBlock
            {
                Text = " " + unit,
                FontSize = 13,
                Opacity = 0.7,
                Foreground = text,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 6),
            });

            Grid.SetRow(row, 1);
            grid.Children.Add(caption);
            grid.Children.Add(row);
            return grid;
        }

        var metrics = new Grid { Margin = new Thickness(0, 18, 0, 10) };
        metrics.ColumnDefinitions.Add(new ColumnDefinition());
        metrics.ColumnDefinitions.Add(new ColumnDefinition());

        var down = Metric("Download", _download, "Mbps");
        var up = Metric("Upload", _upload, "Mbps");
        Grid.SetColumn(down, 0);
        Grid.SetColumn(up, 1);
        metrics.Children.Add(down);
        metrics.Children.Add(up);

        var detail = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        detail.Children.Add(_latency);
        detail.Children.Add(new TextBlock { Text = "     ", FontSize = 15 });
        detail.Children.Add(_jitter);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 18, 0, 0),
        };

        controls.Children.Add(_provider);
        controls.Children.Add(_run);

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = "Speed Test",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = text,
            Margin = new Thickness(0, 0, 0, 10),
        });

        root.Children.Add(_phase);
        root.Children.Add(_progress);
        root.Children.Add(metrics);
        root.Children.Add(detail);
        root.Children.Add(_server);
        root.Children.Add(controls);

        // Zero-sized and never shown. This is a measurement engine, not a page.
        _engine.Width = 0;
        _engine.Height = 0;
        _engine.Visibility = Visibility.Collapsed;
        root.Children.Add(_engine);

        return root;
    }

    private async Task InitialiseAsync()
    {
        try
        {
            // Its own user-data folder under LOCALAPPDATA: the default sits beside the exe,
            // which fails outright when the app is installed to Program Files.
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NetFluss",
                    "WebView2"));

            await _engine.EnsureCoreWebView2Async(environment);

            var core = _engine.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;

            var assets = Path.Combine(AppContext.BaseDirectory, "SpeedTest");
            core.SetVirtualHostNameToFolderMapping(VirtualHost, assets, CoreWebView2HostResourceAccessKind.Allow);

            // The shim, and the whole of the port: the page asks for a WKWebView message
            // handler, so give it one that forwards to WebView2.
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                """
                window.webkit = window.webkit || {};
                window.webkit.messageHandlers = window.webkit.messageHandlers || {};
                window.webkit.messageHandlers.speedTestBridge = {
                    postMessage: function (message) { window.chrome.webview.postMessage(message); }
                };
                """);

            core.WebMessageReceived += OnWebMessage;
            _ready = true;
            Reset("Ready.");
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or DllNotFoundException)
        {
            // The Evergreen runtime is present on Windows 11 and usually on 10, but not
            // always. Say so plainly rather than leaving a dead Start button.
            _ready = false;
            _run.IsEnabled = false;
            _phase.Text = "The WebView2 runtime is not installed.";
            _server.Text = "The speed test needs Microsoft Edge WebView2, which the NetFluss installer will bootstrap. Everything else in NetFluss works without it.";
        }
        catch (Exception e)
        {
            _ready = false;
            _run.IsEnabled = false;
            _phase.Text = "The speed test engine could not start.";
            _server.Text = e.Message;
        }
    }

    private async void Start()
    {
        if (!_ready || _engine.CoreWebView2 is not { } core)
        {
            return;
        }

        _runId++;
        _run.IsEnabled = false;
        Reset("Starting…");
        _progress.IsIndeterminate = true;

        var page = _provider.SelectedIndex == 1 ? "mlab.html" : "cloudflare.html";
        var provider = _provider.SelectedIndex == 1 ? "mlab" : "cloudflare";

        var completion = new TaskCompletionSource();

        void OnNavigated(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            core.NavigationCompleted -= OnNavigated;
            completion.TrySetResult();
        }

        core.NavigationCompleted += OnNavigated;
        core.Navigate($"https://{VirtualHost}/{page}");

        await completion.Task;

        var payload = JsonSerializer.Serialize(new
        {
            runId = _runId,
            provider,
            clientName = "NetFluss",
            clientVersion = "0.1.0",
        });

        await core.ExecuteScriptAsync($"window.NetFlussSpeedTest.start({payload});");
    }

    /// <summary>
    /// Handles one message from the engine. The shapes are the macOS app's, unchanged —
    /// phase, progress, latency, download, upload, result, error.
    /// </summary>
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement message;
        try
        {
            message = JsonSerializer.Deserialize<JsonElement>(e.WebMessageAsJson);
        }
        catch (JsonException)
        {
            return;
        }

        // The engine tags every message with the run it belongs to. A late message from a
        // cancelled run would otherwise overwrite the current one's numbers.
        if (message.TryGetProperty("runId", out var runId) &&
            runId.ValueKind == JsonValueKind.Number &&
            runId.GetInt32() != _runId)
        {
            return;
        }

        var type = message.TryGetProperty("type", out var t) ? t.GetString() : null;

        switch (type)
        {
            case "phase":
                _phase.Text = Text(message, "detail") ?? Text(message, "phase") ?? _phase.Text;
                var name = Text(message, "serverName");
                var location = Text(message, "serverLocation");
                _server.Text = name is null ? string.Empty : location is null ? name : $"{name} — {location}";
                break;

            // "progress" carries the whole running summary rather than a completion
            // fraction — the same payload as "result", posted repeatedly as the engine
            // refines it. Treating it as a percentage left every figure on "—" until the
            // test finished, which made a 20-second run look like it had hung.
            case "progress":
                Apply(message);
                break;

            case "result":
                Apply(message);
                _phase.Text = "Finished.";
                _progress.IsIndeterminate = false;
                _progress.Value = 1;
                _run.IsEnabled = true;
                break;

            case "error":
                _phase.Text = Text(message, "message") ?? "The speed test failed.";
                _progress.IsIndeterminate = false;
                _progress.Value = 0;
                _run.IsEnabled = true;
                break;
        }
    }

    /// <summary>
    /// Applies whichever metrics a payload carries, leaving the rest alone. Shared by
    /// "progress" and "result" because the engine sends the same summary shape for both.
    /// </summary>
    private void Apply(JsonElement result)
    {
        if (Number(result, "downloadMbps") is { } down)
        {
            _download.Text = Format(down);
        }

        if (Number(result, "uploadMbps") is { } up)
        {
            _upload.Text = Format(up);
        }

        if (Number(result, "latencyMs") is { } latency)
        {
            _latency.Text = string.Format(CultureInfo.InvariantCulture, "Latency {0:N0} ms", latency);
        }

        if (Number(result, "jitterMs") is { } jitter)
        {
            _jitter.Text = string.Format(CultureInfo.InvariantCulture, "Jitter {0:N0} ms", jitter);
        }

        var name = Text(result, "serverName");
        var location = Text(result, "serverLocation");
        if (name is not null)
        {
            _server.Text = location is null ? name : $"{name} — {location}";
        }
    }

    private void Reset(string phase)
    {
        _phase.Text = phase;
        _server.Text = string.Empty;
        _download.Text = "—";
        _upload.Text = "—";
        _latency.Text = "Latency —";
        _jitter.Text = "Jitter —";
        _progress.Value = 0;
        _progress.IsIndeterminate = false;
        _run.IsEnabled = _ready;
    }

    /// <summary>Invariant, so "781.0 Mbps" cannot sit beside the meter's "1.36 MB/s".</summary>
    private static string Format(double? mbps)
        => mbps is { } value ? value.ToString("N1", CultureInfo.InvariantCulture) : "—";

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Number(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static SolidColorBrush Brush(ThemeColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    protected override void OnClosed(EventArgs e)
    {
        // The engine keeps a browser process alive; leaving one per open would accumulate.
        _engine.Dispose();
        base.OnClosed(e);
    }
}
