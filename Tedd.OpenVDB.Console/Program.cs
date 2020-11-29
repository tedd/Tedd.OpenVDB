using System;

namespace Tedd.OpenVDB.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            //Console.WriteLine("Hello World!");
            Tedd.OpenVDB.OpenVDB.initialize();
            var grid = new Tedd.OpenVDB.Int32Grid(0);
            System.Console.WriteLine(grid.activeVoxelCount());
            grid.set(new BlockPos(1,2,3), 50);
            System.Console.WriteLine(grid.activeVoxelCount());
            System.Console.WriteLine(grid.get(new BlockPos(1, 2, 3)));

            //Tedd.OpenVDB.OpenVDB.
        }
        // https://stackoverflow.com/questions/9631276/c-cli-could-not-load-file-or-assembly-x-or-one-of-its-dependencies-is-not-a
    }
}
