// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/VectorTransformer.h.
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Transforms every stored vector, including inactive values and tiles, according to native vector metadata.</summary>
public static class VectorTransformer
{
    /// <remarks>The implicit root background and the grid transform are unchanged. Local-space and invariant vectors are unchanged.
    /// Covariant transformation inverts before mutating. Cancellation leaves completed value writes visible.</remarks>
    public static void TransformVectors<T>(Grid<T> grid, Mat4d matrix, CancellationToken cancellationToken = default) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(grid); VectorTransformArithmetic<T>.Validate();
        if (!grid.IsInWorldSpace) return;
        var type = grid.VectorType; if (type == VectorType.Invariant) return;
        if (type is VectorType.Covariant or VectorType.CovariantNormalize) matrix = matrix.Inverse().Transpose();
        cancellationToken.ThrowIfCancellationRequested(); int count = 0;
        foreach (var value in grid.Tree.EnumerateValueHandles(ValueIterationMode.All))
        {
            if ((count++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            value.SetValue(Transform(value.Value, matrix, type));
        }
    }
    internal static T Transform<T>(T value, Mat4d matrix, VectorType type) where T : unmanaged
    {
        Vec3d v = VectorTransformArithmetic<T>.ToDouble(value);
        v = type == VectorType.ContravariantAbsolute ? matrix.TransformH(v) : matrix.TransformDirection(v);
        if (type == VectorType.CovariantNormalize) v.TryNormalize(out v);
        return VectorTransformArithmetic<T>.FromDouble(v);
    }
}
public readonly record struct HomogeneousMatMul(Mat4d Matrix)
{
    public T Apply<T>(T value) where T : unmanaged { VectorTransformArithmetic<T>.Validate(); return VectorTransformer.Transform(value, Matrix, VectorType.ContravariantAbsolute); }
    public void Apply<T>(TreeValue<T> value) where T : unmanaged => value.SetValue(Apply(value.Value));
}
public readonly record struct MatMul(Mat4d Matrix)
{
    public T Apply<T>(T value) where T : unmanaged { VectorTransformArithmetic<T>.Validate(); return VectorTransformer.Transform(value, Matrix, VectorType.ContravariantRelative); }
    public void Apply<T>(TreeValue<T> value) where T : unmanaged => value.SetValue(Apply(value.Value));
}
public readonly record struct MatMulNormalize(Mat4d Matrix)
{
    public T Apply<T>(T value) where T : unmanaged { VectorTransformArithmetic<T>.Validate(); return VectorTransformer.Transform(value, Matrix, VectorType.CovariantNormalize); }
    public void Apply<T>(TreeValue<T> value) where T : unmanaged => value.SetValue(Apply(value.Value));
}
internal static class VectorTransformArithmetic<T> where T : unmanaged
{
    internal static void Validate() { if (typeof(T) != typeof(Vec3d) && typeof(T) != typeof(Vec3f) && typeof(T) != typeof(Vec3i)) throw new InvalidCastException("Vector transformation requires a Vec3d, Vec3f or Vec3i grid."); }
    internal static Vec3d ToDouble(T value)
    {
        if (typeof(T) == typeof(Vec3d)) return Unsafe.As<T, Vec3d>(ref value);
        if (typeof(T) == typeof(Vec3f)) { var v = Unsafe.As<T, Vec3f>(ref value); return new(v.X, v.Y, v.Z); }
        var i = Unsafe.As<T, Vec3i>(ref value); return new(i.X, i.Y, i.Z);
    }
    internal static T FromDouble(Vec3d value)
    {
        if (typeof(T) == typeof(Vec3d)) return Unsafe.As<Vec3d, T>(ref value);
        if (typeof(T) == typeof(Vec3f)) { var v = new Vec3f((float)value.X, (float)value.Y, (float)value.Z); return Unsafe.As<Vec3f, T>(ref v); }
        var i = new Vec3i(checked((int)value.X), checked((int)value.Y), checked((int)value.Z)); return Unsafe.As<Vec3i, T>(ref i);
    }
}