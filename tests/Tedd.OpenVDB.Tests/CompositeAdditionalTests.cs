using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class CompositeAdditionalTests
{
    [Fact]
    public void IntegralDivisionByZeroUsesNativeSaturation()
    {
        CheckDivision<sbyte>(); CheckDivision<short>(); CheckDivision<int>(); CheckDivision<long>(); CheckDivision<Int128>();
        CheckDivision<byte>(); CheckDivision<ushort>(); CheckDivision<uint>(); CheckDivision<ulong>(); CheckDivision<UInt128>();
    }

    private static void CheckDivision<T>() where T : unmanaged, INumber<T>, IMinMaxValue<T>
    {
        var a = new VdbTree<T>(T.Zero); var b = new VdbTree<T>(T.Zero);
        a.SetValueOn(new Coord(0), T.One); a.SetValueOn(new Coord(1), T.Zero);
        a.SetValueOn(new Coord(2), T.CreateChecked(9)); b.SetValueOn(new Coord(2), T.CreateChecked(2));
        if (T.MinValue < T.Zero) a.SetValueOn(new Coord(3), -T.One);
        Composite.CompDiv(a, b);
        Assert.Equal(T.MaxValue, a.GetValue(new Coord(0)));
        Assert.Equal(T.Zero, a.GetValue(new Coord(1)));
        Assert.Equal(T.CreateChecked(4), a.GetValue(new Coord(2)));
        if (T.MinValue < T.Zero) Assert.Equal(-T.MaxValue, a.GetValue(new Coord(3)));
        Assert.Equal(0, b.LeafCount);
    }

    [Fact]
    public void FloatingDivisionPreservesIeeeInfinityNaNAndSignedZero()
    {
        var a = new VdbTree<double>(1); var b = new VdbTree<double>(1);
        a.SetValueOn(new Coord(0), 1); b.SetValueOn(new Coord(0), 0);
        a.SetValueOn(new Coord(1), -1); b.SetValueOn(new Coord(1), 0);
        a.SetValueOn(new Coord(2), 0); b.SetValueOn(new Coord(2), 0);
        a.SetValueOn(new Coord(3), -0d); b.SetValueOn(new Coord(3), 1);
        Composite.CompDiv(a, b);
        Assert.Equal(double.PositiveInfinity, a.GetValue(new Coord(0)));
        Assert.Equal(double.NegativeInfinity, a.GetValue(new Coord(1)));
        Assert.True(double.IsNaN(a.GetValue(new Coord(2))));
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0d), BitConverter.DoubleToInt64Bits(a.GetValue(new Coord(3))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BooleanScalarOperationsFollowNativeTruthTables(int operation)
    {
        var a = new VdbTree<bool>(); var b = new VdbTree<bool>();
        for (var index = 0; index < 4; ++index)
        { a.SetValueOn(new Coord(index), (index & 2) != 0); b.SetValueOn(new Coord(index), (index & 1) != 0); }
        switch (operation)
        {
            case 0: Composite.CompSum(a, b); break;
            case 1: Composite.CompMul(a, b); break;
            case 2: Composite.CompMin(a, b); break;
            case 3: Composite.CompMax(a, b); break;
            case 4: Composite.CompDiv(a, b); break;
        }
        for (var index = 0; index < 4; ++index)
        {
            var left = (index & 2) != 0; var right = (index & 1) != 0;
            var expected = operation is 0 or 3 ? left || right : operation is 1 or 2 ? left && right : left;
            Assert.Equal(expected, a.GetValue(new Coord(index)));
            Assert.True(a.IsValueOn(new Coord(index)));
        }
        Assert.True(b.IsEmpty);
    }

    [Fact]
    public void ReplacePreservesInactiveSourceLeafTopologyWithoutCopyingItsValues()
    {
        var a = new VdbTree<int>(3); var b = new VdbTree<int>(5);
        b.SetValueOff(new Coord(1), 100);
        Composite.CompReplace(a, b);
        Assert.Equal(1, a.LeafCount); Assert.Equal(3, a.GetValue(new Coord(1)));
        Assert.False(a.IsValueOn(new Coord(1))); Assert.Equal(100, b.GetValue(new Coord(1)));
    }

    [Fact]
    public void ActiveLeafCompositionMovesUniqueLeavesAndOnlyInvokesOperationForActiveOverlap()
    {
        var source = new VdbTree<int>(5); var destination = new VdbTree<int>(3);
        source.SetValueOn(new Coord(1), 10); source.SetValueOff(new Coord(2), 99);
        source.SetValueOn(new Coord(9), 20); source.SetValueOff(new Coord(10), 88);
        source.AddTile(3, new Coord(4096), 100, true);
        destination.SetValueOn(new Coord(1), 2); destination.SetValueOn(new Coord(2), 4);
        var moved = source.ProbeLeaf(new Coord(9)); var calls = 0;
        var sourceAccessor = source.GetAccessor(); sourceAccessor.GetValue(new Coord(9));
        Composite.CompActiveLeafVoxels(source, destination, (left, right) => { ++calls; return left + right; });
        Assert.Equal(1, calls); Assert.Equal(12, destination.GetValue(new Coord(1)));
        Assert.Equal(4, destination.GetValue(new Coord(2))); Assert.True(destination.IsValueOn(new Coord(2)));
        Assert.Same(moved, destination.ProbeLeaf(new Coord(9)));
        Assert.Equal(20, destination.GetValue(new Coord(9))); Assert.Equal(88, destination.GetValue(new Coord(10)));
        Assert.Equal(3, destination.GetValue(new Coord(4096))); Assert.Equal(0, source.RootEntryCount);
        Assert.Equal(5, sourceAccessor.GetValue(new Coord(9)));
        sourceAccessor.SetValueOn(new Coord(9), 123); Assert.Equal(20, destination.GetValue(new Coord(9)));
    }

    [Fact]
    public void ActiveLeafCompositionSupportsOpaqueUnmanagedBlockPayloads()
    {
        var source = new VdbTree<Block>(); var destination = new VdbTree<Block>();
        source.SetValueOn(new Coord(1), new(17, 9)); destination.SetValueOff(new Coord(1), new(4, 2));
        Composite.CompActiveLeafVoxels(source, destination, static (left, right) => new(right.Material, (byte)(left.Flags | right.Flags)));
        Assert.Equal(new Block(17, 11), destination.GetValue(new Coord(1)));
        Assert.True(destination.IsValueOn(new Coord(1)));
        Assert.Throws<ArgumentException>(() => Composite.CompActiveLeafVoxels(destination, destination));
    }

    [Fact]
    public void GridArithmeticConsumesOnlySourceTreeAndRetainsGridMetadata()
    {
        var a = new Grid<float>(2) { Name = "A", GridClass = GridClass.FogVolume };
        var b = new Grid<float>(3) { Name = "B" };
        a[new Coord(1)] = 4; b[new Coord(1)] = 5;
        Composite.CompSum(a, b);
        Assert.Equal(9f, a[new Coord(1)]); Assert.Equal(5f, a.Background);
        Assert.Equal("A", a.Name); Assert.Equal(GridClass.FogVolume, a.GridClass);
        Assert.Equal("B", b.Name); Assert.True(b.IsEmpty);
    }

    private readonly record struct Block(ushort Material, byte Flags);
}
