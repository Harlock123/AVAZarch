using AVAZarch.Engine;
using static AVAZarch.Tests.Harness;

namespace AVAZarch.Tests;

/// <summary>The landscape: hills, sea and the pad, wrapping round, and the virus on it.</summary>
public sealed class LandTests
{
    [Fact]
    public void The_land_wraps_round_at_every_edge()
    {
        var land = new Land(5);
        for (double x = 0.3; x < Land.Size; x += 3.7)
            for (double z = 0.1; z < Land.Size; z += 5.3)
            {
                Assert.Equal(land.Ground(x, z), land.Ground(x + Land.Size, z), 9);
                Assert.Equal(land.Ground(x, z), land.Ground(x, z - Land.Size), 9);
            }
        // No cliff at the seam: across it the ground is as smooth as anywhere.
        for (int z = 0; z < Land.Size; z++)
            Assert.True(Math.Abs(land.H(Land.Size - 1, z) - land.H(0, z)) < 1.5);
    }

    [Fact]
    public void The_shortest_way_round_is_taken()
    {
        Assert.Equal(2, Land.Delta(63, 1), 9);
        Assert.Equal(-2, Land.Delta(1, 63), 9);
        Assert.Equal(5, Land.Delta(10, 15), 9);
        Assert.Equal(Math.Sqrt(10), Land.FlatDistance(new Vec3(62.5, 0, 0.5), new Vec3(1.5, 4, 63.5)), 9);
    }

    [Fact]
    public void There_are_hills_sea_and_a_flat_pad()
    {
        var land = new Land(1);
        int sea = 0, grass = 0, beach = 0;
        double top = 0;
        for (int z = 0; z < Land.Size; z++)
            for (int x = 0; x < Land.Size; x++)
            {
                switch (land.TileAt(x, z))
                {
                    case Tile.Sea: sea++; Assert.Equal(Land.SeaLevel, land.Ground(x + 0.5, z + 0.5), 9); break;
                    case Tile.Grass: grass++; break;
                    case Tile.Beach: beach++; break;
                }
                top = Math.Max(top, land.H(x, z));
            }
        Assert.InRange(sea, 400, 2000);
        Assert.True(grass > 1500, "mostly land");
        Assert.True(beach > 100, "with beaches round the sea");
        Assert.True(top > 3, "and hills");
        for (int z = Land.PadZ; z < Land.PadZ + Land.PadSize; z++)
            for (int x = Land.PadX; x < Land.PadX + Land.PadSize; x++)
            {
                Assert.Equal(Tile.Pad, land.TileAt(x, z));
                Assert.Equal(Land.PadHeight, land.H(x, z), 9);
            }
        Assert.Equal(Land.PadHeight, land.Ground(Land.PadCentre.X, Land.PadCentre.Z), 9);
    }

    [Fact]
    public void Grass_can_be_infected_but_not_the_sea_or_the_pad()
    {
        var w = Empty();
        var (gx, gz) = Grass(w);
        var (sx, sz) = Sea(w);
        Assert.True(w.Land.Infect(gx, gz));
        Assert.False(w.Land.Infect(gx, gz), "only once");
        Assert.False(w.Land.Infect(sx, sz));
        Assert.False(w.Land.Infect(Land.PadX, Land.PadZ));
        Assert.Equal(1, w.Land.InfectedCount);
        Assert.Equal(1.0 / w.Land.LandCount, w.Land.Infection, 9);
        Assert.True(w.Land.Cleanse(gx, gz));
        Assert.Equal(0, w.Land.InfectedCount);
    }

    [Fact]
    public void Left_alone_the_virus_creeps_into_the_land_next_to_it()
    {
        var w = Empty();
        var (x, z) = GrassPatch(w, 4);
        w.Land.Infect(x, z);
        var r = new Random(3);
        for (int i = 0; i < 400; i++) w.Land.Spread(r, 0.05);
        Assert.True(w.Land.InfectedCount > 8);
        foreach (var (ix, iz) in w.Land.InfectedTiles())
        {
            Assert.NotEqual(Tile.Sea, w.Land.TileAt(ix, iz));
            Assert.True(Land.FlatDistance(new Vec3(ix, 0, iz), new Vec3(x, 0, z)) < 30);
        }
    }

    [Fact]
    public void Between_waves_a_third_of_the_infection_heals()
    {
        var w = Empty();
        var (x, z) = GrassPatch(w, 3);
        for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
                w.Land.Infect(x + dx, z + dz);
        Assert.Equal(25, w.Land.InfectedCount);
        Assert.Equal(8, Sim.Heal(w));
        Assert.Equal(17, w.Land.InfectedCount);
    }
}
