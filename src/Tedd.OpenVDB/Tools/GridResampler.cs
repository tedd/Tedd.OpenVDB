// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/GridTransformer.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.Tools;

/// <summary>A pure invertible mapping from input index coordinates to output index coordinates.</summary>
public interface IGridTransform
{
    bool IsAffine { get; }
    Vec3d Transform(Vec3d coordinate);
    Vec3d InverseTransform(Vec3d coordinate);
}

/// <summary>Maps between the index spaces of two retained grid transforms.</summary>
public sealed class ABTransform : IGridTransform
{
    public ABTransform(Transform source, Transform target)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source)); Target = target ?? throw new ArgumentNullException(nameof(target));
        IsAffine = source.IsLinear && target.IsLinear; IsIdentity = IsAffine && source.IsEquivalentTo(target);
    }
    public Transform Source { get; }
    public Transform Target { get; }
    public bool IsAffine { get; }
    public bool IsIdentity { get; }
    public Vec3d Transform(Vec3d coordinate) => Target.WorldToIndex(Source.IndexToWorld(coordinate));
    public Vec3d InverseTransform(Vec3d coordinate) => Source.WorldToIndex(Target.IndexToWorld(coordinate));
}

/// <summary>An invertible homogeneous matrix mapping, including native projective division.</summary>
public sealed class MatrixGridTransform : IGridTransform
{
    public MatrixGridTransform(Mat4d matrix)
    {
        if (!matrix.IsFinite) throw new ArgumentException("The transformation matrix must be finite.", nameof(matrix));
        Matrix = matrix; InverseMatrix = matrix.Inverse();
    }
    public Mat4d Matrix { get; }
    public Mat4d InverseMatrix { get; }
    public bool IsAffine => Matrix.IsAffine;
    public Vec3d Transform(Vec3d coordinate) => Matrix.TransformH(coordinate);
    public Vec3d InverseTransform(Vec3d coordinate) => InverseMatrix.TransformH(coordinate);
}

/// <summary>Resamples transformed leaf/tile regions using native affine stepping or nonlinear inverse mapping.</summary>
/// <remarks>Existing active output values are preserved when the sampled value is inactive. Output metadata and transform are unchanged. Shared input/output trees are snapshotted. Cancellation can leave completed output regions. Tile sampling retains native voxel-by-voxel behavior. Input trees and mapping objects must remain stable during sampling. Types require the background arithmetic supported by ChangeBackground.</remarks>
public class GridResampler
{
    /// <summary>Samples independent rows concurrently and commits in deterministic native serial region order.</summary>
    public bool Threaded { get; set; } = true;
    /// <summary>Includes active and non-background inactive tiles for non-level-set inputs.</summary>
    public bool TransformTiles { get; set; } = true;
    /// <summary>Optional inclusive output clipping, a managed extension applied only to the final mip pass. Intermediate passes retain complete support.</summary>
    public CoordBBox? OutputClip { get; set; }
    /// <summary>Maximum total samples, including overlapping regions and intermediate mip passes. Initial preflight failure leaves the output unchanged; later-pass failures can retain completed changes.</summary>
    public ulong MaximumSamples { get; set; } = ulong.MaxValue;

    public void TransformGrid<T>(IGridTransform transform, Grid<T> input, Grid<T> output, SamplingOrder order = SamplingOrder.Box, bool staggered = false, CancellationToken cancellationToken = default) where T : unmanaged
    {
        Validate(transform, input, output, order, staggered, cancellationToken);
        Grid<T> source = ReferenceEquals(input.Tree, output.Tree) ? input.DeepCopy() : input;
        var budget = new SampleBudget(MaximumSamples);
        Preflight(transform, source, order, budget);
        ChangeBackground.Apply(output.Tree, source.Background);
        ApplyTransform(transform, source, output, order, staggered, budget, cancellationToken);
    }

    protected static void Validate<T>(IGridTransform transform, Grid<T> input, Grid<T> output, SamplingOrder order, bool staggered, CancellationToken token) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(transform); ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(output);
        token.ThrowIfCancellationRequested(); SamplingKernel<T, TreeSampleSource<T>>.Validate(order, staggered);
        if (input.GridClass == GridClass.LevelSet && !SupportsSignedFill<T>()) throw new NotSupportedException("Level-set resampling requires a signed scalar tree.");
    }
    protected void Preflight<T>(IGridTransform transform, Grid<T> input, SamplingOrder order, SampleBudget budget, bool applyOutputClip = true) where T : unmanaged => budget.Check(Regions(transform, input, order, applyOutputClip).Aggregate((UInt128)0, (sum, region) => sum + region.Output.Volume128));

    protected void ApplyTransform<T>(IGridTransform transform, Grid<T> input, Grid<T> output, SamplingOrder order, bool staggered, SampleBudget budget, CancellationToken token, bool applyOutputClip = true) where T : unmanaged
    {
        var regions = Regions(transform, input, order, applyOutputClip);
        UInt128 samples = 0; foreach (var region in regions) samples += region.Output.Volume128;
        budget.Reserve(samples); token.ThrowIfCancellationRequested();
        foreach (var region in regions)
        {
            token.ThrowIfCancellationRequested();
            TransformRegion(transform, region, input, output.Tree, order, staggered, token);
        }
        if (input.GridClass == GridClass.LevelSet) FinishLevelSet(output.Tree);
    }

    private List<Region<T>> Regions<T>(IGridTransform transform, Grid<T> input, SamplingOrder order, bool applyOutputClip) where T : unmanaged
    {
        var result = new List<Region<T>>(); int radius = order == SamplingOrder.Point ? 0 : 1;
        bool levelSet = input.GridClass == GridClass.LevelSet;
        if (!levelSet && TransformTiles)
            foreach (var tile in Tiles(input.Tree)) Add(tile.Bounds, true, tile.Value, tile.IsActive);
        CoordBBox active = CoordBBox.Empty;
        if (levelSet) input.Tree.TryGetActiveVoxelBoundingBox(out active);
        foreach (var leaf in input.Tree.EnumerateLeaves())
        {
            // Native RangeProcessor uses origin + DIM as an inclusive upper bound.
            var box = new CoordBBox(leaf.Origin, new(checked(leaf.Origin.X + 8), checked(leaf.Origin.Y + 8), checked(leaf.Origin.Z + 8)));
            if (!active.IsEmpty) box = box.Intersection(active);
            if (!box.IsEmpty) Add(box, false, default, false);
        }
        return result;
        void Add(CoordBBox box, bool tile, T value, bool on)
        {
            CoordBBox outputBox = TransformBounds(transform, box, radius);
            if (applyOutputClip && OutputClip is CoordBBox clip) outputBox = outputBox.Intersection(clip);
            if (!outputBox.IsEmpty) result.Add(new(box, outputBox, tile, value, on));
        }
    }

    // Native limits ValueAllIter to internal-node depth; do not visit 512 values per leaf.
    private static IEnumerable<VoxelValue<T>> Tiles<T>(VdbTree<T> tree) where T : unmanaged
    {
        foreach (var pair in tree.Entries.OrderBy(pair => pair.Key))
        {
            if (pair.Value.Child is InternalNode<T> node)
            {
                foreach (var value in Visit(node)) yield return value;
            }
            else if (pair.Value.Active || !ValueArithmetic<T>.ApproximatelyEqual(pair.Value.Value, tree.Background))
                yield return new(VdbTree<T>.NodeBounds(pair.Key, 12), pair.Value.Value, pair.Value.Active, 3);
        }
        IEnumerable<VoxelValue<T>> Visit(InternalNode<T> node)
        {
            T[] values = node.Values;
            for (int offset = 0; offset < values.Length; ++offset)
            {
                if (node.Children.TryGetValue(offset, out object? child))
                {
                    if (child is InternalNode<T> inner) foreach (var value in Visit(inner)) yield return value;
                }
                else if (node.Active.IsOn(offset) || !ValueArithmetic<T>.ApproximatelyEqual(values[offset], tree.Background))
                    yield return new(VdbTree<T>.NodeBounds(node.ChildOrigin(offset), node.ChildLog2Dim), values[offset], node.Active.IsOn(offset), node.Level);
            }
        }
    }
    private static CoordBBox TransformBounds(IGridTransform transform, CoordBBox box, int radius)
    {
        Vec3d min = box.Min.AsVec3d(), max = box.Max.AsVec3d() + Vec3d.One;
        Vec3d a = transform.Transform(min), b = transform.Transform(max), lo = Vec3d.Min(a, b), hi = Vec3d.Max(a, b);
        for (int i = 0; i < 8; ++i)
        {
            Vec3d value = transform.Transform(new((i & 1) != 0 ? max.X : min.X, (i & 2) != 0 ? max.Y : min.Y, (i & 4) != 0 ? max.Z : min.Z));
            lo = Vec3d.Min(lo, value); hi = Vec3d.Max(hi, value);
        }
        if (!lo.IsFinite || !hi.IsFinite) throw new ArgumentOutOfRangeException(nameof(transform), "The transformed bounds are not finite.");
        return new(new(checked((int)System.Math.Floor(lo.X) - radius), checked((int)System.Math.Floor(lo.Y) - radius), checked((int)System.Math.Floor(lo.Z) - radius)),
            new(checked((int)System.Math.Ceiling(hi.X) + radius), checked((int)System.Math.Ceiling(hi.Y) + radius), checked((int)System.Math.Ceiling(hi.Z) + radius)));
    }

    private void TransformRegion<T>(IGridTransform transform, Region<T> region, Grid<T> input, VdbTree<T> output, SamplingOrder order, bool staggered, CancellationToken token) where T : unmanaged
    {
        CoordBBox box = region.Output; int maxRows = (int)System.Math.Min(32, box.Dimensions.Y), maxColumns = (int)System.Math.Min(2048, box.Dimensions.Z);
        T[] values = GC.AllocateUninitializedArray<T>(maxRows * maxColumns); bool[] active = new bool[values.Length];
        Vec3d[] rowPositions = new Vec3d[maxRows];
        Vec3d dx = Vec3d.Zero, dy = Vec3d.Zero, dz = Vec3d.Zero, xStart = Vec3d.Zero;
        if (transform.IsAffine)
        {
            Vec3d translation = transform.InverseTransform(Vec3d.Zero);
            dx = transform.InverseTransform(Vec3d.UnitX) - translation;
            dy = transform.InverseTransform(Vec3d.UnitY) - translation;
            dz = transform.InverseTransform(Vec3d.UnitZ) - translation;
            xStart = transform.InverseTransform(box.Min.AsVec3d());
        }
        // BBox.expand takes abs(padding), so the native TileSampler cache expands despite its shrink comment.
        var tileCache = new BBoxd(region.Input.Min.AsVec3d(), region.Input.Max.AsVec3d()).Expand(-(order == SamplingOrder.Point ? 0 : 1));
        for (long x = box.Min.X; x <= box.Max.X; ++x, xStart += dx)
        {
            token.ThrowIfCancellationRequested(); Vec3d yStart = xStart;
            for (long y = box.Min.Y; y <= box.Max.Y; y += maxRows)
            {
                token.ThrowIfCancellationRequested(); int rows = (int)System.Math.Min(maxRows, (long)box.Max.Y - y + 1);
                for (int row = 0; row < rows; ++row, yStart += dy) rowPositions[row] = yStart;
                for (long z = box.Min.Z; z <= box.Max.Z; z += maxColumns)
                {
                    token.ThrowIfCancellationRequested(); int columns = (int)System.Math.Min(maxColumns, (long)box.Max.Z - z + 1);
                    if (Threaded && rows > 1) Parallel.For(0, rows, new ParallelOptions { CancellationToken = token }, SampleRow);
                    else for (int row = 0; row < rows; ++row) SampleRow(row);
                    token.ThrowIfCancellationRequested();
                    for (int row = 0; row < rows; ++row) for (int column = 0; column < columns; ++column)
                    {
                        int index = row * maxColumns + column; Coord coordinate = new((int)x, (int)(y + row), (int)(z + column));
                        if (active[index]) output.SetValueOn(coordinate, values[index]);
                        else if (!output.IsValueOn(coordinate)) output.SetValueOff(coordinate, values[index]);
                    }
                    void SampleRow(int row)
                    {
                        var sampler = new GridSampler<T>(input.GetAccessor(), input.Transform, order, staggered);
                        Vec3d position = rowPositions[row];
                        for (int column = 0; column < columns; ++column, position += dz)
                        {
                            if (!transform.IsAffine) position = transform.InverseTransform(new(x, y + row, z + column));
                            int index = row * maxColumns + column;
                            if (region.Tile && order != SamplingOrder.Point && !tileCache.IsEmpty && tileCache.Contains(position)) { values[index] = region.Value; active[index] = region.Active; }
                            else active[index] = sampler.IndexSample(position, out values[index]);
                        }
                        rowPositions[row] = position;
                    }
                }
            }
        }
    }

    private readonly record struct Region<T>(CoordBBox Input, CoordBBox Output, bool Tile, T Value, bool Active) where T : unmanaged;
    protected sealed class SampleBudget(ulong maximum)
    {
        private UInt128 _used;
        internal void Check(UInt128 samples) { if (samples > maximum || _used > (UInt128)maximum - samples) throw new InvalidOperationException("Resampling exceeds MaximumSamples."); }
        internal void Reserve(UInt128 samples) { Check(samples); _used += samples; }
    }
    private static bool SupportsSignedFill<T>() where T : unmanaged => typeof(T) == typeof(float) || typeof(T) == typeof(double) || typeof(T) == typeof(Half) || typeof(T) == typeof(int) || typeof(T) == typeof(long);
    private static void FinishLevelSet<T>(VdbTree<T> tree) where T : unmanaged
    {
        if (typeof(T) == typeof(float)) Finish((VdbTree<float>)(object)tree);
        else if (typeof(T) == typeof(double)) Finish((VdbTree<double>)(object)tree);
        else if (typeof(T) == typeof(Half)) Finish((VdbTree<Half>)(object)tree);
        else if (typeof(T) == typeof(int)) Finish((VdbTree<int>)(object)tree);
        else if (typeof(T) == typeof(long)) Finish((VdbTree<long>)(object)tree);
        else throw new NotSupportedException("Signed flood filling requires signed scalar values.");
        static void Finish<TValue>(VdbTree<TValue> value) where TValue : unmanaged, INumber<TValue>, ISignedNumber<TValue> { Prune.PruneLevelSet(value); SignedFloodFill.Apply(value); }
    }
}
