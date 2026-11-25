using System;
using System.Collections.Generic;
using Bitmap = System.Drawing.Bitmap;
using ColorConverter = System.Windows.Media.ColorConverter;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace QuickDrawCapture
{
    public partial class OverlayWindow : Window
    {
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        static readonly string[] ColorHex = { "#FF3030", "#168BFF", "#16B96B", "#FFCC00", "#FFFFFF", "#111111" };
        static readonly double[] Widths = { 3, 6, 12 };

        readonly Bitmap _bmp;
        readonly System.Drawing.Rectangle _bounds;
        readonly List<Border> _colorButtons = new List<Border>();
        readonly List<Border> _sizeButtons = new List<Border>();
        int _color = 0, _size = 1;
        bool _done;

        public OverlayWindow(Bitmap bmp, System.Drawing.Rectangle bounds)
        {
            InitializeComponent();
            _bmp = bmp;
            _bounds = bounds;

            // 1 bitmap pixel = 1 physical pixel; no DPI rescaling of the picture
            var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                var wb = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, PixelFormats.Pbgra32, null,
                    data.Scan0, data.Stride * bmp.Height, data.Stride);
                wb.Freeze();
                Shot.Source = wb;
            }
            finally { bmp.UnlockBits(data); }

            BuildToolbar();
            ApplyPen();

            SourceInitialized += (s, e) =>
            {
                // position in physical pixels (works with negative coords and mixed DPI)
                var h = new WindowInteropHelper(this).Handle;
                SetWindowPos(h, new IntPtr(-1), _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, 0x0040);
            };
            PreviewKeyDown += OnKey;

            // toolbar: horizontally centred, on the line between middle and lower third
            ContentRendered += (s, e) =>
            {
                Toolbar.UpdateLayout();
                double x = (ActualWidth - Toolbar.ActualWidth) / 2;
                double y = ActualHeight * 2 / 3 - Toolbar.ActualHeight / 2;
                Toolbar.Margin = new Thickness(x, y, 0, 0);
            };
        }

        void BuildToolbar()
        {
            for (int i = 0; i < ColorHex.Length; i++)
            {
                int idx = i;
                var b = new Border
                {
                    Width = 24, Height = 24, Margin = new Thickness(3), CornerRadius = new CornerRadius(12),
                    Background = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(ColorHex[i])),
                    BorderThickness = new Thickness(3), Cursor = Cursors.Hand
                };
                b.MouseLeftButtonDown += (s, e) => { _color = idx; EraserBtn.IsChecked = false; ApplyPen(); e.Handled = true; };
                _colorButtons.Add(b); Colors.Children.Add(b);
            }
            for (int i = 0; i < Widths.Length; i++)
            {
                int idx = i;
                var b = new Border
                {
                    Width = 30, Height = 30, Margin = new Thickness(2), CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent
                };
                b.Child = new Ellipse
                {
                    Width = 4 + Widths[i], Height = 4 + Widths[i], Fill = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                b.MouseLeftButtonDown += (s, e) => { _size = idx; ApplyPen(); e.Handled = true; };
                _sizeButtons.Add(b); Sizes.Children.Add(b);
            }
        }

        void ApplyPen()
        {
            bool erase = EraserBtn.IsChecked == true;
            Ink.EditingMode = erase ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.Ink;
            var col = (System.Windows.Media.Color)ColorConverter.ConvertFromString(ColorHex[_color]);
            Ink.DefaultDrawingAttributes = new DrawingAttributes
            {
                Color = col, Width = Widths[_size], Height = Widths[_size],
                FitToCurve = true, IgnorePressure = true, StylusTip = StylusTip.Ellipse
            };
            // active-tool indication: white ring on selected colour / size
            for (int i = 0; i < _colorButtons.Count; i++)
                _colorButtons[i].BorderBrush = (!erase && i == _color) ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
            for (int i = 0; i < _sizeButtons.Count; i++)
                _sizeButtons[i].BorderBrush = i == _size ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
        }

        void OnKey(object s, KeyEventArgs e)
        {
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (App.Settings.IsConfirm(k))
            { CopyAndClose(); e.Handled = true; }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            { Undo(); e.Handled = true; }
        }

        void Toolbar_Drag(object s, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Border || e.OriginalSource is StackPanel || e.OriginalSource is Rectangle)
            {
                if (e.OriginalSource is Border b && (_colorButtons.Contains(b) || _sizeButtons.Contains(b))) return;
                var start = e.GetPosition(this);
                var m = Toolbar.Margin;
                Toolbar.CaptureMouse();
                MouseEventHandler move = null;
                MouseButtonEventHandler up = null;
                move = (a, ev) =>
                {
                    var p = ev.GetPosition(this);
                    Toolbar.Margin = new Thickness(m.Left + p.X - start.X, m.Top + p.Y - start.Y, 0, 0);
                };
                up = (a, ev) => { Toolbar.ReleaseMouseCapture(); Toolbar.MouseMove -= move; Toolbar.MouseLeftButtonUp -= up; };
                Toolbar.MouseMove += move; Toolbar.MouseLeftButtonUp += up;
            }
        }

        void Eraser_Click(object s, RoutedEventArgs e) => ApplyPen();
        void Undo_Click(object s, RoutedEventArgs e) => Undo();
        void Copy_Click(object s, RoutedEventArgs e) => CopyAndClose();
        void Cancel_Click(object s, RoutedEventArgs e) => Finish();

        void Undo()
        {
            if (Ink.Strokes.Count > 0) Ink.Strokes.RemoveAt(Ink.Strokes.Count - 1);
        }

        void CopyAndClose()
        {
            if (_done) return;
            try
            {
                Toolbar.Visibility = Visibility.Hidden;
                Canvas.UpdateLayout();
                double w = Canvas.ActualWidth, h = Canvas.ActualHeight;
                // render at exact physical size of the captured monitor
                double dpiX = 96.0 * _bmp.Width / w, dpiY = 96.0 * _bmp.Height / h;
                var rtb = new RenderTargetBitmap(_bmp.Width, _bmp.Height, dpiX, dpiY, PixelFormats.Pbgra32);
                rtb.Render(Canvas);
                rtb.Freeze();
                BitmapSource final = rtb;
                var crop = ComputeCrop(_bmp.Width / w);
                if (crop.HasValue)
                {
                    var cb = new CroppedBitmap(rtb, crop.Value);
                    cb.Freeze();
                    final = cb;
                }
                System.Windows.Clipboard.SetDataObject(new System.Windows.DataObject(System.Windows.DataFormats.Bitmap, final), true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Copy failed: " + ex.Message);
            }
            Finish();
        }

        // Smallest circle around all drawn points, radius + margin, cropped as a square (in physical pixels).
        Int32Rect? ComputeCrop(double scale)
        {
            if (!App.Settings.CropEnabled || Ink.Strokes.Count == 0) return null;

            var pts = new List<System.Windows.Point>();
            double maxHalfWidth = 0;
            foreach (var st in Ink.Strokes)
            {
                maxHalfWidth = Math.Max(maxHalfWidth, st.DrawingAttributes.Width / 2);
                foreach (var sp in st.StylusPoints) pts.Add(new System.Windows.Point(sp.X, sp.Y));
            }
            if (pts.Count == 0) return null;

            MinEnclosingCircle(pts, out System.Windows.Point c, out double r);
            r += maxHalfWidth;
            double R = Math.Max(r * (1 + App.Settings.CropMarginPercent / 100.0), 60); // DIP, not too tiny

            int W = _bmp.Width, H = _bmp.Height;
            double side = 2 * R * scale;
            double x0 = c.X * scale - side / 2, y0 = c.Y * scale - side / 2;
            int ix, iy, iw, ih;
            if (side >= W) { ix = 0; iw = W; } else { ix = (int)Math.Round(Math.Max(0, Math.Min(W - side, x0))); iw = (int)Math.Round(side); }
            if (side >= H) { iy = 0; ih = H; } else { iy = (int)Math.Round(Math.Max(0, Math.Min(H - side, y0))); ih = (int)Math.Round(side); }
            iw = Math.Min(iw, W - ix); ih = Math.Min(ih, H - iy);
            if (iw < 2 || ih < 2) return null;
            return new Int32Rect(ix, iy, iw, ih);
        }

        static void MinEnclosingCircle(List<System.Windows.Point> src, out System.Windows.Point center, out double radius)
        {
            var p = new List<System.Windows.Point>(src);
            var rnd = new Random(1);
            for (int i = p.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = p[i]; p[i] = p[j]; p[j] = t; }

            var cen = p[0]; double rad = 0;
            const double eps = 1e-7;
            Func<System.Windows.Point, bool> inside = q => Dist(q, cen) <= rad + eps;
            for (int i = 1; i < p.Count; i++)
            {
                if (inside(p[i])) continue;
                cen = p[i]; rad = 0;
                for (int j = 0; j < i; j++)
                {
                    if (inside(p[j])) continue;
                    cen = new System.Windows.Point((p[i].X + p[j].X) / 2, (p[i].Y + p[j].Y) / 2);
                    rad = Dist(p[i], p[j]) / 2;
                    for (int k = 0; k < j; k++)
                    {
                        if (inside(p[k])) continue;
                        Circumcircle(p[i], p[j], p[k], out cen, out rad);
                    }
                }
            }
            center = cen; radius = rad;
        }

        static double Dist(System.Windows.Point a, System.Windows.Point b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy);
        }

        static void Circumcircle(System.Windows.Point a, System.Windows.Point b, System.Windows.Point c, out System.Windows.Point center, out double radius)
        {
            double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
            if (Math.Abs(d) < 1e-9)
            {
                // collinear: circle over the farthest pair
                var pairs = new[] { (a, b), (a, c), (b, c) };
                double best = -1; center = a; radius = 0;
                foreach (var (u, v) in pairs)
                {
                    double dd = Dist(u, v);
                    if (dd > best) { best = dd; center = new System.Windows.Point((u.X + v.X) / 2, (u.Y + v.Y) / 2); radius = dd / 2; }
                }
                return;
            }
            double a2 = a.X * a.X + a.Y * a.Y, b2 = b.X * b.X + b.Y * b.Y, c2 = c.X * c.X + c.Y * c.Y;
            double ux = (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d;
            double uy = (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d;
            center = new System.Windows.Point(ux, uy);
            radius = Dist(center, a);
        }

        void Finish()
        {
            if (_done) return;
            _done = true;
            Close();
        }
    }
}
