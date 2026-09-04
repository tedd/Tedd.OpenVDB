// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Vector arithmetic and magnitude/lexicographic selection from tools/Composite.h.
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tools;

public static partial class Composite
{
    public static void CompSum(VdbTree<Vec2d> a, VdbTree<Vec2d> b) => a.Combine(b, static (Vec2d x, Vec2d y) => x + y);
    public static void CompSum(Grid<Vec2d> a, Grid<Vec2d> b) { ValidateGrids(a, b); CompSum(a.Tree, b.Tree); }
    public static void CompMul(VdbTree<Vec2d> a, VdbTree<Vec2d> b) => a.Combine(b, static (Vec2d x, Vec2d y) => x * y);
    public static void CompMul(Grid<Vec2d> a, Grid<Vec2d> b) { ValidateGrids(a, b); CompMul(a.Tree, b.Tree); }
    public static void CompDiv(VdbTree<Vec2d> a, VdbTree<Vec2d> b) => a.Combine(b, static (Vec2d x, Vec2d y) => x / y);
    public static void CompDiv(Grid<Vec2d> a, Grid<Vec2d> b) { ValidateGrids(a, b); CompDiv(a.Tree, b.Tree); }
    public static void CompMin(VdbTree<Vec2d> a, VdbTree<Vec2d> b) => a.Combine(b, static (Vec2d x, Vec2d y) => VectorMin(x, y));
    public static void CompMin(Grid<Vec2d> a, Grid<Vec2d> b) { ValidateGrids(a, b); CompMin(a.Tree, b.Tree); }
    public static void CompMax(VdbTree<Vec2d> a, VdbTree<Vec2d> b) => a.Combine(b, static (Vec2d x, Vec2d y) => VectorMax(x, y));
    public static void CompMax(Grid<Vec2d> a, Grid<Vec2d> b) { ValidateGrids(a, b); CompMax(a.Tree, b.Tree); }
    private static Vec2d VectorMin(Vec2d a, Vec2d b)
    {
        var left = a.LengthSquared; var right = b.LengthSquared;
        if (left < right) return a;
        if (right < left) return b;
        if (a.X != b.X) return a.X < b.X ? a : b;
        return a.Y < b.Y ? a : b;
    }
    private static Vec2d VectorMax(Vec2d a, Vec2d b)
    {
        var left = a.LengthSquared; var right = b.LengthSquared;
        if (left > right) return a;
        if (right > left) return b;
        if (a.X != b.X) return a.X > b.X ? a : b;
        return a.Y > b.Y ? a : b;
    }

    public static void CompSum(VdbTree<Vec3d> a, VdbTree<Vec3d> b) => a.Combine(b, static (Vec3d x, Vec3d y) => x + y);
    public static void CompSum(Grid<Vec3d> a, Grid<Vec3d> b) { ValidateGrids(a, b); CompSum(a.Tree, b.Tree); }
    public static void CompMul(VdbTree<Vec3d> a, VdbTree<Vec3d> b) => a.Combine(b, static (Vec3d x, Vec3d y) => x * y);
    public static void CompMul(Grid<Vec3d> a, Grid<Vec3d> b) { ValidateGrids(a, b); CompMul(a.Tree, b.Tree); }
    public static void CompDiv(VdbTree<Vec3d> a, VdbTree<Vec3d> b) => a.Combine(b, static (Vec3d x, Vec3d y) => x / y);
    public static void CompDiv(Grid<Vec3d> a, Grid<Vec3d> b) { ValidateGrids(a, b); CompDiv(a.Tree, b.Tree); }
    public static void CompMin(VdbTree<Vec3d> a, VdbTree<Vec3d> b) => a.Combine(b, static (Vec3d x, Vec3d y) => VectorMin(x, y));
    public static void CompMin(Grid<Vec3d> a, Grid<Vec3d> b) { ValidateGrids(a, b); CompMin(a.Tree, b.Tree); }
    public static void CompMax(VdbTree<Vec3d> a, VdbTree<Vec3d> b) => a.Combine(b, static (Vec3d x, Vec3d y) => VectorMax(x, y));
    public static void CompMax(Grid<Vec3d> a, Grid<Vec3d> b) { ValidateGrids(a, b); CompMax(a.Tree, b.Tree); }
    private static Vec3d VectorMin(Vec3d a, Vec3d b)
    {
        var left = a.LengthSquared; var right = b.LengthSquared;
        if (left < right) return a;
        if (right < left) return b;
        if (a.X != b.X) return a.X < b.X ? a : b;
        if (a.Y != b.Y) return a.Y < b.Y ? a : b;
        return a.Z < b.Z ? a : b;
    }
    private static Vec3d VectorMax(Vec3d a, Vec3d b)
    {
        var left = a.LengthSquared; var right = b.LengthSquared;
        if (left > right) return a;
        if (right > left) return b;
        if (a.X != b.X) return a.X > b.X ? a : b;
        if (a.Y != b.Y) return a.Y > b.Y ? a : b;
        return a.Z > b.Z ? a : b;
    }

    public static void CompSum(VdbTree<Vec3f> a, VdbTree<Vec3f> b) => a.Combine(b, static (Vec3f x, Vec3f y) => x + y);
    public static void CompSum(Grid<Vec3f> a, Grid<Vec3f> b) { ValidateGrids(a, b); CompSum(a.Tree, b.Tree); }
    public static void CompMul(VdbTree<Vec3f> a, VdbTree<Vec3f> b) => a.Combine(b, static (Vec3f x, Vec3f y) => x * y);
    public static void CompMul(Grid<Vec3f> a, Grid<Vec3f> b) { ValidateGrids(a, b); CompMul(a.Tree, b.Tree); }
    public static void CompDiv(VdbTree<Vec3f> a, VdbTree<Vec3f> b) => a.Combine(b, static (Vec3f x, Vec3f y) => x / y);
    public static void CompDiv(Grid<Vec3f> a, Grid<Vec3f> b) { ValidateGrids(a, b); CompDiv(a.Tree, b.Tree); }
    public static void CompMin(VdbTree<Vec3f> a, VdbTree<Vec3f> b) => a.Combine(b, static (Vec3f x, Vec3f y) => VectorMin(x, y));
    public static void CompMin(Grid<Vec3f> a, Grid<Vec3f> b) { ValidateGrids(a, b); CompMin(a.Tree, b.Tree); }
    public static void CompMax(VdbTree<Vec3f> a, VdbTree<Vec3f> b) => a.Combine(b, static (Vec3f x, Vec3f y) => VectorMax(x, y));
    public static void CompMax(Grid<Vec3f> a, Grid<Vec3f> b) { ValidateGrids(a, b); CompMax(a.Tree, b.Tree); }
    private static Vec3f VectorMin(Vec3f a, Vec3f b)
    {
        var left = a.LengthSquared; var right = b.LengthSquared;
        if (left < right) return a;
        if (right < left) return b;
        if (a.X != b.X) return a.X < b.X ? a : b;
        if (a.Y != b.Y) return a.Y < b.Y ? a : b;
        return a.Z < b.Z ? a : b;
    }
    private static Vec3f VectorMax(Vec3f a, Vec3f b)
    {
        var left = a.LengthSquared; var right = b.LengthSquared;
        if (left > right) return a;
        if (right > left) return b;
        if (a.X != b.X) return a.X > b.X ? a : b;
        if (a.Y != b.Y) return a.Y > b.Y ? a : b;
        return a.Z > b.Z ? a : b;
    }

    public static void CompSum(VdbTree<Vec3i> a, VdbTree<Vec3i> b) => a.Combine(b, static (Vec3i x, Vec3i y) => x + y);
    public static void CompSum(Grid<Vec3i> a, Grid<Vec3i> b) { ValidateGrids(a, b); CompSum(a.Tree, b.Tree); }
    public static void CompMul(VdbTree<Vec3i> a, VdbTree<Vec3i> b) => a.Combine(b, static (Vec3i x, Vec3i y) => x * y);
    public static void CompMul(Grid<Vec3i> a, Grid<Vec3i> b) { ValidateGrids(a, b); CompMul(a.Tree, b.Tree); }
    public static void CompDiv(VdbTree<Vec3i> a, VdbTree<Vec3i> b) => a.Combine(b, static (Vec3i x, Vec3i y) => x / y);
    public static void CompDiv(Grid<Vec3i> a, Grid<Vec3i> b) { ValidateGrids(a, b); CompDiv(a.Tree, b.Tree); }
    public static void CompMin(VdbTree<Vec3i> a, VdbTree<Vec3i> b) => a.Combine(b, static (Vec3i x, Vec3i y) => VectorMin(x, y));
    public static void CompMin(Grid<Vec3i> a, Grid<Vec3i> b) { ValidateGrids(a, b); CompMin(a.Tree, b.Tree); }
    public static void CompMax(VdbTree<Vec3i> a, VdbTree<Vec3i> b) => a.Combine(b, static (Vec3i x, Vec3i y) => VectorMax(x, y));
    public static void CompMax(Grid<Vec3i> a, Grid<Vec3i> b) { ValidateGrids(a, b); CompMax(a.Tree, b.Tree); }
    private static Vec3i VectorMin(Vec3i a, Vec3i b)
    {
        var left = a.LengthSquared; var right = b.LengthSquared;
        if (left < right) return a;
        if (right < left) return b;
        if (a.X != b.X) return a.X < b.X ? a : b;
        if (a.Y != b.Y) return a.Y < b.Y ? a : b;
        return a.Z < b.Z ? a : b;
    }
    private static Vec3i VectorMax(Vec3i a, Vec3i b)
    {
        var left = a.LengthSquared; var right = b.LengthSquared;
        if (left > right) return a;
        if (right > left) return b;
        if (a.X != b.X) return a.X > b.X ? a : b;
        if (a.Y != b.Y) return a.Y > b.Y ? a : b;
        return a.Z > b.Z ? a : b;
    }

    public static void CompSum(VdbTree<Vec4d> a, VdbTree<Vec4d> b) => a.Combine(b, static (Vec4d x, Vec4d y) => x + y);
    public static void CompSum(Grid<Vec4d> a, Grid<Vec4d> b) { ValidateGrids(a, b); CompSum(a.Tree, b.Tree); }
    public static void CompMul(VdbTree<Vec4d> a, VdbTree<Vec4d> b) => a.Combine(b, static (Vec4d x, Vec4d y) => x * y);
    public static void CompMul(Grid<Vec4d> a, Grid<Vec4d> b) { ValidateGrids(a, b); CompMul(a.Tree, b.Tree); }
    public static void CompDiv(VdbTree<Vec4d> a, VdbTree<Vec4d> b) => a.Combine(b, static (Vec4d x, Vec4d y) => x / y);
    public static void CompDiv(Grid<Vec4d> a, Grid<Vec4d> b) { ValidateGrids(a, b); CompDiv(a.Tree, b.Tree); }
    public static void CompMin(VdbTree<Vec4d> a, VdbTree<Vec4d> b) => a.Combine(b, static (Vec4d x, Vec4d y) => VectorMin(x, y));
    public static void CompMin(Grid<Vec4d> a, Grid<Vec4d> b) { ValidateGrids(a, b); CompMin(a.Tree, b.Tree); }
    public static void CompMax(VdbTree<Vec4d> a, VdbTree<Vec4d> b) => a.Combine(b, static (Vec4d x, Vec4d y) => VectorMax(x, y));
    public static void CompMax(Grid<Vec4d> a, Grid<Vec4d> b) { ValidateGrids(a, b); CompMax(a.Tree, b.Tree); }
    private static Vec4d VectorMin(Vec4d a, Vec4d b)
    {
        var left = a.LengthSquared; var right = b.LengthSquared;
        if (left < right) return a;
        if (right < left) return b;
        if (a.X != b.X) return a.X < b.X ? a : b;
        if (a.Y != b.Y) return a.Y < b.Y ? a : b;
        if (a.Z != b.Z) return a.Z < b.Z ? a : b;
        return a.W < b.W ? a : b;
    }
    private static Vec4d VectorMax(Vec4d a, Vec4d b)
    {
        var left = a.LengthSquared; var right = b.LengthSquared;
        if (left > right) return a;
        if (right > left) return b;
        if (a.X != b.X) return a.X > b.X ? a : b;
        if (a.Y != b.Y) return a.Y > b.Y ? a : b;
        if (a.Z != b.Z) return a.Z > b.Z ? a : b;
        return a.W > b.W ? a : b;
    }

}
