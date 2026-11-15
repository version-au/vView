using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace vView.Ui
{
    /// <summary>
    /// Over a Jellyfin video's bottom right corner: a Skip intro button during the intro, and near the end "Up next" - the next episode's
    /// picture and name, the countdown, Play now and Cancel (as Jellyfin's own player). MainWindow.UpNext.cs decides what shows when.
    /// </summary>
    sealed class JellyfinPromptWindow : CornerWindow
    {
        public Action SkipIntro, PlayNow, Cancel;

        string _mode = "";   // "", "intro", "next"
        readonly Border _card;
        readonly Button _skip;
        readonly StackPanel _next;
        readonly Border _thumb;
        readonly TextBlock _title, _series, _count;
        string _imageFor;

        public JellyfinPromptWindow(Window main, FrameworkElement anchor) : base(main, anchor, "vView - Jellyfin")
        {
            _card = new Border
            {
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 14, 14),
                BorderThickness = new Thickness(1.5),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.4 }
            };
            _card.SetResourceReference(Border.BackgroundProperty, "vx.Popup");
            _card.SetResourceReference(Border.BorderBrushProperty, "vx.Accent");
            _card.SetResourceReference(Border.CornerRadiusProperty, "vx.MenuRadius");
            _card.SetResourceReference(TextElement.ForegroundProperty, "vx.Fg");

            // Skip intro: one button
            _skip = MakeButton("Skip intro  ⏭", true, () => SkipIntro?.Invoke());
            _skip.Margin = new Thickness(0);
            _skip.Padding = new Thickness(16, 0, 16, 0);   // the button style's own height (more padding cut the text off)

            // Up next: the picture beside "Up next", its name, the series and the countdown; Play now / Cancel under them
            _next = new StackPanel { Width = 380 };
            var top = new DockPanel();
            _thumb = new Border { Width = 150, Height = 84, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            _thumb.SetResourceReference(Border.BackgroundProperty, "vx.Ctrl");
            DockPanel.SetDock(_thumb, Dock.Left);
            top.Children.Add(_thumb);
            var text = new StackPanel();
            var label = new TextBlock { Text = "Up next", FontSize = 11.5 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            text.Children.Add(label);
            _title = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 40, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            text.Children.Add(_title);
            _series = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            _series.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            text.Children.Add(_series);
            _count = new TextBlock { FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
            _count.SetResourceReference(TextBlock.ForegroundProperty, "vx.Accent");
            text.Children.Add(_count);
            top.Children.Add(text);
            _next.Children.Add(top);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(MakeButton("Play now", true, () => PlayNow?.Invoke()));
            buttons.Children.Add(MakeButton("Cancel", false, () => Cancel?.Invoke()));
            _next.Children.Add(buttons);

            Content = new Border { Padding = new Thickness(14, 14, 0, 0), Child = _card };
        }

        static Button MakeButton(string text, bool main, Action click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
            b.SetResourceReference(StyleProperty, "vx.TextButton");
            if (main)
            {
                b.SetResourceReference(Control.BackgroundProperty, "vx.Accent");
                b.SetResourceReference(Control.ForegroundProperty, "vx.OnAccent");
                b.FontWeight = FontWeights.SemiBold;
            }
            b.Click += (o, e) => click();
            return b;
        }

        protected override bool Wanted { get { return _mode.Length > 0; } }

        public string Mode { get { return _mode; } }

        public void ShowSkipIntro()
        {
            if (_mode != "intro") { _mode = "intro"; _card.Padding = new Thickness(6); _card.Child = _skip; }
            Sync();
        }

        /// <summary>Up next: what (and its picture), and the countdown line ("Plays in 12 s", or "" with no countdown).</summary>
        public void ShowUpNext(string title, string series, string image, string countdown)
        {
            if (_mode != "next") { _mode = "next"; _card.Padding = new Thickness(14); _card.Child = _next; }
            _title.Text = title ?? "";
            _series.Text = series ?? "";
            _series.Visibility = string.IsNullOrEmpty(series) ? Visibility.Collapsed : Visibility.Visible;
            _count.Text = countdown ?? "";
            _count.Visibility = string.IsNullOrEmpty(countdown) ? Visibility.Collapsed : Visibility.Visible;
            if (_imageFor != image)
            {
                _imageFor = image;
                _thumb.Visibility = Visibility.Collapsed;
                Uri u;
                if (!string.IsNullOrEmpty(image) && Uri.TryCreate(image, UriKind.Absolute, out u))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = u;
                        bmp.DecodePixelWidth = 300;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        Action shown = () => { if (_imageFor == image) { _thumb.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill }; _thumb.Visibility = Visibility.Visible; } };
                        if (bmp.IsDownloading) bmp.DownloadCompleted += (o, e) => shown(); else shown();
                    }
                    catch { }
                }
            }
            Sync();
        }

        public void Clear()
        {
            if (_mode.Length == 0) return;
            _mode = "";
            Sync();
        }
    }
}
