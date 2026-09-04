using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public class GridTests
{
    [Fact]
    public void NewGridHasBackgroundIdentityTransformAndNoMetadata()
    {
        var grid = new Grid<int>(-1);
        Assert.Equal(-1, grid.Background);
        Assert.Equal(-1, grid[new Coord(-32)]);
        Assert.True(grid.IsEmpty);
        Assert.Equal(0UL, grid.ActiveVoxelCount);
        Assert.Equal(string.Empty, grid.Name);
        Assert.Equal(GridClass.Unknown, grid.GridClass);
        Assert.Empty(grid.Metadata);
        Assert.True(grid.Transform.IsIdentity);
        Assert.False(grid.TryGetActiveVoxelBoundingBox(out _));
    }
    [Fact]
    public void GridWrapsSuppliedTreeAndForwardsStorage()
    {
        var tree = new VdbTree<int>(8);
        var grid = new Grid<int>(tree);
        Assert.Same(tree, grid.Tree);
        grid[new Coord(-1)] = 4;
        Assert.Equal(4, tree.GetValue(new Coord(-1)));
        grid.GetAccessor().SetValueOn(new Coord(1), 9);
        Assert.Equal(9, grid[new Coord(1)]);
        grid.Fill(CoordBBox.CreateCube(new Coord(8), 8), 7);
        Assert.Equal(514UL, grid.ActiveVoxelCount);
        Assert.True(grid.TryGetActiveVoxelBoundingBox(out var bounds));
        Assert.Equal(new CoordBBox(new Coord(-1), new Coord(15)), bounds);
    }
    [Theory]
    [InlineData(GridClass.Unknown, "unknown")]
    [InlineData(GridClass.LevelSet, "level set")]
    [InlineData(GridClass.FogVolume, "fog volume")]
    [InlineData(GridClass.Staggered, "staggered")]
    public void GridClassUsesUpstreamMetadataStrings(GridClass gridClass, string expected)
    {
        var grid = new Grid<float> { GridClass = gridClass };
        Assert.Equal(gridClass, grid.GridClass);
        Assert.Equal(expected, grid.GetMetadata<string>(Grid<float>.ClassMetadataKey));
        Assert.Equal(gridClass, Grid<float>.ParseGridClass(expected));
        grid.RemoveMetadata(Grid<float>.ClassMetadataKey);
        Assert.Equal(GridClass.Unknown, grid.GridClass);
    }
    [Fact]
    public void GridNameAndClassReflectMetadataEdits()
    {
        var grid = new Grid<byte> { Name = "blocks" };
        Assert.Equal("blocks", grid.GetMetadata<string>("name"));
        grid.SetMetadata("name", "terrain");
        Assert.Equal("terrain", grid.Name);
        grid.SetMetadata("class", "fog volume");
        Assert.Equal(GridClass.FogVolume, grid.GridClass);
        grid.SetMetadata("class", 7);
        Assert.Equal(GridClass.Unknown, grid.GridClass);
        Assert.Equal(GridClass.Unknown, Grid<byte>.ParseGridClass("future-class"));
    }
    [Fact]
    public void TypedMetadataSupportsValuesButRejectsMutableReferences()
    {
        var grid = new Grid<int>();
        object[] values = [true, 7, 8L, 1.5f, 2.5, "hello", new Coord(1, 2, 3), new Vec3d(1, 2, 3), Mat4d.Identity];
        string[] types = ["bool", "int32", "int64", "float", "double", "string", "vec3i", "vec3d", "mat4d"];
        for (int i = 0; i < values.Length; ++i)
        {
            grid.SetMetadata($"key{i}", values[i]);
            Assert.Equal(values[i], grid.Metadata[$"key{i}"].Value);
            Assert.Equal(types[i], grid.Metadata[$"key{i}"].TypeName);
        }
        Assert.True(grid.TryGetMetadata<int>("key1", out int result));
        Assert.Equal(7, result);
        Assert.False(grid.TryGetMetadata<double>("key1", out _));
        Assert.False(grid.TryGetMetadata<int>("missing", out _));
        Assert.Throws<InvalidCastException>(() => grid.GetMetadata<double>("key1"));
        Assert.Throws<KeyNotFoundException>(() => grid.GetMetadata<int>("missing"));
        Assert.Throws<ArgumentException>(() => grid.SetMetadata("bad", new List<int>()));
        Assert.Throws<ArgumentException>(() => grid.SetMetadata("", 1));
        Assert.Throws<ArgumentNullException>(() => new MetadataValue(null!));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, MetadataValue>)grid.Metadata).Add("external", new MetadataValue(1)));
    }
    [Fact]
    public void DeepCopySeparatesStorageTransformAndMetadataChanges()
    {
        var grid = new Grid<int>(-2) { Name = "terrain", GridClass = GridClass.FogVolume, Transform = Transform.CreateLinearTransform(2) };
        grid.Transform.PostTranslate(new Vec3d(1, 2, 3));
        grid.SetMetadata("seed", 341L);
        grid.SetMetadata("origin", new Coord(-1));
        grid[new Coord(-8)] = 17;
        grid.Tree.SetValueOff(new Coord(32), 19);
        var copy = grid.DeepCopy();
        Assert.NotSame(grid.Tree, copy.Tree);
        Assert.NotSame(grid.Transform, copy.Transform);
        Assert.NotSame(grid.Metadata, copy.Metadata);
        Assert.Equal(grid.Name, copy.Name);
        Assert.Equal(grid.GridClass, copy.GridClass);
        Assert.Equal(grid.GetMetadata<long>("seed"), copy.GetMetadata<long>("seed"));
        Assert.Equal(19, copy[new Coord(32)]);
        Assert.False(copy.Tree.IsValueOn(new Coord(32)));
        Assert.Equal(grid.IndexToWorld(new Coord(7)), copy.IndexToWorld(new Coord(7)));
        copy.Name = "copy";
        copy.GridClass = GridClass.LevelSet;
        copy.SetMetadata("seed", 99L);
        copy.RemoveMetadata("origin");
        copy.Transform.PostScale(4);
        copy[new Coord(-8)] = 42;
        Assert.Equal("terrain", grid.Name);
        Assert.Equal(GridClass.FogVolume, grid.GridClass);
        Assert.Equal(341L, grid.GetMetadata<long>("seed"));
        Assert.True(grid.Metadata.ContainsKey("origin"));
        Assert.Equal(new Vec3d(2), grid.Transform.VoxelSize);
        Assert.Equal(17, grid[new Coord(-8)]);
    }
    [Fact]
    public void GridIndexAndWorldCoordinatesRoundTripWithNonuniformTransform()
    {
        var grid = new Grid<int>();
        grid.Transform.PostScale(new Vec3d(2, 4, 8));
        grid.Transform.PostTranslate(new Vec3d(7, 9, 11));
        Coord coordinate = new(-3, 7, -11);
        Vec3d expected = new(1, 37, -77);
        Assert.Equal(expected, grid.IndexToWorld(coordinate));
        Assert.Equal(expected, grid.IndexToWorld(coordinate.AsVec3d()));
        Assert.Equal(coordinate.AsVec3d(), grid.WorldToIndex(expected));
        Assert.Throws<ArgumentNullException>(() => grid.Transform = null!);
        Assert.Throws<ArgumentNullException>(() => new Grid<int>((VdbTree<int>)null!));
    }
}
