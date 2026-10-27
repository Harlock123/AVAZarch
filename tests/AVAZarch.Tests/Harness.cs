using AVAZarch.Engine;

namespace AVAZarch.Tests;

/// <summary>Worlds and games set up for the tests, with anything saved kept in a temporary folder.</summary>
internal static class Harness
{
    public static string TempFolder() => Path.Combine(Path.GetTempPath(), "avazarch-tests-" + Guid.NewGuid().ToString("N"));

    public static GameEngine Game(int seed = 1) => new(HighScoreTable.Load(TempFolder()), seed) { Silent = true };

    /// <summary>A land with no aliens, the lander on its pad.</summary>
    public static World Empty(int seed = 1)
    {
        var w = new World();
        Sim.Start(w, seed);
        return w;
    }

    public static void Run(World w, int steps, Controls c = default, bool aliens = true)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c, aliens);
    }

    /// <summary>The lander hanging in the air at a height over the ground below, still, level and untouchable no more.</summary>
    public static Ship Airborne(World w, double height = 5, double x = Land.PadX + 1.5, double z = Land.PadZ + 1.5)
    {
        var s = w.Ship;
        s.Landed = false;
        s.Safe = 0;
        s.Vel = Vec3.Zero;
        s.Pos = new Vec3(x, w.Land.Ground(x, z) + Field.Base + height, z);
        return s;
    }

    /// <summary>An alien put exactly where it's wanted.</summary>
    public static Enemy Alien(World w, Foe kind, Vec3 at) => Sim.Spawn(w, kind, at);

    /// <summary>A square of plain grass (no pad, no sea), somewhere well away from the pad.</summary>
    public static (int X, int Z) Grass(World w, int fromX = 5, int fromZ = 5)
    {
        for (int z = fromZ; z < fromZ + Land.Size; z++)
            for (int x = fromX; x < fromX + Land.Size; x++)
                if (w.Land.TileAt(x, z) == Tile.Grass && !w.Land.Infected(x, z) && !Land.NearPad(x, z, 6)) return (Land.Wrap(x), Land.Wrap(z));
        throw new InvalidOperationException("no grass");
    }

    /// <summary>The middle of a square patch of grass, <paramref name="r"/> squares each way, well away from the pad.</summary>
    public static (int X, int Z) GrassPatch(World w, int r)
    {
        for (int z = 0; z < Land.Size; z++)
            for (int x = 0; x < Land.Size; x++)
            {
                if (Land.NearPad(x, z, r + 4)) continue;
                bool all = true;
                for (int dz = -r; dz <= r && all; dz++)
                    for (int dx = -r; dx <= r && all; dx++)
                        all = w.Land.TileAt(x + dx, z + dz) == Tile.Grass;
                if (all) return (x, z);
            }
        throw new InvalidOperationException("no grass patch");
    }

    /// <summary>A square of open sea.</summary>
    public static (int X, int Z) Sea(World w)
    {
        for (int z = 0; z < Land.Size; z++)
            for (int x = 0; x < Land.Size; x++)
                if (w.Land.TileAt(x, z) == Tile.Sea && w.Land.TileAt(x + 1, z) == Tile.Sea && w.Land.TileAt(x - 1, z) == Tile.Sea
                    && w.Land.TileAt(x, z + 1) == Tile.Sea && w.Land.TileAt(x, z - 1) == Tile.Sea) return (x, z);
        throw new InvalidOperationException("no sea");
    }
}
