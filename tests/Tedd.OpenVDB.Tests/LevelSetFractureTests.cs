using System.Collections;
using System.Runtime.InteropServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetFractureTests
{
    [Fact]
    public void SuccessfulCutPreservesResidualTreeAccessorsAndMetadata()
    {
        Grid<float> source = Source(); var tree = source.Tree; var accessor = source.GetAccessor(); var transform = source.Transform; source.SetMetadata("material", 17);
        Grid<float> cutter = Cutter(), cutterCopy = cutter.DeepCopy(); List<Grid<float>> residuals = [source]; var fracture = new LevelSetFracture<float>(); fracture.Fracture(residuals, cutter);
        Assert.Single(fracture.Fragments); Assert.Single(residuals); Assert.Same(source, residuals[0]); Assert.Same(tree, source.Tree); Assert.Same(transform, source.Transform);
        Assert.Equal(17, source.Metadata["material"].GetValue<int>()); Assert.Equal(17, fracture.Fragments[0].Metadata["material"].GetValue<int>());
        Assert.Equal(source.Tree.GetValue(new(2, 0, 0)), accessor.GetValue(new(2, 0, 0))); Assert.True(source.Tree.GetValue(new(2, 0, 0)) > 0); Assert.True(fracture.Fragments[0].Tree.GetValue(new(2, 0, 0)) < 0);
        LevelSetMorphTests.AssertEqual(cutterCopy.Tree, cutter.Tree);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidCutsLeaveInputGridExactlyUntouched(int kind)
    {
        var source = Source(); var copy = source.DeepCopy(); var cutter = kind == 0 ? Sphere(1, new(100, 0, 0)) : kind == 1 ? source : Sphere(6);
        List<Grid<float>> grids = [source]; var fracture = new LevelSetFracture<float>(); fracture.Fracture(grids, cutter); Assert.Empty(fracture.Fragments); Assert.Same(source, Assert.Single(grids)); LevelSetMorphTests.AssertEqual(copy.Tree, source.Tree);
    }
    [Fact]
    public void CutPiecesReconstructInputSignAtEveryNearbyVoxel()
    {
        var source = Source(); var original = source.DeepCopy(); List<Grid<float>> grids = [source]; var fracture = new LevelSetFracture<float>(); fracture.Fracture(grids, Cutter());
        var fragment = Assert.Single(fracture.Fragments);
        for (int x = -7; x <= 7; ++x) for (int y = -7; y <= 7; ++y) for (int z = -7; z <= 7; ++z)
        {
            Coord c = new(x, y, z); bool left = source.Tree.GetValue(c) < 0, right = fragment.Tree.GetValue(c) < 0;
            Assert.Equal(original.Tree.GetValue(c) < 0, left || right); Assert.False(left && right);
        }
    }
    [Fact]
    public void FragmentListIsPersistentEditableAndClearDoesNotMutateGrids()
    {
        var source = Source(); List<Grid<float>> grids = [source]; var fracture = new LevelSetFracture<float>(); var view = fracture.Fragments;
        fracture.Fracture(grids, Cutter()); var fragment = Assert.Single(view); var saved = fragment.DeepCopy(); fracture.Clear(); Assert.Empty(view); Assert.Same(view, fracture.Fragments); LevelSetMorphTests.AssertEqual(saved.Tree, fragment.Tree);
        view.Add(fragment); Assert.Single(fracture.Fragments); fracture.Clear(); Assert.Empty(fracture.Fragments);
    }
    [Fact]
    public void MismatchedRotationCountUsesPointSampling()
    {
        var a = Source(); var b = a.DeepCopy(); List<Grid<float>> aa = [a], bb = [b]; var fa = new LevelSetFracture<float>(); var fb = new LevelSetFracture<float>(); Vec3f[] points = [new(-.75f, 0, 0), new(.75f, 1.25f, 0)];
        fa.Fracture(aa, Cutter(), points: points); fb.Fracture(bb, Cutter(), points: points, rotations: [Quatf.FromAxisAngle(Axis.Z, 1)]);
        EqualLists(aa, bb); EqualLists(fa.Fragments, fb.Fragments);
    }
    [Fact]
    public void EmptyInstanceListUsesOriginalCutterAndIgnoresRotations()
    {
        var a = Source(); var b = a.DeepCopy(); List<Grid<float>> aa = [a], bb = [b]; var fa = new LevelSetFracture<float>(); var fb = new LevelSetFracture<float>();
        fa.Fracture(aa, Cutter()); fb.Fracture(bb, Cutter(), points: [], rotations: [new(float.NaN, 0, 0, 1)]); EqualLists(aa, bb); EqualLists(fa.Fragments, fb.Fragments);
    }
    [Fact]
    public void PreCancelledCallAndResamplingBudgetPreserveAllPublishedGrids()
    {
        var source = Source(); var copy = source.DeepCopy(); List<Grid<float>> grids = [source]; var fracture = new LevelSetFracture<float> { MaximumResamplingSamples = 0 };
        Assert.Throws<OperationCanceledException>(() => fracture.Fracture(grids, Cutter(), cancellationToken: new(true))); Assert.Empty(fracture.Fragments); LevelSetMorphTests.AssertEqual(copy.Tree, source.Tree);
        Assert.Throws<InvalidOperationException>(() => fracture.Fracture(grids, Cutter(), points: [new(.25f, 0, 0)])); Assert.Empty(fracture.Fragments); LevelSetMorphTests.AssertEqual(copy.Tree, source.Tree);
    }
    [Fact]
    public void CancellationBetweenInstancesRetainsCompletedCutAndFragment()
    {
        var source = Source(); var expected = source.DeepCopy(); List<Grid<float>> grids = [source], control = [expected]; var actual = new LevelSetFracture<float>(); var baseline = new LevelSetFracture<float>(); var cutter = Cutter();
        baseline.Fracture(control, cutter, points: [Vec3f.Zero]); using var cancel = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => actual.Fracture(grids, cutter, points: new CancellingPoints(cancel), cancellationToken: cancel.Token));
        Assert.Single(actual.Fragments); EqualLists(control, grids); EqualLists(baseline.Fragments, actual.Fragments);
    }
    [Fact]
    public void SegmentationSplitsDisconnectedResidualsAndRetainsMetadata()
    {
        var source = Source(); Composite.CsgUnion(source, Sphere(2, new(0, 8, 0))); List<Grid<float>> grids = [source]; var fracture = new LevelSetFracture<float>(); fracture.Fracture(grids, Cutter(), segment: true);
        Assert.True(grids.Count >= 2); Assert.All(grids, g => Assert.Equal("object", g.Name)); Assert.All(fracture.Fragments, g => Assert.Equal("object", g.Name));
        Assert.All(grids, g => Assert.NotSame(source, g));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingFragmentsCanBeProcessedWhenResidualListIsEmpty(bool overlap)
    {
        var source = Source(); var fracture = new LevelSetFracture<float>(); fracture.Fragments.Add(source); List<Grid<float>> empty = [];
        fracture.Fracture(empty, Cutter(), cutterOverlap: overlap); Assert.Empty(empty); Assert.Equal(overlap ? 2 : 1, fracture.Fragments.Count);
    }
    [Fact]
    public void IndependentFractureInstancesDoNotShareState()
    {
        List<Grid<float>> a = [Source()], b = [Source()]; var first = new LevelSetFracture<float>(); var second = new LevelSetFracture<float>(); Parallel.Invoke(() => first.Fracture(a, Cutter()), () => second.Fracture(b, Cutter())); EqualLists(a, b); EqualLists(first.Fragments, second.Fragments); Assert.NotSame(first.Fragments[0].Tree, second.Fragments[0].Tree);
    }
    [Fact]
    public void ValidationAndUnsupportedScalarAreExplicit()
    {
        var fracture = new LevelSetFracture<float>(); Assert.Throws<ArgumentNullException>(() => fracture.Fracture(null!, Cutter())); Assert.Throws<ArgumentNullException>(() => fracture.Fracture([], null!)); Assert.Throws<ArgumentException>(() => fracture.Fracture(Array.AsReadOnly(new[] { Source() }), Cutter())); Assert.Throws<NotSupportedException>(() => new LevelSetFracture<Half>());
    }
    [Fact]
    public void QuaternionLayoutEqualityAndSpanStorageAreNativeCompatible()
    {
        Assert.Equal(16, Marshal.SizeOf<Quatf>()); Quatf q = new(.1f, -.2f, .3f, .4f); Span<float> values = stackalloc float[4]; q.CopyTo(values); Assert.Equal(q, Quatf.FromSpan(values)); Assert.Throws<ArgumentException>(() => q.CopyTo(new float[3])); Assert.Throws<ArgumentException>(() => Quatf.FromSpan(new float[3]));
        var nan = new Quatf(float.NaN, 0, 0, 1); Assert.False(nan.Equals(nan)); Assert.Equal(Quatf.Identity, q.Unit() * q.Unit().Conjugate(), new QuaternionComparer());
    }
    [Fact]
    public void QuaternionIdentityZeroAndScalingPreserveRotation()
    {
        Vec3f vector = new(2, 3, -1); Assert.Equal(vector, Quatf.Zero.RotateVector(vector)); Assert.Equal(vector, Quatf.Identity.RotateVector(vector)); Assert.Throws<ArithmeticException>(() => Quatf.Zero.Unit());
        Quatf q = Quatf.FromAxisAngle(Axis.Z, .3f); Vec3f a = q.RotateVector(vector), b = (3 * q).RotateVector(vector); Assert.InRange((a - b).Length, 0, 5e-7f); Assert.InRange(System.MathF.Abs(a.Length - vector.Length), 0, 5e-7f);
        Assert.Throws<ArgumentException>(() => Quatf.FromAxisAngle(new Vec3f(2, 0, 0), 1));
    }
    private sealed class QuaternionComparer : IEqualityComparer<Quatf>
    {
        public bool Equals(Quatf a, Quatf b) => System.MathF.Abs(a.X - b.X) < 2e-7f && System.MathF.Abs(a.Y - b.Y) < 2e-7f && System.MathF.Abs(a.Z - b.Z) < 2e-7f && System.MathF.Abs(a.W - b.W) < 2e-7f;
        public int GetHashCode(Quatf q) => 0;
    }
    private sealed class CancellingPoints(CancellationTokenSource cancellation) : IReadOnlyList<Vec3f>
    {
        public int Count => 2;
        public Vec3f this[int index] { get { if (index == 1) cancellation.Cancel(); return new(index, 0, 0); } }
        public IEnumerator<Vec3f> GetEnumerator() => new[] { Vec3f.Zero, Vec3f.UnitX }.AsEnumerable().GetEnumerator(); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static void EqualLists(IList<Grid<float>> a, IList<Grid<float>> b) { Assert.Equal(a.Count, b.Count); for (int i = 0; i < a.Count; ++i) LevelSetMorphTests.AssertEqual(a[i].Tree, b[i].Tree); }
    private static Grid<float> Source() => LevelSetFractureConformanceTests.Scene<float>(0);
    private static Grid<float> Sphere(float radius, Vec3f center = default) => LevelSetFractureConformanceTests.Sphere<float>(radius, center);
    private static Grid<float> Cutter() => Sphere(2.5f, new(2, .25f, 0));
}
