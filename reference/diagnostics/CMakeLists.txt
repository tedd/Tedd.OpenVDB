cmake_minimum_required(VERSION 3.28)
project(TeddDiagnosticsReference LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 17)
add_executable(diagnostics-reference ../diagnostics-oracle.cpp)
target_include_directories(diagnostics-reference PRIVATE "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath" "${OPENVDB_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_BUILD_DIR}/openvdb/openvdb/openvdb" "${TBB_ROOT}/include")
target_compile_definitions(diagnostics-reference PRIVATE OPENVDB_STATICLIB _USE_MATH_DEFINES NOMINMAX)
target_link_libraries(diagnostics-reference PRIVATE "${OPENVDB_BUILD_DIR}/openvdb/openvdb/Release/libopenvdb.lib" "${TBB_ROOT}/lib/intel64/vc14/tbb12.lib")
set_property(TARGET diagnostics-reference PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
target_compile_options(diagnostics-reference PRIVATE $<$<CXX_COMPILER_ID:MSVC>:/bigobj>)


# Optional deliberately ill-formed target. It is excluded from normal builds.
if(DIAGNOSTICS_INVALID_PROBE)
    add_executable(diagnostics-invalid-probe EXCLUDE_FROM_ALL ../diagnostics-invalid-templates.cpp)
    target_include_directories(diagnostics-invalid-probe PRIVATE "${OPENVDB_SOURCE_DIR}/openvdb" "${OPENVDB_SOURCE_DIR}/ext/imath" "${OPENVDB_BUILD_DIR}/openvdb/openvdb" "${OPENVDB_BUILD_DIR}/openvdb/openvdb/openvdb" "${TBB_ROOT}/include")
    target_compile_definitions(diagnostics-invalid-probe PRIVATE OPENVDB_STATICLIB _USE_MATH_DEFINES NOMINMAX "DIAGNOSTICS_PROBE_KIND=${DIAGNOSTICS_INVALID_PROBE}")
    set_property(TARGET diagnostics-invalid-probe PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
