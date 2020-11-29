#pragma once
#pragma managed(push, on)
namespace Tedd {
	namespace OpenVDB {

		public value struct BlockPos {
		public:
			BlockPos(int x, int y, int z) {
				X = x;
				Y = y;
				Z = z;
			}
			int X;
			int Y;
			int Z;
		};

		public value struct Box {
		public:
			Box(int xmin, int ymin, int zmin, int xmax,int ymax,int zmax)
			{
				xMin = xmin;
				yMin = ymin;
				zMin = zmin;
				xMax = xmax;
				yMax = ymax;
				zMax = zmax;
			}
			
			int xMin;
			int yMin;
			int zMin;
			int xMax;
			int yMax;
			int zMax;
		};
	}
}
#pragma managed(pop)