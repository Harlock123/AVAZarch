using Avalonia.Input;

namespace AVAZarch.Engine;

/// <summary>
/// Keyboard, mouse and gamepad boiled down to the controls: the lean (east and north, each -1..1), the engine and
/// the gun. Up the screen is north, so Up leans the lander away into the screen. With the mouse, as in the
/// original, where the pointer is from the middle of the screen sets the lean: further out leans further. The
/// left button is the engine, the right the gun. With <see cref="InvertY"/> up and down swap, like a flight stick.
/// </summary>
public sealed class InputHandler : IDisposable
{
    /// <summary>How far the pointer goes from the middle for the steepest lean, in the game's screen pixels.</summary>
    public const double MouseReach = 270, MouseDeadZone = 10;

    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager _pad = new(0);
    private double _mouseX = View.Width / 2, _mouseY = View.Height / 2;
    /// <summary>The pointer has moved since the keys or the stick last leaned the lander.</summary>
    private bool _mouseFresh;

    /// <summary>Up leans back towards the camera and down away from it, the way a flight stick works.</summary>
    public bool InvertY { get; set; }

    /// <summary>The mouse leans the lander (on unless turned off).</summary>
    public bool Mouse { get; set; } = true;

    /// <summary>The left mouse button: the engine.</summary>
    public bool MouseThrust { get; set; }

    /// <summary>The right mouse button: the gun.</summary>
    public bool MouseFire { get; set; }

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
        if (LeanKeys.Contains(key)) _mouseFresh = false;
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    /// <summary>The pointer, in the game's own 1024×768 screen.</summary>
    public void MouseAt(double x, double y)
    {
        if (Math.Abs(x - _mouseX) + Math.Abs(y - _mouseY) > 0.5) _mouseFresh = true;
        (_mouseX, _mouseY) = (x, y);
    }

    /// <summary>Where the pointer is, in the game's own screen.</summary>
    public (double X, double Y) Pointer => (_mouseX, _mouseY);

    /// <summary>Whether the mouse is leaning the lander just now.</summary>
    public bool MouseSteering => Mouse && _mouseFresh;

    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    private static readonly Key[] LeanKeys = [Key.Left, Key.A, Key.NumPad4, Key.Right, Key.D, Key.NumPad6, Key.Up, Key.W, Key.NumPad8, Key.Down, Key.S, Key.NumPad2];

    public void Update()
    {
        _pad.Update();
        if (Math.Abs(_pad.LeftStickX) > 0.25f || Math.Abs(_pad.LeftStickY) > 0.25f || _pad.DPadLeft || _pad.DPadRight || _pad.DPadUp || _pad.DPadDown) _mouseFresh = false;
    }

    /// <summary>
    /// The arrows or WASD lean the lander (Up leans it away, north); Space, Shift or X fire the engine; Z or Ctrl
    /// the gun. On a gamepad: the left stick or the D-pad leans, A or the right trigger is the engine, and X, B or
    /// the right bumper the gun. With the mouse: the pointer leans it, the left button is the engine and the
    /// right the gun.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            double tx = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            // North (up the screen, into it) is +1.
            double tz = (Any(Key.Up, Key.W, Key.NumPad8) ? 1 : 0) - (Any(Key.Down, Key.S, Key.NumPad2) ? 1 : 0);
            if (tx == 0 && tz == 0)
            {
                if (pad.DPadLeft || pad.DPadRight || pad.DPadUp || pad.DPadDown)
                {
                    tx = (pad.DPadRight ? 1 : 0) - (pad.DPadLeft ? 1 : 0);
                    tz = (pad.DPadUp ? 1 : 0) - (pad.DPadDown ? 1 : 0);
                }
                else if (Math.Abs(pad.LeftStickX) > 0.15f || Math.Abs(pad.LeftStickY) > 0.15f)
                {
                    tx = pad.LeftStickX;
                    tz = -pad.LeftStickY;
                }
            }
            if (InvertY) tz = -tz;
            bool thrust = Any(Key.Space, Key.LeftShift, Key.RightShift, Key.X) || pad.ButtonA || pad.RightTrigger > 0.5f || MouseThrust;
            bool fire = Any(Key.Z, Key.LeftCtrl, Key.RightCtrl) || pad.ButtonX || pad.ButtonB || pad.RightBumper || MouseFire;
            // The keys or the stick take over from the mouse until it moves again.
            if (tx == 0 && tz == 0 && MouseSteering)
            {
                double dx = _mouseX - View.Width / 2, dy = _mouseY - View.Height / 2, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < MouseDeadZone) return new Controls(0, 0, thrust, fire);
                double f = Math.Min(1, (d - MouseDeadZone) / (MouseReach - MouseDeadZone)) / d;
                return new Controls(dx * f, -dy * f, thrust, fire);
            }
            return new Controls(tx, tz, thrust, fire);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;
    /// <summary>Y on the pad: invert up and down.</summary>
    public bool PadInvert => _pad.ButtonY;

    public void Dispose() => _pad.Dispose();
}
