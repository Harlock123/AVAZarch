namespace AVAZarch.Engine;

/// <summary>
/// The controls for one step. <see cref="Tx"/> and <see cref="Tz"/> are how far to tilt the lander east and north,
/// each -1..1 (1 is the steepest tilt); thrust fires the engine along the lander's own up, which the tilt leans.
/// </summary>
public readonly record struct Controls(double Tx, double Tz, bool Thrust, bool Fire);

/// <summary>The kinds of alien.</summary>
public enum Foe
{
    /// <summary>Flies about over the land dropping virus spores.</summary>
    Seeder,
    /// <summary>A slow sentry that drifts about and takes shots at the lander when it comes near.</summary>
    Drone,
    /// <summary>Skims low from infected land to clean land nearby, spreading the virus as it goes.</summary>
    Pest,
    /// <summary>Flies high and drops bombs that infect a whole patch at once.</summary>
    Bomber,
    /// <summary>Hunts the lander and fires at it.</summary>
    Fighter,
    /// <summary>What a seeder left too long becomes: fast, erratic, and out to ram the lander.</summary>
    Mutant,
}

/// <summary>Everything the world's measures hang on.</summary>
public static class Field
{
    public const double Step = 1 / 60.0;
    public const double Gravity = 4.2;
    public const double ThrustAccel = 10.5;
    /// <summary>The steepest the lander leans, as the sine of the angle (about 60°).</summary>
    public const double MaxTilt = 0.87;
    /// <summary>How fast the lander leans towards where it's told, in units of full tilt per second.</summary>
    public const double TiltRate = 3.2;
    public const double Drag = 0.55;
    /// <summary>Above this the engine weakens, and it gives out altogether by <see cref="Ceiling"/>.</summary>
    public const double ThinAir = 16, Ceiling = 22;
    public const double FuelMax = 100, FuelBurn = 3.2, FuelRefill = 35, FuelLow = 22;
    /// <summary>From the lander's middle down to its feet.</summary>
    public const double Base = 0.22;
    /// <summary>The hardest the lander can come down, and the fastest it can slide, and still land.</summary>
    public const double SafeDescent = 3.2, SafeSlide = 3.5, SafeLean = 0.42;
    public const double ShotSpeed = 30, ShotLife = 0.9, ShotGap = 0.12;
    public const double EnemyShotSpeed = 9, EnemyShotLife = 3.2;
    public const int StartLives = 3, ExtraLifeEvery = 10000;
    /// <summary>The virus holding this much of the land ends the game.</summary>
    public const double Overrun = 0.7;
}

/// <summary>The player's lander.</summary>
public sealed class Ship
{
    public Vec3 Pos, Vel;
    /// <summary>The lean: the level part of the lander's up direction (its length is the sine of the lean).</summary>
    public double TiltX, TiltZ;
    /// <summary>Which way the nose points, as an angle from east towards north.</summary>
    public double Yaw = Math.PI / 2;
    public double Fuel = Field.FuelMax;
    public bool Landed = true, Thrusting, Dead;
    public double DeadTime, Safe, FireClock;

    public double Lean => Math.Sqrt(TiltX * TiltX + TiltZ * TiltZ);

    /// <summary>The lander's up direction, leaning with the tilt: the way the engine pushes.</summary>
    public Vec3 Up => new(TiltX, Math.Sqrt(Math.Max(0, 1 - TiltX * TiltX - TiltZ * TiltZ)), TiltZ);

    /// <summary>The way the nose points: level when the lander is, dipping as it leans forward.</summary>
    public Vec3 Nose
    {
        get
        {
            var d = new Vec3(Math.Cos(Yaw), 0, Math.Sin(Yaw));
            var up = Up;
            return (d - up * d.Dot(up)).Unit;
        }
    }
}

public sealed class Enemy
{
    public Foe Kind;
    public Vec3 Pos, Vel;
    public double Heading, Clock, DropClock, FireClock, Age, Spin;
    /// <summary>Its own small difference from the others of its kind, 0..1.</summary>
    public double Quirk;
    public int Hp = 1;
    /// <summary>Where a pest is heading.</summary>
    public (int X, int Z)? Goal;
    public bool Dead;

    public static int Points(Foe k) => k switch
    {
        Foe.Seeder => 100, Foe.Drone => 150, Foe.Pest => 200, Foe.Bomber => 250, Foe.Fighter => 300, _ => 400,
    };

    public static int Toughness(Foe k) => k is Foe.Bomber or Foe.Fighter ? 2 : 1;

    public static double Radius(Foe k) => k switch { Foe.Bomber => 0.75, Foe.Pest => 0.4, Foe.Drone => 0.5, _ => 0.55 };
}

public enum Shot { Player, Enemy, Spore, Bomb }

/// <summary>Anything fired or dropped: the lander's shots, the aliens' shots, spores and bombs.</summary>
public sealed class Missile
{
    public Shot Kind;
    public Vec3 Pos, Vel;
    public double Life;
}

public enum Bit { Debris, Smoke, Spark, Exhaust, Splash }

/// <summary>A particle: a shard of debris, a puff of smoke, a spark, a drop of exhaust or spray.</summary>
public sealed class Particle
{
    public Bit Kind;
    public Vec3 Pos, Vel;
    public double Life, MaxLife, Size, Spin, Angle;
    public uint Colour;
}

/// <summary>The whole state of a game in progress, with nothing about how it's shown.</summary>
public sealed class World
{
    public Land Land = new();
    public Ship Ship = new();
    public readonly List<Enemy> Enemies = [];
    public readonly List<Missile> Missiles = [];
    public readonly List<Particle> Particles = [];
    /// <summary>Sounds asked for this step.</summary>
    public readonly List<Sfx> Heard = [];
    public Random Rng = new(1);
    public int Wave, Score, Lives = Field.StartLives, Kills;
    public int NextExtraLife = Field.ExtraLifeEvery;
    public double Clock, SpreadClock;
    /// <summary>The camera's height, following the lander's lazily.</summary>
    public double CamY = 6;
    /// <summary>True while the land has no aliens left to fight.</summary>
    public bool Cleared => Enemies.Count == 0;
}
