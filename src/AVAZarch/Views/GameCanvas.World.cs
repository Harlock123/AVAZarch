using Avalonia;
using Avalonia.Media;
using AVAZarch.Engine;

namespace AVAZarch.Views;

/// <summary>A solid shape of flat-shaded faces: points (x forward, y up, z across) and faces in a colour each.</summary>
internal sealed record Mesh(Vec3[] Points, (int[] Corners, uint Colour)[] Faces);

public sealed partial class GameCanvas
{
    /// <summary>Where the light comes from: high, behind the camera and to the left.</summary>
    private static readonly Vec3 Sun = new Vec3(-0.45, 1, -0.55).Unit;

    /// <summary>The land's colours: each pair is the chequer's two shades.</summary>
    private const uint Grass1 = 0xFF3CB43C, Grass2 = 0xFF2E9A2E, Virus1 = 0xFFD8301E, Virus2 = 0xFFB42214,
        Sand1 = 0xFFD8C060, Sand2 = 0xFFC4AA4A, Sea1 = 0xFF1E46D2, Sea2 = 0xFF1A3CB4, Pad1 = 0xFFB4B4C0, Pad2 = 0xFF9696A6;

    /// <summary>Things to draw on each row of the patch, with how far off they are.</summary>
    private readonly List<(double Depth, Action Draw)>[] _rows = Enumerable.Range(0, View.RowsBehind + View.RowsAhead + 1).Select(_ => new List<(double, Action)>()).ToArray();

    /// <summary>
    /// The patch of land round the lander, drawn row by row from the back, each row's squares from the sides in,
    /// and after each row whatever stands or flies over it, so nearer hills hide what's behind them.
    /// </summary>
    private void DrawWorld(DrawingContext dc, World w, View v)
    {
        foreach (var r in _rows) r.Clear();
        var land = w.Land;
        int fx = (int)Math.Floor(v.FocusX), fz = (int)Math.Floor(v.FocusZ);
        void Add(Vec3 at, Action<DrawingContext> draw)
        {
            int row = (int)Math.Floor(Land.Delta(fz, at.Z)), col = (int)Math.Floor(Land.Delta(fx, at.X));
            if (row < -View.RowsBehind || row > View.RowsAhead || col < -View.Columns || col > View.Columns) return;
            double depth = View.Project(v.Rel(at)).Depth;
            if (depth < 0.4) return;
            _rows[row + View.RowsBehind].Add((depth, () => draw(dc)));
        }
        // Trees.
        for (int row = -View.RowsBehind; row <= View.RowsAhead; row++)
            for (int col = -View.Columns; col <= View.Columns; col++)
            {
                int x = fx + col, z = fz + row;
                if (!land.HasTree(x, z)) continue;
                var at = new Vec3(Land.Wrap(x) + 0.5, land.Ground(x + 0.5, z + 0.5), Land.Wrap(z) + 0.5);
                bool dead = land.Infected(x, z);
                Add(at, d => DrawMesh(d, v, dead ? DeadTree : Tree, at, new Vec3(1, 0, 0), Vec3.Up, 1));
            }
        // Shadows, straight down on the ground.
        var s = w.Ship;
        if (!s.Dead) Add(s.Pos, d => Shadow(d, v, land, s.Pos, 0.42));
        foreach (var e in w.Enemies) Add(e.Pos, d => Shadow(d, v, land, e.Pos, Enemy.Radius(e.Kind) * 0.8));
        foreach (var e in w.Enemies)
        {
            var (mesh, fwd) = EnemyLook(e, w.Clock);
            Add(e.Pos, d => DrawMesh(d, v, mesh, e.Pos, fwd, Vec3.Up, 1.25));
        }
        if (!s.Dead && !(s.Safe > 0 && !s.Landed && (int)(s.Safe * 8) % 2 == 1))
        {
            // On the ground the lander sits level.
            var up = s.Landed ? Vec3.Up : s.Up;
            var nose = s.Landed ? new Vec3(Math.Cos(s.Yaw), 0, Math.Sin(s.Yaw)) : s.Nose;
            Add(s.Pos, d => DrawMesh(d, v, Lander, s.Pos, nose, up, 1.3));
        }
        foreach (var m in w.Missiles) Add(m.Pos, d => DrawMissile(d, v, m, w.Clock));
        foreach (var b in w.Particles) Add(b.Pos, d => DrawParticle(d, v, b));

        for (int row = View.RowsAhead; row >= -View.RowsBehind; row--)
        {
            for (int k = 0; k <= 2 * View.Columns; k++)
            {
                // From the edges in towards the middle.
                int col = k % 2 == 0 ? -View.Columns + k / 2 : View.Columns - k / 2;
                DrawTile(dc, v, land, fx + col, fz + row, col, row, w.Clock);
            }
            var list = _rows[row + View.RowsBehind];
            list.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            foreach (var (_, draw) in list) draw();
        }
    }

    /// <summary>How far off something is, as a dimming: the far rows fade towards black.</summary>
    private static double Distance(double depth) => 1 - 0.6 * Math.Clamp((depth - 7) / 17, 0, 1);

    private static void DrawTile(DrawingContext dc, View v, Land land, int x, int z, int col, int row, double clock)
    {
        double h00 = land.H(x, z), h10 = land.H(x + 1, z), h01 = land.H(x, z + 1), h11 = land.H(x + 1, z + 1);
        var a = View.Project(v.RelCorner(col, row, h00));
        var b = View.Project(v.RelCorner(col + 1, row, h10));
        var c = View.Project(v.RelCorner(col + 1, row + 1, h11));
        var d = View.Project(v.RelCorner(col, row + 1, h01));
        if (a.Depth < 0.3 || b.Depth < 0.3 || c.Depth < 0.3 || d.Depth < 0.3) return;
        double minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)), maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)), maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        if (maxX < 0 || minX > W || maxY < 0 || minY > H) return;
        bool odd = ((Land.Wrap(x) + Land.Wrap(z)) & 1) == 1;
        var tile = land.TileAt(x, z);
        uint colour;
        double light;
        if (tile == Tile.Sea)
        {
            colour = odd ? Sea2 : Sea1;
            light = 0.92 + 0.1 * Math.Sin(clock * 2.2 + Land.Wrap(x) * 0.9 + Land.Wrap(z) * 0.5);
        }
        else
        {
            bool sick = land.Infected(x, z);
            colour = tile switch
            {
                Tile.Pad => odd ? Pad2 : Pad1,
                _ when sick => odd ? Virus2 : Virus1,
                Tile.Beach => odd ? Sand2 : Sand1,
                _ => odd ? Grass2 : Grass1,
            };
            // Higher ground is a little paler, as in the original's hills.
            if (tile == Tile.Grass && !sick) colour = Mix(colour, 0xFF9AD04A, Math.Clamp((Math.Max(Math.Max(h00, h10), Math.Max(h01, h11)) - 2.2) / 3, 0, 0.45));
            double dhdx = (h10 - h00 + h11 - h01) / 2, dhdz = (h01 - h00 + h11 - h10) / 2;
            var n = new Vec3(-dhdx, 1, -dhdz).Unit;
            light = 0.5 + 0.6 * Math.Max(0, n.Dot(Sun));
        }
        light *= Distance((a.Depth + c.Depth) / 2);
        uint fill = Lit(colour, light);
        Poly(dc, fill, fill, 0.75, new Point(a.X, a.Y), new Point(b.X, b.Y), new Point(c.X, c.Y), new Point(d.X, d.Y));
    }

    /// <summary>A dark patch on the ground under something flying.</summary>
    private static void Shadow(DrawingContext dc, View v, Land land, Vec3 at, double r)
    {
        double ground = land.Ground(at.X, at.Z);
        double height = at.Y - ground;
        if (height > 14) return;
        var pts = new Point[8];
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4, x = at.X + Math.Cos(a) * r, z = at.Z + Math.Sin(a) * r;
            var p = View.Project(v.Rel(new Vec3(x, land.Ground(x, z) + 0.02, z)));
            if (p.Depth < 0.4) return;
            pts[i] = new Point(p.X, p.Y);
        }
        Poly(dc, Alpha(0xFF000000, Math.Clamp(0.55 - height * 0.03, 0.15, 0.55)), null, 0, pts);
    }

    private readonly record struct Face(Point[] Pts, uint Colour, double Depth);
    private readonly List<Face> _faces = [];

    /// <summary>
    /// A mesh in the world: its forward pointing along <paramref name="forward"/>, its up along
    /// <paramref name="up"/>. Each face lit by how squarely it faces the light, back faces first.
    /// </summary>
    private void DrawMesh(DrawingContext dc, View v, Mesh mesh, Vec3 at, Vec3 forward, Vec3 up, double scale)
    {
        var side = forward.Cross(up);
        if (side.Length < 1e-6) side = new Vec3(1, 0, 0).Cross(up);
        side = side.Unit;
        forward = up.Cross(side).Unit;
        var basePos = v.Rel(at);
        var world = new Vec3[mesh.Points.Length];
        var screen = new (double X, double Y, double Depth)[mesh.Points.Length];
        for (int i = 0; i < world.Length; i++)
        {
            var p = mesh.Points[i] * scale;
            world[i] = basePos + forward * p.X + up * p.Y + side * p.Z;
            screen[i] = View.Project(world[i]);
            if (screen[i].Depth < 0.3) return;
        }
        double dim = Distance(screen[0].Depth);
        _faces.Clear();
        foreach (var (corners, colour) in mesh.Faces)
        {
            var a = world[corners[0]];
            var n = (world[corners[1]] - a).Cross(world[corners[2]] - a).Unit;
            double light = (0.45 + 0.6 * Math.Abs(n.Dot(Sun))) * dim;
            var pts = new Point[corners.Length];
            double depth = 0;
            for (int i = 0; i < corners.Length; i++)
            {
                var s = screen[corners[i]];
                pts[i] = new Point(s.X, s.Y);
                depth += s.Depth;
            }
            _faces.Add(new Face(pts, Lit(colour, light), depth / corners.Length));
        }
        _faces.Sort((x, y) => y.Depth.CompareTo(x.Depth));
        foreach (var f in _faces) Poly(dc, f.Colour, f.Colour, 0.5, f.Pts);
    }

    private static void DrawMissile(DrawingContext dc, View v, Missile m, double clock)
    {
        var p = View.Project(v.Rel(m.Pos));
        if (p.Depth < 0.4) return;
        double s = View.Focal / p.Depth, dim = Distance(p.Depth);
        switch (m.Kind)
        {
            case Shot.Player:
            {
                var tail = View.Project(v.Rel(m.Pos - m.Vel * 0.025));
                dc.DrawLine(Pen(Lit(0xFFFFF0A0, dim), Math.Max(2, s * 0.06)), new Point(tail.X, tail.Y), new Point(p.X, p.Y));
                break;
            }
            case Shot.Enemy:
                dc.DrawEllipse(Brush(Alpha(0xFFFF4020, 0.5)), null, new Point(p.X, p.Y), Math.Max(3, s * 0.14), Math.Max(3, s * 0.14));
                dc.DrawEllipse(Brush(0xFFFFE0A0), null, new Point(p.X, p.Y), Math.Max(1.5, s * 0.06), Math.Max(1.5, s * 0.06));
                break;
            case Shot.Spore:
            {
                double r = Math.Max(2, s * (0.07 + 0.02 * Math.Sin(clock * 12)));
                dc.DrawEllipse(Brush(Lit(0xFFFF2A1A, dim)), null, new Point(p.X, p.Y), r, r);
                break;
            }
            case Shot.Bomb:
            {
                double r = Math.Max(3, s * 0.13);
                dc.DrawEllipse(Brush(Lit(0xFF404048, dim)), Pen(Lit(0xFFFF3030, dim), 1.5), new Point(p.X, p.Y), r, r);
                break;
            }
        }
    }

    private static void DrawParticle(DrawingContext dc, View v, Particle b)
    {
        var p = View.Project(v.Rel(b.Pos));
        if (p.Depth < 0.4) return;
        double s = View.Focal / p.Depth, size = Math.Max(1.6, b.Size * s), dim = Distance(p.Depth);
        double fade = Math.Clamp(b.Life / (b.MaxLife * 0.5), 0, 1);
        switch (b.Kind)
        {
            case Bit.Debris:
            {
                var pts = new Point[3];
                for (int i = 0; i < 3; i++)
                {
                    double a = b.Angle + i * 2.1;
                    pts[i] = new Point(p.X + Math.Cos(a) * size, p.Y + Math.Sin(a) * size * 0.8);
                }
                Poly(dc, Lit(b.Colour, dim * (0.6 + 0.4 * Math.Abs(Math.Sin(b.Angle)))), null, 0, pts);
                break;
            }
            case Bit.Smoke:
                dc.DrawEllipse(Brush(Alpha(Lit(b.Colour, dim), 0.55 * fade)), null, new Point(p.X, p.Y), size, size);
                break;
            case Bit.Exhaust:
            {
                // Hot out of the engine, cooling to red as it falls.
                uint c = Mix(b.Colour, 0xFFB02010, 1 - b.Life / 0.9);
                dc.FillRectangle(Brush(Alpha(Lit(c, dim), 0.4 + 0.6 * fade)), new Rect(p.X - size / 2, p.Y - size / 2, size, size));
                break;
            }
            default:
                dc.FillRectangle(Brush(Alpha(Lit(b.Colour, dim), fade)), new Rect(p.X - size / 2, p.Y - size / 2, size, size));
                break;
        }
    }

    // ───────────────────────────── the shapes ─────────────────────────────

    private static Vec3 P(double x, double y, double z) => new(x, y, z);

    /// <summary>The player's lander: a flat arrowhead with a raised spine, white above, blue at the back.</summary>
    private static readonly Mesh Lander = new(
        [P(0.6, 0, 0), P(-0.42, 0, -0.46), P(-0.42, 0, 0.46), P(-0.14, 0.28, 0), P(-0.2, -0.14, 0), P(-0.36, 0.08, 0)],
        [
            ([0, 1, 3], 0xFFF4F4FC), ([0, 3, 2], 0xFFDCDCEC), ([1, 5, 3], 0xFF5A7AFF), ([3, 5, 2], 0xFF4A68F0),
            ([0, 4, 1], 0xFF8890A8), ([0, 2, 4], 0xFF788098), ([1, 4, 5], 0xFF485078), ([5, 4, 2], 0xFF404870),
        ]);

    /// <summary>A ring of points round the up axis.</summary>
    private static Vec3[] Ring(int n, double r, double y, double turn = 0) =>
        Enumerable.Range(0, n).Select(i => P(Math.Cos(i * 2 * Math.PI / n + turn) * r, y, Math.Sin(i * 2 * Math.PI / n + turn) * r)).ToArray();

    /// <summary>A double cone: a ring with a point above and below.</summary>
    private static Mesh Spindle(int n, double r, double top, double bottom, uint upper, uint lower, uint? alt = null)
    {
        var pts = Ring(n, r, 0).Append(P(0, top, 0)).Append(P(0, bottom, 0)).ToArray();
        var faces = new List<(int[], uint)>();
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            faces.Add(([i, j, n], i % 2 == 1 && alt is { } a ? a : upper));
            faces.Add(([j, i, n + 1], lower));
        }
        return new Mesh(pts, faces.ToArray());
    }

    /// <summary>The seeder: a spinning saucer, orange and gold.</summary>
    private static readonly Mesh Seeder = Spindle(6, 0.55, 0.28, -0.16, 0xFFFF9A20, 0xFF8A3A10, 0xFFFFD040);

    /// <summary>The drone: a cube on its point.</summary>
    private static readonly Mesh Drone = new(
        [P(0, 0.42, 0), P(0.32, 0, 0.32), P(0.32, 0, -0.32), P(-0.32, 0, -0.32), P(-0.32, 0, 0.32), P(0, -0.42, 0)],
        [
            ([0, 1, 2], 0xFF40D0FF), ([0, 2, 3], 0xFF2A90C8), ([0, 3, 4], 0xFF40D0FF), ([0, 4, 1], 0xFF2A90C8),
            ([5, 2, 1], 0xFF1A5070), ([5, 3, 2], 0xFF206088), ([5, 4, 3], 0xFF1A5070), ([5, 1, 4], 0xFF206088),
        ]);

    /// <summary>The pest: a little hopping pyramid, sickly green.</summary>
    private static readonly Mesh Pest = new(
        [P(0.34, -0.1, 0), P(-0.2, -0.1, 0.26), P(-0.2, -0.1, -0.26), P(0, 0.28, 0)],
        [([0, 1, 3], 0xFFB0FF30), ([1, 2, 3], 0xFF7AB020), ([2, 0, 3], 0xFF9AE028), ([0, 2, 1], 0xFF405810)]);

    /// <summary>The bomber: a broad flying wing with a red belly and a fin.</summary>
    private static readonly Mesh Bomber = new(
        [P(0.6, 0, 0), P(-0.45, 0, 0.85), P(-0.45, 0, -0.85), P(-0.25, 0.22, 0), P(-0.2, -0.18, 0), P(-0.55, 0.45, 0)],
        [
            ([0, 3, 1], 0xFFA0A0B0), ([0, 2, 3], 0xFF8A8A9A), ([1, 3, 2], 0xFF606070), ([0, 1, 4], 0xFFD03030), ([0, 4, 2], 0xFFB02828),
            ([1, 2, 4], 0xFF702020), ([3, 5, 1], 0xFF505060),
        ]);

    /// <summary>The fighter: a dart with swept wings, yellow and blue.</summary>
    private static readonly Mesh Fighter = new(
        [P(0.7, 0, 0), P(-0.4, 0, 0.5), P(-0.4, 0, -0.5), P(-0.3, 0.16, 0), P(-0.3, -0.1, 0), P(-0.5, 0.36, 0)],
        [
            ([0, 3, 1], 0xFFFFF060), ([0, 2, 3], 0xFFE0D040), ([1, 3, 2], 0xFF3090FF), ([0, 1, 4], 0xFF2060B0), ([0, 4, 2], 0xFF2870C8),
            ([1, 2, 4], 0xFF183860), ([3, 5, 4], 0xFF3090FF),
        ]);

    /// <summary>The mutant: two pyramids through each other, a spiky star.</summary>
    private static readonly Mesh Mutant = new(
        [P(0.5, 0.4, 0), P(-0.5, 0.4, 0), P(0, -0.4, 0.5), P(0, -0.4, -0.5), P(0.5, -0.4, 0), P(-0.5, -0.4, 0), P(0, 0.4, 0.5), P(0, 0.4, -0.5)],
        [
            ([0, 1, 2], 0xFFFF40FF), ([0, 3, 1], 0xFFC020C0), ([0, 2, 3], 0xFFFF80FF), ([1, 3, 2], 0xFF801080),
            ([4, 6, 5], 0xFFFFFFFF), ([4, 5, 7], 0xFFD0A0FF), ([4, 7, 6], 0xFFFFD0FF), ([5, 6, 7], 0xFF9060C0),
        ]);

    /// <summary>A tree: a pyramid of leaves on a trunk. Its square infected, it turns red and dies.</summary>
    private static Mesh TreeOf(uint leaves, uint dark) => new(
        [P(0.32, 0.2, 0.32), P(0.32, 0.2, -0.32), P(-0.32, 0.2, -0.32), P(-0.32, 0.2, 0.32), P(0, 0.95, 0),
         P(0.06, 0, 0), P(-0.06, 0, 0), P(0.06, 0.22, 0), P(-0.06, 0.22, 0)],
        [
            ([5, 6, 8, 7], 0xFF6A4020),
            ([0, 1, 4], leaves), ([1, 2, 4], dark), ([2, 3, 4], leaves), ([3, 0, 4], dark), ([0, 3, 2, 1], dark),
        ]);

    private static readonly Mesh Tree = TreeOf(0xFF2EA02E, 0xFF1E7E1E), DeadTree = TreeOf(0xFFA01810, 0xFF6A1008);

    /// <summary>An alien's shape and the way it faces.</summary>
    private static (Mesh, Vec3) EnemyLook(Enemy e, double clock)
    {
        var heading = new Vec3(Math.Cos(e.Heading), 0, Math.Sin(e.Heading));
        var spin = new Vec3(Math.Cos(e.Spin), 0, Math.Sin(e.Spin));
        return e.Kind switch
        {
            Foe.Seeder => (Seeder, spin),
            Foe.Drone => (Drone, spin),
            Foe.Pest => (Pest, heading),
            Foe.Bomber => (Bomber, heading),
            Foe.Fighter => (Fighter, heading),
            _ => (Mutant, new Vec3(Math.Cos(e.Spin * 1.7 + clock), 0, Math.Sin(e.Spin * 1.7 + clock))),
        };
    }
}
