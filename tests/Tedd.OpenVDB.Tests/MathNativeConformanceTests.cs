using System.Globalization;
using System.Text.Json;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathNativeConformanceTests
{
    private static readonly JsonDocument Fixture = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-math.json")));
    public static IEnumerable<object[]> Cases()
    {
        int index = 0;
        foreach (JsonElement test in Fixture.RootElement.GetProperty("cases").EnumerateArray())
            yield return new object[] { index++, test.GetProperty("kind").GetString()! };
    }
    [Fact]
    public void FixtureIsPinnedToReviewedUpstreamRevision()
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.RootElement.GetProperty("upstreamCommit").GetString());
        Assert.True(Fixture.RootElement.GetProperty("cases").GetArrayLength() >= 275);
    }
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble();
    private static double Scalar(JsonElement test, string name) => Number(test.GetProperty(name));
    private static double[] Values(JsonElement test, string name) => test.GetProperty(name).EnumerateArray().Select(Number).ToArray();
    private static Vec3d Vec(JsonElement test, string name) { double[] v = Values(test, name); return new(v[0], v[1], v[2]); }
    private static Quatd Quat(JsonElement test, string name) { double[] v = Values(test, name); return new(v[0], v[1], v[2], v[3]); }
    private static Mat3d Mat3(JsonElement test, string name) => Mat3d.FromSpan(Values(test, name));
    private static Mat4d Mat4(JsonElement test, string name) => Mat4d.FromSpan(Values(test, name));
    private static void Equal(double expected, double actual, string field)
    {
        if (double.IsNaN(expected)) { Assert.True(double.IsNaN(actual), field); return; }
        if (double.IsInfinity(expected)) { Assert.Equal(expected, actual); return; }
        double tolerance = 2e-10 * System.Math.Max(1, System.Math.Abs(expected));
        Assert.True(System.Math.Abs(expected - actual) <= tolerance, $"{field}: native {expected:R}, managed {actual:R}, tolerance {tolerance:R}");
    }
    private static void Check(JsonElement test, string field, double actual) => Equal(Scalar(test, field), actual, field);
    private static void Check(JsonElement test, string field, Vec3d actual) { double[] v = Values(test, field); for (int i = 0; i < 3; ++i) Equal(v[i], actual[i], $"{field}[{i}]"); }
    private static void Check(JsonElement test, string field, Quatd actual) { double[] v = Values(test, field); for (int i = 0; i < 4; ++i) Equal(v[i], actual[i], $"{field}[{i}]"); }
    private static void Check(JsonElement test, string field, Mat3d actual) { double[] v = Values(test, field); for (int i = 0; i < 9; ++i) Equal(v[i], actual[i / 3, i % 3], $"{field}[{i}]"); }
    private static void Check(JsonElement test, string field, Mat4d actual) { double[] v = Values(test, field); for (int i = 0; i < 16; ++i) Equal(v[i], actual[i / 4, i % 4], $"{field}[{i}]"); }
    [Theory]
    [MemberData(nameof(Cases))]
    public void ManagedResultsMatchNativeOpenVdbMath(int index, string kind)
    {
        JsonElement test = Fixture.RootElement.GetProperty("cases")[index];
        switch (kind)
        {
            case "rotation":
                Check(test, "result", Mat3d.CreateRotation(Vec(test, "from"), Vec(test, "to")));
                break;
            case "mat3":
                {
                    Mat3d m = Mat3(test, "m");
                    Check(test, "inverse", m.Inverse()); Check(test, "transpose", m.Transpose()); Check(test, "cofactor", m.Cofactor());
                    Check(test, "adjoint", m.Adjoint()); Check(test, "determinant", m.Determinant);
                    break;
                }
            case "mat4":
                {
                    Mat4d m = Mat4(test, "m"); Vec3d p = Vec(test, "p");
                    Check(test, "inverse", m.Inverse()); Check(test, "determinant", m.Determinant);
                    Check(test, "transformH", m.TransformH(p)); Check(test, "pretransformH", m.PreTransformH(p));
                    break;
                }
            case "quat":
                {
                    Quatd a = Quat(test, "a"), b = Quat(test, "b");
                    Check(test, "product", a * b); Check(test, "inverse", a.Inverse()); Check(test, "slerp", Quatd.Slerp(a, b, Scalar(test, "t")));
                    Check(test, "derivative", a.Derivative(Vec(test, "omega"))); Check(test, "matrix", a.ToMat3());
                    Check(test, "matrixQuat", Quatd.FromRotationMatrixUnchecked(a.ToMat3())); Check(test, "axis", a.Axis); Check(test, "angle", a.Angle);
                    break;
                }
            case "vector":
                {
                    Vec3d v = Vec(test, "v");
                    Check(test, "unitSafe", v.UnitSafe()); Check(test, "length", v.Length); Check(test, "equalsSelf", v.Equals(v) ? 1 : 0);
                    break;
                }
            case "nan":
                {
                    Vec3d v = new(double.NaN, 1, 0);
                    Mat3d m = Mat3d.Identity with { M00 = double.NaN }; Mat4d n = Mat4d.Identity with { M00 = double.NaN };
                    Quatd q = new(double.NaN, 0, 0, 1);
                    Check(test, "vectorNormalize", v.TryNormalize(out _) ? 1 : 0);
                    Check(test, "vectorUnitThrows", Record.Exception(() => v.Unit()) is ArithmeticException ? 1 : 0);
                    Check(test, "vectorApprox", v.ApproxEquals(new(3, 1, 0)) ? 1 : 0);
                    Check(test, "matrixApprox", m.ApproxEquals(Mat3d.Identity) ? 1 : 0);
                    Check(test, "matrix4Approx", n.ApproxEquals(Mat4d.Identity) ? 1 : 0);
                    Check(test, "matrixUnitary", m.IsUnitary ? 1 : 0);
                    Check(test, "matrix3InverseThrows", Record.Exception(() => m.Inverse()) is ArithmeticException ? 1 : 0);
                    Check(test, "matrix4InverseThrows", Record.Exception(() => n.Inverse()) is ArithmeticException ? 1 : 0);
                    Check(test, "quatInverseThrows", Record.Exception(() => q.Inverse()) is ArithmeticException ? 1 : 0);
                    Check(test, "quatNormalize", q.TryNormalize(out _) ? 1 : 0);
                    Check(test, "quatApprox", q.ApproxEquals(new(3, 0, 0, 1)) ? 1 : 0);
                    Check(test, "quatUnit", q.Unit()); Check(test, "quatSlerp", Quatd.Slerp(q, Quatd.Identity, 0.5));
                    Check(test, "quatMatrix", q.ToMat3()); Check(test, "quatFromMatrix", Quatd.FromRotationMatrix(m));
                    Check(test, "quatFromNanAxis", Quatd.FromAxisAngle(v, 0.3));
                    Check(test, "vectorLargeRelative", new Vec3d(1e12, 1, 1).ApproxEquals(new Vec3d(1e12 + 1, 1, 1), 1e-7) ? 1 : 0);
                    break;
                }

            case "frustum":
                {
                    var map = new NonlinearFrustumMap(new BBoxd(Vec(test, "min"), Vec(test, "max")), Scalar(test, "taper"), Scalar(test, "depth"), Mat4(test, "secondary"));
                    Vec3d p = Vec(test, "p"), v = Vec(test, "v"); Mat3d h = Mat3(test, "h");
                    Check(test, "world", map.ApplyMap(p)); Check(test, "inverse", map.ApplyInverseMap(Vec(test, "world")));
                    Check(test, "j", map.ApplyJacobian(v, p)); Check(test, "ij", map.ApplyInverseJacobian(v, p)); Check(test, "jt", map.ApplyJacobianTranspose(v, p));
                    Check(test, "ijt", map.ApplyInverseJacobianTranspose(v, p)); Check(test, "ijc", map.ApplyInverseJacobianCurvature(h, v, p));
                    Check(test, "size", map.VoxelSize); Check(test, "sizeAt", map.VoxelSizeAt(p)); Check(test, "determinant", map.Determinant); Check(test, "determinantAt", map.DeterminantAt(p));
                    Check(test, "j0", map.ApplyJacobian(v)); Check(test, "ij0", map.ApplyInverseJacobian(v)); Check(test, "jt0", map.ApplyJacobianTranspose(v)); Check(test, "ijt0", map.ApplyInverseJacobianTranspose(v));
                    break;
                }
            case "camera":
                {
                    var map = NonlinearFrustumMap.CreateFromCamera(Vec(test, "position"), Vec(test, "direction"), Vec(test, "up"), Scalar(test, "aspect"),
                        Scalar(test, "near"), Scalar(test, "depth"), (int)Scalar(test, "x"), (int)Scalar(test, "z"));
                    Check(test, "min", map.BoundingBox.Min); Check(test, "max", map.BoundingBox.Max); Check(test, "secondary", map.SecondMap);
                    Check(test, "taper", map.Taper); Check(test, "normalizedDepth", map.Depth); Check(test, "nearCenter", map.ApplyMap(new(40, 20, 0)));
                    Check(test, "farCenter", map.ApplyMap(new(40, 20, 120))); Check(test, "corner", map.ApplyMap(new(80, 40, 120)));
                    break;
                }
            default: throw new InvalidOperationException("Unknown native math case " + kind);
        }
    }
}
