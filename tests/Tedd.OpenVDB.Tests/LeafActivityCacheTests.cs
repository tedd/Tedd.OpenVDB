using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class LeafActivityCacheTests
{
    [Theory]
    [InlineData("coordinate off")]
    [InlineData("offset off")]
    [InlineData("value off")]
    [InlineData("coordinate state")]
    [InlineData("offset state")]
    [InlineData("fill")]
    [InlineData("partial fill")]
    [InlineData("values")]
    [InlineData("intersection")]
    [InlineData("difference")]
    [InlineData("modify")]
    [InlineData("clip")]
    public void SaturatedMaskCacheNeverSuppressesReactivation(string mutation)
    {
        var leaf = new LeafNode<int>(Coord.Zero, 7, true).DeepCopy();
        Coord point = Coord.Zero;
        switch (mutation)
        {
            case "coordinate off": leaf.SetValueOff(point); break;
            case "offset off": leaf.SetValueOff(0); break;
            case "value off": leaf.SetValueOff(point, 17); break;
            case "coordinate state": leaf.SetActiveState(point, false); break;
            case "offset state": leaf.SetActiveState(0, false); break;
            case "fill": leaf.Fill(17, false); break;
            case "partial fill": leaf.Fill(new CoordBBox(point, point), 17, false); break;
            case "values": leaf.SetValues(new int[512], false); break;
            case "intersection": leaf.TopologyIntersection(new LeafNode<byte>(Coord.Zero)); break;
            case "difference": leaf.TopologyDifference(new LeafNode<byte>(Coord.Zero, 1, true)); break;
            case "modify": leaf.ModifyValueAndActiveState(point, _ => new VoxelState<int>(17, false)); break;
            case "clip": leaf.Clip(new CoordBBox(new Coord(1), new Coord(7)), 17); break;
            default: throw new InvalidOperationException(mutation);
        }
        Assert.False(leaf.IsValueOn(point));
        leaf.SetValueOn(point, 19);
        Assert.True(leaf.ProbeValue(point, out int value));
        Assert.Equal(19, value);
    }
}
