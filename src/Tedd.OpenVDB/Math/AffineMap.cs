// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Math;

public sealed class AffineMap : MapBase
{
    private readonly Mat3d _jacobianInverse;
    public AffineMap()
    {
        Matrix = Mat4d.Identity; InverseMatrix = Mat4d.Identity; _jacobianInverse = Mat3d.Identity;
        Determinant = 1; VoxelSize = Vec3d.One; IsDiagonal = true;
    }
    public AffineMap(AffineMap other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Matrix = other.Matrix; InverseMatrix = other.InverseMatrix; _jacobianInverse = other._jacobianInverse;
        Determinant = other.Determinant; VoxelSize = other.VoxelSize; IsDiagonal = other.IsDiagonal;
    }
    public AffineMap(Mat3d matrix) : this(matrix.ToMat4()) { }
    public AffineMap(Mat4d matrix)
    {
        if (!matrix.IsFinite || !matrix.IsAffine) throw new ArgumentException("The affine matrix must be finite and affine.", nameof(matrix));
        Matrix = matrix; Mat3d linear = matrix.GetMat3(); Determinant = linear.Determinant;
        if (!double.IsFinite(Determinant) || System.Math.Abs(Determinant) < 3e-15) throw new ArithmeticException("The affine matrix is nearly singular.");
        InverseMatrix = matrix.Inverse(); _jacobianInverse = linear.Inverse().Transpose();
        if (!InverseMatrix.IsFinite || !_jacobianInverse.IsFinite) throw new ArithmeticException("The affine inverse is not finite.");
        Vec3d origin = ApplyMap(Vec3d.Zero);
        VoxelSize = new((ApplyMap(Vec3d.UnitX) - origin).Length, (ApplyMap(Vec3d.UnitY) - origin).Length, (ApplyMap(Vec3d.UnitZ) - origin).Length);
        double sum = 0; for (int row = 0; row < 4; ++row) for (int column = 0; column < 4; ++column) if (row != column) sum += System.Math.Abs(matrix[row, column]);
        IsDiagonal = MathComparison.IsApproxZero(sum);
    }
    public AffineMap(AffineMap first, AffineMap second) : this(first.Matrix * second.Matrix) { }
    public override string MapType => nameof(AffineMap);
    public override Mat4d Matrix { get; }
    public override Mat4d InverseMatrix { get; }
    public Mat3d InverseJacobianTranspose => _jacobianInverse;
    public override double Determinant { get; }
    public override Vec3d VoxelSize { get; }
    public bool IsDiagonal { get; }
    public bool IsScale => IsDiagonal;
    public bool IsScaleTranslate => Matrix.GetMat3().IsDiagonal;
    public override bool HasUniformScale => !MathComparison.IsApproxZero(Determinant) && (Matrix.GetMat3() * (1.0 / System.Math.Pow(System.Math.Abs(Determinant), 1.0 / 3.0))).IsUnitary;
    public override Vec3d ApplyMap(Vec3d value) => Matrix.TransformPoint(value);
    public override Vec3d ApplyInverseMap(Vec3d value) => InverseMatrix.TransformPoint(value);
    public override Vec3d ApplyJacobian(Vec3d value) => Matrix.TransformDirection(value);
    public override Vec3d ApplyInverseJacobian(Vec3d value) => InverseMatrix.TransformDirection(value);
    public override Vec3d ApplyJacobianTranspose(Vec3d value) => new(Matrix.M00 * value.X + Matrix.M01 * value.Y + Matrix.M02 * value.Z, Matrix.M10 * value.X + Matrix.M11 * value.Y + Matrix.M12 * value.Z, Matrix.M20 * value.X + Matrix.M21 * value.Y + Matrix.M22 * value.Z);
    public override Vec3d ApplyInverseJacobianTranspose(Vec3d value) => value * _jacobianInverse;
    public override Mat3d ApplyInverseJacobianCurvature(Mat3d value) => _jacobianInverse.Transpose() * value * _jacobianInverse;
    public override AffineMap Copy() => new(this);
    public override AffineMap InverseMap() => new(InverseMatrix);
    public override AffineMap GetAffineMap() => Copy();
    public override bool IsEqual(MapBase? other) => other is AffineMap map && Matrix.ApproxEquals(map.Matrix) && InverseMatrix.ApproxEquals(map.InverseMatrix);
    public override MapBase PreTranslate(Vec3d value) => new AffineMap(Mat4d.CreateTranslation(value) * Matrix);
    public override MapBase PostTranslate(Vec3d value) => new AffineMap(Matrix * Mat4d.CreateTranslation(value));
    public override MapBase PreScale(Vec3d value) => new AffineMap(MapMatrix.Scale(Matrix, value, true));
    public override MapBase PostScale(Vec3d value) => new AffineMap(MapMatrix.Scale(Matrix, value, false));
}
