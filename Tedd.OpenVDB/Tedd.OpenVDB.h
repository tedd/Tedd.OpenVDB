#pragma once
#pragma unmanaged
#include <openvdb/openvdb.h>
#pragma managed(push, on)
#include "Grid.h"
#include <iostream>
#include <openvdb/Types.h>
#include "native_shared_ptr.h"
//using FloatGrid = Tedd.OpenVDB.Grid<FloatTree>;


using namespace System;

namespace Tedd {
	namespace OpenVDB	{

		//// One-dimensional scalar types
		//using Index32 = uint32_t;
		//using Index64 = uint64_t;
		//using Index = Index32;
		//using Int16 = int16_t;
		//using Int32 = int32_t;
		//using Int64 = int64_t;
		//using Int = Int32;
		//using Byte = unsigned char;
		//using Real = double;


		///// Common tree types
		//using BoolTree = tree::Tree4<bool, 5, 4, 3>::Type;
		//using DoubleTree = tree::Tree4<double, 5, 4, 3>::Type;
		//using FloatTree = tree::Tree4<float, 5, 4, 3>::Type;
		//using Int32Tree = tree::Tree4<int32_t, 5, 4, 3>::Type;
		//using Int64Tree = tree::Tree4<int64_t, 5, 4, 3>::Type;
		//using MaskTree = tree::Tree4<ValueMask, 5, 4, 3>::Type;
		//using StringTree = Tree4<std::string, 5, 4, 3>::Type;
		//using UInt32Tree = Tree4<uint32_t, 5, 4, 3>::Type;
		//using Vec2DTree = Tree4<Vec2d, 5, 4, 3>::Type;
		//using Vec2ITree = Tree4<Vec2i, 5, 4, 3>::Type;
		//using Vec2STree = Tree4<Vec2s, 5, 4, 3>::Type;
		//using Vec3DTree = Tree4<Vec3d, 5, 4, 3>::Type;
		//using Vec3ITree = Tree4<Vec3i, 5, 4, 3>::Type;
		//using Vec3STree = Tree4<Vec3f, 5, 4, 3>::Type;
		//using ScalarTree = FloatTree;
		//using TopologyTree = MaskTree;
		//using Vec3dTree = Vec3DTree;
		//using Vec3fTree = Vec3STree;
		//using VectorTree = Vec3fTree;

		///// Common grid types
		//using BoolGrid = Tedd::OpenVDB::Grid<BoolTree>;
		//using DoubleGrid = Grid<DoubleTree>;
		//using FloatGrid = Grid<FloatTree>;
		//using Int32Grid = Grid<Int32Tree>;
		//using Int64Grid = Grid<Int64Tree>;
		//using MaskGrid = Grid<MaskTree>;
		//using StringGrid = Grid<StringTree>;
		//using Vec3DGrid = Grid<Vec3DTree>;
		//using Vec3IGrid = Grid<Vec3ITree>;
		//using Vec3SGrid = Grid<Vec3STree>;
		//using ScalarGrid = FloatGrid;
		//using TopologyGrid = MaskGrid;
		//using Vec3dGrid = Vec3DGrid;
		//using Vec3fGrid = Vec3SGrid;
		//using VectorGrid = Vec3fGrid;

		public ref class OpenVDB abstract sealed
		{
		public:
			static void initialize()
			{
				openvdb::initialize();
				//openvdb::FloatGrid
			}
			
		};

	
		/*public ref class ValueAccessor
		{
			
		};*/
	}
}
#pragma managed(pop)