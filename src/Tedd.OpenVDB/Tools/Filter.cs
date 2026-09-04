// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of tools/Filter.h and tools/Interpolation.h::AlphaMask.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Scalar mean, Gaussian, median, and offset filters with optional world-aligned alpha masks.</summary>
public sealed class Filter<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    public Filter(Grid<T> grid) => Grid = grid ?? throw new ArgumentNullException(nameof(grid));
    public Grid<T> Grid { get; }
    public bool ProcessTiles { get; set; }
    public bool Parallel { get; set; } = true;
    public float MinMask { get; private set; }
    public float MaxMask { get; private set; } = 1;
    public bool IsMaskInverted { get; set; }
    public void SetMaskRange(float min, float max)
    {
        if (!(min < max)) throw new ArgumentException("The mask range requires min < max.");
        MinMask = min; MaxMask = max;
    }
    public void Mean(int width = 1, int iterations = 1, Grid<float>? mask = null, CancellationToken cancellationToken = default) =>
        Smooth(width, iterations, mask, false, false, cancellationToken);
    /// <summary>Four separable mean passes per iteration, as in OpenVDB.</summary>
    public void Gaussian(int width = 1, int iterations = 1, Grid<float>? mask = null, CancellationToken cancellationToken = default) =>
        Smooth(width, iterations, mask, true, false, cancellationToken);
    public void Median(int width = 1, int iterations = 1, Grid<float>? mask = null, CancellationToken cancellationToken = default) =>
        Smooth(width, iterations, mask, false, true, cancellationToken);
    public void Offset(T value, Grid<float>? mask = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ProcessTiles)
        {
            foreach (var tile in Grid.Tree.EnumerateValues().Where(v => !v.IsVoxel).ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                float alpha = Alpha(tile.Coordinate, mask);
                if (alpha > 0) Grid.Tree.AddTile(tile.Level, tile.Coordinate, OffsetValue(tile.Value, value, alpha), true);
            }
        }
        var manager = new LeafManager<T>(Grid.Tree);
        manager.ForEach((leaf, _) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (int offset in leaf.ActiveOffsets)
            {
                float alpha = Alpha(leaf.OffsetToGlobalCoord(offset), mask);
                if (alpha > 0) leaf.SetValueOnly(offset, OffsetValue(leaf.GetValue(offset), value, alpha));
            }
        }, Parallel);
    }
    private void Smooth(int width, int iterations, Grid<float>? mask, bool gaussian, bool median, CancellationToken token)
    {
        if (iterations <= 0) return;
        int w = System.Math.Max(1, width);
        int diameter = checked(2 * w + 1);
        int stencilSize = median ? checked(checked(diameter * diameter) * diameter) : 0;
        token.ThrowIfCancellationRequested();
        int influence = checked(w * (gaussian ? 4 : 1));
        bool refine = ProcessTiles;
        bool allNeighbors = median || iterations > 1;
        if (refine && RefineTiles(influence, allNeighbors, token) == 0) refine = false;
        var manager = new LeafManager<T>(Grid.Tree, 1);
        int searches = 1;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            token.ThrowIfCancellationRequested();
            if (iteration > 0 && refine)
            {
                int remaining = checked(influence * (iteration + 1) - searches * 8);
                if (remaining > 0)
                {
                    int performed = RefineTiles(remaining, allNeighbors, token);
                    if (performed == 0) refine = false;
                    else manager.Rebuild(1);
                    searches += performed;
                }
            }
            if (median) Pass(manager, w, stencilSize, -1, mask, token);
            else for (int repeat = 0; repeat < (gaussian ? 4 : 1); repeat++)
            {
                Pass(manager, w, 0, 0, mask, token);
                // Native preserves an original indexing typo: X, Z, Y order.
                Pass(manager, w, 0, 2, mask, token);
                Pass(manager, w, 0, 1, mask, token);
            }
        }
    }
    private void Pass(LeafManager<T> manager, int width, int stencilSize, int axis, Grid<float>? mask, CancellationToken token)
    {
        // A complete pass is computed before swapping buffers, so readers see a stable source.
        manager.ForEach((leaf, index) =>
        {
            token.ThrowIfCancellationRequested();
            var source = Grid.Tree.GetAccessor();
            var output = manager.GetBuffer(index, 1);
            T[]? stencil = axis < 0 ? GC.AllocateUninitializedArray<T>(stencilSize) : null;
            float fraction = 1f / (2 * width + 1); // Native explicitly stores this fraction as float, even for DoubleGrid.
            foreach (int offset in leaf.ActiveOffsets)
            {
                Coord coordinate = leaf.OffsetToGlobalCoord(offset);
                float alpha = Alpha(coordinate, mask, out float beta);
                if (!(alpha > 0)) continue;
                T result;
                if (axis < 0)
                {
                    int next = 0;
                    for (long x = (long)coordinate.X - width; x <= (long)coordinate.X + width; x++)
                        for (long y = (long)coordinate.Y - width; y <= (long)coordinate.Y + width; y++)
                            for (long z = (long)coordinate.Z - width; z <= (long)coordinate.Z + width; z++)
                                stencil![next++] = GetAt(source, x, y, z);
                    stencil.AsSpan().Sort(); result = stencil![stencilSize >> 1];
                }
                else
                {
                    T sum = T.Zero;
                    for (int step = -width; step <= width; step++)
                        sum += GetAt(source, (long)coordinate.X + (axis == 0 ? step : 0), (long)coordinate.Y + (axis == 1 ? step : 0), (long)coordinate.Z + (axis == 2 ? step : 0));
                    result = Scale(sum, fraction);
                }
                output[offset] = mask is null ? result : Blend(leaf.GetValue(offset), result, alpha, beta);
            }
        }, Parallel);
        token.ThrowIfCancellationRequested();
        manager.SwapBuffer(1);
    }
    // C++ half promotes products and sums with float coefficients to float before one final half cast.
    private static T Scale(T value, float coefficient) => typeof(T) == typeof(Half)
        ? T.CreateChecked(float.CreateChecked(value) * coefficient) : value * T.CreateChecked(coefficient);
    private static T Blend(T original, T filtered, float alpha, float beta) => typeof(T) == typeof(Half)
        ? T.CreateChecked(beta * float.CreateChecked(original) + alpha * float.CreateChecked(filtered))
        : T.CreateChecked(beta) * original + T.CreateChecked(alpha) * filtered;
    private static T OffsetValue(T original, T offset, float alpha) => typeof(T) == typeof(Half)
        ? T.CreateChecked(float.CreateChecked(original) + alpha * float.CreateChecked(offset))
        : original + T.CreateChecked(alpha) * offset;
    private T GetAt(ValueAccessor<T> accessor, long x, long y, long z) =>
        x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue || z < int.MinValue || z > int.MaxValue
            ? Grid.Background : accessor.GetValue(new((int)x, (int)y, (int)z));
    private float Alpha(Coord coordinate, Grid<float>? mask) => Alpha(coordinate, mask, out _);
    private float Alpha(Coord coordinate, Grid<float>? mask, out float beta)
    {
        if (mask is null) { beta = 0; return 1; }
        float value;
        if (ReferenceEquals(Grid.Transform, mask.Transform) || Grid.Transform.Equals(mask.Transform)) value = mask.Tree.GetValue(coordinate);
        else
        {
            Vec3d world = Grid.Transform.IndexToWorld(coordinate);
            value = BoxSampler.Sample(mask.Tree, mask.Transform.WorldToIndex(world));
        }
        float x = (value - MinMask) * (1 / (MaxMask - MinMask));
        float a = x > 0 ? x < 1 ? (3 - 2 * x) * x * x : 1 : 0;
        float b = 1 - a; beta = IsMaskInverted ? a : b; return IsMaskInverted ? b : a;
    }
    private int RefineTiles(int width, bool allNeighbors, CancellationToken token)
    {
        if (Grid.Tree.ActiveTileCount == 0) return 0;
        int searches = 0;
        for (int distance = 0; distance < width; distance += 8, searches++)
        {
            token.ThrowIfCancellationRequested();
            var coordinates = new HashSet<Coord>();
            foreach (var tile in Grid.Tree.EnumerateValues().Where(v => !v.IsVoxel))
            {
                if (tile.Level == 1) Test(tile.Coordinate, tile.Value);
                else
                {
                    int dimension = 1 << (tile.Level == 2 ? 7 : 12);
                    for (int face = 0; face < 3; face++) for (int side = 0; side < 2; side++)
                        for (int u = 0; u < dimension; u += 8) for (int v = 0; v < dimension; v += 8)
                        {
                            int fixedOffset = side == 0 ? 0 : dimension - 1;
                            Coord origin = tile.Coordinate;
                            Coord c = face switch { 0 => new(origin.X + fixedOffset, origin.Y + u, origin.Z + v), 1 => new(origin.X + u, origin.Y + fixedOffset, origin.Z + v), _ => new(origin.X + u, origin.Y + v, origin.Z + fixedOffset) };
                            Test(c, tile.Value);
                        }
                }
            }
            foreach (Coord coordinate in coordinates) Grid.Tree.TouchLeaf(coordinate);
            void Test(Coord c, T value)
            {
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                {
                    int distance = System.Math.Abs(x) + System.Math.Abs(y) + System.Math.Abs(z);
                    if (distance == 0 || (!allNeighbors && distance != 1)) continue;
                    long nx = (long)c.X + x * 8, ny = (long)c.Y + y * 8, nz = (long)c.Z + z * 8;
                    bool outside = nx < int.MinValue || nx > int.MaxValue || ny < int.MinValue || ny > int.MaxValue || nz < int.MinValue || nz > int.MaxValue;
                    if (outside ? Grid.Background != value : Grid.Tree.GetValue(new((int)nx, (int)ny, (int)nz)) != value || Grid.Tree.ProbeLeaf(new((int)nx, (int)ny, (int)nz)) is not null)
                    { coordinates.Add(VdbTree<T>.Align(c, 3)); return; }
                }
            }
        }
        return searches;
    }
}
