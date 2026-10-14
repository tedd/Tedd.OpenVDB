cmake_minimum_required(VERSION 3.28)
project(CachedAccessNative LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
foreach(required OPENVDB_SOURCE_DIR OPENVDB_BUILD_DIR TBB_ROOT ZLIB_LIBRARY)
  if(NOT ${required})
    message(FATAL_ERROR "Set ${required}")
  endif()
endforeach()
if(NOT MSVC)
  message(FATAL_ERROR "This reproduction configuration requires Windows x64/MSVC.")
endif()
add_executable(cached-access-native main.cpp)
target_include_directories(cached-access-native PRIVATE
  "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath"
  "${OPENVDB_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_BUILD_DIR}/openvdb/openvdb/openvdb"
  "${TBB_ROOT}/include")
target_compile_definitions(cached-access-native PRIVATE OPENVDB_STATICLIB NOMINMAX WIN32_LEAN_AND_MEAN)
target_link_libraries(cached-access-native PRIVATE
  "${OPENVDB_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib"
  "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib" "${ZLIB_LIBRARY}")
set_property(TARGET cached-access-native PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded")
