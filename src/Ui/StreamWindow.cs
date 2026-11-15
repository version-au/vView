using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using vView.Core;

namespace vView.Ui
{
    /// <summary>
    /// Open a network stream (like VLC's): an address - rtsp://, rtmp://, http(s)://, udp://, srt:// … - typed, pasted (an address on
    /// the clipboard is filled in) or picked from the recent ones. Enter = Play, Esc = Cancel.
    /// </summary>
    public sealed class StreamWindow : Window
    {
        readonly TextBox _box;
        readonly TextBlock _error;
        string _result;

        /// <summary>The address to play, or null.</summary>
        public static string Ask(MainWindow owner, AppSettings s)
        {
            var w = new StreamWindow(owner, s);
            w.ShowDialog();
            return w._result;
        }

        StreamWindow(MainWindow owner, AppSettings s)
        {
            Owner = owner?.Front;   // over the classic player while it shows
            Title = "Open a network stream";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12.5;
            UseLayoutRounding = true;
            Background = Brushes.Transparent;
            SetResourceReference(ForegroundProperty, "vx.Fg");
            bool blur = owner != null && owner.Blurred;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 30, ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false
            });
            SourceInitialized += (o, e) =>
            {
                if (owner != null && owner.CurrentTheme != null) WindowEffects.Look(this, owner.CurrentTheme, blur);
            };

            var root = new Border { Padding = new Thickness(24, 20, 24, 20) };
            root.SetResourceReference(Border.BackgroundProperty, "vx.Bg");
            var stack = new StackPanel();
            root.Child = stack;
            stack.Children.Add(new TextBlock { Text = "Open a network stream", FontSize = 18, FontWeight = FontWeights.SemiBold });
            stack.Children.Add(Dim("A camera, a server or a live stream - rtsp://, rtmp://, http(s)://, udp://, srt:// …", new Thickness(0, 4, 0, 14)));

            var boxBorder = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 10, 7) };
            boxBorder.SetResourceReference(Border.BackgroundProperty, "vx.Panel");
            boxBorder.SetResourceReference(Border.BorderBrushProperty, "vx.Track");
            _box = new TextBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 13.5 };
            _box.SetResourceReference(ForegroundProperty, "vx.Fg");
            _box.SetResourceReference(TextBox.CaretBrushProperty, "vx.Fg");
            _box.SetResourceReference(TextBox.SelectionBrushProperty, "vx.Accent");
            AutomationProperties_SetName(_box, "Network address");
            boxBorder.Child = _box;
            stack.Children.Add(boxBorder);
            _error = Dim("", new Thickness(2, 6, 0, 0));
            _error.SetResourceReference(TextBlock.ForegroundProperty, "vx.Secondary");
            _error.Visibility = Visibility.Collapsed;
            stack.Children.Add(_error);

            // an address on the clipboard: filled in, ready to play
            try
            {
                string clip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
                _box.Text = Media.IsAddress(clip) ? clip : s.RecentStreams.FirstOrDefault() ?? "";
            }
            catch { }
            _box.SelectAll();

            if (s.RecentStreams.Count > 0)
            {
                stack.Children.Add(new TextBlock { Text = "Recent", FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 16, 0, 6) });
                var list = new StackPanel();
                foreach (string addr in s.RecentStreams.Take(8))
                {
                    string a = addr;
                    var row = new Button { Content = a, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 30, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 0, 2), ToolTip = "Play this one" };
                    row.SetResourceReference(StyleProperty, "vx.CommandButton");
                    row.HorizontalAlignment = HorizontalAlignment.Stretch;
                    row.Click += (o, e) => { _result = a; Close(); };
                    list.Children.Add(row);
                }
                stack.Children.Add(list);
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = new Button { Content = "Cancel", MinWidth = 90 };
            cancel.SetResourceReference(StyleProperty, "vx.TextButton");
            cancel.Click += (o, e) => Close();
            var play = new Button { Content = "Play", MinWidth = 90, Margin = new Thickness(0), FontWeight = FontWeights.SemiBold };
            play.SetResourceReference(StyleProperty, "vx.TextButton");
            play.SetResourceReference(BackgroundProperty, "vx.Accent");
            play.SetResourceReference(ForegroundProperty, "vx.OnAccent");
            play.Click += (o, e) => Accept();
            buttons.Children.Add(cancel);
            buttons.Children.Add(play);
            stack.Children.Add(buttons);
            Content = root;

            Loaded += (o, e) => { _box.Focus(); Keyboard.Focus(_box); };
            PreviewKeyDown += (o, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; Close(); }
                else if (e.Key == Key.Enter) { e.Handled = true; Accept(); }
            };
        }

        static void AutomationProperties_SetName(DependencyObject d, string name) { System.Windows.Automation.AutomationProperties.SetName(d, name); }

        void Accept()
        {
            string a = (_box.Text ?? "").Trim();
            if (!Media.IsAddress(a))
            {
                _error.Text = a.Length == 0 ? "Type or paste the stream's address." : "That doesn't look like a network address - it should start with rtsp://, rtmp://, http:// …";
                _error.Visibility = Visibility.Visible;
                _box.Focus();
                return;
            }
            _result = a;
            Close();
        }

        static TextBlock Dim(string text, Thickness margin)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
            t.SetResourceReference(TextBlock.ForegroundProperty, "vx.FgDim");
            return t;
        }
    }
}
