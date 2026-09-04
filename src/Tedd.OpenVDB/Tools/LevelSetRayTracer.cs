// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/RayTracer.h.
using System.Numerics;
using Tedd.OpenVDB.Math;

namespace Tedd.OpenVDB.Tools;

/// <summary>Reference CPU ray tracer for float and double narrow-band level sets.</summary>
/// <remarks>The grid, camera and shader inputs must remain unchanged during rendering.</remarks>
public sealed class LevelSetRayTracer<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private LevelSetRayIntersector<T> _intersector;
    private BaseShader _shader;
    private BaseCamera _camera;
    private double[]? _randomOffsets;
    private int _subPixels;

    public LevelSetRayTracer(Grid<T> grid, BaseShader shader, BaseCamera camera, int pixelSamples = 1, uint seed = 0)
        : this(new LevelSetRayIntersector<T>(grid ?? throw new ArgumentNullException(nameof(grid))), shader, camera, pixelSamples, seed) { }

    public LevelSetRayTracer(LevelSetRayIntersector<T> intersector, BaseShader shader, BaseCamera camera, int pixelSamples = 1, uint seed = 0)
    {
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double))
            throw new NotSupportedException("Level-set ray tracing supports float and double grids.");
        _intersector = new(intersector ?? throw new ArgumentNullException(nameof(intersector)));
        _shader = (shader ?? throw new ArgumentNullException(nameof(shader))).Copy();
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        SetPixelSamples(pixelSamples, seed);
    }

    public BaseCamera Camera => _camera;
    public int PixelSamples => _subPixels + 1;

    public void SetGrid(Grid<T> grid) => _intersector = new(grid ?? throw new ArgumentNullException(nameof(grid)));
    public void SetIntersector(LevelSetRayIntersector<T> intersector) => _intersector = new(intersector ?? throw new ArgumentNullException(nameof(intersector)));
    public void SetShader(BaseShader shader) => _shader = (shader ?? throw new ArgumentNullException(nameof(shader))).Copy();
    public void SetCamera(BaseCamera camera) => _camera = camera ?? throw new ArgumentNullException(nameof(camera));

    public void SetPixelSamples(int pixelSamples, uint seed = 0)
    {
        if (pixelSamples <= 0) throw new ArgumentOutOfRangeException(nameof(pixelSamples), "Pixel samples must be larger than zero.");
        _subPixels = pixelSamples - 1;
        if (_subPixels == 0) { _randomOffsets = null; return; }
        var random = new Random01(seed);
        double[] values = GC.AllocateUninitializedArray<double>(16);
        for (int i = 0; i < values.Length; ++i) values[i] = random.Next();
        _randomOffsets = values;
    }

    /// <summary>Renders into the camera film. Parallel supersampling uses a global pixel sequence for deterministic output.</summary>
    public void Render(bool threaded = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!threaded || _camera.Height < 2)
        {
            RenderRows(0, _camera.Height, _intersector, _shader, cancellationToken);
            return;
        }
        var options = new ParallelOptions { CancellationToken = cancellationToken };
        Parallel.For(
            0,
            _camera.Height,
            options,
            () => new Worker(new LevelSetRayIntersector<T>(_intersector), _shader.Copy()),
            (row, _, worker) => { RenderRows(row, row + 1, worker.Intersector, worker.Shader, cancellationToken); return worker; },
            static _ => { });
    }

    private void RenderRows(int start, int end, LevelSetRayIntersector<T> intersector, BaseShader shader, CancellationToken token)
    {
        BaseCamera camera = _camera;
        double[]? random = _randomOffsets;
        int subPixels = _subPixels, width = camera.Width;
        float fraction = 1.0f / (1.0f + (float)subPixels);
        for (int y = start; y < end; ++y)
        {
            token.ThrowIfCancellationRequested();
            long sequence = (long)y * width * subPixels * 2;
            for (int x = 0; x < width; ++x)
            {
                ref Film.Rgba destination = ref camera.Pixel(x, y);
                Film.Rgba background = destination;
                Rayd ray = camera.GetRay(x, y);
                Film.Rgba color = intersector.IntersectsWorld(ray, out Vec3d position, out Vec3d normal, out _)
                    ? shader.Shade(position, normal, ray.Direction)
                    : background;
                for (int sample = 0; sample < subPixels; ++sample, sequence += 2)
                {
                    ray = camera.GetRay(x, y, random![(int)(sequence & 15)], random[(int)((sequence + 1) & 15)]);
                    Film.Rgba contribution = intersector.IntersectsWorld(ray, out position, out normal, out _)
                        ? shader.Shade(position, normal, ray.Direction)
                        : background;
                    color.Add(contribution);
                }
                destination = color * fraction;
            }
        }
    }

    private sealed record Worker(LevelSetRayIntersector<T> Intersector, BaseShader Shader);
}

public static class RayTracer
{
    public static void RayTrace<T>(Grid<T> grid, BaseShader shader, BaseCamera camera, int pixelSamples = 1, uint seed = 0, bool threaded = true, CancellationToken cancellationToken = default)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        new LevelSetRayTracer<T>(grid, shader, camera, pixelSamples, seed).Render(threaded, cancellationToken);
}
