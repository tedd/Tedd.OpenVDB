# Third-party notices

This managed implementation contains adaptations of OpenVDB source code.

Copyright Contributors to the OpenVDB Project
SPDX-License-Identifier: Apache-2.0

Source: https://github.com/AcademySoftwareFoundation/openvdb
Revision: f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf
Upstream version: 13.0.1

The Apache License 2.0 is reproduced in LICENSE. Derived source files retain the
upstream copyright and SPDX identifier. This repository is an independent managed
port and is not represented as an official OpenVDB project release.

The native reference-build scripts acquire oneTBB for test-fixture regeneration.
oneTBB is not a managed runtime dependency and its binaries are not included in the
NuGet package. See reference documentation for exact dependency provenance/licenses.
