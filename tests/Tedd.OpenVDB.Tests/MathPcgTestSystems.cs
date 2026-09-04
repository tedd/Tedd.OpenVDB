using System.Numerics;
using Pcg = Tedd.OpenVDB.Math.Pcg;
namespace Tedd.OpenVDB.Tests;

internal static class PcgTestSystems
{
    internal static Pcg.SparseStencilMatrix<T> Build<T>(int profile) where T : unmanaged, IFloatingPointIeee754<T>
    {
        int n = profile == 0 ? 5 : profile == 1 ? 9 : profile == 2 ? 17 : profile == 3 ? 27 : profile == 4 ? 1331 : 63;
        var matrix = new Pcg.SparseStencilMatrix<T>(n, 7);
        void Set(int row, int column, double value) => matrix.SetValue(row, column, T.CreateTruncating(value));
        if (profile == 0)
        {
            Set(0, 0, 24); Set(0, 2, 6); Set(1, 1, 8); Set(1, 2, 2); Set(2, 0, 6); Set(2, 1, 2); Set(2, 2, 8);
            Set(2, 3, -6); Set(2, 4, 2); Set(3, 2, -6); Set(3, 3, 24); Set(4, 2, 2); Set(4, 4, 8); return matrix;
        }
        for (int row = 0; row < n; ++row)
        {
            if (profile == 1) { matrix[row, row] = T.CreateTruncating((row + 1) * (row + 1)) / T.CreateTruncating(3); continue; }
            if (profile == 2 || profile == 5)
            {
                Set(row, row, profile == 2 ? 3 : 2.001);
                if (row > 0) Set(row, row - 1, -1); if (row + 1 < n) Set(row, row + 1, -1); continue;
            }
            int d = profile == 3 ? 3 : 11, x = row / (d * d), y = row / d % d, z = row % d;
            Set(row, row, 7);
            if (x > 0) Set(row, row - d * d, -1); if (x + 1 < d) Set(row, row + d * d, -1);
            if (y > 0) Set(row, row - d, -1); if (y + 1 < d) Set(row, row + d, -1);
            if (z > 0) Set(row, row - 1, -1); if (z + 1 < d) Set(row, row + 1, -1);
        }
        return matrix;
    }
}
