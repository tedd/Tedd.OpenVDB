#include <openvdb/openvdb.h>
#include "Accessor.h"
#include "native_shared_ptr.h"

namespace Tedd {
	namespace OpenVDB {

		// ***
		using TREE = typename openvdb::tree::Tree4<GRIDTYPE, 5, 4, 3>::Type;
		using GRID = openvdb::Grid<TREE>;
		public ref class NAME
		{
		public:
			NAME()
			{
				ptr_ = native_shared_ptr<GRID>(GRID::create());
			}

			~NAME()
			{
				ptr_.reset();
				ptr_ = nullptr;
			}

			//Accessor<GRIDTYPE> getAccessor()
			//{
			//	return new Accessor<GRIDTYPE>(ptr_.get().getAccessor());
			//}

			long activeVoxelCount() {
				return ptr_->activeVoxelCount();
				//return ptr_.get().activeVoxelCount();
			}

		private:
			native_shared_ptr<GRID> ptr_;
		};
	}
}
#pragma managed(pop)