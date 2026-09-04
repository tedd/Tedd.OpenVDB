using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class CsgTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LeafSelectionMatchesSignedDistanceBooleanArithmeticForAllIeeeTypes(int operation)
    {
        CheckNumeric<float>(operation);
        CheckNumeric<double>(operation);
        CheckNumeric<Half>(operation);
    }

    private static void CheckNumeric<T>(int operation) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var a = new VdbTree<T>(T.CreateChecked(3));
        var b = new VdbTree<T>(T.CreateChecked(5));
        var leafA = a.TouchLeaf(new Coord(-8));
        var leafB = b.TouchLeaf(new Coord(-8));
        var valuesA = new T[512]; var valuesB = new T[512];
        for (var offset = 0; offset < 512; ++offset)
        {
            valuesA[offset] = T.CreateChecked((offset % 9 - 4) * 0.25);
            valuesB[offset] = T.CreateChecked(((offset * 17) % 11 - 5) * 0.25);
            leafA.SetValueOn(offset, valuesA[offset]);
            leafB.SetValueOn(offset, valuesB[offset]);
        }
        Apply(a, b, operation, false);
        Assert.Same(leafA, a.ProbeLeaf(new Coord(-8)));
        Assert.Equal(512UL, a.ActiveVoxelCount);
        for (var offset = 0; offset < 512; ++offset)
        {
            var candidate = operation == 2 ? -valuesB[offset] : valuesB[offset];
            var expected = operation == 0 ? T.Min(valuesA[offset], candidate) : T.Max(valuesA[offset], candidate);
            Assert.Equal(expected, leafA.GetValue(offset));
            Assert.Equal(valuesB[offset], leafB.GetValue(offset));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TiesAndNaNKeepFirstOperandValueBitsAndActivity(int operation)
    {
        var a = new VdbTree<float>(3); var b = new VdbTree<float>(3);
        var left = a.TouchLeaf(new Coord(0)); var right = b.TouchLeaf(new Coord(0));
        var payloadNaN = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc01234));
        var values = new[] { 0f, -0f, payloadNaN, 1f, -2f };
        for (var i = 0; i < values.Length; ++i)
        {
            left.SetValueOnly(i, values[i]);
            right.SetValueOn(i, operation == 2 ? -values[i] : values[i]);
        }
        left.SetValueOnly(6, 1); right.SetValueOn(6, float.NaN);
        Apply(a, b, operation, false);
        for (var i = 0; i < values.Length; ++i)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(values[i]), BitConverter.SingleToInt32Bits(left.GetValue(i)));
            Assert.False(left.IsValueOn(i));
        }
        Assert.Equal(1f, left.GetValue(6)); Assert.False(left.IsValueOn(6));
    }

    [Theory]
    [InlineData(0, -3)]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    public void CancellationRequiresPruningAndComparesEveryStoredVoxel(int operation, float background)
    {
        foreach (var prune in new[] { false, true })
        {
            var a = new VdbTree<float>(3); var b = new VdbTree<float>(3);
            var left = a.TouchLeaf(new Coord(0)); var right = b.TouchLeaf(new Coord(0));
            for (var i = 0; i < 512; ++i)
            {
                var value = (i % 9 - 4) * 0.25f;
                left.SetValueOn(i, value); right.SetValueOn(i, operation == 2 ? value : -value);
            }
            Apply(a, b, operation, prune, true);
            if (prune)
            {
                Assert.Equal(0UL, a.ActiveVoxelCount); Assert.Equal(0, a.LeafCount);
                Assert.Equal(background, a.GetValue(new Coord(0)));
            }
            else { Assert.Equal(512UL, a.ActiveVoxelCount); Assert.Equal(1, a.LeafCount); }
        }
    }

    [Fact]
    public void UnionMovesUniqueBranchAndInvalidatesSourceAccessorWithoutAliasingFutureWrites()
    {
        var a = new VdbTree<float>(3); var b = new VdbTree<float>(5); var coordinate = new Coord(1);
        b.SetValueOn(coordinate, -1); b.SetValueOff(new Coord(2), -5);
        var sourceLeaf = b.ProbeLeaf(coordinate);
        var oldSourceAccessor = b.GetAccessor(); Assert.Equal(-1f, oldSourceAccessor.GetValue(coordinate));
        Composite.CsgUnion(a, b, false);
        Assert.Same(sourceLeaf, a.ProbeLeaf(coordinate));
        Assert.Null(b.ProbeLeaf(coordinate)); Assert.Equal(5f, oldSourceAccessor.GetValue(coordinate));
        Assert.Equal(-3f, a.GetValue(new Coord(2)));
        oldSourceAccessor.SetValueOn(coordinate, 8);
        Assert.Equal(-1f, a.GetValue(coordinate)); Assert.Equal(8f, b.GetValue(coordinate));
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.Equal(-1f, a.GetValue(coordinate));
    }

    [Fact]
    public void RootTileBooleanOperationsRetainSparseRepresentation()
    {
        var a = new VdbTree<float>(3); var b = new VdbTree<float>(3);
        a.AddTile(3, new Coord(0), 9, true); b.AddTile(3, new Coord(0), -9, true);
        Composite.CsgUnion(a, b);
        Assert.Equal(-3f, a.GetValue(new Coord(2048))); Assert.Equal(0, a.LeafCount);
        Assert.Equal(4096UL * 4096 * 4096, a.ActiveVoxelCount); Assert.Equal(0, a.GetValueDepth(new Coord(2048)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CopyLeavesInputsIntactAndSharesNoLeafBuffers(int operation)
    {
        var a = new VdbTree<double>(3); var b = new VdbTree<double>(5);
        a.SetValueOn(new Coord(1), -1); a.SetValueOff(new Coord(2), -2);
        b.SetValueOn(new Coord(1), -2); b.SetValueOn(new Coord(9), 1);
        var beforeA = a.DeepCopy(); var beforeB = b.DeepCopy();
        var result = Copy(a, b, operation);
        foreach (var coordinate in new[] { new Coord(0), new Coord(1), new Coord(2), new Coord(9) })
        {
            Assert.Equal(beforeA.GetValue(coordinate), a.GetValue(coordinate));
            Assert.Equal(beforeB.GetValue(coordinate), b.GetValue(coordinate));
            Assert.Equal(beforeA.IsValueOn(coordinate), a.IsValueOn(coordinate));
            Assert.Equal(beforeB.IsValueOn(coordinate), b.IsValueOn(coordinate));
        }
        Assert.Equal(beforeA.LeafCount, a.LeafCount); Assert.Equal(beforeB.LeafCount, b.LeafCount);
        Assert.NotSame(a.ProbeLeaf(new Coord(1)), result.ProbeLeaf(new Coord(1)));
        Assert.NotSame(b.ProbeLeaf(new Coord(1)), result.ProbeLeaf(new Coord(1)));
        result.SetValueOn(new Coord(1), 100);
        Assert.Equal(-1, a.GetValue(new Coord(1))); Assert.Equal(-2, b.GetValue(new Coord(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CopyAcceptsSameInputAndGridCopiesTransformAndMetadata(int operation)
    {
        var a = LevelSetSphere.Create(4f, new Vec3f(0), 1);
        a.Name = "source"; a.SetMetadata("custom", 42); a.Transform.PostTranslate(new Vec3d(2, 3, 4));
        var result = operation switch { 0 => Composite.CsgUnionCopy(a, a), 1 => Composite.CsgIntersectionCopy(a, a), _ => Composite.CsgDifferenceCopy(a, a) };
        Assert.Equal(a.Name, result.Name); Assert.Equal(GridClass.LevelSet, result.GridClass);
        Assert.Equal(42, result.GetMetadata<int>("custom"));
        Assert.NotSame(a.Transform, result.Transform);
        Assert.Equal(a.Transform.IndexToWorld(new Vec3d(1)), result.Transform.IndexToWorld(new Vec3d(1)));
        result.Name = "result"; result.Transform.PostTranslate(new Vec3d(1));
        Assert.Equal("source", a.Name);
        Assert.NotEqual(a.Transform.IndexToWorld(new Vec3d(1)), result.Transform.IndexToWorld(new Vec3d(1)));
        Assert.True(a.Tree.GetValue(new Coord(0)) < 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TileOnlyCopyFollowsNativeLeafConstructionAndIsEmpty(int operation)
    {
        var a = new VdbTree<float>(3); var b = new VdbTree<float>(3);
        a.AddTile(3, new Coord(0), -3, false); b.AddTile(3, new Coord(0), -3, false);
        var result = Copy(a, b, operation);
        Assert.True(result.IsEmpty); Assert.Equal(-1, result.GetValueDepth(new Coord(0)));
        Assert.Equal(3f, result.GetValue(new Coord(0)));
        Assert.Equal(-3f, a.GetValue(new Coord(0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidMutatingInputsFailBeforeEitherTreeChanges(int operation)
    {
        foreach (var background in new[] { 0f, -3f, float.NaN })
        {
            var a = new VdbTree<float>(3); a.SetValueOn(new Coord(1), -1);
            var b = new VdbTree<float>(background); b.SetValueOn(new Coord(1), -2);
            Assert.Throws<ArgumentException>(() => Apply(a, b, operation));
            Assert.Equal(-1, a.GetValue(new Coord(1))); Assert.Equal(-2, b.GetValue(new Coord(1)));
        }
        var tree = new VdbTree<float>(3); tree.SetValueOn(new Coord(1), -1);
        Assert.Throws<ArgumentException>(() => Apply(tree, tree, operation));
        Assert.Equal(-1f, tree.GetValue(new Coord(1)));
    }

    private static void Apply<T>(VdbTree<T> a, VdbTree<T> b, int operation, bool prune = true, bool cancel = false)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (operation == 0) Composite.CsgUnion(a, b, prune, cancel);
        else if (operation == 1) Composite.CsgIntersection(a, b, prune, cancel);
        else Composite.CsgDifference(a, b, prune, cancel);
    }
    private static VdbTree<T> Copy<T>(VdbTree<T> a, VdbTree<T> b, int operation)
        where T : unmanaged, IFloatingPointIeee754<T> => operation switch
        { 0 => Composite.CsgUnionCopy(a, b), 1 => Composite.CsgIntersectionCopy(a, b), _ => Composite.CsgDifferenceCopy(a, b) };
}
