namespace AVAZarch.Engine;

/// <summary>
/// The computer player, for the demonstration and the tests. It flies the lander the way a player does, with
/// the lean and the engine only: it works out the push it wants (to get somewhere, to hold a height, to stay off
/// the ground) and leans that way, blipping the engine on and off for as much push as it needs. To shoot it
/// climbs above an alien, then leans so the nose points at where the alien will be and fires when the shots
/// would hit. Low on fuel, it flies home and lands on the pad to fill up.
/// </summary>
public sealed class Pilot
{
    private double _pulse;
    private Enemy? _target;
    private double _retarget;
    private bool _refuelling;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _pulse = 0;
        _target = null;
        _retarget = 0;
        _refuelling = false;
    }

    public Controls Decide(GameEngine g)
    {
        if (g.State is not (GameState.Playing or GameState.Attract)) return default;
        return Decide(g.World);
    }

    public Controls Decide(World w)
    {
        var s = w.Ship;
        if (s.Dead) return default;
        var land = w.Land;
        if (s.Fuel < 28 && !s.Landed) _refuelling = true;
        if (_refuelling) return Refuel(w);
        if (s.Landed && s.Fuel < Field.FuelMax - 1 && land.TileAt(s.Pos.X, s.Pos.Z) == Tile.Pad && (w.Enemies.Count == 0 || s.Fuel < 60))
        {
            Doing = "filling up";
            return default;
        }
        if (Dodge(w) is { } dodge)
        {
            Doing = "dodging";
            return Push(dodge, fire: _target != null && WouldHit(w, _target));
        }
        _retarget -= Field.Step;
        if (_target is { Dead: true } || !w.Enemies.Contains(_target!) || _retarget <= 0)
        {
            _target = Choose(w);
            _retarget = 1.5;
        }
        if (_target == null)
        {
            // Nothing to shoot: hover over the pad.
            Doing = "waiting";
            var home = Land.PadCentre;
            return FlyTo(w, home with { Y = Land.PadHeight + 4 }, 4, fire: false);
        }
        return Attack(w, _target);
    }

    /// <summary>The nearest alien, with the ones that hunt the lander first when they're close.</summary>
    private static Enemy? Choose(World w)
    {
        Enemy? best = null;
        double bestD = double.MaxValue;
        foreach (var e in w.Enemies)
        {
            double d = Land.FlatDistance(w.Ship.Pos, e.Pos);
            if (e.Kind is Foe.Fighter or Foe.Mutant && d < 14) d *= 0.4;
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    /// <summary>Where a shot fired now would have to point (as a nose direction) to meet the target, if it can.</summary>
    private static Vec3? Aim(World w, Enemy e)
    {
        var s = w.Ship;
        var r = Land.Offset(s.Pos, e.Pos);
        // Lead the target by where it will be when the shot gets there.
        for (int i = 0; i < 2; i++) r = Land.Offset(s.Pos, e.Pos) + e.Vel * (r.Length / Field.ShotSpeed);
        var dir = r.Unit;
        double rv = dir.Dot(s.Vel), k = rv + Math.Sqrt(Math.Max(0, rv * rv - s.Vel.Dot(s.Vel) + Field.ShotSpeed * Field.ShotSpeed));
        var n = (dir * k - s.Vel) * (1 / Field.ShotSpeed);
        return n.Length < 0.5 ? null : n.Unit;
    }

    private Controls Attack(World w, Enemy e)
    {
        var s = w.Ship;
        var land = w.Land;
        var off = Land.Offset(s.Pos, e.Pos);
        double flat = off.Flat;
        bool fire = WouldHit(w, e);
        // Close enough and above it: lean the nose onto it, holding height with the engine.
        if (flat < 13 && flat > 2.5 && Aim(w, e) is { } n && -n.Y > 0.06 && -n.Y < 0.72 && Clearance(w) > 1.6)
        {
            Doing = "attacking";
            double lean = -n.Y, hx = n.X, hz = n.Z, hl = Math.Sqrt(hx * hx + hz * hz);
            double wantY = Math.Max(e.Pos.Y + flat * 0.3, land.Ground(s.Pos.X, s.Pos.Z) + 2.4);
            double ay = Math.Clamp(2.2 * (wantY - s.Pos.Y) - 1.6 * s.Vel.Y, -3, 4);
            double cos = Math.Sqrt(1 - lean * lean);
            bool thrust = Pulse((Field.Gravity + ay) / (Field.ThrustAccel * cos));
            return new Controls(hx / hl * lean / Field.MaxTilt, hz / hl * lean / Field.MaxTilt, thrust, fire);
        }
        // Otherwise get into place: back off from it a little, and above it.
        Doing = "closing in";
        var away = flat > 0.01 ? new Vec3(-off.X / flat, 0, -off.Z / flat) : new Vec3(1, 0, 0);
        double stand = flat < 2.5 ? 8 : Math.Min(flat, 7);
        var spot = e.Pos + away * stand;
        double height = Math.Clamp(e.Pos.Y + 2.2, land.Ground(spot.X, spot.Z) + 2.6, Field.ThinAir - 2);
        return FlyTo(w, spot with { Y = height }, 9, fire);
    }

    /// <summary>A push out of the way of an alien's shot that would otherwise hit within the next second, if any.</summary>
    private static Vec3? Dodge(World w)
    {
        var s = w.Ship;
        foreach (var m in w.Missiles)
        {
            if (m.Kind != Shot.Enemy) continue;
            var to = Land.Offset(s.Pos, m.Pos);
            var rel = m.Vel - s.Vel;
            double t = -to.Dot(rel) / rel.Dot(rel);
            if (t < 0 || t > 1.0) continue;
            var miss = to + rel * t;
            if (miss.Length > 1.1) continue;
            // Sideways across the shot's path, away from where it will pass, and up if anything.
            var side = new Vec3(-rel.Z, 0, rel.X).Unit;
            if (side.Dot(miss) > 0) side = -side;
            return side * 9 + new Vec3(0, Field.Gravity + 2, 0);
        }
        return null;
    }

    /// <summary>Whether a shot fired now, along the nose, would pass close enough to hit.</summary>
    private static bool WouldHit(World w, Enemy e)
    {
        var s = w.Ship;
        var v = s.Nose * Field.ShotSpeed + s.Vel;
        var to = Land.Offset(s.Pos, e.Pos);
        var rel = v - e.Vel;
        double t = Math.Clamp(to.Dot(rel) / rel.Dot(rel), 0, Field.ShotLife);
        return (to - rel * t).Length < Enemy.Radius(e.Kind) * 0.9 && t > 0.01;
    }

    private Controls Refuel(World w)
    {
        var s = w.Ship;
        var pad = Land.PadCentre;
        var off = Land.Offset(s.Pos, pad);
        if (s.Landed)
        {
            if (w.Land.TileAt(s.Pos.X, s.Pos.Z) == Tile.Pad)
            {
                Doing = "filling up";
                if (s.Fuel >= Field.FuelMax - 0.5) _refuelling = false;
                return default;
            }
        }
        if (off.Flat > 0.6 || s.Vel.Flat > 0.8)
        {
            Doing = "going home";
            return FlyTo(w, pad with { Y = Land.PadHeight + 3.5 }, 8, fire: false);
        }
        Doing = "landing";
        // Over the pad: let down gently.
        double above = s.Pos.Y - Field.Base - Land.PadHeight;
        double wantVy = above > 2 ? -2 : -0.9;
        var push = new Vec3(off.X * 1.5 - s.Vel.X * 1.8, Field.Gravity + Math.Clamp((wantVy - s.Vel.Y) * 3, -3, 5), off.Z * 1.5 - s.Vel.Z * 1.8);
        return Push(push, fire: false);
    }

    /// <summary>Flies to a point and stops there.</summary>
    private Controls FlyTo(World w, Vec3 to, double maxSpeed, bool fire)
    {
        var s = w.Ship;
        var land = w.Land;
        var off = Land.Offset(s.Pos, to);
        var want = new Vec3(off.X * 0.9, 0, off.Z * 0.9);
        if (want.Flat > maxSpeed) want *= maxSpeed / want.Flat;
        // Lower ground ahead or below sets the least height.
        double floor = 0;
        for (double t = 0; t <= 1.6; t += 0.4)
            floor = Math.Max(floor, land.Ground(s.Pos.X + s.Vel.X * t, s.Pos.Z + s.Vel.Z * t));
        double wantY = Math.Max(to.Y, floor + 2.6);
        double wantVy = Math.Clamp((wantY - s.Pos.Y) * 1.4, -3, 4);
        var push = new Vec3((want.X - s.Vel.X) * 1.6, Field.Gravity + (wantVy - s.Vel.Y) * 2.4, (want.Z - s.Vel.Z) * 1.6);
        if (Clearance(w) < 1.4) push = new Vec3(-s.Vel.X * 0.6, Field.ThrustAccel, -s.Vel.Z * 0.6);
        return Push(push, fire);
    }

    /// <summary>How far above the ground it will be over the next second, at the least.</summary>
    private static double Clearance(World w)
    {
        var s = w.Ship;
        double least = double.MaxValue;
        for (double t = 0; t <= 1.0; t += 0.2)
        {
            var p = s.Pos + s.Vel * t + new Vec3(0, -0.5 * Field.Gravity * t * t * (s.Thrusting ? 0 : 1), 0);
            least = Math.Min(least, p.Y - Field.Base - w.Land.Ground(p.X, p.Z));
        }
        return least;
    }

    /// <summary>The lean and the engine for a push (an acceleration, gravity included).</summary>
    private Controls Push(Vec3 push, bool fire)
    {
        double y = Math.Max(0.3, push.Y), flat = push.Flat;
        double lean = Math.Min(Field.MaxTilt * 0.95, flat / Math.Sqrt(flat * flat + y * y));
        double hx = flat > 1e-6 ? push.X / flat : 0, hz = flat > 1e-6 ? push.Z / flat : 0;
        double strength = Math.Sqrt(flat * flat + y * y);
        bool thrust = push.Y > 0 && Pulse(strength / Field.ThrustAccel);
        return new Controls(hx * lean / Field.MaxTilt, hz * lean / Field.MaxTilt, thrust, fire);
    }

    /// <summary>The engine blipped on for the given share of the time.</summary>
    private bool Pulse(double share)
    {
        _pulse += Math.Clamp(share, 0, 1);
        if (_pulse < 1) return false;
        _pulse -= 1;
        return true;
    }
}
