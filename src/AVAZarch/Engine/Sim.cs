namespace AVAZarch.Engine;

/// <summary>
/// The rules, one 60 Hz step at a time: the lander's flight (gravity, inertia, thrust along its leaning up,
/// fuel, landing and crashing), its shots, the aliens and what they drop, the virus creeping over the land,
/// and the particles.
/// </summary>
public static class Sim
{
    /// <summary>A new game on a new land, the lander on its pad.</summary>
    public static void Start(World w, int seed)
    {
        w.Land = new Land(seed);
        w.Rng = new Random(seed);
        w.Ship = new Ship();
        w.Enemies.Clear();
        w.Missiles.Clear();
        w.Particles.Clear();
        w.Heard.Clear();
        w.Wave = 1;
        w.Score = 0;
        w.Kills = 0;
        w.Lives = Field.StartLives;
        w.NextExtraLife = Field.ExtraLifeEvery;
        w.Clock = 0;
        Respawn(w);
        w.CamY = w.Ship.Pos.Y + View.Rise;
    }

    /// <summary>A fresh lander on the pad, fuelled, and untouchable for a moment.</summary>
    public static void Respawn(World w)
    {
        var s = new Ship { Pos = Land.PadCentre with { Y = Land.PadHeight + Field.Base }, Safe = 3 };
        w.Ship = s;
    }

    /// <summary>How many of each alien a wave brings.</summary>
    public static IEnumerable<(Foe Kind, int Count)> WaveOf(int wave)
    {
        yield return (Foe.Seeder, Math.Min(12, 4 + wave));
        if (wave >= 2) yield return (Foe.Drone, Math.Min(4, wave - 1));
        if (wave >= 3) yield return (Foe.Pest, Math.Min(4, wave - 2));
        if (wave >= 4) yield return (Foe.Bomber, Math.Min(3, (wave - 2) / 2));
        if (wave >= 5) yield return (Foe.Fighter, Math.Min(4, wave - 4));
        if (wave >= 6) yield return (Foe.Mutant, Math.Min(3, wave - 5));
    }

    /// <summary>Faster aliens, quicker to fire and drop, wave by wave.</summary>
    public static double Pace(int wave) => Math.Min(1.6, 1 + 0.06 * (wave - 1));

    public static void SpawnWave(World w)
    {
        foreach (var (kind, n) in WaveOf(w.Wave))
            for (int i = 0; i < n; i++) Spawn(w, kind);
    }

    /// <summary>An alien somewhere out of sight, away from the lander and the pad.</summary>
    public static Enemy Spawn(World w, Foe kind, Vec3? at = null)
    {
        Vec3 p;
        if (at is { } a) p = a;
        else
        {
            int tries = 0;
            do
            {
                p = new Vec3(w.Rng.NextDouble() * Land.Size, 0, w.Rng.NextDouble() * Land.Size);
            } while (tries++ < 200 && (Land.FlatDistance(p, w.Ship.Pos) < 20 || Land.FlatDistance(p, Land.PadCentre) < 14));
            p = p with { Y = w.Land.Ground(p.X, p.Z) + Cruise(kind) };
        }
        var e = new Enemy
        {
            Kind = kind, Pos = p, Hp = Enemy.Toughness(kind), Heading = w.Rng.NextDouble() * Math.PI * 2,
            DropClock = 1 + w.Rng.NextDouble() * 3, FireClock = 2 + w.Rng.NextDouble() * 2, Spin = w.Rng.NextDouble() * 6, Quirk = w.Rng.NextDouble(),
            Clock = w.Rng.NextDouble() * 10,
        };
        w.Enemies.Add(e);
        return e;
    }

    /// <summary>How high above the ground each kind likes to fly.</summary>
    public static double Cruise(Foe k) => k switch
    {
        Foe.Seeder => 4.2, Foe.Drone => 3.2, Foe.Pest => 1.1, Foe.Bomber => 8.5, Foe.Fighter => 4, _ => 3,
    };

    /// <summary>The land recovering between waves: a third of the infected squares come clean.</summary>
    public static int Heal(World w)
    {
        var list = w.Land.InfectedTiles().ToList();
        int n = list.Count / 3, done = 0;
        foreach (var (x, z) in list.OrderBy(_ => w.Rng.Next()).Take(n))
            if (w.Land.Cleanse(x, z)) done++;
        return done;
    }

    public static void AddScore(World w, int points)
    {
        w.Score += points;
        while (w.Score >= w.NextExtraLife)
        {
            w.Lives++;
            w.NextExtraLife += Field.ExtraLifeEvery;
            w.Heard.Add(Sfx.ExtraLife);
        }
    }

    /// <summary>One step. With <paramref name="aliens"/> false the aliens hold still (between waves there are none anyway).</summary>
    public static void Step(World w, Controls c, bool aliens = true)
    {
        double dt = Field.Step;
        w.Clock += dt;
        StepShip(w, c, dt);
        if (aliens)
            foreach (var e in w.Enemies.ToList())
                if (!e.Dead) StepEnemy(w, e, dt);
        w.Enemies.RemoveAll(e => e.Dead);
        StepMissiles(w, dt);
        StepParticles(w, dt);
        w.SpreadClock += dt;
        if (w.SpreadClock >= 1)
        {
            w.SpreadClock -= 1;
            w.Land.Spread(w.Rng, 0.008 + 0.002 * w.Wave);
        }
        // The camera rises and falls with the lander, lazily, and keeps high enough to see it over the hills.
        var sp = w.Ship.Pos;
        double camTarget = Math.Max(sp.Y, 1.2) + View.Rise;
        for (double t = 0; t < 0.9; t += 0.15)
        {
            double ground = w.Land.Ground(sp.X, sp.Z - View.Back * (1 - t));
            camTarget = Math.Max(camTarget, (ground + 0.6 - sp.Y * t) / (1 - t));
        }
        w.CamY += (camTarget - w.CamY) * Math.Min(1, 4 * dt);
    }

    // ───────────────────────────── the lander ─────────────────────────────

    private static void StepShip(World w, Controls c, double dt)
    {
        var s = w.Ship;
        if (s.Dead)
        {
            s.DeadTime += dt;
            if (s.Thrusting) { s.Thrusting = false; w.Heard.Add(Sfx.ThrustOff); }
            return;
        }
        s.Safe = Math.Max(0, s.Safe - dt);
        // Lean towards where the controls say, never past the steepest.
        double tx = c.Tx, tz = c.Tz, m = Math.Sqrt(tx * tx + tz * tz);
        if (m > 1) { tx /= m; tz /= m; }
        tx *= Field.MaxTilt;
        tz *= Field.MaxTilt;
        double dx = tx - s.TiltX, dz = tz - s.TiltZ, d = Math.Sqrt(dx * dx + dz * dz), most = Field.TiltRate * Field.MaxTilt * dt;
        if (d > most) { dx *= most / d; dz *= most / d; }
        s.TiltX += dx;
        s.TiltZ += dz;
        if (s.Lean > 0.04)
        {
            // The nose swings round to the way it leans.
            double want = Math.Atan2(s.TiltZ, s.TiltX), turn = Math.IEEERemainder(want - s.Yaw, Math.PI * 2);
            s.Yaw += Math.Clamp(turn, -9 * dt, 9 * dt);
        }
        bool thrust = c.Thrust && s.Fuel > 0;
        if (thrust != s.Thrusting) w.Heard.Add(thrust ? Sfx.ThrustOn : Sfx.ThrustOff);
        s.Thrusting = thrust;
        var up = s.Up;
        // The engine weakens in the thin air up high.
        double power = Math.Clamp((Field.Ceiling - s.Pos.Y) / (Field.Ceiling - Field.ThinAir), 0, 1);
        var acc = new Vec3(0, -Field.Gravity, 0);
        if (thrust)
        {
            acc += up * (Field.ThrustAccel * power);
            s.Fuel = Math.Max(0, s.Fuel - Field.FuelBurn * dt);
            if (s.Fuel <= 0) w.Heard.Add(Sfx.FuelLow);
            Exhaust(w, s);
        }
        double before = s.Fuel;
        if (s.Landed)
        {
            if (acc.Y > 0.5)
            {
                s.Landed = false;
                s.Pos = s.Pos with { Y = s.Pos.Y + 0.02 };
            }
            else
            {
                s.Vel = Vec3.Zero;
                if (w.Land.TileAt(s.Pos.X, s.Pos.Z) == Tile.Pad && s.Fuel < Field.FuelMax)
                {
                    s.Fuel = Math.Min(Field.FuelMax, s.Fuel + Field.FuelRefill * dt);
                    if ((int)(s.Fuel / 10) != (int)(before / 10)) w.Heard.Add(Sfx.Refuel);
                }
                Fire(w, s, c, dt);
                return;
            }
        }
        if (before > Field.FuelLow && s.Fuel <= Field.FuelLow) w.Heard.Add(Sfx.FuelLow);
        s.Vel += acc * dt;
        s.Vel *= 1 - Field.Drag * dt;
        var p = s.Pos + s.Vel * dt;
        s.Pos = new Vec3(Land.Wrap(p.X), p.Y, Land.Wrap(p.Z));
        double ground = w.Land.Ground(s.Pos.X, s.Pos.Z);
        if (s.Pos.Y - Field.Base <= ground)
        {
            var tile = w.Land.TileAt(s.Pos.X, s.Pos.Z);
            if (tile == Tile.Sea)
            {
                Splash(w, s.Pos with { Y = 0 }, 60);
                Crash(w);
                return;
            }
            if (-s.Vel.Y <= Field.SafeDescent && s.Vel.Flat <= Field.SafeSlide && s.Lean <= Field.SafeLean)
            {
                s.Pos = s.Pos with { Y = ground + Field.Base };
                s.Vel = Vec3.Zero;
                s.Landed = true;
                w.Heard.Add(Sfx.Land);
            }
            else
            {
                Crash(w);
                return;
            }
        }
        Fire(w, s, c, dt);
    }

    private static void Fire(World w, Ship s, Controls c, double dt)
    {
        s.FireClock -= dt;
        if (!c.Fire || s.FireClock > 0) return;
        s.FireClock = Field.ShotGap;
        var n = s.Nose;
        w.Missiles.Add(new Missile { Kind = Shot.Player, Pos = s.Pos + n * 0.45, Vel = n * Field.ShotSpeed + s.Vel, Life = Field.ShotLife });
        w.Heard.Add(Sfx.Shoot);
    }

    /// <summary>Drops of burning exhaust out of the engine, falling to the ground.</summary>
    private static void Exhaust(World w, Ship s)
    {
        var r = w.Rng;
        var down = -s.Up;
        for (int i = 0; i < 2; i++)
        {
            var jitter = new Vec3(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5) * 1.6;
            w.Particles.Add(new Particle
            {
                Kind = Bit.Exhaust, Pos = s.Pos + down * 0.2, Vel = s.Vel + down * (5 + r.NextDouble() * 3) + jitter,
                Life = 0.5 + r.NextDouble() * 0.5, MaxLife = 1, Size = 0.07, Colour = r.Next(3) == 0 ? 0xFFFFFFFF : 0xFFFFD040,
            });
        }
    }

    /// <summary>The lander blown to pieces.</summary>
    public static void Crash(World w)
    {
        var s = w.Ship;
        if (s.Dead) return;
        s.Dead = true;
        s.DeadTime = 0;
        Burst(w, s.Pos, 70, [0xFFF0F0F0, 0xFF8AA0FF, 0xFFFFC040, 0xFFB0B0B0], 7, smoke: 18);
        w.Heard.Add(Sfx.BigBlast);
    }

    // ───────────────────────────── the aliens ─────────────────────────────

    private static void StepEnemy(World w, Enemy e, double dt)
    {
        var r = w.Rng;
        var land = w.Land;
        var ship = w.Ship;
        double pace = Pace(w.Wave);
        e.Clock += dt;
        e.Age += dt;
        e.Spin += dt * (e.Kind == Foe.Mutant ? 6 : 2);
        e.FireClock -= dt;
        e.DropClock -= dt;
        var toShip = Land.Offset(e.Pos, ship.Pos);
        bool live = !ship.Dead;
        double speed, cruise = Cruise(e.Kind), wantY;
        switch (e.Kind)
        {
            case Foe.Seeder:
            {
                speed = 2.6 * pace;
                Wander(e, r, dt, 1.2);
                cruise += Math.Sin(e.Clock * 0.7) * 0.8;
                var t = land.TileAt(e.Pos.X, e.Pos.Z);
                int tx = (int)Math.Floor(e.Pos.X), tz = (int)Math.Floor(e.Pos.Z);
                if (e.DropClock <= 0 && t is Tile.Grass or Tile.Beach && !land.Infected(tx, tz))
                {
                    e.DropClock = (2.6 + r.NextDouble() * 2.2) / pace;
                    w.Missiles.Add(new Missile { Kind = Shot.Spore, Pos = e.Pos with { Y = e.Pos.Y - 0.3 }, Vel = e.Vel * 0.3 + new Vec3(0, -1.5, 0), Life = 10 });
                    w.Heard.Add(Sfx.Spore);
                }
                // Left alone long enough, a seeder mutates.
                if (e.Age > 50 + e.Quirk * 20)
                {
                    e.Kind = Foe.Mutant;
                    e.Hp = 1;
                    e.Age = 0;
                    Burst(w, e.Pos, 12, [0xFFFF40FF, 0xFFFFFFFF], 3);
                    w.Heard.Add(Sfx.Mutate);
                }
                break;
            }
            case Foe.Drone:
                speed = 1.7 * pace;
                Wander(e, r, dt, 0.8);
                if (live && toShip.Length < 13 && e.FireClock <= 0)
                {
                    e.FireClock = 3.4 / pace;
                    FireAt(w, e);
                }
                break;
            case Foe.Pest:
            {
                speed = 3.3 * pace;
                if (e.Goal is { } old && (land.Infected(old.X, old.Z) || e.Clock > 20)) e.Goal = null;
                if (e.Goal is null && e.Clock > 1)
                {
                    e.Goal = PestGoal(land, e.Pos, r);
                    e.Clock = 0;
                }
                if (e.Goal is { } g)
                {
                    if (Arrived(e, g))
                    {
                        if (e.DropClock <= 0)
                        {
                            if (land.Infect(g.X, g.Z)) Puff(w, new Vec3(g.X + 0.5, land.Ground(g.X + 0.5, g.Z + 0.5), g.Z + 0.5), 0xFFFF3020);
                            e.DropClock = 2.2 / pace;
                            e.Goal = null;
                        }
                        speed = 0.3;
                    }
                    else
                    {
                        // Nimble: straight at it, slowing as it gets there.
                        double gx = Land.Delta(e.Pos.X, g.X + 0.5), gz = Land.Delta(e.Pos.Z, g.Z + 0.5);
                        e.Heading = Math.Atan2(gz, gx);
                        speed = Math.Min(speed, 0.3 + 3 * Math.Sqrt(gx * gx + gz * gz));
                    }
                }
                else Wander(e, r, dt, 1.5);
                cruise += Math.Abs(Math.Sin(e.Clock * 4)) * 0.6;
                break;
            }
            case Foe.Bomber:
                speed = 2.1 * pace;
                Wander(e, r, dt, 0.5);
                if (e.DropClock <= 0 && land.TileAt(e.Pos.X, e.Pos.Z) is Tile.Grass or Tile.Beach)
                {
                    e.DropClock = (6 + r.NextDouble() * 3) / pace;
                    w.Missiles.Add(new Missile { Kind = Shot.Bomb, Pos = e.Pos with { Y = e.Pos.Y - 0.5 }, Vel = e.Vel * 0.5, Life = 12 });
                    w.Heard.Add(Sfx.BombAway);
                }
                break;
            case Foe.Fighter:
            {
                speed = 5.2 * pace;
                var lead = toShip + ship.Vel * 0.6;
                if (live) e.Heading = Turn(e.Heading, Math.Atan2(lead.Z, lead.X) + Math.Sin(e.Clock * 0.9) * 0.5, 1.7 * dt);
                else Wander(e, r, dt, 0.6);
                if (live) cruise = Math.Max(2, ship.Pos.Y - land.Ground(e.Pos.X, e.Pos.Z) + 1.2);
                if (live && toShip.Length < 14 && e.FireClock <= 0)
                {
                    e.FireClock = 1.7 / pace;
                    FireAt(w, e);
                }
                break;
            }
            default:
            {
                speed = 6.2 * pace;
                if (live)
                {
                    e.Heading = Turn(e.Heading, Math.Atan2(toShip.Z, toShip.X) + Math.Sin(e.Clock * 2.3) * 0.9, 2.6 * dt);
                    cruise = Math.Max(1.2, ship.Pos.Y - land.Ground(e.Pos.X, e.Pos.Z) + Math.Sin(e.Clock * 1.7));
                    if (toShip.Length < 12 && e.FireClock <= 0)
                    {
                        e.FireClock = 2.4 / pace;
                        FireAt(w, e);
                    }
                }
                else Wander(e, r, dt, 2);
                break;
            }
        }
        // Over the land, never into it: it looks ahead for rising ground.
        var dir = new Vec3(Math.Cos(e.Heading), 0, Math.Sin(e.Heading));
        double ground = Math.Max(land.Ground(e.Pos.X, e.Pos.Z), land.Ground(e.Pos.X + dir.X * 1.5, e.Pos.Z + dir.Z * 1.5));
        wantY = Math.Min(Field.ThinAir + 2, ground + Math.Max(0.9, cruise));
        double vy = Math.Clamp((wantY - e.Pos.Y) * 2, -3, 4);
        e.Vel = new Vec3(dir.X * speed, vy, dir.Z * speed);
        var p = e.Pos + e.Vel * dt;
        e.Pos = new Vec3(Land.Wrap(p.X), Math.Max(p.Y, land.Ground(p.X, p.Z) + 0.5), Land.Wrap(p.Z));
        // Ramming the lander takes both.
        if (live && ship.Safe <= 0 && Land.Offset(e.Pos, ship.Pos).Length < Enemy.Radius(e.Kind) + 0.35)
        {
            Destroy(w, e);
            Crash(w);
        }
    }

    private static bool Arrived(Enemy e, (int X, int Z) g) =>
        Math.Abs(Land.Delta(e.Pos.X, g.X + 0.5)) < 0.35 && Math.Abs(Land.Delta(e.Pos.Z, g.Z + 0.5)) < 0.35;

    /// <summary>The nearest clean square next to an infected one, for a pest to spread to.</summary>
    public static (int X, int Z)? PestGoal(Land land, Vec3 from, Random r)
    {
        int cx = (int)Math.Floor(from.X), cz = (int)Math.Floor(from.Z);
        (int, int)? best = null;
        double bestD = double.MaxValue;
        for (int z = cz - 12; z <= cz + 12; z++)
            for (int x = cx - 12; x <= cx + 12; x++)
            {
                if (land.Infected(x, z) || land.TileAt(x, z) is Tile.Sea or Tile.Pad) continue;
                if (!(land.Infected(x + 1, z) || land.Infected(x - 1, z) || land.Infected(x, z + 1) || land.Infected(x, z - 1))) continue;
                double d = (x - cx) * (x - cx) + (z - cz) * (z - cz) + r.NextDouble();
                if (d < bestD) { bestD = d; best = (Land.Wrap(x), Land.Wrap(z)); }
            }
        return best;
    }

    private static void Wander(Enemy e, Random r, double dt, double turn) =>
        e.Heading += (Math.Sin(e.Clock * 0.37 + e.Spin) + (r.NextDouble() - 0.5)) * turn * dt;

    private static double Turn(double from, double to, double most) => from + Math.Clamp(Math.IEEERemainder(to - from, Math.PI * 2), -most, most);

    /// <summary>A shot at where the lander will be when it gets there.</summary>
    private static void FireAt(World w, Enemy e)
    {
        var ship = w.Ship;
        var to = Land.Offset(e.Pos, ship.Pos);
        double t = to.Length / Field.EnemyShotSpeed;
        var aim = (to + ship.Vel * (t * 0.7)).Unit;
        w.Missiles.Add(new Missile { Kind = Shot.Enemy, Pos = e.Pos, Vel = aim * Field.EnemyShotSpeed, Life = Field.EnemyShotLife });
        w.Heard.Add(Sfx.EnemyShot);
    }

    public static void Destroy(World w, Enemy e)
    {
        if (e.Dead) return;
        e.Dead = true;
        w.Kills++;
        AddScore(w, Enemy.Points(e.Kind));
        Burst(w, e.Pos, 34, Colours(e.Kind), 5, smoke: 8);
        w.Heard.Add(Sfx.Blast);
    }

    /// <summary>The colours its debris comes in.</summary>
    public static uint[] Colours(Foe k) => k switch
    {
        Foe.Seeder => [0xFFFF8A20, 0xFFFFD040, 0xFF904010],
        Foe.Drone => [0xFF40D0FF, 0xFF2070A0, 0xFFFFFFFF],
        Foe.Pest => [0xFFB0FF30, 0xFF507010],
        Foe.Bomber => [0xFF9090A0, 0xFFFF3030, 0xFF505060],
        Foe.Fighter => [0xFFFFFF60, 0xFF30A0FF, 0xFFE0E0E0],
        _ => [0xFFFF40FF, 0xFFFFFFFF, 0xFF8020A0],
    };

    // ───────────────────────────── shots, spores and bombs ─────────────────────────────

    private static void StepMissiles(World w, double dt)
    {
        var land = w.Land;
        var ship = w.Ship;
        for (int i = w.Missiles.Count - 1; i >= 0; i--)
        {
            var m = w.Missiles[i];
            m.Life -= dt;
            if (m.Kind is Shot.Spore) m.Vel += new Vec3(0, -Field.Gravity * 0.4, 0) * dt;
            if (m.Kind is Shot.Bomb) m.Vel += new Vec3(0, -Field.Gravity * 0.8, 0) * dt;
            var from = m.Pos;
            var p = m.Pos + m.Vel * dt;
            m.Pos = new Vec3(Land.Wrap(p.X), p.Y, Land.Wrap(p.Z));
            bool gone = m.Life <= 0;
            if (m.Kind == Shot.Player)
                foreach (var e in w.Enemies)
                {
                    if (e.Dead) continue;
                    if (Miss(from, m.Vel * dt, e.Pos) < Enemy.Radius(e.Kind))
                    {
                        gone = true;
                        Sparks(w, m.Pos, 8, 0xFFFFFFA0);
                        if (--e.Hp <= 0) Destroy(w, e);
                        else w.Heard.Add(Sfx.Hit);
                        break;
                    }
                }
            else if (m.Kind == Shot.Enemy && !ship.Dead && ship.Safe <= 0 && Miss(from, m.Vel * dt, ship.Pos) < 0.42)
            {
                gone = true;
                Crash(w);
            }
            double ground = land.Ground(m.Pos.X, m.Pos.Z);
            if (!gone && m.Pos.Y <= ground)
            {
                gone = true;
                var at = m.Pos with { Y = ground };
                bool sea = land.TileAt(m.Pos.X, m.Pos.Z) == Tile.Sea;
                int tx = (int)Math.Floor(m.Pos.X), tz = (int)Math.Floor(m.Pos.Z);
                switch (m.Kind)
                {
                    case Shot.Player or Shot.Enemy:
                        if (sea) Splash(w, at, 6);
                        else Sparks(w, at, 5, m.Kind == Shot.Player ? 0xFFFFE080 : 0xFFFF6060);
                        break;
                    case Shot.Spore:
                        if (sea) Splash(w, at, 4);
                        else
                        {
                            land.Infect(tx, tz);
                            Puff(w, at, 0xFFFF3020);
                        }
                        break;
                    case Shot.Bomb:
                        if (sea) Splash(w, at, 40);
                        else
                        {
                            for (int dz = -1; dz <= 1; dz++)
                                for (int dx = -1; dx <= 1; dx++)
                                    land.Infect(tx + dx, tz + dz);
                            Burst(w, at, 26, [0xFFFF3020, 0xFFFFA020, 0xFF601010], 5, smoke: 6);
                        }
                        w.Heard.Add(Sfx.Blast);
                        if (!ship.Dead && ship.Safe <= 0 && Land.Offset(at, ship.Pos).Length < 1.6) Crash(w);
                        break;
                }
            }
            if (gone) w.Missiles.RemoveAt(i);
        }
    }

    /// <summary>How close a point comes to a target as it moves by <paramref name="move"/> (round the wrap).</summary>
    public static double Miss(Vec3 from, Vec3 move, Vec3 target)
    {
        var to = Land.Offset(from, target);
        double len2 = move.Dot(move);
        double t = len2 < 1e-12 ? 0 : Math.Clamp(to.Dot(move) / len2, 0, 1);
        return (to - move * t).Length;
    }

    // ───────────────────────────── particles ─────────────────────────────

    private const int MostParticles = 1600;

    private static void StepParticles(World w, double dt)
    {
        var land = w.Land;
        for (int i = w.Particles.Count - 1; i >= 0; i--)
        {
            var b = w.Particles[i];
            b.Life -= dt;
            if (b.Life <= 0) { w.Particles.RemoveAt(i); continue; }
            if (b.Kind == Bit.Smoke)
            {
                b.Vel = new Vec3(b.Vel.X * 0.98, b.Vel.Y + 0.4 * dt, b.Vel.Z * 0.98);
                b.Size += 0.35 * dt;
            }
            else b.Vel += new Vec3(0, -Field.Gravity * (b.Kind == Bit.Debris ? 1.2 : 1), 0) * dt;
            b.Angle += b.Spin * dt;
            var p = b.Pos + b.Vel * dt;
            b.Pos = new Vec3(Land.Wrap(p.X), p.Y, Land.Wrap(p.Z));
            double ground = land.Ground(b.Pos.X, b.Pos.Z);
            if (b.Pos.Y < ground && b.Kind != Bit.Smoke)
            {
                if (b.Kind is Bit.Spark or Bit.Splash || land.TileAt(b.Pos.X, b.Pos.Z) == Tile.Sea) { w.Particles.RemoveAt(i); continue; }
                b.Pos = b.Pos with { Y = ground };
                b.Vel = new Vec3(b.Vel.X * 0.5, -b.Vel.Y * 0.35, b.Vel.Z * 0.5);
                b.Spin *= 0.5;
            }
        }
        if (w.Particles.Count > MostParticles) w.Particles.RemoveRange(0, w.Particles.Count - MostParticles);
    }

    /// <summary>An explosion: shards of the thing flying every way, and smoke.</summary>
    public static void Burst(World w, Vec3 at, int shards, uint[] colours, double force, int smoke = 0)
    {
        var r = w.Rng;
        for (int i = 0; i < shards; i++)
        {
            var dir = new Vec3(r.NextDouble() * 2 - 1, r.NextDouble() * 1.4 - 0.2, r.NextDouble() * 2 - 1).Unit;
            w.Particles.Add(new Particle
            {
                Kind = Bit.Debris, Pos = at, Vel = dir * (force * (0.3 + r.NextDouble() * 0.9)), Life = 1.5 + r.NextDouble() * 2, MaxLife = 3.5,
                Size = 0.08 + r.NextDouble() * 0.16, Spin = (r.NextDouble() - 0.5) * 20, Angle = r.NextDouble() * 6, Colour = colours[r.Next(colours.Length)],
            });
        }
        for (int i = 0; i < smoke; i++)
            w.Particles.Add(new Particle
            {
                Kind = Bit.Smoke, Pos = at + new Vec3(r.NextDouble() - 0.5, r.NextDouble() * 0.5, r.NextDouble() - 0.5) * 0.8,
                Vel = new Vec3(r.NextDouble() - 0.5, 0.6 + r.NextDouble(), r.NextDouble() - 0.5), Life = 1.2 + r.NextDouble() * 1.6, MaxLife = 2.8,
                Size = 0.2 + r.NextDouble() * 0.2, Colour = r.Next(3) == 0 ? 0xFF808080 : 0xFFA8A8A8,
            });
        for (int i = 0; i < shards / 3; i++)
            Sparks(w, at, 1, 0xFFFFE070, force * 1.5);
    }

    public static void Sparks(World w, Vec3 at, int n, uint colour, double force = 3)
    {
        var r = w.Rng;
        for (int i = 0; i < n; i++)
            w.Particles.Add(new Particle
            {
                Kind = Bit.Spark, Pos = at, Vel = new Vec3(r.NextDouble() * 2 - 1, r.NextDouble() * 1.5, r.NextDouble() * 2 - 1) * force,
                Life = 0.25 + r.NextDouble() * 0.35, MaxLife = 0.6, Size = 0.05, Colour = colour,
            });
    }

    /// <summary>Spray off the sea.</summary>
    public static void Splash(World w, Vec3 at, int n)
    {
        var r = w.Rng;
        for (int i = 0; i < n; i++)
            w.Particles.Add(new Particle
            {
                Kind = Bit.Splash, Pos = at, Vel = new Vec3((r.NextDouble() * 2 - 1) * 1.5, 2 + r.NextDouble() * 4, (r.NextDouble() * 2 - 1) * 1.5),
                Life = 0.6 + r.NextDouble() * 0.6, MaxLife = 1.2, Size = 0.07, Colour = r.Next(2) == 0 ? 0xFFFFFFFF : 0xFF80B0FF,
            });
    }

    /// <summary>A little puff of coloured smoke where something lands.</summary>
    public static void Puff(World w, Vec3 at, uint colour)
    {
        var r = w.Rng;
        for (int i = 0; i < 4; i++)
            w.Particles.Add(new Particle
            {
                Kind = Bit.Smoke, Pos = at, Vel = new Vec3(r.NextDouble() - 0.5, 0.5 + r.NextDouble() * 0.5, r.NextDouble() - 0.5) * 0.8,
                Life = 0.6 + r.NextDouble() * 0.5, MaxLife = 1.1, Size = 0.12, Colour = colour,
            });
    }
}
