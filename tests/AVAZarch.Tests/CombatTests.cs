using AVAZarch.Engine;
using static AVAZarch.Tests.Harness;

namespace AVAZarch.Tests;

/// <summary>The lander's gun, and the aliens: what they drop, how they fight, and what they're worth.</summary>
public sealed class CombatTests
{
    private static readonly Controls Fire = new(0, 0, false, true);

    /// <summary>The lander level in the air, its nose pointing east.</summary>
    private static (World W, Ship S) Level(double height = 5)
    {
        var w = Empty();
        var (x, z) = GrassPatch(w, 3);
        var s = Airborne(w, height, x + 0.5, z + 0.5);
        s.Yaw = 0;
        return (w, s);
    }

    [Fact]
    public void The_gun_fires_along_the_nose_and_keeps_firing_while_held()
    {
        var (w, s) = Level();
        Sim.Step(w, Fire);
        var shot = Assert.Single(w.Missiles);
        Assert.Equal(Shot.Player, shot.Kind);
        Assert.Equal(Field.ShotSpeed, shot.Vel.X, 1);
        Assert.Equal(0, shot.Vel.Z, 6);
        Run(w, 59, Fire);
        int shots = w.Heard.Count(h => h == Sfx.Shoot);
        Assert.InRange(shots, 8, 9);
    }

    [Fact]
    public void Leaning_dips_the_nose_and_the_shots_go_down_with_it()
    {
        var (w, s) = Level(8);
        Run(w, 30, new Controls(0.6, 0, false, false));
        Sim.Step(w, new Controls(0.6, 0, false, true));
        var shot = w.Missiles.Single();
        Assert.True(shot.Vel.Y < -Field.ShotSpeed * 0.4);
        Assert.True(shot.Vel.X > Field.ShotSpeed * 0.6);
    }

    [Fact]
    public void A_shot_brings_down_a_seeder_in_front_for_100()
    {
        var (w, s) = Level();
        var e = Alien(w, Foe.Seeder, s.Pos + new Vec3(5, 0, 0));
        Sim.Step(w, Fire, aliens: false);
        Run(w, 20, default, aliens: false);
        Assert.True(e.Dead);
        Assert.Empty(w.Enemies);
        Assert.Equal(100, w.Score);
        Assert.Equal(1, w.Kills);
        Assert.Contains(Sfx.Blast, w.Heard);
        Assert.True(w.Particles.Count(p => p.Kind == Bit.Debris) > 20);
        Assert.Contains(w.Particles, p => p.Kind == Bit.Smoke);
    }

    [Fact]
    public void A_shot_to_one_side_misses()
    {
        var (w, s) = Level();
        var e = Alien(w, Foe.Seeder, s.Pos + new Vec3(5, 0, 1.5));
        Sim.Step(w, Fire, aliens: false);
        Run(w, 40, default, aliens: false);
        Assert.False(e.Dead);
    }

    [Fact]
    public void Bombers_and_fighters_take_two_shots()
    {
        foreach (var kind in new[] { Foe.Bomber, Foe.Fighter })
        {
            var (w, s) = Level();
            var e = Alien(w, kind, s.Pos + new Vec3(4, 0, 0));
            Sim.Step(w, Fire, aliens: false);
            Run(w, 10, default, aliens: false);
            Assert.False(e.Dead);
            Assert.Contains(Sfx.Hit, w.Heard);
            Run(w, 20, Fire, aliens: false);
            Assert.True(e.Dead);
            Assert.Equal(Enemy.Points(kind), w.Score);
        }
    }

    [Fact]
    public void Shots_reach_across_the_edge_of_the_world()
    {
        var w = Empty();
        var s = Airborne(w, 9, Land.Size - 1.5, 20.5);
        s.Yaw = 0;
        var e = Alien(w, Foe.Seeder, new Vec3(2, s.Pos.Y, 20.5));
        Sim.Step(w, Fire, aliens: false);
        Run(w, 20, default, aliens: false);
        Assert.True(e.Dead);
    }

    [Fact]
    public void The_points_go_up_with_how_dangerous_the_alien_is()
    {
        Assert.Equal([100, 150, 200, 250, 300, 400], Enum.GetValues<Foe>().Select(Enemy.Points));
    }

    [Fact]
    public void A_seeder_drops_spores_that_turn_the_land_below_red()
    {
        var w = Empty();
        var (x, z) = GrassPatch(w, 5);
        var e = Alien(w, Foe.Seeder, new Vec3(x + 0.5, w.Land.Ground(x + 0.5, z + 0.5) + 4, z + 0.5));
        e.DropClock = 0;
        Sim.Step(w, default);
        Assert.Contains(w.Missiles, m => m.Kind == Shot.Spore);
        Assert.Contains(Sfx.Spore, w.Heard);
        Run(w, 60 * 3);
        Assert.True(w.Land.InfectedCount >= 1);
        var (ix, iz) = w.Land.InfectedTiles().First();
        Assert.True(Land.FlatDistance(new Vec3(ix, 0, iz), new Vec3(x, 0, z)) < 5);
    }

    [Fact]
    public void A_tree_on_an_infected_square_dies()
    {
        var w = Empty();
        for (int z = 0; z < Land.Size; z++)
            for (int x = 0; x < Land.Size; x++)
                if (w.Land.HasTree(x, z))
                {
                    w.Land.Infect(x, z);
                    Assert.True(w.Land.Infected(x, z) && w.Land.HasTree(x, z), "it stands, dead and red");
                    return;
                }
        Assert.Fail("no trees");
    }

    [Fact]
    public void A_seeder_left_alone_too_long_mutates()
    {
        var w = Empty();
        var e = Alien(w, Foe.Seeder, new Vec3(10, 6, 10));
        e.Age = 75;
        Sim.Step(w, default);
        Assert.Equal(Foe.Mutant, e.Kind);
        Assert.Contains(Sfx.Mutate, w.Heard);
    }

    [Fact]
    public void A_pest_spreads_the_virus_from_infected_land_to_clean()
    {
        var w = Empty();
        var (x, z) = GrassPatch(w, 5);
        w.Land.Infect(x, z);
        Alien(w, Foe.Pest, new Vec3(x + 0.5, w.Land.Ground(x, z) + 1, z + 0.5));
        Run(w, 60 * 10);
        Assert.True(w.Land.InfectedCount >= 3);
        foreach (var (ix, iz) in w.Land.InfectedTiles())
            Assert.True(Land.FlatDistance(new Vec3(ix, 0, iz), new Vec3(x, 0, z)) < 6, "it spreads out from where it was");
    }

    [Fact]
    public void A_bombers_bomb_infects_a_whole_patch_at_once()
    {
        var w = Empty();
        var (x, z) = GrassPatch(w, 6);
        var e = Alien(w, Foe.Bomber, new Vec3(x + 0.5, w.Land.Ground(x, z) + 8.5, z + 0.5));
        e.DropClock = 0;
        Sim.Step(w, default);
        Assert.Contains(w.Missiles, m => m.Kind == Shot.Bomb);
        e.DropClock = 100;
        Run(w, 60 * 4);
        Assert.True(w.Land.InfectedCount >= 9);
    }

    [Fact]
    public void A_fighter_hunts_the_lander_and_fires_at_it()
    {
        var (w, s) = Level(6);
        var e = Alien(w, Foe.Fighter, s.Pos + new Vec3(-12, 0, -6));
        double d = Land.Offset(e.Pos, s.Pos).Length;
        s.Safe = 100;
        bool fired = false;
        for (int i = 0; i < 60 * 3; i++)
        {
            s.Pos = s.Pos with { Y = w.Land.Ground(s.Pos.X, s.Pos.Z) + 6 };
            s.Vel = Vec3.Zero;
            Sim.Step(w, default);
            fired |= w.Missiles.Any(m => m.Kind == Shot.Enemy);
        }
        Assert.True(Land.Offset(e.Pos, s.Pos).Length < d - 5, "it closes in");
        Assert.True(fired);
    }

    [Fact]
    public void An_alien_shot_destroys_the_lander_unless_it_is_untouchable()
    {
        var (w, s) = Level();
        w.Missiles.Add(new Missile { Kind = Shot.Enemy, Pos = s.Pos + new Vec3(-1, 0, 0), Vel = new Vec3(Field.EnemyShotSpeed, 0, 0), Life = 2 });
        s.Safe = 1;
        Run(w, 20, default, aliens: false);
        Assert.False(s.Dead);
        w.Missiles.Add(new Missile { Kind = Shot.Enemy, Pos = s.Pos + new Vec3(-1, 0, 0), Vel = new Vec3(Field.EnemyShotSpeed, 0, 0), Life = 2 });
        s.Safe = 0;
        Run(w, 20, default, aliens: false);
        Assert.True(s.Dead);
    }

    [Fact]
    public void Ramming_takes_both()
    {
        var (w, s) = Level();
        var e = Alien(w, Foe.Mutant, s.Pos + new Vec3(0.3, 0, 0));
        Sim.Step(w, default);
        Assert.True(s.Dead);
        Assert.True(e.Dead);
        Assert.Equal(400, w.Score);
    }

    [Fact]
    public void Aliens_fly_over_the_land_never_into_it()
    {
        var w = Empty();
        w.Wave = 8;
        Sim.SpawnWave(w);
        w.Ship.Safe = 1e9;
        for (int i = 0; i < 60 * 20; i++)
        {
            Sim.Step(w, default);
            foreach (var e in w.Enemies) Assert.True(e.Pos.Y > w.Land.Ground(e.Pos.X, e.Pos.Z) + 0.4, $"{e.Kind}");
        }
    }

    [Fact]
    public void Each_wave_brings_more_and_new_kinds_join_as_the_waves_go_on()
    {
        int Count(int wave) => Sim.WaveOf(wave).Sum(k => k.Count);
        for (int wave = 1; wave < 12; wave++) Assert.True(Count(wave + 1) >= Count(wave));
        Assert.True(Count(8) > 5 * Count(1));
        Assert.Equal([Foe.Seeder], Sim.WaveOf(1).Select(k => k.Kind));
        Assert.Equal(Enum.GetValues<Foe>(), Sim.WaveOf(6).Select(k => k.Kind));
        Assert.True(Sim.Pace(8) > Sim.Pace(1));
        var w = Empty();
        w.Wave = 3;
        Sim.SpawnWave(w);
        Assert.Equal(Count(3), w.Enemies.Count);
        foreach (var e in w.Enemies)
        {
            Assert.True(Land.FlatDistance(e.Pos, w.Ship.Pos) >= 14, "out of sight of the pad");
        }
    }
}
