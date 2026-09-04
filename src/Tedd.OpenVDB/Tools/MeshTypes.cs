// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Contiguous managed polygon storage corresponding to tools/VolumeToMesh.h.
namespace Tedd.OpenVDB.Tools;

/// <summary>Zero-based triangle indices. Negative indices are invalid.</summary>
public readonly record struct MeshTriangle(int A, int B, int C)
{
    public int this[int index] => index switch { 0 => A, 1 => B, 2 => C, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
}
/// <summary>Zero-based quad indices. D == -1 can represent a triangle in mixed-polygon adapters.</summary>
public readonly record struct MeshQuad(int A, int B, int C, int D)
{
    public int this[int index] => index switch { 0 => A, 1 => B, 2 => C, 3 => D, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    public int VertexCount => D == -1 ? 3 : 4;
}
[Flags]
public enum PolygonFlags : byte { None = 0, Exterior = 1, FractureSeam = 2, Subdivided = 4 }

/// <summary>A native leaf-region polygon pool with independent triangle/quad flags.</summary>
public sealed class PolygonPool
{
    internal MeshQuad[] QuadArray;
    internal MeshTriangle[] TriangleArray;
    internal byte[] QuadFlagArray;
    internal byte[] TriangleFlagArray;
    public PolygonPool(int numQuads = 0, int numTriangles = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(numQuads); ArgumentOutOfRangeException.ThrowIfNegative(numTriangles);
        QuadArray = new MeshQuad[numQuads]; TriangleArray = new MeshTriangle[numTriangles]; QuadFlagArray = new byte[numQuads]; TriangleFlagArray = new byte[numTriangles];
    }
    public ReadOnlyMemory<MeshQuad> Quads => QuadArray;
    public ReadOnlyMemory<MeshTriangle> Triangles => TriangleArray;
    public ReadOnlyMemory<byte> QuadFlags => QuadFlagArray;
    public ReadOnlyMemory<byte> TriangleFlags => TriangleFlagArray;
    public int NumQuads => QuadArray.Length;
    public int NumTriangles => TriangleArray.Length;
    public PolygonPool DeepCopy() => new() { QuadArray = (MeshQuad[])QuadArray.Clone(), TriangleArray = (MeshTriangle[])TriangleArray.Clone(), QuadFlagArray = (byte[])QuadFlagArray.Clone(), TriangleFlagArray = (byte[])TriangleFlagArray.Clone() };
    public void ClearQuads() { QuadArray = []; QuadFlagArray = []; }
    public void ClearTriangles() { TriangleArray = []; TriangleFlagArray = []; }
    public void ResetQuads(int count) { ArgumentOutOfRangeException.ThrowIfNegative(count); QuadArray = new MeshQuad[count]; QuadFlagArray = new byte[count]; }
    public void ResetTriangles(int count) { ArgumentOutOfRangeException.ThrowIfNegative(count); TriangleArray = new MeshTriangle[count]; TriangleFlagArray = new byte[count]; }
    public bool TrimQuads(int count) { ArgumentOutOfRangeException.ThrowIfNegative(count); if (count >= NumQuads) return false; Array.Resize(ref QuadArray, count); Array.Resize(ref QuadFlagArray, count); return true; }
    public bool TrimTriangles(int count) { ArgumentOutOfRangeException.ThrowIfNegative(count); if (count >= NumTriangles) return false; Array.Resize(ref TriangleArray, count); Array.Resize(ref TriangleFlagArray, count); return true; }
    public MeshQuad GetQuad(int index) => QuadArray[index];
    public MeshTriangle GetTriangle(int index) => TriangleArray[index];
    public void SetQuad(int index, MeshQuad quad, PolygonFlags flags = PolygonFlags.None) { QuadArray[index] = quad; QuadFlagArray[index] = (byte)flags; }
    public void SetTriangle(int index, MeshTriangle triangle, PolygonFlags flags = PolygonFlags.None) { TriangleArray[index] = triangle; TriangleFlagArray[index] = (byte)flags; }
}
