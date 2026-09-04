// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/RayTracer.h.
using System.Runtime.InteropServices;
using System.Text;

namespace Tedd.OpenVDB.Tools;

/// <summary>A float RGBA image whose distinct pixels can be written concurrently.</summary>
public sealed class Film
{
    /// <summary>OpenVDB's float color representation. Color-producing operators reset alpha to one.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Rgba : IEquatable<Rgba>
    {
        public Rgba() { R = G = B = 0; A = 1; }
        public Rgba(float intensity) { R = G = B = intensity; A = 1; }
        public Rgba(double intensity) : this((float)intensity) { }
        public Rgba(float red, float green, float blue, float alpha = 1) { R = red; G = green; B = blue; A = alpha; }
        public Rgba(double red, double green, double blue, double alpha = 1)
            : this((float)red, (float)green, (float)blue, (float)alpha) { }

        public float R;
        public float G;
        public float B;
        public float A;

        public static Rgba operator *(Rgba color, float scale) => new(color.R * scale, color.G * scale, color.B * scale);
        public static Rgba operator *(float scale, Rgba color) => color * scale;
        public static Rgba operator +(Rgba left, Rgba right) => new(left.R + right.R, left.G + right.G, left.B + right.B);
        public static Rgba operator *(Rgba left, Rgba right) => new(left.R * right.R, left.G * right.G, left.B * right.B);
        public static Rgba operator +(Rgba left, float value) => new(left.R + value, left.G + value, left.B + value);

        /// <summary>Adds every channel, including alpha, as used by native subpixel accumulation.</summary>
        public void Add(Rgba other) { R += other.R; G += other.G; B += other.B; A += other.A; }

        /// <summary>Composites <paramref name="other"/> behind this premultiplied color.</summary>
        public void Over(Rgba other)
        {
            float scale = other.A * (1.0f - A);
            R = A * R + scale * other.R;
            G = A * G + scale * other.G;
            B = A * B + scale * other.B;
            A += scale;
        }

        public readonly bool Equals(Rgba other) => R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);
        public override readonly bool Equals(object? obj) => obj is Rgba other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(R, G, B, A);
        public static bool operator ==(Rgba left, Rgba right) => left.Equals(right);
        public static bool operator !=(Rgba left, Rgba right) => !left.Equals(right);
        public override readonly string ToString() => $"[{R}, {G}, {B}, {A}]";
    }

    private readonly Rgba[] _pixels;

    public Film(int width, int height) : this(width, height, new Rgba()) { }

    public Film(int width, int height, Rgba background)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Width = width;
        Height = height;
        _pixels = new Rgba[checked(width * height)];
        Array.Fill(_pixels, background);
    }

    public int Width { get; }
    public int Height { get; }
    public int PixelCount => _pixels.Length;
    public Span<Rgba> Pixels => _pixels;
    public ReadOnlySpan<Rgba> ReadOnlyPixels => _pixels;

    public ref Rgba this[int x, int y]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)x, (uint)Width, nameof(x));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)y, (uint)Height, nameof(y));
            return ref _pixels[x + y * Width];
        }
    }

    public void Fill() => Fill(new Rgba(0f));
    public void Fill(Rgba color) => Array.Fill(_pixels, color);

    public void Checkerboard(Rgba? color1 = null, Rgba? color2 = null, int size = 32)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        Rgba first = color1 ?? new Rgba(0.3f), second = color2 ?? new Rgba(0.6f);
        Rgba[] pixels = _pixels;
        int width = Width, height = Height, index = 0;
        for (int y = 0; y < height; ++y)
            for (int x = 0; x < width; ++x)
                pixels[index++] = ((x & size) ^ (y & size)) != 0 ? first : second;
    }

    /// <summary>Converts channels in the documented [0, 1] domain using native truncation.</summary>
    public byte[] ConvertToByteBuffer(bool includeAlpha = true)
    {
        int channels = includeAlpha ? 4 : 3;
        byte[] destination = GC.AllocateUninitializedArray<byte>(checked(_pixels.Length * channels));
        int offset = 0;
        foreach (Rgba pixel in _pixels)
        {
            destination[offset++] = ConvertChannel(pixel.R);
            destination[offset++] = ConvertChannel(pixel.G);
            destination[offset++] = ConvertChannel(pixel.B);
            if (includeAlpha) destination[offset++] = ConvertChannel(pixel.A);
        }
        return destination;
    }

    public void WritePpm(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        byte[] header = Encoding.ASCII.GetBytes($"P6\n{Width} {Height}\n255\n");
        destination.Write(header);
        destination.Write(ConvertToByteBuffer(includeAlpha: false));
    }

    /// <summary>Writes P6 data, appending .ppm only when the supplied name contains no dot.</summary>
    public string SavePpm(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        string path = fileName.Contains('.') ? fileName : fileName + ".ppm";
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        WritePpm(stream);
        return path;
    }

    private static byte ConvertChannel(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(nameof(value), "Film channels must be finite and in [0, 1].");
        return (byte)(255.0f * value);
    }
}
