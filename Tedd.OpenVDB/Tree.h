//#include "pch.h"
//#pragma once
//#pragma managed(push, on)
//#include "openvdb/tree/Tree.h"
//namespace Tedd {
//	namespace OpenVDB {
//		/// @brief Tree3<T, N1, N2>::Type is the type of a three-level tree
///// (Root, Internal, Leaf) with value type T and
///// internal and leaf node log dimensions N1 and N2, respectively.
///// @note This is NOT the standard tree configuration (Tree4 is).
//		template<typename T, Index N1 = 4, Index N2 = 3>
//		struct Tree3 {
//			using Type = Tree<RootNode<InternalNode<LeafNode<T, N2>, N1>>>;
//		};
//
//
//		/// @brief Tree4<T, N1, N2, N3>::Type is the type of a four-level tree
//		/// (Root, Internal, Internal, Leaf) with value type T and
//		/// internal and leaf node log dimensions N1, N2 and N3, respectively.
//		/// @note This is the standard tree configuration.
//		template<typename T, Index N1 = 5, Index N2 = 4, Index N3 = 3>
//		struct Tree4 {
//			using Type = Tree<RootNode<InternalNode<InternalNode<LeafNode<T, N3>, N2>, N1>>>;
//		};
//
//		/// @brief Tree5<T, N1, N2, N3, N4>::Type is the type of a five-level tree
///// (Root, Internal, Internal, Internal, Leaf) with value type T and
///// internal and leaf node log dimensions N1, N2, N3 and N4, respectively.
///// @note This is NOT the standard tree configuration (Tree4 is).
//		template<typename T, Index N1 = 6, Index N2 = 5, Index N3 = 4, Index N4 = 3>
//		struct Tree5 {
//			using Type =
//				Tree<RootNode<InternalNode<InternalNode<InternalNode<LeafNode<T, N4>, N3>, N2>, N1>>>;
//		};
//	}
//}
//#pragma managed(pop)