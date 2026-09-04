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


## Differential and grid operators

The operators oracle directly instantiates the pinned native GradStencil, WenoStencil,
CurvatureStencil, Operators.h scalar kernels and vector derivatives. Its 144 cases
cover float/double precision, constant and polynomial fields, random neighborhoods,
tiny gradients and NaN, all fourteen scalar first-difference schemes, five biased
gradient schemes, three second-difference orders, affine maps and nonlinear frustums.
The managed test also exercises accessor and cached-stencil overloads.

The grid-operators oracle invokes native tools::gradient, laplacian, meanCurvature,
cpt, divergence, curl, magnitude and normalize. Its 320 cases cover both scalar/vector
precisions, affine/frustum maps, sparse leaves, active tiles, inactive neighbors,
staggered grids, empty and inactive-only root entries, with and without masks.
Tests compare every stored active output value, active counts, leaf counts,
backgrounds, inactive probes, value depths and output metadata, in sequential
and parallel managed execution.

Reproduction uses reference/Build-MathReference.ps1 with the same pinned source,
static OpenVDB and TBB paths documented above. The grid executable needs MSVC /bigobj
because it instantiates the upstream grid operator map/type combinations.
Neither oracle is a managed runtime dependency.

Native distinctions deliberately retained:
- Grad/Weno convenience stencils cache uniform dx and select the forward derivative
  when velocity is zero; general biased operators select the backward derivative.
- WenoStencil's non-SIMD Godunov step uses double intermediates, even for float grids.
- CurvatureStencil has its own nineteen-point buffer order, distinct from DensePt.
- The generic affine CPT index-space branch applies the inverse point map, including
  its translation, to the displacement. The world-space CPT follows the native
  world-position-minus-displacement formula.
- Native closest-point grid operations retain active tiles and evaluate a tile only
  at its origin. This may produce an equal output vector throughout that tile.
- Native Laplacian and mean-curvature scalar grids carry covariant vector metadata.
- General managed transforms represent AffineMap or NonlinearFrustumMap. Native
  specialized scale/translation map arithmetic is not a distinct managed map type.

Convenience operators assume a uniform voxel scale and retain IEEE behavior for
explicit zero/nonfinite dx; callers needing arbitrary maps should use the general
operators. Scalar gradient results use Vec3d storage with components rounded to the
input precision. Float/double whole-grid outputs retain Vec3f/Vec3d precision.
Vector accessor derivatives additionally permit componentwise WENO, built from
the verified native scalar kernels; upstream D1Vec WENO templates do not compile,
so direct native vector conformance covers the ten other first-difference schemes.

Remaining API gaps include vector-valued cached stencil overloads, separate native
specialized linear-map types, generic vector/scalar type conversion traits, and
the native interrupter/class wrappers. GridOperatorOptions provides .NET cancellation,
parallel execution and an explicit leaf-densification budget instead of TBB types.
