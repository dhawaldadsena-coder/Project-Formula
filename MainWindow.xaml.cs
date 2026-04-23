using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using F1TrackMapper.ViewModels;

namespace F1TrackMapper;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly Dictionary<string, UiThemePreset> _themePresets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RossoCarbon"] = new("Rosso Carbon", "#2B2D42", "#EF233C"),
        ["GraphiteGold"] = new("Graphite Gold", "#2D3142", "#FCA311"),
        ["AquaVelocity"] = new("Aqua Velocity", "#0B132B", "#5BC0BE"),
        ["IceCircuit"] = new("Ice Circuit", "#1D3557", "#A8DADC"),
        ["EmeraldNight"] = new("Emerald Night", "#1B4332", "#74C69D"),
        ["SunsetGrid"] = new("Sunset Grid", "#3D405B", "#E07A5F"),
        ["VioletKerb"] = new("Violet Kerb", "#2B193D", "#C77DFF"),
        ["ArcticSignal"] = new("Arctic Signal", "#264653", "#E9F5F2")
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ThemeSelector.Items.Count > 0)
        {
            ThemeSelector.SelectedIndex = 0;
        }
        else
        {
            ApplyTheme("RossoCarbon");
        }
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelector.SelectedItem is not ComboBoxItem item || item.Tag is not string key)
        {
            return;
        }

        ApplyTheme(key);
    }

    private void ApplyTheme(string key)
    {
        if (!_themePresets.TryGetValue(key, out var theme))
        {
            return;
        }

        var baseColor = ParseColor(theme.BaseHex);
        var accentColor = ParseColor(theme.AccentHex);
        var appBlack = Darken(baseColor, 0.34);
        var panel = Lighten(baseColor, 0.08);
        var panelRaised = Lighten(baseColor, 0.14);
        var panelAlt = Blend(baseColor, accentColor, 0.16);
        var trackShell = ParseColor("#0E1116");
        var accentContrast = GetReadableText(accentColor);

        SetBrushResource("AppBlackBrush", appBlack);
        SetBrushResource("AppPanelBrush", panel);
        SetBrushResource("AppPanelRaisedBrush", panelRaised);
        SetBrushResource("AppPanelAltBrush", panelAlt);
        SetBrushResource("AppBorderBrush", Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
        SetBrushResource("AppTextBrush", ParseColor("#F8FAFD"));
        SetBrushResource("AppTextMutedBrush", ParseColor("#C9D2DC"));
        SetBrushResource("AppTextSoftBrush", ParseColor("#98A5B4"));
        SetBrushResource("AppTrackShellBrush", trackShell);
        SetBrushResource("AppRedBrush", accentColor);
        SetBrushResource("AppRedSoftBrush", Color.FromArgb(0x5A, accentColor.R, accentColor.G, accentColor.B));
        SetBrushResource("AppAccentContrastBrush", accentContrast);
        SetBrushResource("AppTrackOutlineBrush", Darken(baseColor, 0.58));
        SetBrushResource("AppTrackGlowBrush", Color.FromArgb(0x52, accentColor.R, accentColor.G, accentColor.B));
        SetBrushResource("AppTrackLaneBrush", ParseColor("#F8FAFC"));
        SetBrushResource("AppTrackEdgeBrush", Darken(baseColor, 0.16));
        SetBrushResource("AppTurnCardBrush", Color.FromArgb(0xD9, panelAlt.R, panelAlt.G, panelAlt.B));
        SetBrushResource("AppPitBaseBrush", Blend(accentColor, ParseColor("#6F5310"), 0.35));
        SetBrushResource("AppPitDashBrush", Blend(accentColor, Colors.White, 0.38));
        SetBrushResource("AppCanvasFrameBrush", Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));


        SetGradientResource("PanelBrush",
            Lighten(baseColor, 0.12),
            Lighten(baseColor, 0.05));

        SetGradientResource("HeroPanelBrush",
            Blend(Lighten(baseColor, 0.16), accentColor, 0.12),
            Lighten(baseColor, 0.07),
            Blend(baseColor, accentColor, 0.08));
    }

    private void SetBrushResource(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        Resources[key] = brush;
    }

    private void SetGradientResource(string key, params Color[] colors)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };

        if (colors.Length == 1)
        {
            brush.GradientStops.Add(new GradientStop(colors[0], 0));
        }
        else
        {
            for (var index = 0; index < colors.Length; index++)
            {
                var offset = colors.Length == 1 ? 0 : (double)index / (colors.Length - 1);
                brush.GradientStops.Add(new GradientStop(colors[index], offset));
            }
        }

        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        Resources[key] = brush;
    }

    private static Color GetReadableText(Color background)
    {
        var luminance = ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / 255.0;
        return luminance > 0.62 ? ParseColor("#102030") : ParseColor("#F8FAFD");
    }

    private static Color ParseColor(string hex)
        => (Color)ColorConverter.ConvertFromString(hex);

    private static Color Blend(Color left, Color right, double amount)
    {
        amount = Math.Max(0, Math.Min(1, amount));
        return Color.FromArgb(
            0xFF,
            BlendChannel(left.R, right.R, amount),
            BlendChannel(left.G, right.G, amount),
            BlendChannel(left.B, right.B, amount));
    }

    private static Color Lighten(Color color, double amount)
        => Blend(color, Colors.White, amount);

    private static Color Darken(Color color, double amount)
        => Blend(color, Colors.Black, amount);

    private static byte BlendChannel(byte left, byte right, double amount)
        => (byte)Math.Round(left + ((right - left) * amount));

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
        => _viewModel.ZoomOut();

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
        => _viewModel.ResetZoom();

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
        => _viewModel.ZoomIn();

    private async void TelemetryToggle_Click(object sender, RoutedEventArgs e)
        => await _viewModel.ToggleTelemetryModeAsync();

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private sealed record UiThemePreset(string Name, string BaseHex, string AccentHex);
}


