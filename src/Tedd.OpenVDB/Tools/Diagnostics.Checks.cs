// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/Diagnostics.h.
using System.Globalization;
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>A diagnostic predicate: true means that a value fails the check.</summary>
/// <remarks>Override Copy when a predicate owns mutable accessor/stencil state. Vector bounds in the
/// built-in value checks must broadcast one scalar bound, matching native ElementType parameters.</remarks>
public abstract class DiagnosticCheck<T> where T : unmanaged
{
    protected DiagnosticCheck(ValueIterationMode iterationMode = ValueIterationMode.Active)
    {
        if ((uint)iterationMode > (uint)ValueIterationMode.All) throw new ArgumentOutOfRangeException(nameof(iterationMode)); IterationMode = iterationMode;
    }
    public ValueIterationMode IterationMode { get; }
    public abstract string Description { get; }
    public abstract bool Fails(T value);
    public virtual bool FailsVoxel(Coord coordinate, T value) => Fails(value);
    public virtual bool FailsTile(CoordBBox bounds, T value) => Fails(value);
    public virtual DiagnosticCheck<T> Copy() => this;
}
public sealed class CheckNan<T> : DiagnosticCheck<T> where T : unmanaged
{
    public CheckNan(ValueIterationMode mode = ValueIterationMode.Active) : base(mode) => DiagnosticNumeric<T>.Validate();
    public override string Description => "NaN";
    public override bool Fails(T value) { for (int i = 0; i < DiagnosticNumeric<T>.Components; ++i) if (double.IsNaN(DiagnosticNumeric<T>.Component(value, i))) return true; return false; }
}
public sealed class CheckInf<T> : DiagnosticCheck<T> where T : unmanaged
{
    public CheckInf(ValueIterationMode mode = ValueIterationMode.Active) : base(mode) => DiagnosticNumeric<T>.Validate();
    public override string Description => "infinite";
    public override bool Fails(T value) { for (int i = 0; i < DiagnosticNumeric<T>.Components; ++i) if (double.IsInfinity(DiagnosticNumeric<T>.Component(value, i))) return true; return false; }
}
public sealed class CheckFinite<T> : DiagnosticCheck<T> where T : unmanaged
{
    public CheckFinite(ValueIterationMode mode = ValueIterationMode.Active) : base(mode) => DiagnosticNumeric<T>.Validate();
    public override string Description => "not finite";
    public override bool Fails(T value) { for (int i = 0; i < DiagnosticNumeric<T>.Components; ++i) if (!double.IsFinite(DiagnosticNumeric<T>.Component(value, i))) return true; return false; }
}
public sealed class CheckMagnitude<T> : DiagnosticCheck<T> where T : unmanaged
{
    public CheckMagnitude(T magnitude, ValueIterationMode mode = ValueIterationMode.Inactive) : this(magnitude, DiagnosticNumeric<T>.Tolerance(), mode) { }
    public CheckMagnitude(T magnitude, T tolerance, ValueIterationMode mode = ValueIterationMode.Inactive) : base(mode)
    {
        DiagnosticNumeric<T>.ValidateBound(magnitude); DiagnosticNumeric<T>.ValidateBound(tolerance); Magnitude = DiagnosticNumeric<T>.Abs(magnitude); Tolerance = DiagnosticNumeric<T>.Abs(tolerance);
    }
    public T Magnitude { get; }
    public T Tolerance { get; }
    public override string Description => $"not equal to +/-{DiagnosticNumeric<T>.Format(Magnitude)} with a tolerance of {DiagnosticNumeric<T>.Format(Tolerance)}";
    public override bool Fails(T value) => DiagnosticNumeric<T>.MagnitudeFails(value, Magnitude, Tolerance);
}
public sealed class CheckRange<T> : DiagnosticCheck<T> where T : unmanaged
{
    public CheckRange(T minimum, T maximum, bool minimumInclusive = true, bool maximumInclusive = true, ValueIterationMode mode = ValueIterationMode.Active) : base(mode)
    {
        DiagnosticNumeric<T>.ValidateBound(minimum); DiagnosticNumeric<T>.ValidateBound(maximum);
        if (DiagnosticNumeric<T>.Less(maximum, minimum)) throw new ArgumentException("CheckRange: Invalid range (min > max).");
        Minimum = minimum; Maximum = maximum; MinimumInclusive = minimumInclusive; MaximumInclusive = maximumInclusive;
    }
    public T Minimum { get; }
    public T Maximum { get; }
    public bool MinimumInclusive { get; }
    public bool MaximumInclusive { get; }
    public override string Description => $"outside the value range {(MinimumInclusive ? "[" : "]")}{DiagnosticNumeric<T>.Format(Minimum)},{DiagnosticNumeric<T>.Format(Maximum)}{(MaximumInclusive ? "]" : "[")}";
    public override bool Fails(T value)
    {
        for (int i = 0; i < DiagnosticNumeric<T>.Components; ++i)
            if (DiagnosticNumeric<T>.Below(value, Minimum, i, !MinimumInclusive) || DiagnosticNumeric<T>.Below(Maximum, value, i, !MaximumInclusive)) return true;
        return false;
    }
}
public sealed class CheckMin<T> : DiagnosticCheck<T> where T : unmanaged
{
    public CheckMin(T minimum, ValueIterationMode mode = ValueIterationMode.Active) : base(mode) { DiagnosticNumeric<T>.ValidateBound(minimum); Minimum = minimum; }
    public T Minimum { get; }
    public override string Description => $"smaller than {DiagnosticNumeric<T>.Format(Minimum)}";
    public override bool Fails(T value) { for (int i = 0; i < DiagnosticNumeric<T>.Components; ++i) if (DiagnosticNumeric<T>.Below(value, Minimum, i)) return true; return false; }
}
public sealed class CheckMax<T> : DiagnosticCheck<T> where T : unmanaged
{
    public CheckMax(T maximum, ValueIterationMode mode = ValueIterationMode.Active) : base(mode) { DiagnosticNumeric<T>.ValidateBound(maximum); Maximum = maximum; }
    public T Maximum { get; }
    public override string Description => $"larger than {DiagnosticNumeric<T>.Format(Maximum)}";
    public override bool Fails(T value) { for (int i = 0; i < DiagnosticNumeric<T>.Components; ++i) if (DiagnosticNumeric<T>.Below(Maximum, value, i)) return true; return false; }
}

internal static class DiagnosticNumeric<T> where T : unmanaged
{
    internal static int Components => typeof(T) == typeof(Vec2f) || typeof(T) == typeof(Vec2d) || typeof(T) == typeof(Vec2i) ? 2 : typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec3d) || typeof(T) == typeof(Vec3i) ? 3 : typeof(T) == typeof(Vec4f) || typeof(T) == typeof(Vec4d) || typeof(T) == typeof(Vec4i) ? 4 : 1;
    private static bool Single => typeof(T) == typeof(float) || typeof(T) == typeof(Vec2f) || typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec4f);
    private static bool Int32 => typeof(T) == typeof(int) || typeof(T) == typeof(Vec2i) || typeof(T) == typeof(Vec3i) || typeof(T) == typeof(Vec4i);
    private static bool Double => typeof(T) == typeof(double) || typeof(T) == typeof(Vec2d) || typeof(T) == typeof(Vec3d) || typeof(T) == typeof(Vec4d);
    internal static bool FloatingScalar => typeof(T) == typeof(float) || typeof(T) == typeof(double);
    internal static void Validate()
    {
        if (!Single && !Double && !Int32 && typeof(T) != typeof(bool) && typeof(T) != typeof(sbyte) && typeof(T) != typeof(byte) && typeof(T) != typeof(short) && typeof(T) != typeof(ushort) && typeof(T) != typeof(uint) && typeof(T) != typeof(long) && typeof(T) != typeof(ulong) && typeof(T) != typeof(Half)) throw new NotSupportedException($"Diagnostics are not defined for {typeof(T)}.");
    }
    internal static void ValidateBound(T value)
    {
        Validate(); double first = Component(value, 0);
        for (int i = 1; i < Components; ++i) { double c = Component(value, i); if (first != c && !(double.IsNaN(first) && double.IsNaN(c))) throw new ArgumentException("A vector bound must broadcast one scalar component.", nameof(value)); }
    }
    internal static double Component(T value, int index)
    {
        if (Single) return Unsafe.Add(ref Unsafe.As<T, float>(ref value), index);
        if (Double) return Unsafe.Add(ref Unsafe.As<T, double>(ref value), index);
        if (Int32) return Unsafe.Add(ref Unsafe.As<T, int>(ref value), index);
        if (typeof(T) == typeof(Half)) return (float)Unsafe.As<T, Half>(ref value);
        if (typeof(T) == typeof(long)) return Unsafe.As<T, long>(ref value);
        if (typeof(T) == typeof(ulong)) return Unsafe.As<T, ulong>(ref value);
        if (typeof(T) == typeof(uint)) return Unsafe.As<T, uint>(ref value);
        if (typeof(T) == typeof(short)) return Unsafe.As<T, short>(ref value);
        if (typeof(T) == typeof(ushort)) return Unsafe.As<T, ushort>(ref value);
        if (typeof(T) == typeof(sbyte)) return Unsafe.As<T, sbyte>(ref value);
        if (typeof(T) == typeof(byte)) return Unsafe.As<T, byte>(ref value);
        if (typeof(T) == typeof(bool)) return Unsafe.As<T, bool>(ref value) ? 1 : 0;
        throw new NotSupportedException();
    }
    private static void Set(ref T value, int index, double component)
    {
        if (Single) Unsafe.Add(ref Unsafe.As<T, float>(ref value), index) = (float)component;
        else if (Double) Unsafe.Add(ref Unsafe.As<T, double>(ref value), index) = component;
        else if (Int32) Unsafe.Add(ref Unsafe.As<T, int>(ref value), index) = checked((int)component);
        else if (typeof(T) == typeof(Half)) Unsafe.As<T, Half>(ref value) = (Half)(float)component;
        else if (typeof(T) == typeof(long)) Unsafe.As<T, long>(ref value) = checked((long)component);
        else if (typeof(T) == typeof(ulong)) Unsafe.As<T, ulong>(ref value) = checked((ulong)component);
        else if (typeof(T) == typeof(uint)) Unsafe.As<T, uint>(ref value) = checked((uint)component);
        else if (typeof(T) == typeof(short)) Unsafe.As<T, short>(ref value) = checked((short)component);
        else if (typeof(T) == typeof(ushort)) Unsafe.As<T, ushort>(ref value) = checked((ushort)component);
        else if (typeof(T) == typeof(sbyte)) Unsafe.As<T, sbyte>(ref value) = checked((sbyte)component);
        else if (typeof(T) == typeof(byte)) Unsafe.As<T, byte>(ref value) = checked((byte)component);
        else if (typeof(T) == typeof(bool)) Unsafe.As<T, bool>(ref value) = component != 0;
        else throw new NotSupportedException();
    }
    internal static T Tolerance() => Broadcast(Single ? 1e-8f : Double ? 1e-15 : typeof(T) == typeof(Half) ? (float)(Half)0.00097656f : 0);
    internal static T Broadcast(double scalar) { Validate(); T result = default; for (int i = 0; i < Components; ++i) Set(ref result, i, scalar); return result; }
    internal static T Abs(T value)
    {
        if (typeof(T) == typeof(long)) { long v = System.Math.Abs(Unsafe.As<T, long>(ref value)); return Unsafe.As<long, T>(ref v); }
        if (typeof(T) == typeof(ulong) || typeof(T) == typeof(uint)) return value;
        for (int i = 0; i < Components; ++i) Set(ref value, i, System.Math.Abs(Component(value, i))); return value;
    }
    internal static T Negate(T value)
    {
        if (typeof(T) == typeof(long)) { long v = checked(-Unsafe.As<T, long>(ref value)); return Unsafe.As<long, T>(ref v); }
        if (typeof(T) == typeof(ulong)) { ulong v = unchecked(0UL - Unsafe.As<T, ulong>(ref value)); return Unsafe.As<ulong, T>(ref v); }
        for (int i = 0; i < Components; ++i) Set(ref value, i, -Component(value, i)); return value;
    }
    internal static bool Below(T a, T b, int component, bool inclusive = false)
    {
        if (typeof(T) == typeof(long)) { long x = Unsafe.As<T, long>(ref a), y = Unsafe.As<T, long>(ref b); return inclusive ? x <= y : x < y; }
        if (typeof(T) == typeof(ulong)) { ulong x = Unsafe.As<T, ulong>(ref a), y = Unsafe.As<T, ulong>(ref b); return inclusive ? x <= y : x < y; }
        double left = Component(a, component), right = Component(b, component); return inclusive ? left <= right : left < right;
    }
    internal static bool Less(T a, T b)
    {
        for (int i = 0; i < Components - 1; ++i) { if (Component(a, i) != Component(b, i)) return Below(a, b, i); }
        return Below(a, b, Components - 1);
    }
    internal static bool MagnitudeFails(T value, T magnitude, T tolerance)
    {
        if (typeof(T) == typeof(long)) return System.Math.Abs(checked(System.Math.Abs(Unsafe.As<T, long>(ref value)) - Unsafe.As<T, long>(ref magnitude))) > Unsafe.As<T, long>(ref tolerance);
        if (typeof(T) == typeof(ulong)) return unchecked(Unsafe.As<T, ulong>(ref value) - Unsafe.As<T, ulong>(ref magnitude)) > Unsafe.As<T, ulong>(ref tolerance);
        if (typeof(T) == typeof(uint)) return unchecked(Unsafe.As<T, uint>(ref value) - Unsafe.As<T, uint>(ref magnitude)) > Unsafe.As<T, uint>(ref tolerance);
        for (int i = 0; i < Components; ++i)
        {
            double v = System.Math.Abs(Component(value, i)), m = Component(magnitude, i), t = Component(tolerance, i);
            double difference = Single || typeof(T) == typeof(Half) ? (float)v - (float)m : v - m;
            if (System.Math.Abs(difference) > t) return true;
        }
        return false;
    }
    internal static string Format(T value)
    {
        if (typeof(T) == typeof(long)) return Unsafe.As<T, long>(ref value).ToString(CultureInfo.InvariantCulture);
        if (typeof(T) == typeof(ulong)) return Unsafe.As<T, ulong>(ref value).ToString(CultureInfo.InvariantCulture);
        return !Single && !Double && typeof(T) != typeof(Half) ? Component(value, 0).ToString("0", CultureInfo.InvariantCulture) : FormatDouble(Component(value, 0));
    }
    internal static string FormatDouble(double value) => double.IsNaN(value) ? "nan" : double.IsPositiveInfinity(value) ? "inf" : double.IsNegativeInfinity(value) ? "-inf" : value.ToString("G6", CultureInfo.InvariantCulture).Replace('E', 'e');
}
