using System.Text;
using Extensions.Deconstruction.Stl;

namespace Cycon.Host.Tests.Deconstruction;

public sealed class StlLoaderTests
{
    [Fact]
    public void Load_Ascii_ParsesVerticesAndBounds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cycon-stl-ascii-{Guid.NewGuid():N}.stl");
        try
        {
            File.WriteAllText(path, """
solid test
  facet normal 0 0 1
    outer loop
      vertex 0 0 0
      vertex 1 0 0
      vertex 0 2 0
    endloop
  endfacet
endsolid test
""", Encoding.UTF8);

            var data = StlLoader.Load(path);

            Assert.Equal(3, data.Vertices.Length);
            Assert.Equal(3, data.Indices.Length);
            Assert.Equal(0f, data.Bounds.Min.X);
            Assert.Equal(0f, data.Bounds.Min.Y);
            Assert.Equal(0f, data.Bounds.Min.Z);
            Assert.Equal(1f, data.Bounds.Max.X);
            Assert.Equal(2f, data.Bounds.Max.Y);
            Assert.Equal(0f, data.Bounds.Max.Z);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Load_Binary_ParsesVerticesAndBounds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cycon-stl-bin-{Guid.NewGuid():N}.stl");
        try
        {
            using (var fs = File.Create(path))
            using (var bw = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: false))
            {
                bw.Write(new byte[80]);
                bw.Write((uint)1); // triangle count

                // normal
                bw.Write(0f); bw.Write(0f); bw.Write(1f);

                // v0
                bw.Write(0f); bw.Write(0f); bw.Write(0f);
                // v1
                bw.Write(1f); bw.Write(0f); bw.Write(0f);
                // v2
                bw.Write(0f); bw.Write(2f); bw.Write(0f);

                bw.Write((ushort)0);
            }

            var data = StlLoader.Load(path);

            Assert.Equal(3, data.Vertices.Length);
            Assert.Equal(3, data.Indices.Length);
            Assert.Equal(0f, data.Bounds.Min.X);
            Assert.Equal(0f, data.Bounds.Min.Y);
            Assert.Equal(0f, data.Bounds.Min.Z);
            Assert.Equal(1f, data.Bounds.Max.X);
            Assert.Equal(2f, data.Bounds.Max.Y);
            Assert.Equal(0f, data.Bounds.Max.Z);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
