using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAZarch.Engine;
using AVAZarch.Views;

namespace AVAZarch.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set ZARCH_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avazarch-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("ZARCH_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAZarch-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 120)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            // Kept alive for the pictures: they're of the world, not of the pilot's luck.
            g.World.Lives = Math.Max(g.World.Lives, 3);
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Play(string name, int wave, Func<GameEngine, bool> moment, int seconds = 120, Action<GameEngine>? setup = null)
    {
        var (w, c, g) = Open();
        g.Go(wave);
        setup?.Invoke(g);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static bool Flying(GameEngine g) => !g.World.Ship.Dead && !g.World.Ship.Landed && g.World.Ship.Safe <= 0 && g.Phase == Phase.Fight;

    private static int InView(GameEngine g, Func<Enemy, bool> which)
    {
        var v = View.Of(g.World);
        return g.World.Enemies.Count(e => which(e) && v.InPatch(e.Pos) && Land.Offset(g.World.Ship.Pos, e.Pos).Z > -2);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        for (int i = 0; i < 60 * 12; i++) c.Advance(1 / 60.0);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 61250, 9);
        g.HighScores.Insert("AVA", 38400, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 60 && g.State != GameState.HighScores; i++) g.Idle(1);
        c.Advance(1);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Lift_off()
    {
        var (w, c, g) = Open();
        g.StartGame();
        // Up off the pad, then hovering, leaning gently north, once the callout has gone.
        for (int i = 0; i < 200; i++) { g.Tick(new AVAZarch.Engine.Controls(0, 0.3, i < 45 || i % 5 < 2, false)); if (i % 3 == 0) c.Advance(1 / 20.0); }
        // Caught between the flashes while it's untouchable.
        g.World.Ship.Safe = 0;
        Shot(w, c, "lift-off");
    }

    /// <summary>The aliens left to their work for a while first, the lander safe on its pad.</summary>
    private static void Spread(GameEngine g, double seconds)
    {
        g.World.Ship.Safe = 1e6;
        for (int i = 0; i < seconds * 60; i++) Sim.Step(g.World, default);
        g.World.Ship.Safe = 0;
        g.World.Heard.Clear();
    }

    [AvaloniaFact]
    public void Seeders_and_the_virus() => Play("virus", 3, g => Flying(g) && InView(g, e => e.Kind == Foe.Seeder) >= 1
        && g.World.Missiles.Any(m => m.Kind == AVAZarch.Engine.Shot.Spore && View.Of(g.World).InPatch(m.Pos))
        && Height(g) < 6 && Red(g) > 25, 300, g => Spread(g, 150));

    /// <summary>How many infected squares are in the patch on the screen.</summary>
    private static int Red(GameEngine g)
    {
        var v = View.Of(g.World);
        return g.World.Land.InfectedTiles().Count(t => v.InPatch(new Vec3(t.X + 0.5, 0, t.Z + 0.5)));
    }

    [AvaloniaFact]
    public void A_seeder_shot_down() => Play("blast", 1, g => Flying(g) && Height(g) < 6
        && g.World.Particles.Count(p => p.Kind == Bit.Debris && p.Life > 1.2 && Land.FlatDistance(p.Pos, g.World.Ship.Pos) < 8 && Land.Offset(g.World.Ship.Pos, p.Pos).Z > 0.5) > 20, 300);

    [AvaloniaFact]
    public void Fighters_and_mutants() => Play("mutants", 7, g => Flying(g) && Height(g) > 2 && InView(g, e => e.Kind is Foe.Fighter or Foe.Mutant or Foe.Bomber) >= 3, 240, g => Spread(g, 40));

    private static double Height(GameEngine g) => g.World.Ship.Pos.Y - g.World.Land.Ground(g.World.Ship.Pos.X, g.World.Ship.Pos.Z);
}
