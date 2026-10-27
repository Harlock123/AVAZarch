using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAZarch.Engine;

namespace AVAZarch.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAZarch/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        width = Math.Round(width * 4) / 4;
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA8C8, Cyan = 0xFF6AE8FF, Green = 0xFF5AE05A;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private static uint Mix(uint a, uint b, double f)
    {
        f = Math.Clamp(f, 0, 1);
        uint C(int shift) => (uint)((((a >> shift) & 0xFF) * (1 - f) + ((b >> shift) & 0xFF) * f)) << shift;
        return 0xFF000000 | C(16) | C(8) | C(0);
    }

    /// <summary>A colour lit to a brightness (1 is as it is), its channels rounded so the brushes can be shared.</summary>
    private static uint Lit(uint c, double light)
    {
        light = Math.Max(0, light);
        uint C(int shift) => (uint)Math.Min(255, (int)(((c >> shift) & 0xFF) * light) & ~3) << shift;
        return (c & 0xFF000000) | C(16) | C(8) | C(0);
    }

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private const double W = View.Width, H = View.Height;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        var world = InGame ? g.World : _show;
        DrawWorld(dc, world, View.Of(world));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default:
                DrawHud(dc);
                DrawMessages(dc);
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, W, H));
            Text(dc, "PAUSED", new Point(512, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(512, 300), 100, Red, center: true);
        if (g.State == GameState.Attract)
        {
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, H - 18), 24, Yellow, center: true);
            Text(dc, KeyList, new Point(512, H - 42), 19, Dim, center: true);
        }
        if (g.State == GameState.Playing && Input.MouseSteering)
        {
            // The middle of the screen, and the pointer leaning the lander away from it.
            var (px, py) = Input.Pointer;
            var ring = Pen(Alpha(Cyan, 0.35), 1.5);
            dc.DrawEllipse(null, ring, new Point(W / 2, H / 2), InputHandler.MouseReach, InputHandler.MouseReach);
            dc.DrawEllipse(null, ring, new Point(W / 2, H / 2), InputHandler.MouseDeadZone, InputHandler.MouseDeadZone);
            var pen = Pen(Alpha(Cyan, 0.85), 2);
            dc.DrawLine(Pen(Alpha(Cyan, 0.3), 1.5), new Point(W / 2, H / 2), new Point(px, py));
            dc.DrawLine(pen, new Point(px - 9, py), new Point(px + 9, py));
            dc.DrawLine(pen, new Point(px, py - 9), new Point(px, py + 9));
        }
        if (_toast != null && _toastTime > 0)
        {
            dc.FillRectangle(Brush(0xB0000000), new Rect(W / 2 - 330, H - 92, 660, 40), 8);
            Text(dc, _toast, new Point(W / 2, H - 72), 26, Cyan, center: true);
        }
    }

    private const string KeyList = "MOUSE: POINTER LEANS, LEFT THRUST, RIGHT FIRE   KEYS: ARROWS/WASD LEAN, SPACE THRUST, Z FIRE";

    // ───────────────────────────── around the edges ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        var s = w.Ship;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, W, 74));
        dc.FillRectangle(Brush(0xFF303848), new Rect(0, 74, W, 2));
        Text(dc, "SCORE", new Point(22, 20), 22, Dim);
        Text(dc, g.Score.ToString("D7"), new Point(22, 48), 38, White);
        Text(dc, "HIGH", new Point(212, 20), 22, Dim);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D7"), new Point(212, 48), 38, Yellow);
        Text(dc, "WAVE", new Point(400, 20), 22, Dim);
        Text(dc, w.Wave.ToString(), new Point(400, 48), 38, Cyan);
        // The landers in hand.
        int lives = Math.Max(0, w.Lives - (g.Phase == Phase.Down ? 1 : 0));
        Text(dc, "LANDERS", new Point(480, 20), 22, Dim);
        for (int i = 0; i < Math.Min(7, lives); i++)
        {
            double x = 492 + i * 26, y = 50;
            Poly(dc, 0xFFE0E0F0, null, 0, new Point(x, y - 10), new Point(x + 10, y + 8), new Point(x, y + 4), new Point(x - 10, y + 8));
        }
        // Fuel, flashing when low.
        double fx = 700, fw = 160;
        bool low = s.Fuel <= Field.FuelLow && !s.Dead;
        Text(dc, "FUEL", new Point(fx, 20), 22, low && (int)(_clock * 4) % 2 == 0 ? Red : Dim);
        dc.FillRectangle(Brush(0xFF202430), new Rect(fx, 40, fw, 16), 3);
        uint fuelColour = low ? Red : s.Fuel < 50 ? Yellow : Green;
        dc.FillRectangle(Brush(fuelColour), new Rect(fx, 40, fw * s.Fuel / Field.FuelMax, 16), 3);
        // Height above the ground.
        double height = Math.Max(0, s.Pos.Y - Field.Base - w.Land.Ground(s.Pos.X, s.Pos.Z));
        Text(dc, "HEIGHT", new Point(882, 20), 22, Dim);
        Text(dc, height.ToString("0.0", CultureInfo.InvariantCulture), new Point(882, 48), 34, s.Pos.Y > Field.ThinAir ? Red : White);
        DrawRadar(dc, w);
        string modes = (Input.Mouse ? "" : "MOUSE OFF  ") + (Input.InvertY ? "INVERT Y" : "");
        if (modes.Length > 0 && g.State == GameState.Playing) Text(dc, modes.Trim(), new Point(22, H - 24), 20, Dim);
        if (g.State == GameState.Playing && s.Landed && w.Land.TileAt(s.Pos.X, s.Pos.Z) == Tile.Pad && s.Fuel < Field.FuelMax - 0.5)
            Text(dc, "REFUELLING", new Point(W / 2, 112), 30, Green, center: true);
        else if (g.State == GameState.Playing && low && s.Fuel > 0 && (int)(_clock * 2) % 2 == 0)
            Text(dc, "FUEL LOW - RETURN TO THE PAD", new Point(W / 2, 112), 30, Red, center: true);
        else if (g.State == GameState.Playing && s.Fuel <= 0 && !s.Dead)
            Text(dc, "OUT OF FUEL", new Point(W / 2, 112), 30, Red, center: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = W / 2, cy = 150;
        double size = msg.Length > 16 ? 64 : 84;
        Text(dc, msg, new Point(cx + 3, cy + 3), size, 0xFF601010, center: true, shadow: false);
        Text(dc, msg, new Point(cx, cy), size, (int)(_clock * 6) % 2 == 0 ? 0xFFFFE040 : 0xFFFFF4B0, center: true, shadow: false);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + size * 0.62 + 10), 30, White, center: true);
    }

    // ───────────────────────────── title and scores ─────────────────────────────

    /// <summary>The name in big letters, each standing on its own shadow, lit like the land.</summary>
    private void Logo(DrawingContext dc, double cy, double size)
    {
        const string title = "ZARCH";
        double step = size * 0.62, x0 = W / 2 - (title.Length - 1) * step / 2;
        for (int i = 0; i < title.Length; i++)
        {
            double tx = x0 + i * step, ty = cy + Math.Sin(_clock * 1.8 + i * 0.7) * 4;
            string ch = title[i].ToString();
            for (int k = 6; k >= 1; k--)
                Text(dc, ch, new Point(tx + k * 1.5, ty + k * 2), size, Mix(0xFF0A3A0A, 0xFF1A8A1A, 1 - k / 6.0), center: true, shadow: false);
            Text(dc, ch, new Point(tx, ty), size, i % 2 == 0 ? 0xFF7AF07A : 0xFFFF5A3A, center: true, shadow: false);
        }
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = W / 2;
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, 0, W, 330));
        Text(dc, "A V A", new Point(cx, 40), 40, White, center: true);
        Logo(dc, 126, 150);
        Text(dc, "AFTER DAVID BRABEN'S 1987 GAME FOR THE ACORN ARCHIMEDES", new Point(cx, 218), 24, 0xFFFFF0E0, center: true);
        Text(dc, "FLY THE LANDER OVER THE HILLS. SHOOT DOWN THE SEEDERS BEFORE THEIR RED VIRUS TAKES THE LAND.", new Point(cx, 254), 23, White, center: true);
        Text(dc, "MIND YOUR FUEL: LAND GENTLY ON THE PAD TO FILL UP. ONE BUMP TOO MANY AND THE LANDER IS LOST.", new Point(cx, 280), 23, White, center: true);
        Text(dc, "SEEDER 100   DRONE 150   PEST 200   BOMBER 250   FIGHTER 300   MUTANT 400", new Point(cx, 310), 22, Cyan, center: true);
        dc.FillRectangle(Brush(0xB0000000), new Rect(0, H - 130, W, 130));
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER, SPACE OR CLICK TO START", new Point(cx, H - 104), 42, Yellow, center: true);
        Text(dc, KeyList, new Point(cx, H - 66), 20, 0xFFFFF0E0, center: true);
        Text(dc, "PAD: STICK LEANS, A THRUST, X FIRE   TAB: MOUSE ON/OFF   I: INVERT   P: PAUSE   M: SOUND", new Point(cx, H - 44), 20, 0xFFFFF0E0, center: true);
        Text(dc, "HIGH SCORE " + Engine.HighScores.TopScore.ToString("D7"), new Point(cx, H - 18), 22, Cyan, center: true);
    }

    private void Panel(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, W, H));
        dc.DrawRectangle(Brush(0xD0081008), Pen(0xFF5AE05A, 3), new Rect(W / 2 - 330, 60, 660, 650), 6, 6);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = W / 2;
        Panel(dc);
        Text(dc, "DEFENDERS OF THE LAND", new Point(cx, 112), 56, Yellow, center: true);
        Text(dc, "RANK NAME     SCORE   WAVE", new Point(cx, 178), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,9:D7}   {e.Wave,4}", new Point(cx, 224 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 672), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = W / 2;
        Panel(dc);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString("D7"), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    /// <summary>A rounded rectangle with a radius worked out in doubles.</summary>
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
