using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CurveThumb
{
    // Renders STL into a fixed-size depth buffer. Keeping only pixels in memory
    // prevents large ZBrush exports from allocating millions of triangle objects.
    internal static class StlRasterizer
    {
        private const int BatchTriangles = 4096;
        // Dense ZBrush parts can exceed a gigabyte. Sampled faces make otherwise
        // continuous surfaces porous, so rasterize every face through 30M triangles.
        private const uint FullSurfaceLimit = 30000000;
        private const int BoundsBlocks = 1024;
        private const int PreviewBlocks = 4096;
        private const int PreviewBlockTriangles = 512;

        public static Bitmap Render(IStream stream, uint count, int size)
        {
            if (count == 0) throw new InvalidDataException("Empty STL");
            bool approximate = count > FullSurfaceLimit;
            Bounds bounds = new Bounds();
            Visit(stream, count, approximate, BoundsBlocks, delegate(Tri tri) { bounds.Add(tri); });
            if (!bounds.Valid) throw new InvalidDataException("STL has no finite geometry");
            int resolution = Math.Min(512, Math.Max(128, size * 2));
            Canvas canvas = new Canvas(resolution, bounds);
            Visit(stream, count, approximate, PreviewBlocks, delegate(Tri tri) { canvas.Draw(tri); });
            if (approximate) canvas.FillSmallGaps(5);
            return canvas.Finish(size);
        }

        private static void Visit(IStream stream, uint count, bool approximate, int previewBlocks, Action<Tri> visitor)
        {
            IntPtr pointer = Marshal.AllocHGlobal(4);
            byte[] block = new byte[BatchTriangles * 50];
            try
            {
                if (!approximate)
                {
                    stream.Seek(84, 0, IntPtr.Zero);
                    for (long baseIndex = 0; baseIndex < count; baseIndex += BatchTriangles)
                    {
                        int n = (int)Math.Min(BatchTriangles, count - baseIndex);
                        if (ReadFully(stream, block, n * 50, pointer) != n * 50) throw new EndOfStreamException();
                        for (int i = 0; i < n; i++) visitor(ReadTriangle(block, i * 50));
                    }
                }
                else
                {
                    long blockCount = Math.Min(previewBlocks, (long)Math.Ceiling(count / (double)PreviewBlockTriangles));
                    for (long b = 0; b < blockCount; b++)
                    {
                        long index = blockCount == 1 ? 0 : b * (count - PreviewBlockTriangles) / (blockCount - 1);
                        int n = (int)Math.Min(PreviewBlockTriangles, count - index);
                        stream.Seek(84 + index * 50, 0, IntPtr.Zero);
                        if (ReadFully(stream, block, n * 50, pointer) != n * 50) throw new EndOfStreamException();
                        for (int i = 0; i < n; i++) visitor(ReadTriangle(block, i * 50));
                    }
                }
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }

        private static int ReadFully(IStream stream, byte[] block, int requested, IntPtr pointer)
        {
            int filled = 0;
            while (filled < requested)
            {
                byte[] target = filled == 0 ? block : new byte[requested - filled];
                Marshal.WriteInt32(pointer, 0);
                stream.Read(target, requested - filled, pointer);
                int got = Marshal.ReadInt32(pointer);
                if (got <= 0) break;
                if (!Object.ReferenceEquals(target, block)) Buffer.BlockCopy(target, 0, block, filled, got);
                filled += got;
            }
            return filled;
        }

        private static Tri ReadTriangle(byte[] bytes, int offset)
        {
            return new Tri(ReadVertex(bytes, offset + 12), ReadVertex(bytes, offset + 24), ReadVertex(bytes, offset + 36));
        }

        private static V3 ReadVertex(byte[] bytes, int offset)
        {
            return new V3(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4), BitConverter.ToSingle(bytes, offset + 8));
        }

        private sealed class Bounds
        {
            internal double MinX = double.PositiveInfinity, MinY = double.PositiveInfinity, MinZ = double.PositiveInfinity;
            internal double MaxX = double.NegativeInfinity, MaxY = double.NegativeInfinity, MaxZ = double.NegativeInfinity;
            internal bool Valid { get { return MinX <= MaxX && MinY <= MaxY && MinZ <= MaxZ; } }
            internal void Add(Tri triangle) { Add(triangle.A); Add(triangle.B); Add(triangle.C); }
            private void Add(V3 v)
            {
                if (Double.IsNaN(v.X) || Double.IsNaN(v.Y) || Double.IsNaN(v.Z) ||
                    Double.IsInfinity(v.X) || Double.IsInfinity(v.Y) || Double.IsInfinity(v.Z)) return;
                MinX = Math.Min(MinX, v.X); MinY = Math.Min(MinY, v.Y); MinZ = Math.Min(MinZ, v.Z);
                MaxX = Math.Max(MaxX, v.X); MaxY = Math.Max(MaxY, v.Y); MaxZ = Math.Max(MaxZ, v.Z);
            }
        }

        private sealed class Canvas
        {
            private readonly int size;
            private readonly double centerX, centerY, centerZ, scale;
            private readonly int[] color;
            private readonly double[] depth;
            private readonly int background = Color.FromArgb(255, 36, 40, 48).ToArgb();

            internal Canvas(int size, Bounds bounds)
            {
                this.size = size;
                centerX = (bounds.MinX + bounds.MaxX) / 2;
                centerY = (bounds.MinY + bounds.MaxY) / 2;
                centerZ = (bounds.MinZ + bounds.MaxZ) / 2;
                double span = Math.Max(bounds.MaxX - bounds.MinX,
                    Math.Max(bounds.MaxY - bounds.MinY, bounds.MaxZ - bounds.MinZ));
                scale = size * .68 / Math.Max(span, 1e-12);
                color = new int[size * size];
                depth = new double[size * size];
                for (int i = 0; i < color.Length; i++) { color[i] = background; depth[i] = Double.NegativeInfinity; }
                for (int y = (int)(size * .75); y < (int)(size * .88); y++)
                    for (int x = (int)(size * .18); x < (int)(size * .82); x++)
                    {
                        double dx = (x - size * .5) / (size * .32);
                        double dy = (y - size * .815) / (size * .065);
                        if (dx * dx + dy * dy <= 1) color[y * size + x] = Color.FromArgb(255, 29, 32, 39).ToArgb();
                    }
            }

            private Point Project(V3 v)
            {
                double x = v.X - centerX, y = v.Y - centerY, z = v.Z - centerZ;
                return new Point(size * .5 + (x - y) * .70710678 * scale,
                    size * .48 - ((x + y) * .408248 + z * .816497) * scale,
                    (-x - y + z) * .577350269);
            }

            internal void Draw(Tri tri)
            {
                Point a = Project(tri.A), b = Project(tri.B), c = Project(tri.C);
                if (!a.Valid || !b.Valid || !c.Valid) return;
                V3 normal = V3.Cross(tri.B - tri.A, tri.C - tri.A);
                double length = normal.Length;
                if (length <= 1e-15) return;
                double dot = Math.Abs((-normal.X * .4 - normal.Y * .5 + normal.Z) / (length * 1.187434));
                double shade = .38 + .62 * Math.Min(1, dot);
                int red = Math.Min(255, (int)(155 + 100 * shade));
                int argb = Color.FromArgb(255, red, Math.Min(255, (int)(red * .72)), Math.Min(255, (int)(red * .48))).ToArgb();
                double denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
                if (Math.Abs(denominator) < .35)
                {
                    Plot((int)Math.Round((a.X + b.X + c.X) / 3),
                        (int)Math.Round((a.Y + b.Y + c.Y) / 3),
                        (a.Z + b.Z + c.Z) / 3, argb);
                    return;
                }
                int left = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
                int right = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
                int top = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
                int bottom = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
                for (int y = top; y <= bottom; y++)
                    for (int x = left; x <= right; x++)
                    {
                        double w0 = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / denominator;
                        double w1 = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / denominator;
                        double w2 = 1 - w0 - w1;
                        if (w0 >= -.001 && w1 >= -.001 && w2 >= -.001)
                            Plot(x, y, w0 * a.Z + w1 * b.Z + w2 * c.Z, argb);
                    }
            }

            private void Plot(int x, int y, double z, int argb)
            {
                if (x < 0 || y < 0 || x >= size || y >= size) return;
                int index = y * size + x;
                if (z > depth[index]) { depth[index] = z; color[index] = argb; }
            }

            // Very large models are represented by a bounded number of spatial
            // blocks. Only nearby gaps are filled; widely separated parts stay apart.
            internal void FillSmallGaps(int radius)
            {
                int[] original = (int[])color.Clone();
                double[] originalDepth = (double[])depth.Clone();
                for (int y = radius; y < size - radius; y++)
                    for (int x = radius; x < size - radius; x++)
                    {
                        int index = y * size + x;
                        if (!Double.IsNegativeInfinity(originalDepth[index])) continue;
                        int neighbors = 0, chosen = -1, bestDistance = Int32.MaxValue;
                        for (int dy = -radius; dy <= radius; dy++)
                            for (int dx = -radius; dx <= radius; dx++)
                            {
                                int distance = dx * dx + dy * dy;
                                if (distance > radius * radius) continue;
                                int neighbor = (y + dy) * size + (x + dx);
                                if (Double.IsNegativeInfinity(originalDepth[neighbor])) continue;
                                neighbors++;
                                if (distance < bestDistance) { bestDistance = distance; chosen = neighbor; }
                            }
                        if (neighbors >= 3 && chosen >= 0)
                        {
                            color[index] = original[chosen];
                            depth[index] = originalDepth[chosen];
                        }
                    }
            }

            internal Bitmap Finish(int outputSize)
            {
                Bitmap high = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                BitmapData bits = high.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try { Marshal.Copy(color, 0, bits.Scan0, color.Length); }
                finally { high.UnlockBits(bits); }
                Bitmap result;
                if (outputSize == size) result = high;
                else
                {
                    result = new Bitmap(outputSize, outputSize, PixelFormat.Format32bppArgb);
                    using (Graphics graphics = Graphics.FromImage(result))
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(high, 0, 0, outputSize, outputSize);
                    }
                    high.Dispose();
                }
                using (Graphics graphics = Graphics.FromImage(result))
                using (Font font = new Font("Segoe UI", Math.Max(8, outputSize * .075f), FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    SizeF measure = graphics.MeasureString("STL", font);
                    RectangleF box = new RectangleF(outputSize - measure.Width - outputSize * .11f,
                        outputSize - measure.Height - outputSize * .1f,
                        measure.Width + outputSize * .06f, measure.Height + outputSize * .045f);
                    using (Brush backgroundBrush = new SolidBrush(Color.FromArgb(238, 249, 115, 22)))
                        graphics.FillRoundedRectangle(backgroundBrush, box, outputSize * .025f);
                    using (Brush foreground = new SolidBrush(Color.FromArgb(255, 33, 25, 18)))
                        graphics.DrawString("STL", font, foreground, box.X + outputSize * .03f, box.Y + outputSize * .005f);
                }
                return result;
            }

            private struct Point
            {
                internal readonly double X, Y, Z;
                internal bool Valid { get { return !Double.IsNaN(X) && !Double.IsNaN(Y) && !Double.IsNaN(Z) &&
                    !Double.IsInfinity(X) && !Double.IsInfinity(Y) && !Double.IsInfinity(Z); } }
                internal Point(double x, double y, double z) { X = x; Y = y; Z = z; }
            }
        }
    }
}
