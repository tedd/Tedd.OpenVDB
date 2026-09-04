// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// These optional compile-failure probes preserve evidence of defects in the pinned source.
#include <openvdb/openvdb.h>
#include <openvdb/tools/Diagnostics.h>
#ifndef DIAGNOSTICS_PROBE_KIND
#define DIAGNOSTICS_PROBE_KIND 1
#endif
int main()
{
#if DIAGNOSTICS_PROBE_KIND == 1
    openvdb::Vec3fGrid grid;
    openvdb::tools::CheckDivergence<openvdb::Vec3fGrid> check(grid, openvdb::Vec3f(-1), openvdb::Vec3f(1));
    return check(0.0f);
#elif DIAGNOSTICS_PROBE_KIND == 2
    openvdb::FloatGrid grid;
    openvdb::tools::Diagnose<openvdb::FloatGrid> diagnose(grid);
    diagnose.clear();
    return 0;
#elif DIAGNOSTICS_PROBE_KIND == 3
    openvdb::Vec3fGrid grid;
    auto accessor = grid.getConstAccessor();
    return int(openvdb::math::ISDivergence<openvdb::math::FD_WENO5>::result(accessor, openvdb::Coord(0)));
#else
#error Unsupported diagnostics probe kind
#endif
}
