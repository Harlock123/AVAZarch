namespace AVAZarch.Engine;

/// <summary>What a square of the land is.</summary>
public enum Tile : byte { Sea, Beach, Grass, Pad }

/// <summary>A point or a direction in the world: x east, y up, z north.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double Flat => Math.Sqrt(X * X + Z * Z);
    public Vec3 Unit => Length < 1e-12 ? new Vec3(0, 1, 0) : this * (1 / Length);
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 Up = new(0, 1, 0);
}

/// <summary>
/// The landscape: a square of <see cref="Size"/>×<see cref="Size"/> tiles, one unit across, that wraps round at
/// every edge. Rolling hills from a sum of waves that fit the square exactly (so the edges meet), the sea where
/// they dip below sea level, and a flat launch pad. Each square of grass can be infected by the virus, and some
/// carry a tree, which dies when its square goes red.
/// </summary>
public sealed class Land
{
    public const int Size = 64;
    public const double SeaLevel = 0;
    /// <summary>The pad's south-west corner and its width in tiles.</summary>
    public const int PadX = 30, PadZ = 30, PadSize = 3;
    public const double PadHeight = 1.0;

    private readonly double[] _height = new double[Size * Size];
    private readonly Tile[] _tiles = new Tile[Size * Size];
    private readonly bool[] _infected = new bool[Size * Size];
    private readonly bool[] _tree = new bool[Size * Size];

    /// <summary>How many squares can be infected (grass and beach), and how many are.</summary>
    public int LandCount { get; }
    public int InfectedCount { get; private set; }

    /// <summary>The share of the land the virus holds, 0..1.</summary>
    public double Infection => LandCount == 0 ? 0 : (double)InfectedCount / LandCount;

    /// <summary>The middle of the pad, where the lander sits at the start.</summary>
    public static Vec3 PadCentre => new(PadX + PadSize / 2.0, PadHeight, PadZ + PadSize / 2.0);

    public Land(int seed = 1)
    {
        var rng = new Random(seed);
        // Waves with whole numbers of cycles across the square, so the land wraps without a seam.
        (int Kx, int Kz, double A, double P)[] waves =
        [
            (1, 2, 1.25, rng.NextDouble() * 6.3), (2, -1, 1.1, rng.NextDouble() * 6.3), (3, 1, 0.75, rng.NextDouble() * 6.3),
            (-2, 3, 0.6, rng.NextDouble() * 6.3), (4, -3, 0.45, rng.NextDouble() * 6.3), (5, 4, 0.3, rng.NextDouble() * 6.3),
            (-6, 5, 0.22, rng.NextDouble() * 6.3), (7, 2, 0.18, rng.NextDouble() * 6.3),
        ];
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                double h = 0.55;
                foreach (var (kx, kz, a, p) in waves) h += a * Math.Sin(2 * Math.PI * (kx * x + kz * z) / Size + p);
                _height[z * Size + x] = h <= 0 ? SeaLevel : h * 1.15;
            }
        // The pad: flat, with the land round it eased up or down to meet it.
        for (int z = PadZ - 3; z <= PadZ + PadSize + 3; z++)
            for (int x = PadX - 3; x <= PadX + PadSize + 3; x++)
            {
                int d = Math.Max(Math.Max(PadX - x, x - (PadX + PadSize)), Math.Max(PadZ - z, z - (PadZ + PadSize)));
                double f = d <= 0 ? 1 : 1 - d / 4.0;
                int i = Index(x, z);
                _height[i] = _height[i] * (1 - f) + PadHeight * f;
            }
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                double h00 = H(x, z), h10 = H(x + 1, z), h01 = H(x, z + 1), h11 = H(x + 1, z + 1);
                double lo = Math.Min(Math.Min(h00, h10), Math.Min(h01, h11)), hi = Math.Max(Math.Max(h00, h10), Math.Max(h01, h11));
                var t = hi <= SeaLevel + 1e-9 ? Tile.Sea : lo <= SeaLevel + 1e-9 ? Tile.Beach : Tile.Grass;
                if (x >= PadX && x < PadX + PadSize && z >= PadZ && z < PadZ + PadSize) t = Tile.Pad;
                _tiles[z * Size + x] = t;
                if (t is Tile.Grass or Tile.Beach) LandCount++;
                if (t == Tile.Grass && rng.NextDouble() < 0.16 && !NearPad(x, z, 2)) _tree[z * Size + x] = true;
            }
    }

    public static int Wrap(int i) => ((i % Size) + Size) % Size;
    public static double Wrap(double v) => ((v % Size) + Size) % Size;

    /// <summary>The shortest way from <paramref name="from"/> to <paramref name="to"/> round the world, -Size/2..Size/2.</summary>
    public static double Delta(double from, double to)
    {
        double d = Wrap(to - from);
        return d >= Size / 2.0 ? d - Size : d;
    }

    /// <summary>The shortest offset from one place to another, wrapping east-west and north-south.</summary>
    public static Vec3 Offset(Vec3 from, Vec3 to) => new(Delta(from.X, to.X), to.Y - from.Y, Delta(from.Z, to.Z));

    public static double FlatDistance(Vec3 a, Vec3 b) => Offset(a, b).Flat;

    private static int Index(int x, int z) => Wrap(z) * Size + Wrap(x);

    /// <summary>The height of a corner of the grid.</summary>
    public double H(int x, int z) => _height[Index(x, z)];

    public Tile TileAt(int x, int z) => _tiles[Index(x, z)];
    public Tile TileAt(double x, double z) => TileAt((int)Math.Floor(x), (int)Math.Floor(z));
    public bool Infected(int x, int z) => _infected[Index(x, z)];
    public bool HasTree(int x, int z) => _tree[Index(x, z)];

    public static bool NearPad(int x, int z, int margin)
    {
        double dx = Delta(PadX + PadSize / 2.0, x + 0.5), dz = Delta(PadZ + PadSize / 2.0, z + 0.5);
        return Math.Abs(dx) < PadSize / 2.0 + margin && Math.Abs(dz) < PadSize / 2.0 + margin;
    }

    /// <summary>The height of the ground (or the sea) at any point, between the corners of its square.</summary>
    public double Ground(double x, double z)
    {
        x = Wrap(x);
        z = Wrap(z);
        int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
        double fx = x - ix, fz = z - iz;
        double a = H(ix, iz) * (1 - fx) + H(ix + 1, iz) * fx, b = H(ix, iz + 1) * (1 - fx) + H(ix + 1, iz + 1) * fx;
        return a * (1 - fz) + b * fz;
    }

    /// <summary>Infects a square of land; returns whether it was clean before. Sea and the pad can't be infected.</summary>
    public bool Infect(int x, int z)
    {
        int i = Index(x, z);
        if (_infected[i] || _tiles[i] is Tile.Sea or Tile.Pad) return false;
        _infected[i] = true;
        InfectedCount++;
        return true;
    }

    /// <summary>Makes a square clean again (the land recovering between waves).</summary>
    public bool Cleanse(int x, int z)
    {
        int i = Index(x, z);
        if (!_infected[i]) return false;
        _infected[i] = false;
        InfectedCount--;
        return true;
    }

    /// <summary>Every infected square.</summary>
    public IEnumerable<(int X, int Z)> InfectedTiles()
    {
        for (int i = 0; i < _infected.Length; i++)
            if (_infected[i]) yield return (i % Size, i / Size);
    }

    /// <summary>
    /// The virus creeping on: each infected square has a small chance each step of reaching a neighbour.
    /// Returns how many squares it took.
    /// </summary>
    public int Spread(Random rng, double chance)
    {
        if (InfectedCount == 0) return 0;
        int taken = 0;
        var list = InfectedTiles().ToList();
        foreach (var (x, z) in list)
        {
            if (rng.NextDouble() >= chance) continue;
            int d = rng.Next(4);
            if (Infect(x + (d == 0 ? 1 : d == 1 ? -1 : 0), z + (d == 2 ? 1 : d == 3 ? -1 : 0))) taken++;
        }
        return taken;
    }

    /// <summary>The surface's slope at a point, as its upward normal.</summary>
    public Vec3 Normal(double x, double z)
    {
        const double e = 0.25;
        double dx = Ground(x + e, z) - Ground(x - e, z), dz = Ground(x, z + e) - Ground(x, z - e);
        return new Vec3(-dx / (2 * e), 1, -dz / (2 * e)).Unit;
    }
}
