// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/RayTracer.h and openvdb/math/Mat.h.
using Tedd.OpenVDB.Math;

namespace Tedd.OpenVDB.Tools;

/// <summary>Base for Houdini-compatible OpenVDB preview cameras.</summary>
public abstract class BaseCamera
{
    private Mat4d _screenToWorld;
    private Rayd _baseRay;

    protected BaseCamera(Film film, Vec3d rotation, Vec3d translation, double frameWidth, double nearPlane, double farPlane)
    {
        Film = film ?? throw new ArgumentNullException(nameof(film));
        if (film.Width == 0 || film.Height == 0) throw new ArgumentException("A camera requires a nonempty film.", nameof(film));
        if (!rotation.IsFinite) throw new ArgumentException("Camera rotation must be finite.", nameof(rotation));
        if (!translation.IsFinite) throw new ArgumentException("Camera translation must be finite.", nameof(translation));
        if (!double.IsFinite(frameWidth) || frameWidth <= 0) throw new ArgumentOutOfRangeException(nameof(frameWidth));
        if (!double.IsFinite(nearPlane) || nearPlane <= 0) throw new ArgumentOutOfRangeException(nameof(nearPlane));
        if (!double.IsFinite(farPlane) || farPlane <= nearPlane) throw new ArgumentOutOfRangeException(nameof(farPlane));

        ScaleWidth = frameWidth;
        ScaleHeight = frameWidth * film.Height / film.Width;
        const double DegreesToRadians = System.Math.PI / 180.0;
        Mat4d matrix = Mat4d.Identity;
        matrix = MapMatrix.Rotate(matrix, rotation.X * DegreesToRadians, Axis.X, pre: false);
        matrix = MapMatrix.Rotate(matrix, rotation.Y * DegreesToRadians, Axis.Y, pre: false);
        matrix = MapMatrix.Rotate(matrix, rotation.Z * DegreesToRadians, Axis.Z, pre: false);
        matrix *= Mat4d.CreateTranslation(translation);
        _screenToWorld = matrix;
        _baseRay = CreateBaseRay(matrix, nearPlane, farPlane);
    }

    public Film Film { get; }
    public int Width => Film.Width;
    public int Height => Film.Height;
    public double ScaleWidth { get; }
    public double ScaleHeight { get; }
    public Mat4d ScreenToWorld => _screenToWorld;
    public Rayd BaseRay => _baseRay;
    public ref Film.Rgba Pixel(int x, int y) => ref Film[x, y];

    /// <summary>Rotates the camera toward a point. Degenerate orientation leaves the camera unchanged.</summary>
    public void LookAt(Vec3d target, Vec3d? up = null)
    {
        Vec3d origin = _screenToWorld.TransformPoint(Vec3d.Zero);
        Vec3d direction = origin - target;
        try
        {
            Mat4d matrix = Aim(direction, up ?? Vec3d.UnitY) * Mat4d.CreateTranslation(origin);
            Rayd ray = CreateBaseRay(matrix, _baseRay.MinTime, _baseRay.MaxTime);
            _screenToWorld = matrix;
            _baseRay = ray;
        }
        catch (Exception exception) when (exception is ArithmeticException or ArgumentException)
        {
            // RayTracer.h intentionally ignores invalid look-at orientations.
        }
    }

    public Vec3d RasterToScreen(double x, double y, double z) => new(
        (2 * x / Film.Width - 1) * ScaleWidth,
        (1 - 2 * y / Film.Height) * ScaleHeight,
        z);

    public abstract Rayd GetRay(int x, int y, double xOffset = 0.5, double yOffset = 0.5);

    protected void ValidateRaster(int x, int y, double xOffset, double yOffset)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)x, (uint)Width, nameof(x));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)y, (uint)Height, nameof(y));
        if (!double.IsFinite(xOffset)) throw new ArgumentOutOfRangeException(nameof(xOffset));
        if (!double.IsFinite(yOffset)) throw new ArgumentOutOfRangeException(nameof(yOffset));
    }

    private static Rayd CreateBaseRay(Mat4d matrix, double nearPlane, double farPlane) =>
        new(matrix.TransformPoint(Vec3d.Zero), matrix.TransformDirection(-Vec3d.UnitZ), nearPlane, farPlane);

    private static Mat4d Aim(Vec3d direction, Vec3d vertical)
    {
        Vec3d forward = direction.Unit();
        Vec3d horizontal = vertical.Unit().Cross(forward).Unit();
        Vec3d up = forward.Cross(horizontal).Unit();
        return new(
            horizontal.X, horizontal.Y, horizontal.Z, 0,
            up.X, up.Y, up.Z, 0,
            forward.X, forward.Y, forward.Z, 0,
            0, 0, 0, 1);
    }
}

public sealed class PerspectiveCamera : BaseCamera
{
    public PerspectiveCamera(
        Film film,
        Vec3d rotation = default,
        Vec3d translation = default,
        double focalLength = 50,
        double aperture = 41.2136,
        double nearPlane = 1e-3,
        double farPlane = double.MaxValue)
        : base(film, rotation, translation, FrameWidth(focalLength, aperture), nearPlane, farPlane)
    {
        FocalLength = focalLength;
        Aperture = aperture;
    }

    public double FocalLength { get; }
    public double Aperture { get; }

    public override Rayd GetRay(int x, int y, double xOffset = 0.5, double yOffset = 0.5)
    {
        ValidateRaster(x, y, xOffset, yOffset);
        Vec3d direction = ScreenToWorld.TransformDirection(RasterToScreen(x + xOffset, y + yOffset, -1));
        direction.TryNormalize(out direction);
        Rayd ray = BaseRay;
        double scale = 1.0 / direction.Dot(ray.Direction);
        return new(ray.Eye, direction, ray.MinTime * scale, ray.MaxTime * scale);
    }

    public static double FocalLengthToFieldOfView(double length, double aperture) =>
        360.0 / System.Math.PI * System.Math.Atan(aperture / (2.0 * length));

    public static double FieldOfViewToFocalLength(double fieldOfView, double aperture) =>
        aperture / (2.0 * System.Math.Tan(fieldOfView * System.Math.PI / 360.0));

    private static double FrameWidth(double focalLength, double aperture)
    {
        if (!double.IsFinite(focalLength) || focalLength <= 0) throw new ArgumentOutOfRangeException(nameof(focalLength));
        if (!double.IsFinite(aperture) || aperture <= 0) throw new ArgumentOutOfRangeException(nameof(aperture));
        return 0.5 * aperture / focalLength;
    }
}

public sealed class OrthographicCamera : BaseCamera
{
    public OrthographicCamera(
        Film film,
        Vec3d rotation = default,
        Vec3d translation = default,
        double frameWidth = 1,
        double nearPlane = 1e-3,
        double farPlane = double.MaxValue)
        : base(film, rotation, translation, HalfFrameWidth(frameWidth), nearPlane, farPlane)
    {
        FrameWidth = frameWidth;
    }

    public double FrameWidth { get; }

    public override Rayd GetRay(int x, int y, double xOffset = 0.5, double yOffset = 0.5)
    {
        ValidateRaster(x, y, xOffset, yOffset);
        Rayd ray = BaseRay;
        Vec3d eye = ScreenToWorld.TransformPoint(RasterToScreen(x + xOffset, y + yOffset, 0));
        return new(eye, ray.Direction, ray.MinTime, ray.MaxTime);
    }

    private static double HalfFrameWidth(double frameWidth)
    {
        if (!double.IsFinite(frameWidth) || frameWidth <= 0) throw new ArgumentOutOfRangeException(nameof(frameWidth));
        return 0.5 * frameWidth;
    }
}
