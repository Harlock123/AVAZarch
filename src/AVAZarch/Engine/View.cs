namespace AVAZarch.Engine;

/// <summary>
/// The camera: always facing north, behind and above the lander and looking down on it, the way the original
/// shows its patch of the land. It follows the lander east, west, north and south exactly, and up and down
/// lazily. Only a square patch of the land round the lander is drawn; past it is black.
/// </summary>
public readonly struct View
{
    public const double Width = 1024, Height = 768;
    public const double Focal = 640;
    /// <summary>How far behind the lander, and how far above it, the camera sits.</summary>
    public const double Back = 8.8, Rise = 4.7;
    public const double Pitch = 0.5;
    /// <summary>Where on the screen the camera's middle is.</summary>
    public const double CentreY = Height * 0.47;
    /// <summary>The patch drawn: rows behind and ahead of the lander, and columns either side.</summary>
    public const int RowsBehind = 5, RowsAhead = 14, Columns = 11;

    private static readonly double Cos = Math.Cos(Pitch), Sin = Math.Sin(Pitch);

    /// <summary>The point the camera follows, and the camera's height.</summary>
    public readonly double FocusX, FocusZ, CamY;

    public View(double focusX, double focusZ, double camY)
    {
        FocusX = Land.Wrap(focusX);
        FocusZ = Land.Wrap(focusZ);
        CamY = camY;
    }

    public static View Of(World w) => new(w.Ship.Pos.X, w.Ship.Pos.Z, w.CamY);

    /// <summary>A point of the world relative to the camera, taking the short way round the wrap.</summary>
    public Vec3 Rel(Vec3 p) => new(Land.Delta(FocusX, p.X), p.Y - CamY, Land.Delta(FocusZ, p.Z) + Back);

    /// <summary>A grid corner, given as a column and row relative to the focus square (no wrapping needed).</summary>
    public Vec3 RelCorner(int col, int row, double height) =>
        new(Math.Floor(FocusX) + col - FocusX, height - CamY, Math.Floor(FocusZ) + row - FocusZ + Back);

    /// <summary>Where a camera-relative point lands on the screen, and how far in front of the camera it is.</summary>
    public static (double X, double Y, double Depth) Project(Vec3 rel)
    {
        double depth = rel.Z * Cos - rel.Y * Sin, up = rel.Y * Cos + rel.Z * Sin;
        double d = Math.Max(depth, 0.05);
        return (Width / 2 + rel.X * Focal / d, CentreY - up * Focal / d, depth);
    }

    /// <summary>Where a point of the world lands on the screen.</summary>
    public (double X, double Y, double Depth) Screen(Vec3 p) => Project(Rel(p));

    /// <summary>Whether a point of the world is over the patch of land drawn.</summary>
    public bool InPatch(Vec3 p)
    {
        double dx = Land.Delta(Math.Floor(FocusX), p.X), dz = Land.Delta(Math.Floor(FocusZ), p.Z);
        return dx >= -Columns && dx < Columns + 1 && dz >= -RowsBehind && dz < RowsAhead + 1;
    }
}
