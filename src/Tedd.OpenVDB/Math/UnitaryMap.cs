// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Math;

public sealed class UnitaryMap : MapBase
{
    private readonly AffineMap _affine;
    public UnitaryMap() : this(Mat4d.Identity) { }
    public UnitaryMap(Axis axis, double radians) { _affine = new AffineMap(Mat4d.CreateRotation(radians, axis)); }
    public UnitaryMap(Vec3d axis, double radians) { _affine = new AffineMap(Mat4d.CreateRotation(axis, radians)); }
    public UnitaryMap(Mat3d matrix) : this(matrix.ToMat4()) { }
    public UnitaryMap(Mat4d matrix)
    {
        if (!matrix.IsFinite || !matrix.IsAffine || matrix.Translation != Vec3d.Zero || !matrix.GetMat3().IsUnitary)
            throw new ArithmeticException("A unitary map requires a finite rotation or reflection without translation.");
        _affine = new(matrix);
    }
    private UnitaryMap(AffineMap affine) { _affine = affine; }
    internal static UnitaryMap FromSerializedMatrix(Mat4d matrix) => new(new AffineMap(matrix));
    public UnitaryMap(UnitaryMap other) { ArgumentNullException.ThrowIfNull(other); _affine = other._affine.Copy(); }
    public UnitaryMap(UnitaryMap first, UnitaryMap second) { _affine = new(first.Matrix * second.Matrix); }
    public override string MapType => nameof(UnitaryMap);
    public override Mat4d Matrix => _affine.Matrix;
    public override Mat4d InverseMatrix => _affine.InverseMatrix;
    public override bool HasUniformScale => true;
    public override double Determinant => _affine.Determinant;
    public override Vec3d VoxelSize => _affine.VoxelSize;
    public override Vec3d ApplyMap(Vec3d value) => _affine.ApplyMap(value);
    public override Vec3d ApplyInverseMap(Vec3d value) => _affine.ApplyInverseMap(value);
    public override Vec3d ApplyJacobian(Vec3d value) => _affine.ApplyJacobian(value);
    public override Vec3d ApplyInverseJacobian(Vec3d value) => _affine.ApplyInverseJacobian(value);
    public override Vec3d ApplyJacobianTranspose(Vec3d value) => ApplyInverseMap(value);
    public override Vec3d ApplyInverseJacobianTranspose(Vec3d value) => _affine.ApplyInverseJacobianTranspose(value);
    public override Mat3d ApplyInverseJacobianCurvature(Mat3d value) => _affine.ApplyInverseJacobianCurvature(value);
    public override UnitaryMap Copy() => new(this);
    public override UnitaryMap InverseMap() => new(InverseMatrix);
    public override AffineMap GetAffineMap() => _affine.Copy();
    public override bool IsEqual(MapBase? other) => other is UnitaryMap map && _affine.IsEqual(map._affine);
    public override MapBase PreRotate(double radians, Axis axis = Axis.X) => new UnitaryMap(new UnitaryMap(axis, radians), this);
    public override MapBase PostRotate(double radians, Axis axis = Axis.X) => new UnitaryMap(this, new UnitaryMap(axis, radians));
}
