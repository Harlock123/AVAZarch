using AVAZarch.Engine;

namespace AVAZarch.Tests;

/// <summary>
/// How the computer pilot gets on (set ZARCH_PROBE to run it, for tuning: "1" for the pilot from the first wave,
/// or a wave number to start there). ZARCH_SEEDS picks the games.
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_pilot_does()
    {
        string? mode = Environment.GetEnvironmentVariable("ZARCH_PROBE");
        if (mode is null) return;
        int from = int.TryParse(mode, out int n) && n > 1 ? n : 1;
        var seeds = (Environment.GetEnvironmentVariable("ZARCH_SEEDS") ?? "1,2,3").Split(',').Select(int.Parse);
        foreach (int seed in seeds)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "zarch-probe-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go(from);
            g.Pilot.Reset();
            var deaths = new List<string>();
            var waves = new List<string>();
            int t = 0, wave = from, refuels = 0;
            double waveAt = 0;
            bool wasDead = false, wasFilling = false;
            for (; t < 60 * 900 && g.State == GameState.Playing; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                var w = g.World;
                if (w.Ship.Dead && !wasDead) deaths.Add($"{w.Wave}:{g.Pilot.Doing}:{Cause(w)}");
                wasDead = w.Ship.Dead;
                bool filling = g.Pilot.Doing == "filling up";
                if (filling && !wasFilling) refuels++;
                wasFilling = filling;
                if (w.Wave != wave) { waves.Add($"{t / 60.0 - waveAt:0}s/{w.Land.Infection * 100:0}%"); waveAt = t / 60.0; wave = w.Wave; }
            }
            output.WriteLine($"seed {seed}: {g.State}/{g.Phase} at wave {g.World.Wave} after {t / 60}s, score {g.Score}, lives {g.World.Lives}, kills {g.World.Kills}, refuels {refuels}, virus {g.World.Land.Infection * 100:0}%, deaths [{string.Join(" ", deaths)}], waves {string.Join(" ", waves)}");
        }
    }

    private static string Cause(World w)
    {
        var s = w.Ship;
        double ground = w.Land.Ground(s.Pos.X, s.Pos.Z);
        if (s.Pos.Y - Field.Base - ground < 0.3) return w.Land.TileAt(s.Pos.X, s.Pos.Z) == Tile.Sea ? "sea" : $"ground(v{s.Vel.Y:0.0},h{s.Vel.Flat:0.0},l{s.Lean:0.00})";
        var near = w.Enemies.OrderBy(e => Land.Offset(s.Pos, e.Pos).Length).FirstOrDefault();
        return near != null && Land.Offset(s.Pos, near.Pos).Length < 1 ? "rammed" : "shot";
    }
}
