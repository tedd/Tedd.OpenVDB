#pragma once
#pragma managed(push, on)
namespace Tedd {
	namespace OpenVDB {

		[System::Diagnostics::DebuggerDisplay("{X}, {Y}, {Z}")]
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

			virtual System::String^ ToString() override
			{
				return X.ToString("n2") + "," + Y.ToString("n2") + "," + Z.ToString("n2");
			}

			static BlockPos __clrcall operator+ (BlockPos left, const BlockPos right)
			{
				return BlockPos(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
			}
		};


		public value struct Box {
		public:
			Box(int xmin, int ymin, int zmin, int xmax, int ymax, int zmax)
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