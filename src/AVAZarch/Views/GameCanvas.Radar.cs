using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AVAZarch.Engine;

namespace AVAZarch.Views;

public sealed partial class GameCanvas
{
    private const double RadarSize = 192, RadarX = W - RadarSize - 14, RadarY = H - RadarSize - 14;

    private WriteableBitmap? _radar;
    private readonly int[] _radarPixels = new int[Land.Size * Land.Size];

    /// <summary>
    /// The scanner: the whole land at a glance, centred on the lander and wrapping round like the world. Grass is
    /// green, the sea blue, the pad white, and the infected land red; the aliens are bright dots, and the square
    /// in the middle is the patch on the screen.
    /// </summary>
    private void DrawRadar(DrawingContext dc, World w)
    {
        var land = w.Land;
        var s = w.Ship;
        int cx = (int)Math.Floor(s.Pos.X), cz = (int)Math.Floor(s.Pos.Z);
        const int n = Land.Size, half = n / 2;
        for (int py = 0; py < n; py++)
            for (int px = 0; px < n; px++)
            {
                // North is up the scanner, as it is up the screen.
                int x = cx - half + px, z = cz + half - 1 - py;
                uint c = land.TileAt(x, z) switch
                {
                    Tile.Sea => 0xFF10287A,
                    Tile.Pad => 0xFFE0E0E8,
                    _ when land.Infected(x, z) => 0xFFE02818,
                    Tile.Beach => 0xFF8A7A30,
                    _ => land.HasTree(x, z) ? 0xFF186A18 : 0xFF208A20,
                };
                _radarPixels[py * n + px] = (int)c;
            }
        _radar ??= new WriteableBitmap(new PixelSize(n, n), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = _radar.Lock())
            for (int row = 0; row < n; row++)
                Marshal.Copy(_radarPixels, row * n, fb.Address + row * fb.RowBytes, n);
        var box = new Rect(RadarX, RadarY, RadarSize, RadarSize);
        dc.FillRectangle(Brush(0xC0000000), box.Inflate(5), 4);
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        using (dc.PushOpacity(0.85))
            dc.DrawImage(_radar, new Rect(0, 0, n, n), box);
        double k = RadarSize / n;
        Point At(Vec3 p) => new(RadarX + (Land.Delta(cx, p.X) + half) * k, RadarY + (half - Land.Delta(cz, p.Z)) * k);
        // The patch on screen.
        var a = At(new Vec3(cx - View.Columns, 0, cz + View.RowsAhead + 1));
        dc.DrawRectangle(null, Pen(0x90FFFFFF, 1), new Rect(a.X, a.Y, (2 * View.Columns + 1) * k, (View.RowsAhead + View.RowsBehind + 1) * k));
        bool blink = (int)(_clock * 4) % 2 == 0;
        foreach (var e in w.Enemies)
        {
            var p = At(e.Pos);
            uint c = e.Kind switch { Foe.Seeder => 0xFFFFC040, Foe.Mutant => 0xFFFF40FF, Foe.Fighter => 0xFFFFFF80, Foe.Bomber => 0xFFFF8080, Foe.Pest => 0xFFC0FF40, _ => 0xFF60E0FF };
            dc.FillRectangle(Brush(c), new Rect(p.X - 2.5, p.Y - 2.5, 5, 5));
        }
        var me = At(s.Pos);
        if (!s.Dead || blink)
        {
            dc.DrawLine(Pen(0xFFFFFFFF, 2), new Point(me.X - 6, me.Y), new Point(me.X + 6, me.Y));
            dc.DrawLine(Pen(0xFFFFFFFF, 2), new Point(me.X, me.Y - 6), new Point(me.X, me.Y + 6));
        }
        dc.DrawRectangle(null, Pen(0xFF5A6A80, 2), box.Inflate(5), 4, 4);
        // How much of the land the virus holds, and where the game is lost.
        double inf = land.Infection;
        var bar = new Rect(RadarX, RadarY - 30, RadarSize, 12);
        dc.FillRectangle(Brush(0xC0000000), bar.Inflate(3), 3);
        dc.FillRectangle(Brush(0xFF208A20), bar, 2);
        dc.FillRectangle(Brush(inf > 0.5 && blink ? 0xFFFFA020 : 0xFFE02818), new Rect(bar.X, bar.Y, bar.Width * inf, bar.Height), 2);
        double lost = bar.X + bar.Width * Field.Overrun;
        dc.DrawLine(Pen(0xFFFFFFFF, 2), new Point(lost, bar.Y - 3), new Point(lost, bar.Bottom + 3));
        Text(dc, $"VIRUS {inf * 100:0}%", new Point(RadarX, RadarY - 46), 22, inf > 0.5 ? Red : White);
        Text(dc, $"ALIENS {w.Enemies.Count}", new Point(RadarX + RadarSize, RadarY - 46), 22, White, right: true);
    }
}
