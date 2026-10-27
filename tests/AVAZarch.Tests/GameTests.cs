using AVAZarch.Engine;
using static AVAZarch.Tests.Harness;

namespace AVAZarch.Tests;

/// <summary>The game round the flying: waves, lives, the land lost, the title, the demonstration and the scores.</summary>
public sealed class GameTests
{
    private static int Steps(double seconds) => (int)Math.Ceiling(seconds * 60) + 2;

    [Fact]
    public void A_game_starts_on_the_pad_with_the_first_wave_called()
    {
        var g = Game();
        g.StartGame();
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(Phase.Ready, g.Phase);
        Assert.Equal("WAVE 1", g.Message);
        Assert.True(g.World.Ship.Landed);
        Assert.Equal(Tile.Pad, g.World.Land.TileAt(g.World.Ship.Pos.X, g.World.Ship.Pos.Z));
        Assert.Empty(g.World.Enemies);
        g.Run(Steps(GameEngine.ReadySeconds));
        Assert.Equal(Phase.Fight, g.Phase);
        Assert.Equal(Sim.WaveOf(1).Sum(k => k.Count), g.World.Enemies.Count);
    }

    [Fact]
    public void Shooting_every_alien_clears_the_wave_with_a_bonus_for_the_land_saved()
    {
        var g = Game();
        g.Go(1);
        var w = g.World;
        var (x, z) = GrassPatch(w, 4);
        for (int dz = -4; dz <= 4; dz++)
            for (int dx = -4; dx <= 4; dx++)
                w.Land.Infect(x + dx, z + dz);
        int clean = 100 - (int)Math.Round(w.Land.Infection * 100);
        foreach (var e in w.Enemies.ToList()) Sim.Destroy(w, e);
        int score = w.Score;
        g.Run(1);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(clean * 50, g.LastBonus);
        Assert.Equal(score + clean * 50, w.Score);
        Assert.Equal("WAVE 1 CLEARED", g.Message);
        int infected = w.Land.InfectedCount;
        g.Run(Steps(GameEngine.ClearSeconds));
        Assert.Equal(Phase.Ready, g.Phase);
        Assert.Equal(2, w.Wave);
        Assert.Equal("WAVE 2", g.Message);
        Assert.True(w.Land.InfectedCount < infected, "the land heals a little");
        g.Run(Steps(GameEngine.ReadySeconds));
        Assert.Equal(Phase.Fight, g.Phase);
        Assert.Contains(w.Enemies, e => e.Kind == Foe.Drone);
    }

    [Fact]
    public void A_lost_lander_costs_a_life_and_the_next_goes_up_from_the_pad()
    {
        var g = Game();
        g.Go(1);
        var w = g.World;
        Airborne(w, 6, 12.5, 12.5);
        w.Ship.Fuel = 10;
        Sim.Crash(w);
        g.Run(1);
        Assert.Equal(Phase.Down, g.Phase);
        Assert.Equal(3, w.Lives);
        g.Run(Steps(GameEngine.DownSeconds));
        Assert.Equal(Phase.Fight, g.Phase);
        Assert.Equal(2, w.Lives);
        var s = w.Ship;
        Assert.False(s.Dead);
        Assert.True(s.Landed);
        Assert.Equal(Tile.Pad, w.Land.TileAt(s.Pos.X, s.Pos.Z));
        Assert.Equal(Field.FuelMax, s.Fuel);
        Assert.True(s.Safe > 0, "untouchable for a moment");
    }

    [Fact]
    public void The_last_lander_lost_ends_the_game_and_a_good_score_gets_initials()
    {
        var g = Game();
        g.Go(1);
        var w = g.World;
        w.Lives = 1;
        w.Score = 50000;
        Sim.Crash(w);
        g.Run(Steps(GameEngine.DownSeconds));
        Assert.Equal(GameState.GameOver, g.State);
        g.Run(Steps(4));
        Assert.Equal(GameState.EnterInitials, g.State);
        var entry = g.Initials!;
        foreach (char c in "LAN") entry.Type(c);
        g.Run(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(0, g.NewEntryIndex);
        var again = HighScoreTable.Load(g.HighScores.Folder);
        Assert.Equal(("LAN", 50000, 1), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Wave));
        g.Run(Steps(GameEngine.HighScoreSeconds));
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void A_poor_score_goes_straight_to_the_table()
    {
        var g = Game();
        g.Go(1);
        g.World.Lives = 1;
        Sim.Crash(g.World);
        g.Run(Steps(GameEngine.DownSeconds) + Steps(4));
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(-1, g.NewEntryIndex);
    }

    [Fact]
    public void The_virus_taking_most_of_the_land_loses_the_game()
    {
        var g = Game();
        g.Go(1);
        var w = g.World;
        for (int z = 0; z < Land.Size && w.Land.Infection < Field.Overrun; z++)
            for (int x = 0; x < Land.Size; x++)
                w.Land.Infect(x, z);
        g.Run(1);
        Assert.Equal(Phase.Lost, g.Phase);
        Assert.Equal("THE LAND IS OVERRUN", g.Message);
        g.Run(Steps(GameEngine.LostSeconds));
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Every_ten_thousand_points_brings_another_lander()
    {
        var w = Empty();
        Sim.AddScore(w, 9900);
        Assert.Equal(3, w.Lives);
        Sim.AddScore(w, 200);
        Assert.Equal(4, w.Lives);
        Assert.Contains(Sfx.ExtraLife, w.Heard);
        Sim.AddScore(w, 10000);
        Assert.Equal(5, w.Lives);
    }

    [Fact]
    public void Pausing_stops_the_world()
    {
        var g = Game();
        g.Go(1);
        g.Run(10);
        g.Pause();
        Assert.Equal(GameState.Paused, g.State);
        double clock = g.World.Clock;
        g.Run(100);
        Assert.Equal(clock, g.World.Clock);
        g.Resume();
        g.Run(1);
        Assert.True(g.World.Clock > clock);
    }

    [Fact]
    public void Left_at_the_title_it_plays_a_demonstration_then_shows_the_scores()
    {
        var g = Game();
        Assert.Equal(GameState.Title, g.State);
        g.Idle(Steps(GameEngine.TitleIdleSeconds));
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(Steps(GameEngine.AttractSeconds));
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle(Steps(GameEngine.HighScoreSeconds));
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void Enter_starts_a_game_from_the_title_or_the_demonstration()
    {
        var g = Game();
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        var h = Game();
        h.Idle(Steps(GameEngine.TitleIdleSeconds) + 60);
        Assert.Equal(GameState.Attract, h.State);
        h.Tick(default, start: true);
        Assert.Equal(GameState.Playing, h.State);
        Assert.Equal(0, h.Score);
    }

    [Fact]
    public void The_demonstration_is_silent_and_the_game_is_not()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()));
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Idle(Steps(GameEngine.TitleIdleSeconds) + 600);
        Assert.Equal(GameState.Attract, g.State);
        Assert.DoesNotContain(Sfx.Shoot, heard);
        g.Tick(default, start: true);
        g.Run(Steps(GameEngine.ReadySeconds), new Controls(0, 0, true, true));
        Assert.Contains(Sfx.Shoot, heard);
        Assert.Contains(Sfx.ThrustOn, heard);
    }

    [Fact]
    public void Every_sound_has_its_file()
    {
        foreach (var s in Enum.GetValues<Sfx>())
        {
            if (SoundManager.FileName(s) is not { } name) continue;
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Sounds", name)), name);
        }
    }

    [Fact]
    public void The_high_score_table_starts_with_scores_to_beat_and_keeps_new_ones()
    {
        var folder = TempFolder();
        var t = HighScoreTable.Load(folder);
        Assert.Equal(HighScoreTable.Size, t.Entries.Count);
        Assert.True(t.TopScore is > 10000 and < 100000);
        Assert.Equal(2, t.Insert("ZAR", t.Entries[2].Score + 1, 4));
        t.Save();
        Assert.Equal("ZAR", HighScoreTable.Load(folder).Entries[2].Initials);
    }
}
