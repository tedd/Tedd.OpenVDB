// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of tools/PointPartitioner.h native stable bin/bucket ordering.
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

/// <summary>Partitions external point indices into power-of-two buckets with deterministic native bin and bucket order.</summary>
/// <remarks>Only point indices are retained. Nonfinite world positions are omitted. Managed offsets for omitted positions are -1; native leaves those entries uninitialized. Construction publishes a complete new partition after success.</remarks>
public sealed class PointPartitioner
{
    private int[] _indices = [], _offsets = [0], _voxelOffsets = [];
    private Coord[] _origins = [];
    public PointPartitioner(int log2Dimension = 3)
    {
        if ((uint)log2Dimension > 10) throw new ArgumentOutOfRangeException(nameof(log2Dimension), "Managed voxel offsets support bucket log2 dimensions from 0 to 10."); Log2Dimension = log2Dimension;
    }
    public int Log2Dimension { get; }
    public int Count => _origins.Length;
    public bool IsEmpty => Count == 0;
    public int PointCount => _indices.Length;
    public bool UsingCellCenteredTransform { get; private set; } = true;
    public ReadOnlySpan<int> VoxelOffsets => _voxelOffsets;
    public ReadOnlySpan<int> Indices(int bucket)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)bucket, (uint)Count, nameof(bucket)); return _indices.AsSpan(_offsets[bucket], _offsets[bucket + 1] - _offsets[bucket]);
    }
    public Coord Origin(int bucket) => _origins[bucket];
    public CoordBBox GetBounds(int bucket) { Coord origin = Origin(bucket); return new(origin, origin + new Coord((1 << Log2Dimension) - 1)); }
    public void Clear() { _indices = []; _offsets = [0]; _voxelOffsets = []; _origins = []; UsingCellCenteredTransform = true; }
    public void Swap(PointPartitioner other)
    {
        ArgumentNullException.ThrowIfNull(other); if (other.Log2Dimension != Log2Dimension) throw new ArgumentException("Bucket dimensions must match.", nameof(other));
        (_indices, other._indices) = (other._indices, _indices); (_offsets, other._offsets) = (other._offsets, _offsets); (_voxelOffsets, other._voxelOffsets) = (other._voxelOffsets, _voxelOffsets); (_origins, other._origins) = (other._origins, _origins); (UsingCellCenteredTransform, other.UsingCellCenteredTransform) = (other.UsingCellCenteredTransform, UsingCellCenteredTransform);
    }
    public static PointPartitioner Create(IPointSource points, Transform transform, bool voxelOrder = false, bool recordVoxelOffsets = false, bool cellCenteredTransform = true, int log2Dimension = 3, CancellationToken cancellationToken = default)
    {
        var result = new PointPartitioner(log2Dimension); result.Construct(points, transform, voxelOrder, recordVoxelOffsets, cellCenteredTransform, cancellationToken); return result;
    }
    public void Construct(IPointSource points, Transform transform, bool voxelOrder = false, bool recordVoxelOffsets = false, bool cellCenteredTransform = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points); ArgumentNullException.ThrowIfNull(transform); cancellationToken.ThrowIfCancellationRequested();
        int count = points.Count; ArgumentOutOfRangeException.ThrowIfNegative(count); int log = Log2Dimension, mask = (1 << log) - 1;
        var records = GC.AllocateUninitializedArray<PointRecord>(count); int valid = 0; int[] voxelOffsets = recordVoxelOffsets ? new int[count] : [];
        if (recordVoxelOffsets) voxelOffsets.AsSpan().Fill(-1);
        for (int index = 0; index < count; ++index)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested(); Vec3d position = points.GetPosition(index); if (!position.IsFinite) continue;
            Coord coordinate = cellCenteredTransform ? transform.WorldToIndexCellCentered(position) : transform.WorldToIndexNodeCentered(position);
            Coord bin = coordinate.AlignDown(log + 5), origin = coordinate.AlignDown(log), local = (coordinate - bin) >> log;
            int bucket = (local.X << 10) + (local.Y << 5) + local.Z;
            int voxel = ((coordinate.X & mask) << (2 * log)) + ((coordinate.Y & mask) << log) + (coordinate.Z & mask);
            records[valid++] = new(bin, origin, bucket, voxelOrder ? voxel : 0, index); if (recordVoxelOffsets) voxelOffsets[index] = voxel;
        }
        Array.Sort(records, 0, valid, PointRecordComparer.Instance); cancellationToken.ThrowIfCancellationRequested();
        int[] indices = GC.AllocateUninitializedArray<int>(valid); List<int> offsets = []; List<Coord> origins = [];
        for (int i = 0; i < valid; ++i)
        {
            PointRecord record = records[i]; if (i == 0 || record.Origin != records[i - 1].Origin) { offsets.Add(i); origins.Add(record.Origin); }
            indices[i] = record.Index;
        }
        offsets.Add(valid); _indices = indices; _offsets = offsets.ToArray(); _origins = origins.ToArray(); _voxelOffsets = voxelOffsets; UsingCellCenteredTransform = cellCenteredTransform;
    }
    private readonly record struct PointRecord(Coord Bin, Coord Origin, int Bucket, int Voxel, int Index);
    private sealed class PointRecordComparer : IComparer<PointRecord>
    {
        internal static readonly PointRecordComparer Instance = new();
        public int Compare(PointRecord a, PointRecord b)
        {
            int c = a.Bin.CompareTo(b.Bin); if (c != 0) return c; c = a.Bucket.CompareTo(b.Bucket); if (c != 0) return c; c = a.Voxel.CompareTo(b.Voxel); return c != 0 ? c : a.Index.CompareTo(b.Index);
        }
    }
}
