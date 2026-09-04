// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/MeshToVolume.h, MeshToVoxelEdgeData.
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Intersections of mesh polygons with the positive unit lattice edges.</summary>
public sealed class MeshToVoxelEdgeData
{
    /// <summary>Native equality compares primitive indices only, including for inactive tree values.</summary>
    public readonly record struct EdgeData
    {
        public EdgeData() : this(1) { }
        public EdgeData(float distance) { XDistance = YDistance = ZDistance = distance; XPrimitive = YPrimitive = ZPrimitive = -1; }
        public float XDistance { get; init; }
        public float YDistance { get; init; }
        public float ZDistance { get; init; }
        public int XPrimitive { get; init; }
        public int YPrimitive { get; init; }
        public int ZPrimitive { get; init; }
        public bool Equals(EdgeData other) => XPrimitive == other.XPrimitive && YPrimitive == other.YPrimitive && ZPrimitive == other.ZPrimitive;
        public override int GetHashCode() => HashCode.Combine(XPrimitive, YPrimitive, ZPrimitive);
        internal float Distance(int axis) => axis == 0 ? XDistance : axis == 1 ? YDistance : ZDistance;
        internal int Primitive(int axis) => axis == 0 ? XPrimitive : axis == 1 ? YPrimitive : ZPrimitive;
        internal EdgeData WithIntersection(int axis, float distance, int primitive) => axis switch
        {
            0 => this with { XDistance = distance, XPrimitive = primitive },
            1 => this with { YDistance = distance, YPrimitive = primitive },
            _ => this with { ZDistance = distance, ZPrimitive = primitive }
        };
    }
    private static readonly Coord[] Neighbors =
    [
        new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1),
        new(1,0,-1),new(-1,0,-1),new(1,0,1),new(-1,0,1),new(1,1,0),new(-1,1,0),new(1,-1,0),new(-1,-1,0),
        new(0,-1,1),new(0,-1,-1),new(0,1,1),new(0,1,-1),new(-1,-1,-1),new(-1,-1,1),new(1,-1,1),new(1,-1,-1),new(-1,1,-1),new(-1,1,1),new(1,1,1),new(1,1,-1)
    ];
    private static readonly (Coord Offset, int Axis)[] CellEdges =
    [
        (new(0,0,0),0),(new(0,0,0),1),(new(0,0,0),2),
        (new(1,0,0),1),(new(1,0,0),2),(new(1,0,1),1),
        (new(0,0,1),0),(new(0,0,1),1),(new(0,1,1),0),
        (new(0,1,0),0),(new(0,1,0),2),(new(1,1,0),2)
    ];
    private readonly VdbTree<EdgeData> _tree = new(new EdgeData());
    public ValueAccessor<EdgeData> GetAccessor() => new(_tree);
    public VdbTree<EdgeData> CopyTree() => _tree.DeepCopy();

    /// <summary>
    /// Converts float index-space points. A polygon's D index of -1 selects a triangle.
    /// Completed conversions replace the previous data and invalidate existing accessor caches.
    /// Primitive processing is deterministic in input order, matching single-worker native traversal.
    /// </summary>
    public void Convert(ReadOnlyMemory<Vec3f> points, ReadOnlyMemory<MeshQuad> polygons, MeshToVolumeOptions? options = null)
    {
        options ??= new(); options.Validate();
        var generated = new VdbTree<EdgeData>(new EdgeData());
        var accessor = new ValueAccessor<EdgeData>(generated);
        var origins = new HashSet<Coord>();
        long evaluations = 0;
        for (int primitive = 0; primitive < polygons.Length; primitive++)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            var face = polygons.Span[primitive];
            Vec3d a = ReadPoint(points.Span, face.A), b = ReadPoint(points.Span, face.B), c = ReadPoint(points.Span, face.C);
            Vec3d d = face.D == -1 ? default : ReadPoint(points.Span, face.D);
            var pending = new Stack<Coord>(); var visited = new HashSet<Coord>();
            Coord seed = Coord.Floor(a); pending.Push(seed); Evaluate(seed);
            while (pending.TryPop(out Coord coordinate))
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                foreach (Coord delta in Neighbors)
                {
                    Coord neighbor = Add(coordinate, delta);
                    if (visited.Add(neighbor) && Evaluate(neighbor)) pending.Push(neighbor);
                }
            }
            bool Evaluate(Coord coordinate)
            {
                if (++evaluations > options.MaximumVoxelEvaluations) throw new InvalidOperationException("Mesh edge extraction exceeded MaximumVoxelEvaluations.");
                Vec3d origin = coordinate.AsVec3d();
                double distance = (origin - Proximity.ClosestPointOnTriangleToPoint(a, c, b, origin, out _)).LengthSquared;
                EdgeData data = accessor.GetValue(coordinate); bool changed = false;
                TestTriangle(a, c, b);
                if (face.D != -1)
                {
                    double second = (origin - Proximity.ClosestPointOnTriangleToPoint(a, d, c, origin, out _)).LengthSquared;
                    if (second < distance) distance = second;
                    TestTriangle(a, d, c);
                }
                if (changed)
                {
                    Coord leafOrigin = coordinate & -8;
                    if (!origins.Contains(leafOrigin))
                    {
                        if (origins.Count >= options.MaximumLeaves) throw new InvalidOperationException("Mesh edge extraction exceeded MaximumLeaves.");
                        origins.Add(leafOrigin);
                    }
                    accessor.SetValueOn(coordinate, data);
                }
                return distance < 0.86602540378443861;
                void TestTriangle(Vec3d p0, Vec3d p1, Vec3d p2)
                {
                    for (int axis = 0; axis < 3; axis++)
                    {
                        Vec3d direction = axis == 0 ? Vec3d.UnitX : axis == 1 ? Vec3d.UnitY : Vec3d.UnitZ;
                        if (RayTriangleIntersection(origin, direction, p0, p1, p2, out double time) && time < data.Distance(axis))
                        { data = data.WithIntersection(axis, (float)time, primitive); changed = true; }
                    }
                }
            }
        }
        options.CancellationToken.ThrowIfCancellationRequested();
        _tree.Clear(); _tree.Merge(generated);
    }

    /// <summary>Appends a cell's twelve-edge intersections and primitive indices in native edge order.</summary>
    public void GetEdgeData(Coord coordinate, ICollection<Vec3d> points, ICollection<int> primitives) =>
        GetEdgeData(GetAccessor(), coordinate, points, primitives);

    public static void GetEdgeData(ValueAccessor<EdgeData> accessor, Coord coordinate, ICollection<Vec3d> points, ICollection<int> primitives)
    {
        ArgumentNullException.ThrowIfNull(accessor); ArgumentNullException.ThrowIfNull(points); ArgumentNullException.ThrowIfNull(primitives);
        foreach (var (offset, axis) in CellEdges)
        {
            Coord origin = Add(coordinate, offset);
            if (!accessor.ProbeValue(origin, out EdgeData data) || data.Primitive(axis) == -1) continue;
            Vec3d intersection = origin.AsVec3d();
            intersection = axis switch
            {
                0 => new(intersection.X + data.XDistance, intersection.Y, intersection.Z),
                1 => new(intersection.X, intersection.Y + data.YDistance, intersection.Z),
                _ => new(intersection.X, intersection.Y, intersection.Z + data.ZDistance)
            };
            points.Add(intersection); primitives.Add(data.Primitive(axis));
        }
    }
    private static Vec3d ReadPoint(ReadOnlySpan<Vec3f> points, int index)
    {
        var point = points[index]; var result = new Vec3d(point.X, point.Y, point.Z);
        if (!result.IsFinite) throw new ArgumentException("Mesh points must be finite.", nameof(points));
        Coord.Floor(result); return result;
    }
    private static Coord Add(Coord a, Coord b) => new(checked(a.X + b.X), checked(a.Y + b.Y), checked(a.Z + b.Z));
    private static bool RayTriangleIntersection(Vec3d origin, Vec3d direction, Vec3d a, Vec3d b, Vec3d c, out double time)
    {
        time = 0;
        Vec3d e1 = b - a, e2 = c - a, s1 = direction.Cross(e2);
        double divisor = s1.Dot(e1);
        if (!(System.Math.Abs(divisor) > 0)) return false;
        double inverse = 1 / divisor; Vec3d d = origin - a;
        double b1 = d.Dot(s1) * inverse;
        if (b1 < 0 || b1 > 1) return false;
        Vec3d s2 = d.Cross(e1); double b2 = direction.Dot(s2) * inverse;
        if (b2 < 0 || b1 + b2 > 1) return false;
        time = e2.Dot(s2) * inverse; return !(time < 0);
    }
}
