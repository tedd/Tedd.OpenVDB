# Native math verification

Upstream: OpenVDB revision `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` (13.0.1).
The reference programs evaluate the C++ OpenVDB implementation; managed tests consume checked-in JSON and require no native library.

Run `reference/Build-MathReference.ps1` with the pinned source directory, an OpenVDB static build directory, a TBB directory and CMake. The script verifies the source commit, builds both executables, validates the generated JSON and records SHA-256 hashes.

- `math-oracle.cpp`: 276 cases. Random 3x3 and general/projective 4x4 matrix inverses, quaternion products/inverses/slerp/conversions, vector-to-vector rotations, IEEE NaN/infinity behavior, 50 nonlinear frustum derivative cases and nine camera frusta.
- `finite-difference-oracle.cpp`: 100 cases. Ninety float/double cases cover all fourteen first-derivative schemes, all three pure second-derivative schemes, WENO5 flux and Godunov norms. Ten cases verify exact named/dense stencil buffer order and statistics.
- Managed analytic coverage additionally checks polynomial gradients/Hessians, finite-difference convergence identities, transformed trilinear interpolation, stencil cache semantics and coordinate-endpoint failures.

The first-derivative oracle calls native accessor overloads, so native sample ordering is selected by OpenVDB itself. Native fixtures contain string values for NaN/infinity. Tests compare these explicitly, without using the native approximate-equality helper that intentionally accepts unordered differences.

## Preserved upstream behavior

- Native approximate comparison accepts NaN differences. IEEE equality remains false for NaN. Safe vector normalization returns the native unit-X fallback; null/NaN vector unit and matrix/quaternion inverse calls follow the source exception branches.
- Vec3 native approximate equality combines absolute and relative tolerance; matrix/quaternion comparisons use absolute tolerance.
- Quaternion slerp retains supplied quaternion signs, while matrix slerp chooses the shortest rotation.
- NonlinearFrustumMap determinant(location) uses location.Z directly, without applying index-space depth normalization. The actual local index-space determinant is available through Jacobian(indexLocation).Determinant.
- Native frustum curvature uses one gamma factor in its zz correction where the analytic expression has gamma squared. The port preserves that expression. Analytic Hessian tests separately use gamma=1.
- The native skew-matrix helper contains a duplicate assignment and an uninitialized entry. The managed helper supplies the intended complete row-vector skew matrix; this is an explicit correction rather than undefined-data parity.

## Managed differences and remaining scope

Frustum construction rejects nonfinite/degenerate parameters. Coordinate-based stencil sampling rejects neighborhoods outside signed 32-bit coordinates before changing a stencil snapshot. Stencil reads require initialization; local stencil changes do not write to the grid. The median implementation uses managed sorting rather than native nth_element; finite-data order statistics agree.

This milestone does not claim full math API parity. Remaining work includes named Grad/Weno/Curvature stencil convenience operations, grid-wide differential operators, other specialized map classes, matrix decomposition/Euler APIs, and the remaining vector numeric specializations. Finite-difference numerical parity is verified for float and double.
