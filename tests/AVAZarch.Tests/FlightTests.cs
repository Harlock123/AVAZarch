using AVAZarch.Engine;
using static AVAZarch.Tests.Harness;

namespace AVAZarch.Tests;

/// <summary>The lander: gravity, inertia, thrust along its leaning up, fuel, landing and crashing.</summary>
public sealed class FlightTests
{
    private static readonly Controls Thrust = new(0, 0, true, false);

    [Fact]
    public void On_the_pad_it_sits_still_and_fills_up()
    {
        var w = Empty();
        var s = w.Ship;
        Assert.True(s.Landed);
        s.Fuel = 40;
        var at = s.Pos;
        Run(w, 60);
        Assert.True(s.Landed);
        Assert.Equal(at, s.Pos);
        Assert.True(s.Fuel > 40 + Field.FuelRefill * 0.9);
        Run(w, 240);
        Assert.Equal(Field.FuelMax, s.Fuel);
    }

    [Fact]
    public void Thrust_lifts_it_and_burns_fuel()
    {
        var w = Empty();
        var s = w.Ship;
        double y = s.Pos.Y;
        Run(w, 60, Thrust);
        Assert.False(s.Landed);
        Assert.True(s.Pos.Y > y + 1);
        Assert.Equal(Field.FuelMax - Field.FuelBurn, s.Fuel, 1);
        Assert.Contains(w.Particles, p => p.Kind == Bit.Exhaust);
    }

    [Fact]
    public void Without_thrust_it_falls_and_keeps_its_way()
    {
        var w = Empty();
        var s = Airborne(w, 8);
        s.Vel = new Vec3(3, 0, 0);
        double x = s.Pos.X, y = s.Pos.Y;
        Run(w, 30);
        Assert.True(s.Pos.Y < y - 0.3, "gravity");
        Assert.True(s.Vel.Y < -1.5);
        Assert.InRange(Land.Delta(x, s.Pos.X), 1.3, 1.5);
    }

    [Fact]
    public void Leaning_north_and_thrusting_drives_it_north_with_the_nose_dipped()
    {
        var w = Empty();
        var s = Airborne(w, 8);
        double z = s.Pos.Z;
        Run(w, 90, new Controls(0, 1, true, false));
        Assert.True(s.Vel.Z > 3);
        Assert.True(Land.Delta(z, s.Pos.Z) > 2);
        Assert.Equal(Field.MaxTilt, s.Lean, 3);
        Assert.True(s.Up.Z > 0.8);
        Assert.True(s.Nose.Z > 0.4 && s.Nose.Y < -0.8, "the nose points north and down");
        Assert.Equal(Math.PI / 2, s.Yaw, 2);
        // Asked to lean further than it can, it stops at the steepest.
        Run(w, 30, new Controls(3, 3, false, false));
        Assert.Equal(Field.MaxTilt, s.Lean, 6);
    }

    [Fact]
    public void Let_go_it_levels_out()
    {
        var w = Empty();
        var s = Airborne(w, 10);
        Run(w, 30, new Controls(1, 0, false, false));
        Assert.True(s.Lean > 0.5);
        Run(w, 30);
        Assert.True(s.Lean < 0.01);
    }

    [Fact]
    public void A_gentle_touchdown_on_grass_lands_it()
    {
        var w = Empty();
        var (x, z) = GrassPatch(w, 1);
        var s = Airborne(w, 1, x + 0.5, z + 0.5);
        s.Vel = new Vec3(0.5, -2, 0);
        Run(w, 60);
        Assert.False(s.Dead);
        Assert.True(s.Landed);
        Assert.Equal(w.Land.Ground(s.Pos.X, s.Pos.Z) + Field.Base, s.Pos.Y, 3);
        Assert.Equal(Vec3.Zero, s.Vel);
    }

    [Theory]
    [InlineData(0, -8, 0)]
    [InlineData(6, -1, 0)]
    [InlineData(0, -1.5, 0.7)]
    public void Coming_down_too_hard_too_fast_or_leaning_too_far_wrecks_it(double vx, double vy, double tilt)
    {
        var w = Empty();
        var (x, z) = GrassPatch(w, 3);
        var s = Airborne(w, 0.6, x + 0.5, z + 0.5);
        s.Vel = new Vec3(vx, vy, 0);
        s.TiltX = tilt;
        Run(w, 40, new Controls(tilt / Field.MaxTilt, 0, false, false));
        Assert.True(s.Dead);
        Assert.Contains(Sfx.BigBlast, w.Heard);
        Assert.True(w.Particles.Count(p => p.Kind == Bit.Debris) > 30);
    }

    [Fact]
    public void Coming_down_on_the_sea_is_the_end_however_gently()
    {
        var w = Empty();
        var (x, z) = Sea(w);
        var s = Airborne(w, 0.5, x + 0.5, z + 0.5);
        s.Vel = new Vec3(0, -0.5, 0);
        Run(w, 60);
        Assert.True(s.Dead);
        Assert.Contains(w.Particles, p => p.Kind == Bit.Splash);
    }

    [Fact]
    public void Landing_on_grass_does_not_fill_the_tank()
    {
        var w = Empty();
        var (x, z) = GrassPatch(w, 1);
        var s = Airborne(w, 0.1, x + 0.5, z + 0.5);
        s.Fuel = 30;
        Run(w, 120);
        Assert.True(s.Landed);
        Assert.Equal(30, s.Fuel, 6);
    }

    [Fact]
    public void Out_of_fuel_the_engine_is_dead()
    {
        var w = Empty();
        var s = Airborne(w, 8);
        s.Fuel = 0.01;
        double y = s.Pos.Y;
        Run(w, 40, Thrust);
        Assert.Equal(0, s.Fuel);
        Assert.False(s.Thrusting);
        Assert.True(s.Pos.Y < y - 0.5);
    }

    [Fact]
    public void The_air_thins_and_the_engine_gives_out_up_high()
    {
        var w = Empty();
        var s = Airborne(w, 15);
        Run(w, 60 * 12, Thrust);
        Assert.True(s.Pos.Y < Field.Ceiling + 0.3);
        Assert.True(s.Pos.Y > Field.ThinAir);
    }

    [Fact]
    public void Flying_off_one_edge_brings_it_back_at_the_other()
    {
        var w = Empty();
        var s = Airborne(w, 12, Land.Size - 0.5, 0.5);
        s.Vel = new Vec3(5, 0, -5);
        Run(w, 30, Thrust);
        Assert.InRange(s.Pos.X, 0, 3);
        Assert.InRange(s.Pos.Z, Land.Size - 3, Land.Size);
    }
}
