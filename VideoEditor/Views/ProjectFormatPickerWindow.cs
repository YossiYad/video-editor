using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using VideoEditor.Services;

namespace VideoEditor.Views;

public class ProjectFormatPickerWindow : Window
{
    public string SelectedPresetKey { get; private set; } = "source";
    public int SelectedCustomWidth  { get; private set; }
    public int SelectedCustomHeight { get; private set; }

    private readonly Dictionary<string, Border> _cards = new();
    private readonly TextBox _customWBox;
    private readonly TextBox _customHBox;
    private string _selected;

    private static readonly Color CardBg       = Color.FromRgb(0x16, 0x1A, 0x25);
    private static readonly Color CardBgHover  = Color.FromRgb(0x1D, 0x22, 0x31);
    private static readonly Color CardBorder   = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
    private static readonly Color Accent       = Color.FromRgb(0x8B, 0x5C, 0xFF);
    private static readonly Color AccentSoft   = Color.FromArgb(0x33, 0x8B, 0x5C, 0xFF);
    private static readonly Color FrameStroke  = Color.FromRgb(0xA0, 0x7C, 0xFF);
    private static readonly Color FrameFill    = Color.FromArgb(0x55, 0x8B, 0x5C, 0xFF);
    private static readonly Color DeviceBody   = Color.FromRgb(0x2D, 0x34, 0x47);
    private static readonly Color DeviceEdge   = Color.FromArgb(0x80, 0x00, 0x00, 0x00);

    public ProjectFormatPickerWindow()
    {
        var ch = WindowBuilder.Build(this, "▦",
            "Choose project format",
            "Pick the canvas you'll be editing for. You can change this anytime.",
            980, 700, "Use this format", primarySuccess: true);

        _selected = AppSettings.TargetFormatPreset ?? "source";
        SelectedCustomWidth  = AppSettings.CustomTargetWidth;
        SelectedCustomHeight = AppSettings.CustomTargetHeight;

        _customWBox = MakeNumberBox(SelectedCustomWidth.ToString(), 16, 7680);
        _customHBox = MakeNumberBox(SelectedCustomHeight.ToString(), 16, 4320);
        _customWBox.GotFocus += (_, _) => SelectPreset("custom");
        _customHBox.GotFocus += (_, _) => SelectPreset("custom");

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        for (int i = 0; i < ProjectFormats.All.Length; i++)
        {
            var p = ProjectFormats.All[i];
            var card = MakeCard(p);
            Grid.SetColumn(card, i % 4);
            Grid.SetRow(card, i / 4);
            grid.Children.Add(card);
            _cards[p.Key] = card;
        }

        ch.Body.Children.Add(grid);

        ch.Primary.Click += (_, _) => Confirm();

        UpdateSelectionVisual();

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Confirm(); e.Handled = true; } };

        FlowDirection = VideoEditor.Services.Localization.IsHebrew
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        VideoEditor.Services.Localization.TranslateTree(this);
    }

    private void Confirm()
    {
        SelectedPresetKey = _selected;
        if (_selected == "custom")
        {
            if (int.TryParse(_customWBox.Text, out var w) && w >= 16 && w <= 7680) SelectedCustomWidth  = w;
            if (int.TryParse(_customHBox.Text, out var h) && h >= 16 && h <= 4320) SelectedCustomHeight = h;
        }
        DialogResult = true;
        Close();
    }

    private void SelectPreset(string key)
    {
        if (!_cards.ContainsKey(key)) return;
        _selected = key;
        UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        foreach (var kv in _cards)
        {
            bool isSel = kv.Key == _selected;
            kv.Value.BorderBrush = new SolidColorBrush(isSel ? Accent : CardBorder);
            kv.Value.BorderThickness = new Thickness(isSel ? 2 : 1);
            kv.Value.Background = new SolidColorBrush(isSel ? CardBgHover : CardBg);
            if (isSel)
            {
                kv.Value.Effect = new DropShadowEffect
                {
                    Color = Accent, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.45
                };
            }
            else
            {
                kv.Value.Effect = null;
            }
        }
    }

    private Border MakeCard(ProjectFormats.Preset p)
    {
        var border = new Border
        {
            Margin = new Thickness(6),
            Padding = new Thickness(12, 14, 12, 12),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(CardBg),
            BorderBrush = new SolidColorBrush(CardBorder),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true
        };

        border.MouseLeftButtonUp += (_, _) => SelectPreset(p.Key);
        border.MouseEnter += (_, _) =>
        {
            if (_selected != p.Key) border.Background = new SolidColorBrush(CardBgHover);
        };
        border.MouseLeave += (_, _) =>
        {
            if (_selected != p.Key) border.Background = new SolidColorBrush(CardBg);
        };

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };

        var mockupSlot = new Grid { Height = 140, Margin = new Thickness(0, 2, 0, 10) };
        mockupSlot.Children.Add(MakeMockup(p));
        stack.Children.Add(mockupSlot);

        stack.Children.Add(new TextBlock
        {
            Text = p.Label,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF2)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });

        var dimsText = p.Key switch
        {
            "source" => "Match source",
            "custom" => "Pick any width and height",
            _        => $"{p.W} × {p.H}"
        };
        stack.Children.Add(new TextBlock
        {
            Text = dimsText,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x91, 0xA8)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        });

        stack.Children.Add(new TextBlock
        {
            Text = p.Description,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x8A)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        if (p.Key == "custom")
        {
            var dimsGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            dimsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            dimsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dimsGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var wStack = new StackPanel();
            wStack.Children.Add(MakeFieldLabel("Width"));
            wStack.Children.Add(_customWBox);
            Grid.SetColumn(wStack, 0); dimsGrid.Children.Add(wStack);

            dimsGrid.Children.Add(new TextBlock
            {
                Text = "×",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x8A)),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(6, 0, 6, 6)
            });
            Grid.SetColumn(dimsGrid.Children[dimsGrid.Children.Count - 1], 1);

            var hStack = new StackPanel();
            hStack.Children.Add(MakeFieldLabel("Height"));
            hStack.Children.Add(_customHBox);
            Grid.SetColumn(hStack, 2); dimsGrid.Children.Add(hStack);

            stack.Children.Add(dimsGrid);
        }

        border.Child = stack;
        return border;
    }

    private static TextBlock MakeFieldLabel(string t) => new()
    {
        Text = t.ToUpperInvariant(),
        FontSize = 9.5,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x8A)),
        Margin = new Thickness(0, 0, 0, 3)
    };

    private static TextBox MakeNumberBox(string text, int min, int max)
    {
        var tb = new TextBox
        {
            Text = text,
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x19)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF2)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            CaretBrush = Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        tb.LostFocus += (_, _) =>
        {
            if (int.TryParse(tb.Text, out var v))
            {
                v = Math.Max(min, Math.Min(max, v));
                tb.Text = v.ToString();
            }
            else
            {
                tb.Text = min.ToString();
            }
        };
        return tb;
    }

    // ===== Mockups =====
    // Each mockup is centered inside a 140-px-tall slot. The visible rectangle's aspect
    // matches the preset so the visual cue is unmistakable.

    private FrameworkElement MakeMockup(ProjectFormats.Preset p) => p.Mockup switch
    {
        "phone"    => MockupPhone(),
        "monitor"  => MockupMonitor(),
        "square"   => MockupSquare(),
        "portrait" => MockupPortrait(),
        "cinema"   => MockupCinema(),
        "source"   => MockupSource(),
        "custom"   => MockupCustom(),
        _          => MockupSource()
    };

    private static FrameworkElement MockupPhone()
    {
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var body = new Border
        {
            Width = 76, Height = 134,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(DeviceBody),
            BorderBrush = new SolidColorBrush(DeviceEdge),
            BorderThickness = new Thickness(1.5)
        };
        var screen = new Border
        {
            Width = 64, Height = 116,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(FrameFill),
            BorderBrush = new SolidColorBrush(FrameStroke),
            BorderThickness = new Thickness(1.2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var notch = new Border
        {
            Width = 22, Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(DeviceEdge),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 0, 0)
        };
        g.Children.Add(body);
        g.Children.Add(screen);
        g.Children.Add(notch);
        return g;
    }

    private static FrameworkElement MockupMonitor()
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var bezel = new Border
        {
            Width = 144, Height = 88,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(DeviceBody),
            BorderBrush = new SolidColorBrush(DeviceEdge),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(4)
        };
        bezel.Child = new Border
        {
            Background = new SolidColorBrush(FrameFill),
            BorderBrush = new SolidColorBrush(FrameStroke),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(3)
        };
        var stand = new Rectangle
        {
            Width = 44, Height = 6,
            Fill = new SolidColorBrush(DeviceBody),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        var foot = new Rectangle
        {
            Width = 70, Height = 3,
            Fill = new SolidColorBrush(DeviceBody),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        stack.Children.Add(bezel);
        stack.Children.Add(stand);
        stack.Children.Add(foot);
        return stack;
    }

    private static FrameworkElement MockupSquare()
    {
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var phone = new Border
        {
            Width = 92, Height = 134,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(DeviceBody),
            BorderBrush = new SolidColorBrush(DeviceEdge),
            BorderThickness = new Thickness(1.5)
        };
        var inner = new StackPanel
        {
            Margin = new Thickness(8, 14, 8, 14)
        };
        // Top bar (stand-in for IG header)
        inner.Children.Add(new Rectangle
        {
            Height = 6,
            Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            RadiusX = 2, RadiusY = 2,
            Margin = new Thickness(0, 0, 0, 6)
        });
        // The square post itself
        inner.Children.Add(new Border
        {
            Height = 76,
            Background = new SolidColorBrush(FrameFill),
            BorderBrush = new SolidColorBrush(FrameStroke),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(3)
        });
        g.Children.Add(phone);
        g.Children.Add(inner);
        return g;
    }

    private static FrameworkElement MockupPortrait()
    {
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var phone = new Border
        {
            Width = 92, Height = 134,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(DeviceBody),
            BorderBrush = new SolidColorBrush(DeviceEdge),
            BorderThickness = new Thickness(1.5)
        };
        var inner = new StackPanel
        {
            Margin = new Thickness(8, 12, 8, 12)
        };
        inner.Children.Add(new Rectangle
        {
            Height = 5,
            Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            RadiusX = 2, RadiusY = 2,
            Margin = new Thickness(0, 0, 0, 4)
        });
        // 4:5 portrait post - width ≈ 76, height ≈ 95
        inner.Children.Add(new Border
        {
            Width = 76, Height = 95,
            Background = new SolidColorBrush(FrameFill),
            BorderBrush = new SolidColorBrush(FrameStroke),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        g.Children.Add(phone);
        g.Children.Add(inner);
        return g;
    }

    private static FrameworkElement MockupCinema()
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var frame = new Border
        {
            Width = 168, Height = 72,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0C, 0x12)),
            BorderBrush = new SolidColorBrush(FrameStroke),
            BorderThickness = new Thickness(1.2)
        };
        // 21:9 fill bar in the middle
        var fill = new Border
        {
            Height = 60,
            Margin = new Thickness(6, 6, 6, 6),
            Background = new SolidColorBrush(FrameFill),
            CornerRadius = new CornerRadius(2)
        };
        frame.Child = fill;
        stack.Children.Add(frame);

        var sprockets = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        for (int i = 0; i < 9; i++)
        {
            sprockets.Children.Add(new Rectangle
            {
                Width = 6, Height = 4,
                Margin = new Thickness(3, 0, 3, 0),
                Fill = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)),
                RadiusX = 1, RadiusY = 1
            });
        }
        stack.Children.Add(sprockets);
        return stack;
    }

    private static FrameworkElement MockupSource()
    {
        var g = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 120, Height = 120
        };
        var frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x8B, 0x5C, 0xFF)),
            BorderBrush = new SolidColorBrush(FrameStroke),
            BorderThickness = new Thickness(1.2)
        };
        g.Children.Add(frame);
        g.Children.Add(new TextBlock
        {
            Text = "▶",
            FontSize = 38,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF2)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        return g;
    }

    private static FrameworkElement MockupCustom()
    {
        var g = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 140, Height = 110
        };
        var rect = new Rectangle
        {
            Stroke = new SolidColorBrush(FrameStroke),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(AccentSoft),
            RadiusX = 6, RadiusY = 6
        };
        g.Children.Add(rect);
        g.Children.Add(new TextBlock
        {
            Text = "W × H",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF2)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        return g;
    }
}
