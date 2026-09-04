// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/GridTransformer.h affine decomposition and mip passes.
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

/// <summary>Affine/projective grid transformation with native successive-halving downsampling.</summary>
public sealed class GridTransformer : GridResampler
{
    private Vec3d _pivot;
    private Coord _mipLevels;
    private Mat4d _preScale = Mat4d.Identity, _postScale = Mat4d.Identity;
    public GridTransformer(Mat4d transform)
    {
        _ = new MatrixGridTransform(transform); Transform = transform;
        if (Decompose(transform, out Vec3d scale, out Vec3d rotate, out Vec3d translate)) Init(Vec3d.Zero, scale, rotate, translate, "rst", "zyx");
    }
    public GridTransformer(Vec3d pivot, Vec3d scale, Vec3d rotate, Vec3d translate, string transformOrder = "tsr", string rotationOrder = "zyx") => Init(pivot, scale, rotate, translate, transformOrder, rotationOrder);
    public Mat4d Transform { get; private set; }
    public Coord MipLevels => _mipLevels;

    public void TransformGrid<T>(Grid<T> input, Grid<T> output, SamplingOrder order = SamplingOrder.Box, bool staggered = false, CancellationToken cancellationToken = default) where T : unmanaged
    {
        var matrix = new MatrixGridTransform(Transform);
        Validate(matrix, input, output, order, staggered, cancellationToken);
        Grid<T> source = ReferenceEquals(input.Tree, output.Tree) ? input.DeepCopy() : input;
        var budget = new SampleBudget(MaximumSamples);
        bool mipmap = order != SamplingOrder.Point && _mipLevels != Coord.Zero;
        Mat4d first = !mipmap ? Transform : !_preScale.ApproxEquals(Mat4d.Identity) ? _preScale : HalfMatrix(_mipLevels);
        Preflight(new MatrixGridTransform(first), source, order, budget, !mipmap);
        ChangeBackground.Apply(output.Tree, source.Background);
        if (!mipmap) { ApplyTransform(matrix, source, output, order, staggered, budget, cancellationToken); return; }
        bool firstPass = true;
        var temporary = new Grid<T>(source.Background);
        if (!_preScale.ApproxEquals(Mat4d.Identity))
        {
            firstPass = false;
            ApplyTransform(new MatrixGridTransform(_preScale), source, temporary, order, staggered, budget, cancellationToken, false);
        }
        Coord count = _mipLevels;
        while (count != Coord.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var half = new MatrixGridTransform(HalfMatrix(count));
            bool finalPass = count.X <= 1 && count.Y <= 1 && count.Z <= 1 && _postScale.ApproxEquals(Mat4d.Identity);
            if (firstPass) { firstPass = false; ApplyTransform(half, source, temporary, order, staggered, budget, cancellationToken, finalPass); }
            else { var next = new Grid<T>(source.Background); ApplyTransform(half, temporary, next, order, staggered, budget, cancellationToken, finalPass); temporary = next; }
            count = new(System.Math.Max(0, count.X - 1), System.Math.Max(0, count.Y - 1), System.Math.Max(0, count.Z - 1));
        }
        if (!_postScale.ApproxEquals(Mat4d.Identity)) ApplyTransform(new MatrixGridTransform(_postScale), temporary, output, order, staggered, budget, cancellationToken);
        else output.SetTree(temporary.Tree);
    }
    private Mat4d HalfMatrix(Coord count) => Mat4d.CreateTranslation(-_pivot) * Mat4d.CreateScale(new Vec3d(count.X != 0 ? 0.5 : 1, count.Y != 0 ? 0.5 : 1, count.Z != 0 ? 0.5 : 1)) * Mat4d.CreateTranslation(_pivot);

    private void Init(Vec3d pivot, Vec3d scale, Vec3d rotate, Vec3d translate, string transformOrder, string rotationOrder)
    {
        if (!pivot.IsFinite || !scale.IsFinite || !rotate.IsFinite || !translate.IsFinite || scale.X == 0 || scale.Y == 0 || scale.Z == 0) throw new ArgumentException("Transform components must be finite and scale components nonzero.");
        if (transformOrder is null || transformOrder.Length != 3 || !transformOrder.Contains('t') || !transformOrder.Contains('r') || !transformOrder.Contains('s')) throw new ArgumentException("Transform order must be a permutation of tsr.", nameof(transformOrder));
        if (rotationOrder is null || rotationOrder.Length != 3 || !rotationOrder.Contains('x') || !rotationOrder.Contains('y') || !rotationOrder.Contains('z')) throw new ArgumentException("Rotation order must be a permutation of xyz.", nameof(rotationOrder));
        _pivot = pivot;
        int Mips(double value)
        {
            double magnitude = System.Math.Abs(value);
            int levels = magnitude < 0.5 ? checked((int)System.Math.Floor(-System.Math.Log(magnitude) / System.Math.Log(2))) : 0;
            if (levels > 30) throw new ArgumentOutOfRangeException(nameof(scale), "Scale requires more than thirty native mip levels.");
            return levels;
        }
        _mipLevels = new(Mips(scale.X), Mips(scale.Y), Mips(scale.Z));
        Vec3d remainderScale = new(scale.X * (1 << _mipLevels.X), scale.Y * (1 << _mipLevels.Y), scale.Z * (1 << _mipLevels.Z));
        Transform = _preScale = _postScale = Mat4d.Identity; bool beforeScale = false;
        for (int i = 2; i >= 0; --i)
        {
            switch (transformOrder[i])
            {
                case 't': Pre(Mat4d.CreateTranslation(translate)); break;
                case 's':
                    Transform = Mat4d.CreateTranslation(-pivot) * Mat4d.CreateScale(scale) * Mat4d.CreateTranslation(pivot) * Transform;
                    Mat4d remainder = Mat4d.CreateTranslation(-pivot) * Mat4d.CreateScale(remainderScale) * Mat4d.CreateTranslation(pivot);
                    if (beforeScale) _preScale = remainder * _preScale; else _postScale = remainder * _postScale;
                    beforeScale = true;
                    break;
                case 'r':
                    Pre(Mat4d.CreateTranslation(pivot));
                    for (int r = 2; r >= 0; --r)
                        Pre(rotationOrder[r] switch { 'x' => Mat4d.CreateRotation(rotate.X, Axis.X), 'y' => Mat4d.CreateRotation(rotate.Y, Axis.Y), _ => Mat4d.CreateRotation(rotate.Z, Axis.Z) });
                    Pre(Mat4d.CreateTranslation(-pivot));
                    break;
            }
        }
        _ = new MatrixGridTransform(Transform);
        void Pre(Mat4d matrix)
        {
            Transform = matrix * Transform;
            if (beforeScale) _preScale = matrix * _preScale; else _postScale = matrix * _postScale;
        }
    }

    private static bool Decompose(Mat4d matrix, out Vec3d scale, out Vec3d rotate, out Vec3d translate)
    {
        scale = rotate = default; translate = matrix.Translation;
        if (!matrix.IsAffine) return false;
        Mat3d linear = matrix.GetMat3(); Vec3d unsigned = new(linear.Row(0).Length, linear.Row(1).Length, linear.Row(2).Length);
        bool uniform = unsigned.ApproxEquals(new Vec3d(unsigned.X)), valid = false;
        double minimumAngle = double.MaxValue;
        for (int i = 0; i < 8; ++i)
        {
            Vec3d candidate = new((i & 1) != 0 ? -unsigned.X : unsigned.X, (i & 2) != 0 ? -unsigned.Y : unsigned.Y, (i & 4) != 0 ? -unsigned.Z : unsigned.Z);
            Mat3d rotation = linear * Mat3d.CreateScale(candidate).Inverse();
            if (rotation.Determinant < 0) continue;
            Vec3d angles = EulerXyz(rotation);
            Mat3d rebuild = Mat3d.CreateRotation(angles.Z, Axis.Z) * Mat3d.CreateRotation(angles.Y, Axis.Y) * Mat3d.CreateRotation(angles.X, Axis.X) * Mat3d.CreateScale(candidate);
            if (!linear.ApproxEquals(rebuild)) continue;
            double maximum = System.Math.Max(System.Math.Abs(angles.X), System.Math.Max(System.Math.Abs(angles.Y), System.Math.Abs(angles.Z)));
            if (minimumAngle < maximum) continue;
            minimumAngle = maximum; rotate = angles; scale = candidate; valid = true;
            if (uniform || angles.ApproxEquals(Vec3d.Zero)) break;
        }
        return valid;
    }
    private static Vec3d EulerXyz(Mat3d matrix)
    {
        if (System.Math.Abs(matrix.M20 - 1) <= 1e-8) { double angle = 0.5 * System.Math.Atan2(matrix.M12, matrix.M11); return new(angle, System.Math.PI / 2, angle); }
        if (System.Math.Abs(matrix.M20 + 1) <= 1e-8) { double angle = 0.5 * System.Math.Atan2(matrix.M12, matrix.M11); return new(angle, -System.Math.PI / 2, -angle); }
        return new(System.Math.Atan2(-matrix.M21, matrix.M22), System.Math.Atan2(matrix.M20, System.Math.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22)), System.Math.Atan2(-matrix.M10, matrix.M00));
    }
}
