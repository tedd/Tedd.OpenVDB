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

# Optional zlib-enabled reference target; the managed runtime does not link native code.
if(OPENVDB_ZLIB_BUILD_DIR AND ZLIB_LIBRARY)
  add_executable(vdb-compression-reference compression-oracle.cpp)
  target_include_directories(vdb-compression-reference PRIVATE
    "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath"
    "${OPENVDB_ZLIB_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_ZLIB_BUILD_DIR}/openvdb/openvdb/openvdb"
    "${TBB_ROOT}/include")
  target_compile_definitions(vdb-compression-reference PRIVATE OPENVDB_STATICLIB _USE_MATH_DEFINES NOMINMAX)
  target_link_libraries(vdb-compression-reference PRIVATE
    "${OPENVDB_ZLIB_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib"
    "${ZLIB_LIBRARY}" "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
  set_property(TARGET vdb-compression-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  add_executable(vdb-archive-reference archive-oracle.cpp)
  target_include_directories(vdb-archive-reference PRIVATE
    "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath"
    "${OPENVDB_ZLIB_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_ZLIB_BUILD_DIR}/openvdb/openvdb/openvdb"
    "${TBB_ROOT}/include")
  target_compile_definitions(vdb-archive-reference PRIVATE OPENVDB_STATICLIB _USE_MATH_DEFINES NOMINMAX)
  target_link_libraries(vdb-archive-reference PRIVATE
    "${OPENVDB_ZLIB_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib"
    "${ZLIB_LIBRARY}" "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
  set_property(TARGET vdb-archive-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  add_executable(vdb-gridtypes-reference gridtypes-oracle.cpp)
  target_include_directories(vdb-gridtypes-reference PRIVATE
    "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath"
    "${OPENVDB_ZLIB_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_ZLIB_BUILD_DIR}/openvdb/openvdb/openvdb"
    "${TBB_ROOT}/include")
  target_compile_definitions(vdb-gridtypes-reference PRIVATE OPENVDB_STATICLIB _USE_MATH_DEFINES NOMINMAX)
  target_link_libraries(vdb-gridtypes-reference PRIVATE
    "${OPENVDB_ZLIB_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib"
    "${ZLIB_LIBRARY}" "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
  set_property(TARGET vdb-gridtypes-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()


if(TARGET vdb-gridtypes-reference)
  target_compile_options(vdb-gridtypes-reference PRIVATE /bigobj)
endif()

if(OPENVDB_BLOSC_BUILD_DIR AND BLOSC_LIBRARY AND BLOSC_INCLUDE_DIR AND ZLIB_LIBRARY)
  add_executable(vdb-blosc-reference blosc-oracle.cpp)
  target_include_directories(vdb-blosc-reference PRIVATE
    "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath"
    "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/openvdb"
    "${TBB_ROOT}/include" "${BLOSC_INCLUDE_DIR}")
  target_compile_definitions(vdb-blosc-reference PRIVATE OPENVDB_STATICLIB _USE_MATH_DEFINES NOMINMAX)
  target_compile_options(vdb-blosc-reference PRIVATE /bigobj)
  target_link_libraries(vdb-blosc-reference PRIVATE
    "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib"
    "${BLOSC_LIBRARY}" "${ZLIB_LIBRARY}" "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
  set_property(TARGET vdb-blosc-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

if(OPENVDB_ZLIB_BUILD_DIR AND ZLIB_LIBRARY)
  add_executable(vdb-instance-reference instance-oracle.cpp)
  target_include_directories(vdb-instance-reference PRIVATE
    "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath"
    "${OPENVDB_ZLIB_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_ZLIB_BUILD_DIR}/openvdb/openvdb/openvdb" "${TBB_ROOT}/include")
  target_compile_definitions(vdb-instance-reference PRIVATE OPENVDB_STATICLIB _USE_MATH_DEFINES NOMINMAX)
  target_compile_options(vdb-instance-reference PRIVATE /bigobj)
  target_link_libraries(vdb-instance-reference PRIVATE "${OPENVDB_ZLIB_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib"
    "${ZLIB_LIBRARY}" "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
  set_property(TARGET vdb-instance-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

if(BLOSC_LIBRARY AND BLOSC_INCLUDE_DIR AND SNAPPY_LIBRARY AND SNAPPY_INCLUDE_DIR AND ZLIB_LIBRARY)
  add_executable(vdb-codec-reference codec-oracle.cpp)
  target_include_directories(vdb-codec-reference PRIVATE "${BLOSC_INCLUDE_DIR}" "${SNAPPY_INCLUDE_DIR}")
  target_compile_definitions(vdb-codec-reference PRIVATE NOMINMAX)
  target_link_libraries(vdb-codec-reference PRIVATE "${BLOSC_LIBRARY}" "${SNAPPY_LIBRARY}" "${ZLIB_LIBRARY}" bcrypt)
  set_property(TARGET vdb-codec-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  if(TARGET vdb-blosc-reference)
    target_link_libraries(vdb-blosc-reference PRIVATE "${SNAPPY_LIBRARY}")
  endif()
endif()

if(TARGET vdb-codec-reference)
  add_executable(vdb-zstd-reference zstd-oracle.cpp)
  target_include_directories(vdb-zstd-reference PRIVATE "${BLOSC_INCLUDE_DIR}" "${SNAPPY_INCLUDE_DIR}" "${BLOSC_INCLUDE_DIR}/../internal-complibs/zstd-1.5.6")
  target_compile_definitions(vdb-zstd-reference PRIVATE NOMINMAX)
  target_link_libraries(vdb-zstd-reference PRIVATE "${BLOSC_LIBRARY}" "${SNAPPY_LIBRARY}" "${ZLIB_LIBRARY}" bcrypt)
  set_property(TARGET vdb-zstd-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

if(TARGET vdb-blosc-reference)
  add_executable(vdb-metadata-reference metadata-oracle.cpp)
  target_include_directories(vdb-metadata-reference PRIVATE "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/openvdb" "${TBB_ROOT}/include")
  target_compile_definitions(vdb-metadata-reference PRIVATE OPENVDB_STATICLIB NOMINMAX)
  target_compile_options(vdb-metadata-reference PRIVATE /bigobj)
  target_link_libraries(vdb-metadata-reference PRIVATE "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib" "${BLOSC_LIBRARY}" "${SNAPPY_LIBRARY}" "${ZLIB_LIBRARY}" "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
  set_property(TARGET vdb-metadata-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

if(TARGET vdb-blosc-reference)
  add_executable(vdb-registry-reference registry-oracle.cpp)
  target_include_directories(vdb-registry-reference PRIVATE "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/openvdb" "${TBB_ROOT}/include")
  target_compile_definitions(vdb-registry-reference PRIVATE OPENVDB_STATICLIB NOMINMAX)
  target_compile_options(vdb-registry-reference PRIVATE /bigobj)
  target_link_libraries(vdb-registry-reference PRIVATE "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib" "${BLOSC_LIBRARY}" "${SNAPPY_LIBRARY}" "${ZLIB_LIBRARY}" "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
  set_property(TARGET vdb-registry-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

if(TARGET vdb-blosc-reference)
  add_executable(vdb-maps-reference maps-oracle.cpp)
  target_include_directories(vdb-maps-reference PRIVATE "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/openvdb" "${TBB_ROOT}/include")
  target_compile_definitions(vdb-maps-reference PRIVATE OPENVDB_STATICLIB NOMINMAX)
  target_compile_options(vdb-maps-reference PRIVATE /bigobj)
  target_link_libraries(vdb-maps-reference PRIVATE "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib" "${BLOSC_LIBRARY}" "${SNAPPY_LIBRARY}" "${ZLIB_LIBRARY}" "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
  set_property(TARGET vdb-maps-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

if(TARGET vdb-blosc-reference)
  add_executable(vdb-file-partial-reference file-partial-oracle.cpp)
  target_include_directories(vdb-file-partial-reference PRIVATE "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/openvdb" "${TBB_ROOT}/include")
  target_compile_definitions(vdb-file-partial-reference PRIVATE OPENVDB_STATICLIB NOMINMAX)
  target_compile_options(vdb-file-partial-reference PRIVATE /bigobj)
  target_link_libraries(vdb-file-partial-reference PRIVATE "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib" "${BLOSC_LIBRARY}" "${SNAPPY_LIBRARY}" "${ZLIB_LIBRARY}" "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
  set_property(TARGET vdb-file-partial-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
if(TARGET vdb-blosc-reference)
  add_executable(vdb-file-lifecycle-reference file-lifecycle-oracle.cpp)
  target_include_directories(vdb-file-lifecycle-reference PRIVATE "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/openvdb" "${TBB_ROOT}/include")
  target_compile_definitions(vdb-file-lifecycle-reference PRIVATE OPENVDB_STATICLIB NOMINMAX)
  target_compile_options(vdb-file-lifecycle-reference PRIVATE /bigobj)
  target_link_libraries(vdb-file-lifecycle-reference PRIVATE "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib" "${BLOSC_LIBRARY}" "${SNAPPY_LIBRARY}" "${ZLIB_LIBRARY}" "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
  set_property(TARGET vdb-file-lifecycle-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

if(TARGET vdb-blosc-reference)
  add_executable(vdb-string-attribute-reference string-attribute-oracle.cpp)
  target_include_directories(vdb-string-attribute-reference PRIVATE "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/openvdb" "${TBB_ROOT}/include")
  target_compile_definitions(vdb-string-attribute-reference PRIVATE OPENVDB_STATICLIB NOMINMAX)
  target_compile_options(vdb-string-attribute-reference PRIVATE /bigobj)
  target_link_libraries(vdb-string-attribute-reference PRIVATE "${OPENVDB_BLOSC_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib" "${BLOSC_LIBRARY}" "${SNAPPY_LIBRARY}" "${ZLIB_LIBRARY}" "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
  set_property(TARGET vdb-string-attribute-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
