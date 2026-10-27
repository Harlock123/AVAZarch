using AVAZarch.Engine;
using static AVAZarch.Tests.Harness;

namespace AVAZarch.Tests;

/// <summary>The computer pilot, which flies the demonstration: it has to fly, fight and fill up like a player.</summary>
public sealed class PilotTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void It_clears_the_first_wave_without_losing_a_lander(int seed)
    {
        var g = Game(seed);
        g.Go(1);
        g.Pilot.Reset();
        int t = 0;
        for (; t < 60 * 120 && g.Phase == Phase.Fight; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(3, g.World.Lives);
        Assert.True(g.Score >= Sim.WaveOf(1).Sum(k => k.Count) * 100);
        Assert.True(g.World.Land.Infection < 0.1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void It_lives_through_a_long_game_and_keeps_scoring(int seed)
    {
        var g = Game(seed);
        g.Go(1);
        g.Pilot.Reset();
        for (int t = 0; t < 60 * 150 && g.State == GameState.Playing; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.World.Wave >= 3);
        Assert.True(g.Score > 5000);
    }

    [Fact]
    public void Low_on_fuel_it_flies_home_and_lands_on_the_pad_to_fill_up()
    {
        var w = Empty();
        var s = Airborne(w, 5, Land.PadCentre.X + 14, Land.PadCentre.Z - 9);
        s.Fuel = 25;
        var pilot = new Pilot();
        bool filled = false;
        for (int t = 0; t < 60 * 60 && !filled; t++)
        {
            Sim.Step(w, pilot.Decide(w));
            Assert.False(s.Dead);
            filled = s.Landed && w.Land.TileAt(s.Pos.X, s.Pos.Z) == Tile.Pad && s.Fuel >= Field.FuelMax;
        }
        Assert.True(filled);
    }
}
