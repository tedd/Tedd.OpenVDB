#pragma once
#include <openvdb/openvdb.h>
#include "native_shared_ptr.h"
#include <msclr\marshal_cppstd.h>
#pragma managed(push, on)
#include <memory>

#include "Accessor.h"
#include "Structs.h"

#define NAME FloatGrid
using GRIDTYPE = int;
//#include "Grid_Impl.h"

namespace Tedd {
	namespace OpenVDB {
#define NAME Int32Grid
		using GRIDTYPE = int32_t;

		// ***
		using TREE = typename openvdb::tree::Tree4<GRIDTYPE, 5, 4, 3>::Type;
		using GRID = openvdb::Grid<TREE>;
		using ACCESSOR = GRID::Accessor;
		public ref class NAME
		{
		public:
			NAME(GRIDTYPE backgroundValue)
			{
				setGrid(GRID::create(backgroundValue));
			}

			~NAME()
			{
				unsetGrid();
			}

			//Accessor<GRIDTYPE> getAccessor()
			//{
			//	return new Accessor<GRIDTYPE>(ptr_.get().getAccessor());
			//}

			long memUsage() {
				return ptr_->memUsage();
			}

			void setName(System::String^ name) {
				ptr_->setName(SystemString2StdString(name));
			}
			System::String^ getName() {
				return StdString2SystemString(ptr_->getName());
			}

			long activeVoxelCount() {
				return ptr_->activeVoxelCount();
				//return ptr_.get().activeVoxelCount();
			}

			void set(BlockPos pos, GRIDTYPE value)
			{
				// Define a coordinate with large signed indices.
				openvdb::Coord xyz(pos.X, pos.Y, pos.Z);
				// Set the voxel value at (1000, -200000000, 30000000) to 1.
				accessor_->setValue(xyz, value);
			}

			GRIDTYPE get(BlockPos pos)
			{
				openvdb::Coord xyz(pos.X, pos.Y, pos.Z);
				return accessor_->getValue(xyz);
			}

			void saveFile(System::String^ filename)
			{
				openvdb::io::File file(SystemString2StdString(filename));
				openvdb::GridPtrVec grids;
				grids.push_back(ptr_.getSharedPtr());
				file.write(grids);
				file.close();
			}

			void loadFile(System::String^ filename, System::String^ gridName)
			{
				openvdb::io::File file(SystemString2StdString(filename));

				// Loop over all grids in the file and retrieve a shared pointer
				// to the one named "main".  (This can also be done more simply by calling file.readGrid("main").)
				openvdb::GridBase::Ptr gridPtr;
				//for (openvdb::io::File::NameIterator nameIter = file.beginName(); nameIter != file.endName(); ++nameIter)
				//{
				//	// Read in only the grid we are interested in.
				//	if (nameIter.gridName() == "LevelSetSphere") {
				//		grid = file.readGrid(nameIter.gridName());
				//	}
				//	else {
				//		std::cout << "skipping grid " << nameIter.gridName() << std::endl;
				//	}
				//}
				gridPtr = file.readGrid(SystemString2StdString(gridName));

				GRID::Ptr grid = openvdb::gridPtrCast<GRID>(gridPtr);
				
				//openvdb::GridPtrVec grids;
				//grids.push_back(ptr_.getSharedPtr());
				//file.write(grids);
				file.close();

				setGrid(grid);
			}

		private:
			native_shared_ptr<GRID> ptr_;
			native_shared_ptr<ACCESSOR> accessor_;
			bool gridIsSet_;

			void setGrid(GRID::Ptr grid)
			{
				unsetGrid();
				gridIsSet_ = true;
				ptr_ = native_shared_ptr<GRID>(grid);
				accessor_ = native_shared_ptr<ACCESSOR>(std::make_shared<ACCESSOR>(ptr_->getAccessor()));
			}

			void unsetGrid()
			{
				if (gridIsSet_)
				{
					accessor_.reset();
					accessor_ = nullptr;
					ptr_.reset();
					ptr_ = nullptr;
					gridIsSet_ = false;
				}
			}

			const std::string& SystemString2StdString(System::String^ string)
			{
				return msclr::interop::marshal_as<std::string>(string);
			}

			System::String^ StdString2SystemString(std::string string)
			{
				return gcnew System::String(string.c_str());
			}
		};
	}
}
#pragma managed(pop)