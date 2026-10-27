using AVAZarch.Engine;
using static AVAZarch.Tests.Harness;

namespace AVAZarch.Tests;

/// <summary>The camera: behind and above the lander, facing north, following it round the wrapping world.</summary>
public sealed class ViewTests
{
    [Fact]
    public void North_is_up_the_screen_and_east_is_right()
    {
        var v = new View(20, 20, 6);
        var here = v.Screen(new Vec3(20, 1, 20));
        var north = v.Screen(new Vec3(20, 1, 24));
        var east = v.Screen(new Vec3(22, 1, 20));
        Assert.True(north.Y < here.Y);
        Assert.True(north.Depth > here.Depth);
        Assert.Equal(here.X, north.X, 6);
        Assert.True(east.X > here.X);
        Assert.True(v.Screen(new Vec3(20, 3, 20)).Y < here.Y, "higher is higher up the screen");
    }

    [Fact]
    public void Further_off_is_smaller()
    {
        var v = new View(20, 20, 6);
        double Width(double z) => v.Screen(new Vec3(21, 1, z)).X - v.Screen(new Vec3(19, 1, z)).X;
        Assert.True(Width(30) < Width(25) && Width(25) < Width(20));
    }

    [Fact]
    public void The_lander_is_drawn_in_the_middle_of_the_screen()
    {
        var w = Empty();
        var s = Airborne(w, 5, 12.5, 40.5);
        w.CamY = s.Pos.Y + View.Rise;
        var p = View.Of(w).Screen(s.Pos);
        Assert.Equal(View.Width / 2, p.X, 6);
        Assert.InRange(p.Y, View.Height * 0.35, View.Height * 0.6);
    }

    [Fact]
    public void Round_the_wrap_a_place_is_drawn_where_it_is_nearest()
    {
        var v = new View(1, 63, 6);
        var a = v.Screen(new Vec3(63, 2, 1));
        var b = v.Screen(new Vec3(-1, 2, 65));
        Assert.Equal(a, b);
        Assert.True(a.X < View.Width / 2, "just west of the lander");
        Assert.True(v.InPatch(new Vec3(63, 2, 1)));
        Assert.False(v.InPatch(new Vec3(30, 2, 30)));
    }

    [Fact]
    public void The_camera_rises_and_falls_with_the_lander_lazily()
    {
        var w = Empty();
        var s = Airborne(w, 3);
        w.CamY = s.Pos.Y + View.Rise;
        s.Pos = s.Pos with { Y = s.Pos.Y + 6 };
        Sim.Step(w, new Controls(0, 0, true, false));
        Assert.True(w.CamY < s.Pos.Y + View.Rise - 3, "not at once");
        for (int i = 0; i < 180; i++)
        {
            s.Vel = Vec3.Zero;
            Sim.Step(w, new Controls(0, 0, i % 5 < 2, false));
        }
        Assert.Equal(s.Pos.Y + View.Rise, w.CamY, 0);
    }
}
