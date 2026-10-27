using Avalonia.Input;
using AVAZarch.Engine;
using static AVAZarch.Tests.Harness;

namespace AVAZarch.Tests;

/// <summary>The keys, the mouse and the invert option, through the input handler and into the game.</summary>
public sealed class InputTests
{
    /// <summary>Holds keys down through the handler for a second, and says where the lander went.</summary>
    private static (double Dx, double Dz, double Dy, int Shots, World W) Hold(InputHandler input, params Key[] keys)
    {
        var w = Empty();
        var s = Airborne(w, 8);
        foreach (var k in keys) input.OnKeyDown(k);
        var at = s.Pos;
        int shots = 0;
        for (int i = 0; i < 60; i++)
        {
            Sim.Step(w, input.Player);
            shots += w.Heard.Count(h => h == Sfx.Shoot);
            w.Heard.Clear();
        }
        foreach (var k in keys) input.OnKeyUp(k);
        var d = Land.Offset(at, s.Pos);
        return (d.X, d.Z, d.Y, shots, w);
    }

    [Fact]
    public void Up_and_thrust_fly_it_north_which_is_up_the_screen()
    {
        using var input = new InputHandler();
        foreach (var key in new[] { Key.Up, Key.W })
        {
            var (dx, dz, _, _, w) = Hold(input, key, Key.Space);
            Assert.True(dz > 1, $"{key}");
            Assert.Equal(0, dx, 3);
            // Seen from where it started, it has gone up the screen and away.
            var v = new View(w.Ship.Pos.X, w.Ship.Pos.Z - dz, w.CamY);
            var from = v.Screen(w.Ship.Pos with { Z = w.Ship.Pos.Z - dz });
            var to = v.Screen(w.Ship.Pos with { Y = w.Ship.Pos.Y });
            Assert.True(to.Y < from.Y && to.Depth > from.Depth);
        }
        foreach (var key in new[] { Key.Down, Key.S }) Assert.True(Hold(input, key, Key.Space).Dz < -1, $"{key}");
    }

    [Fact]
    public void Left_and_right_fly_it_west_and_east()
    {
        using var input = new InputHandler();
        foreach (var key in new[] { Key.Left, Key.A }) Assert.True(Hold(input, key, Key.Space).Dx < -1, $"{key}");
        foreach (var key in new[] { Key.Right, Key.D }) Assert.True(Hold(input, key, Key.Space).Dx > 1, $"{key}");
    }

    [Fact]
    public void Space_Shift_and_X_fire_the_engine_and_nothing_held_it_falls()
    {
        using var input = new InputHandler();
        foreach (var key in new[] { Key.Space, Key.LeftShift, Key.RightShift, Key.X })
            Assert.True(Hold(input, key).Dy > 0.5, $"{key}");
        Assert.True(Hold(input).Dy < -1);
    }

    [Fact]
    public void Z_and_Ctrl_fire_the_gun_and_holding_keeps_firing()
    {
        using var input = new InputHandler();
        foreach (var key in new[] { Key.Z, Key.LeftCtrl, Key.RightCtrl })
            Assert.True(Hold(input, key).Shots >= 6, $"{key}");
        Assert.Equal(0, Hold(input, Key.Space).Shots);
    }

    [Fact]
    public void Inverted_up_leans_back_and_down_leans_away()
    {
        using var input = new InputHandler { InvertY = true };
        Assert.True(Hold(input, Key.Up, Key.Space).Dz < -1);
        Assert.True(Hold(input, Key.Down, Key.Space).Dz > 1);
        Assert.True(Hold(input, Key.Right, Key.Space).Dx > 1, "across is the same");
    }

    [Fact]
    public void The_pointer_from_the_middle_of_the_screen_leans_the_lander()
    {
        using var input = new InputHandler();
        input.MouseAt(View.Width / 2, View.Height / 2 - InputHandler.MouseReach);
        Assert.True(input.MouseSteering);
        Assert.Equal(0, input.Player.Tx, 6);
        Assert.Equal(1, input.Player.Tz, 6);
        input.MouseAt(View.Width / 2 + InputHandler.MouseReach / 2, View.Height / 2);
        Assert.InRange(input.Player.Tx, 0.4, 0.55);
        input.MouseAt(View.Width / 2 + 3, View.Height / 2 + 2);
        Assert.Equal((0.0, 0.0), (input.Player.Tx, input.Player.Tz));
        // Below the middle leans it back, south, towards the camera.
        input.MouseAt(View.Width / 2, View.Height - 10);
        Assert.Equal(-1, input.Player.Tz, 6);
    }

    [Fact]
    public void The_left_button_is_the_engine_and_the_right_the_gun()
    {
        using var input = new InputHandler();
        input.MouseThrust = true;
        Assert.True(input.Player.Thrust);
        Assert.False(input.Player.Fire);
        input.MouseFire = true;
        Assert.True(input.Player.Fire);
        var w = Empty();
        for (int i = 0; i < 60; i++) Sim.Step(w, input.Player);
        Assert.False(w.Ship.Landed);
        Assert.NotEmpty(w.Missiles);
    }

    [Fact]
    public void Keys_take_over_from_the_mouse_until_it_moves_again()
    {
        using var input = new InputHandler();
        input.MouseAt(View.Width / 2 + 200, View.Height / 2);
        Assert.True(input.Player.Tx > 0.5);
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Tx);
        Assert.False(input.MouseSteering);
        input.OnKeyUp(Key.Left);
        Assert.Equal(0, input.Player.Tx);
        input.MouseAt(View.Width / 2 + 210, View.Height / 2);
        Assert.True(input.Player.Tx > 0.5);
    }

    [Fact]
    public void With_the_mouse_turned_off_the_pointer_does_nothing_but_the_buttons_still_work()
    {
        using var input = new InputHandler { Mouse = false };
        input.MouseAt(100, 100);
        Assert.Equal((0.0, 0.0), (input.Player.Tx, input.Player.Tz));
        input.MouseFire = true;
        Assert.True(input.Player.Fire);
    }

    [Fact]
    public void Enter_starts()
    {
        using var input = new InputHandler();
        Assert.False(input.Start);
        input.OnKeyDown(Key.Enter);
        Assert.True(input.Start);
    }
}
