using System.Drawing;
using System.Drawing.Drawing2D;

namespace GHErrorAssistant
{
    /// <summary>Original small-size mark: a conversation containing a traced dependency.</summary>
    internal static class BrandAssets
    {
        internal static Bitmap CreateIcon(int size = 24)
        {
            var image = new Bitmap(size, size);
            using (var g = Graphics.FromImage(image))
            using (var teal = new SolidBrush(Color.FromArgb(19, 116, 124)))
            using (var white = new SolidBrush(Color.White))
            using (var amber = new SolidBrush(Color.FromArgb(255, 192, 67)))
            using (var line = new Pen(Color.White, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var bubble = new GraphicsPath())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.ScaleTransform(size / 24f, size / 24f);
                bubble.AddArc(2, 2, 8, 8, 180, 90);
                bubble.AddArc(14, 2, 8, 8, 270, 90);
                bubble.AddArc(14, 12, 8, 8, 0, 90);
                bubble.AddLine(16, 20, 9, 20);
                bubble.AddLine(9, 20, 4, 23);
                bubble.AddLine(4, 23, 4, 19);
                bubble.AddArc(2, 12, 8, 8, 90, 90);
                bubble.CloseFigure();
                g.FillPath(teal, bubble);
                g.DrawLines(line, new[] { new PointF(7, 14), new PointF(12, 9), new PointF(17, 14) });
                g.FillEllipse(white, 5, 12, 4, 4);
                g.FillEllipse(white, 10, 7, 4, 4);
                g.FillEllipse(amber, 15, 12, 4, 4);
            }
            return image;
        }

        internal static Eto.Drawing.Bitmap CreateHeaderImage()
        {
            using (var image = CreateIcon(96))
            using (var stream = new System.IO.MemoryStream())
            {
                image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                return new Eto.Drawing.Bitmap(stream);
            }
        }
    }
}
