using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tedd.OpenVDB.Legacy.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            Tedd.OpenVDB.OpenVDB.initialize();
            var grid = new Tedd.OpenVDB.Int32Grid(0);
            System.Console.WriteLine(grid.activeVoxelCount());
            grid.set(new BlockPos(1, 2, 3), 50);
            System.Console.WriteLine(grid.activeVoxelCount());
            System.Console.WriteLine(grid.get(new BlockPos(1, 2, 3)));
        }
    }
}
