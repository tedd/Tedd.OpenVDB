// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/RayTracer.h.
using System.Numerics;
using Tedd.OpenVDB.Math;

namespace Tedd.OpenVDB.Tools;

/// <summary>Reference single-scattering CPU renderer for sparse float and double density volumes.</summary>
/// <remarks>The source grid and camera must remain unchanged during rendering.</remarks>
public class VolumeRender<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private VolumeRayIntersector<T> _intersector;
    private BaseCamera _camera;
    private double _primaryStep = 1;
    private double _shadowStep = 3;
    private double _cutoff = 0.005;
    private double _lightGain = 0.2;
    private Vec3d _lightDirection = new Vec3d(0.3, 0.3, 0).Unit();
    private Vec3d _lightColor = new(0.7);
    private Vec3d _absorption = new(0.1);
    private Vec3d _scattering = new(1.5);

    public VolumeRender(VolumeRayIntersector<T> intersector, BaseCamera camera)
    {
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double))
            throw new NotSupportedException("Volume rendering supports float and double grids.");
        _intersector = new(intersector ?? throw new ArgumentNullException(nameof(intersector)));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    }

    public BaseCamera Camera => _camera;
    public double PrimaryStep { get => _primaryStep; set => _primaryStep = ValidateStep(value, nameof(value)); }
    public double ShadowStep { get => _shadowStep; set => _shadowStep = ValidateStep(value, nameof(value)); }
    public double Cutoff { get => _cutoff; set => _cutoff = value; }
    public double LightGain { get => _lightGain; set => _lightGain = value; }
    public Vec3d LightDirection => _lightDirection;
    public Vec3d LightColor { get => _lightColor; set => _lightColor = value; }
    public Vec3d Absorption { get => _absorption; set => _absorption = value; }
    public Vec3d Scattering { get => _scattering; set => _scattering = value; }

    public void SetCamera(BaseCamera camera) => _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    public void SetIntersector(VolumeRayIntersector<T> intersector) => _intersector = new(intersector ?? throw new ArgumentNullException(nameof(intersector)));
    public void SetLightDirection(Vec3d direction)
    {
        if (!direction.IsFinite) throw new ArgumentException("Light direction must be finite.", nameof(direction));
        _lightDirection = direction.Unit();
    }

    public void Render(bool threaded = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!threaded || _camera.Height < 2)
        {
            var worker = new Worker(_intersector);
            RenderRows(0, _camera.Height, worker, cancellationToken);
            return;
        }
        var options = new ParallelOptions { CancellationToken = cancellationToken };
        Parallel.For(
            0,
            _camera.Height,
            options,
            () => new Worker(_intersector),
            (row, _, worker) => { RenderRows(row, row + 1, worker, cancellationToken); return worker; },
            static _ => { });
    }

    private void RenderRows(int start, int end, Worker worker, CancellationToken token)
    {
        Vec3d extinction = -_scattering - _absorption, one = Vec3d.One;
        Vec3d albedo = _lightColor * _scattering / (_scattering + _absorption);
        double primaryStep = _primaryStep, shadowStep = _shadowStep, cutoff = _cutoff, gain = _lightGain;
        BaseCamera camera = _camera;
        for (int y = start; y < end; ++y)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < camera.Width; ++x)
            {
                ref Film.Rgba destination = ref camera.Pixel(x, y);
                destination = new Film.Rgba(0f, 0f, 0f, 0f);
                Rayd primaryRay = camera.GetRay(x, y);
                if (!worker.Primary.SetWorldRay(primaryRay)) continue;
                Vec3d primaryTransmittance = Vec3d.One, luminance = Vec3d.Zero;
                worker.Primary.Hits(worker.PrimarySpans);
                bool terminatePrimary = false;
                foreach (RayTimeSpan primarySpan in worker.PrimarySpans)
                {
                    double primaryTime = primaryStep * System.Math.Ceiling(primarySpan.Start / primaryStep);
                    while (primaryTime <= primarySpan.End)
                    {
                        Vec3d primaryPosition = worker.Primary.GetWorldPosition(primaryTime);
                        double density = double.CreateTruncating(worker.Sampler.WorldSample(primaryPosition));
                        if (!(density < cutoff))
                        {
                            Vec3d differentialTransmittance = (extinction * density * primaryStep).Exp();
                            Vec3d shadowTransmittance = Vec3d.One;
                            var shadowRay = new Rayd(primaryPosition, _lightDirection);
                            if (worker.Shadow.SetWorldRay(shadowRay))
                            {
                                worker.Shadow.Hits(worker.ShadowSpans);
                                bool terminateShadow = false;
                                foreach (RayTimeSpan shadowSpan in worker.ShadowSpans)
                                {
                                    double shadowTime = shadowStep * System.Math.Ceiling(shadowSpan.Start / shadowStep);
                                    while (shadowTime <= shadowSpan.End)
                                    {
                                        double shadowDensity = double.CreateTruncating(worker.Sampler.WorldSample(worker.Shadow.GetWorldPosition(shadowTime)));
                                        if (!(shadowDensity < cutoff))
                                        {
                                            shadowTransmittance *= (extinction * shadowDensity * shadowStep / (1.0 + shadowTime * gain)).Exp();
                                            if (shadowTransmittance.LengthSquared < cutoff) { terminateShadow = true; break; }
                                        }
                                        double nextShadow = shadowTime + shadowStep;
                                        if (nextShadow <= shadowTime) break;
                                        shadowTime = nextShadow;
                                    }
                                    if (terminateShadow) break;
                                }
                                luminance += albedo * shadowTransmittance * primaryTransmittance * (one - differentialTransmittance);
                                primaryTransmittance *= differentialTransmittance;
                                if (primaryTransmittance.LengthSquared < cutoff) terminatePrimary = true;
                            }
                        }
                        if (terminatePrimary) break;
                        double nextPrimary = primaryTime + primaryStep;
                        if (nextPrimary <= primaryTime) break;
                        primaryTime = nextPrimary;
                    }
                    if (terminatePrimary) break;
                }
                destination.R = (float)luminance.X;
                destination.G = (float)luminance.Y;
                destination.B = (float)luminance.Z;
                destination.A = (float)(1.0 - primaryTransmittance.Sum / 3.0);
            }
        }
    }

    private static double ValidateStep(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(parameterName, "Integration steps must be finite and positive.");
        return value;
    }

    private sealed class Worker
    {
        internal Worker(VolumeRayIntersector<T> source)
        {
            Primary = new(source);
            Shadow = new(source);
            Sampler = new(source.Grid, SamplingOrder.Box);
        }
        internal VolumeRayIntersector<T> Primary { get; }
        internal VolumeRayIntersector<T> Shadow { get; }
        internal GridSampler<T> Sampler { get; }
        internal List<RayTimeSpan> PrimarySpans { get; } = [];
        internal List<RayTimeSpan> ShadowSpans { get; } = [];
    }
}

/// <summary>Descriptive alias for <see cref="VolumeRender{T}"/>.</summary>
public sealed class VolumeRenderer<T> : VolumeRender<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    public VolumeRenderer(VolumeRayIntersector<T> intersector, BaseCamera camera) : base(intersector, camera) { }
}