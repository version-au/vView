using System.Windows;
using System.Windows.Media;

namespace vView.Ui
{
    /// <summary>
    /// vView's icon drawing (AppIcon.Drawer, set in Program): a soft play triangle in the Main Accent holding vShell's three dots (Main
    /// Accent, Secondary Accent, text) - the same recipe as vXplorer's folder (V picked "4 · softer play, dots pointing right"), on a
    /// 64 x 64 grid. The triangle is filled with the theme's background, so the dots read on any surface; the dots make a small play shape
    /// of their own. small: a heavier outline and bigger dots for 16-24 px. `vView.exe --make-icon vview.ico` writes it in vShell's
    /// default colours for the exe's own icon.
    /// </summary>
    public static class Brand
    {
        static readonly Geometry Play = Geometry.Parse("M19.5,12.6 C16.6,11.1 13,13.2 13,16.5 V47.5 C13,50.8 16.6,52.9 19.5,51.4 L50.6,35.4 C53.4,33.9 53.4,30.1 50.6,28.6 Z");

        public static Drawing Draw(Color main, Color secondary, Color text, Color background, bool small)
        {
            double stroke = small ? 5.5 : 4.5, r = small ? 5.2 : 4.4;
            double x1 = small ? 22.5 : 23, x2 = small ? 35.5 : 35, y1 = small ? 24.5 : 25, y2 = small ? 39.5 : 39;
            var g = new DrawingGroup();
            var pen = new Pen(AppIcon.Brush(main), stroke) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            g.Children.Add(new GeometryDrawing(AppIcon.Brush(background), pen, Play));
            g.Children.Add(new GeometryDrawing(AppIcon.Brush(main), null, new EllipseGeometry(new Point(x1, y1), r, r)));
            g.Children.Add(new GeometryDrawing(AppIcon.Brush(secondary), null, new EllipseGeometry(new Point(x1, y2), r, r)));
            g.Children.Add(new GeometryDrawing(AppIcon.Brush(text), null, new EllipseGeometry(new Point(x2, 32), r, r)));
            g.Freeze();
            return g;
        }
    }
}