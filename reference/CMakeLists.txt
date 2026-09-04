cmake_minimum_required(VERSION 3.28)
project(TeddOpenVdbReference LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
foreach(required OPENVDB_SOURCE_DIR OPENVDB_BUILD_DIR TBB_ROOT)
  if(NOT ${required})
    message(FATAL_ERROR "Set ${required}")
  endif()
endforeach()
add_executable(vdb-reference oracle.cpp)
target_include_directories(vdb-reference PRIVATE
  "${OPENVDB_SOURCE_DIR}/openvdb"
  "${OPENVDB_SOURCE_DIR}/ext/imath"
  "${OPENVDB_BUILD_DIR}/openvdb/openvdb"
  "${OPENVDB_BUILD_DIR}/openvdb/openvdb/openvdb"
  "${TBB_ROOT}/include")
target_compile_definitions(vdb-reference PRIVATE OPENVDB_STATICLIB _USE_MATH_DEFINES NOMINMAX)
target_link_libraries(vdb-reference PRIVATE
  "${OPENVDB_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib"
  "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")



set_property(TARGET vdb-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_executable(vdb-io-reference io-oracle.cpp)
target_include_directories(vdb-io-reference PRIVATE
  "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath"
  "${OPENVDB_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_BUILD_DIR}/openvdb/openvdb/openvdb"
  "${TBB_ROOT}/include")
target_compile_definitions(vdb-io-reference PRIVATE OPENVDB_STATICLIB _USE_MATH_DEFINES NOMINMAX)
target_link_libraries(vdb-io-reference PRIVATE
  "${OPENVDB_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib"
  "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
set_property(TARGET vdb-io-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
