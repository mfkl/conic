using System.Runtime.InteropServices;

namespace Conic.Interop;

// Owned geometry copied from VLC's vout display thread. Versioned export v1;
// no pointers or callback-owned memory crosses the plugin boundary.
[StructLayout(LayoutKind.Sequential)]
internal struct VideoGeometry : IEquatable<VideoGeometry>
{
    public double X, Y, Width, Height, SampleAspect, Zoom;
    public int AutoScale, Active;
    public readonly bool Equals(VideoGeometry other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height && SampleAspect == other.SampleAspect && Zoom == other.Zoom && AutoScale == other.AutoScale && Active == other.Active;
}
