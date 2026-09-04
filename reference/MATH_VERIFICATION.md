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

## Preconditioned conjugate gradient

`pcg-oracle.cpp` calls the pinned `math/ConjGradient.h` implementation directly.
`Build-MathReference.ps1` reproduces the 588 cases in
`openvdb-13.0.1-pcg.json`, SHA-256
`061B0824D56519915DB4C3AB00BE654361583DAFCF82789A87DD4C48B2ECC0E2`.

Coverage includes float/double vector lengths 0, 1, 5, 99, 1023, 1024, 1025,
and 10037; cancellation-sensitive reductions; NaN, infinities and signed zeros;
explicitly stored sparse zeros and mixed-precision products; six stencil matrix
families including a 1331-variable three-dimensional Laplacian; complete lower
and upper factors and preconditioner application; pivot thresholds; and 480 solver
runs spanning both preconditioners, initial guesses, zero right-hand sides,
iteration limits and convergence tolerances. Native interruption snapshots verify
solution state after zero or one completed iteration. Vector reductions, mixed
products, factors, preconditioner applications and interruption snapshots match
native representable values exactly. Solver success and iteration counts match
exactly; solution/error comparisons use relative-or-absolute tolerances 3e-6 for
float and 5e-13 for double.

The 633-test focused suite adds independent dense-product and known-solution
checks, 64 seeded tridiagonal solves, alias and bounds failures, factor snapshot
isolation, row concurrency, exact signed-zero SIMD scaling, allocation-free dot
reductions, and managed cancellation tests. The suite passes Release, Debug and
Release with hardware intrinsics disabled.

The managed API is in `Tedd.OpenVDB.Math.Pcg`: `Vector<T>`,
`SparseStencilMatrix<T>`, `Preconditioner<T>`, `JacobiPreconditioner<T>`,
`IncompleteCholeskyPreconditioner<T>`, `State` and `ConjugateGradient`.
Only float and double are accepted. Row capacity is a runtime argument replacing
the native compile-time stencil size. CLR dimensions are signed ints and checked
for allocation overflow. Sparse row views remain attached to their matrix; vector
spans remain attached to the original allocation after a resize or swap.

Arithmetic and source-specific behavior:

- Dot products accumulate serially below 1024 values and use exactly 100 ordered
  bins thereafter. Stack storage avoids a per-reduction GC allocation. Elementwise
  same-precision scaling uses SIMD; mixed-precision scaling preserves C++ promotion.
  Managed kernels currently execute serially; this does not reproduce TBB scheduling.
- Default absolute tolerance is machine epsilon at one multiplied by 100, not the
  CLR subnormal `Epsilon` constant. Residual diagnostics use the infinity norm;
  the source's L2 growth check determines divergence.
- Initial convergence checks relative tolerance only. Empty or initially converged
  systems return before interruption checks. Cancellation is checked at each
  iteration boundary and preserves completed solution updates.
- Jacobi computes reciprocal diagonals in double then narrows to the value type.
  Zero diagonals produce infinity in native release mode; `IsFinite()` detects it.
- Incomplete Cholesky retains the native four-entry triangular capacity and its
  original-coefficient update order. It is intended for stencil systems, not dense
  SPD matrices. Managed code rejects excessive triangular capacity. Pivots below
  1e-5 invalidate the factorization. `IsValid` is the native pivot test, not a
  comprehensive finiteness or positive-definiteness proof: NaN pivots compare
  unordered and can leave it true. Inputs to solving should be finite and SPD.
- The IC temporary vector is reused, so its instance must not be applied concurrently.
  Factor properties return independent copies, replacing native const references.
  Matrix multiplication rejects overlapping spans; solve rejects RHS/solution
  identity; IC rejects input/output identity. Jacobi in-place application is supported.
- Native pointer/index assertions become managed bounds and dimension checks.
  Partially built factors remain inspectable when a pivot invalidates decomposition.

This ports the standalone float/double numerical group. Generic native matrix
operator templates, arbitrary scalar templates, TBB execution types and C++ stream
formatting are not exposed as API equivalents. Poisson grid assembly is a separate
layer above these kernels.

## Poisson assembly and solving

`poisson-oracle.cpp` directly instantiates the pinned `tools/PoissonSolver.h` APIs.
`Build-MathReference.ps1` reproduces 162 cases in `openvdb-13.0.1-poisson.json`,
SHA-256 `BE81CBE938D1363D2E0BA122F80376A49C6F7660A8419FA03D4AE9C9EE91F489`.
The combined PCG/Poisson suite contains 827 tests (633 PCG and 194 Poisson).

The 160 native assembly/solve cases cover float and double source trees, both
Laplacian stencils, IC and Jacobi preconditioning, homogeneous Dirichlet and
order-sensitive custom boundary callbacks, an analytic open tank, independent
source/domain topology, root/leaf boundaries and negative coordinates, active
8-cubed tiles, empty topology, disconnected samples, nonconvergence, and an
ill-posed isolated Neumann example. Every active voxel's index, matrix column
and coefficient, initial/modified RHS and round-trip value is compared. Indexing,
coefficients and boundary edits match exactly. Solver status and iteration counts
match exactly; error diagnostics and double output use tolerance 5e-13, and float
output uses 2e-6. Two further native cases compare Half and bool conversion,
including double-rounding boundaries, signed zero, infinities and NaN.

Managed tests independently verify quadratic Laplacians, tank pressure, sparse
index permutations, numeric/bool round trips, explicitly stored inactive values,
retention of inactive allocated topology, invalid index rejection, tile-expansion
and degree-of-freedom budgets, overflow, callback ordering, preconditioner fallback,
cancellation and input isolation.

The API is `Tools.PoissonSolver`: `CreateIndexTree`, `PopulateIndexTree`,
`CreateVectorFromTree`, `CreateTreeFromVector`, `CreateISLaplacian`,
`CreateISLaplacianWithBoundaryConditions`, `Solve` and
`SolveWithBoundaryConditions`. `PoissonSolverOptions` supplies allocation budgets,
cancellation and a custom preconditioner factory. The boundary delegate receives
voxel/neighbor coordinates and mutable double source/diagonal coefficients.

Source behavior retained:

- Index assignment follows native root, internal-node, leaf and active-offset
  order. Active tiles are expanded before indexing. The index background is -1.
- Domain activity selects unknowns independently of source activity. Stored inactive
  source values and source background values still contribute to the RHS.
- The default collocated stencil samples offsets -2,+2 along X,Y,Z, with unit
  off-diagonal coefficients and no factor 1/4. The staggered stencil samples
  immediate faces in order -X,-Y,-Z,+Z,+Y,+X. The interior mask is used only for
  staggered assembly; active state, not its stored value, classifies interior voxels.
- The Laplacian and solve vectors always use double, even for FloatTree input.
  Matrix and RHS are negated before PCG. Invalid IC factors fall back to Jacobi.
- Solves return the last approximation even on nonconvergence. Output background
  is zero and output retains the densified domain topology. These are index-space
  tree operations; transforms and world-space spacing are not applied implicitly.
- As in native release builds, ill-posed or nonfinite systems can produce NaN
  values while the native infinity-norm logic reports success. The fixture retains
  this behavior rather than silently changing the source algorithm. Well-posed
  boundary conditions and finite inputs are prerequisites for meaningful solutions.
- Low-level conversion accepts unmanaged numeric tree values plus bool overloads,
  with float/double PCG vectors. Double-to-Half first narrows to float, matching
  Imath; bool conversion treats every nonzero value, including NaN, as true.

Managed safeguards differ from unchecked native pointer use: index trees must
contain unique indices spanning their active voxel count and no active tiles;
inconsistent interior masks, invalid dimensions and Int32 neighbor overflow throw.
Budgets are checked before densification; temporary outputs isolate source trees.
Boundary callbacks run serially and preserve per-voxel source order. Cancellation
also occurs between preparation stages and leaves, in addition to PCG iteration
boundaries. High-level exceptions leave the caller's state unchanged; low-level
mutating indexing/RHS APIs can retain completed work. Generic native tree layouts,
TBB scheduling and C++ interrupter types remain represented by the managed fixed
tree configuration and .NET cancellation APIs.

## Quantized unit vectors

`quantized-unit-vector-oracle.cpp` invokes pinned native QuantizedUnitVec functions
for every 16-bit encoded word. The 384-block fixture
`openvdb-13.0.1-quantized-unit-vector.json` has SHA-256
`2E116E512F11AE2E7FF62932E1364AC899D77D70E2EBA324948B9E0D23A8B350`.
It contains all 65,536 decodes and sign flips plus 8,192 float/double encodings,
including quantization boundaries, all-zero vectors, signed zero and magnitude
variation. Decoded component bits, signed zeros, sign masks and packed words are
compared exactly. The 397-test suite adds 40,000 seeded unit-sphere samples, span
bounds and overlapping-buffer rejection, scalar/SIMD tails, involution and
allocation measurements.

`Math.QuantizedUnitVec` provides scalar and span Pack/Unpack plus scalar, ref and
SIMD span FlipSignBits. The exact 8,192 normalization floats are embedded as
little-endian byte RVA data, read through BinaryPrimitives without a managed table
allocation. This avoids a 72-byte-per-decode allocation observed from the compiler's
generic float-span initialization helper in Debug; scalar and batch calls now
allocate zero bytes in both configurations after initialization. Pack uses concrete
float/double arithmetic, preserving native quantization precision.

Native edge behavior is retained: zero encodes as word zero, whose decoded value
is positive Z; negative-zero input signs are discarded by Pack while encoded sign
bits can produce negative-zero output components. Unpack accepts all 65,536 words,
including words not emitted for normalized input. Nonfinite Pack components and
normalizations that would require nonfinite-to-integer casts throw in managed code,
replacing undefined native conversions. Span output tails remain untouched, and
source/destination byte overlap is rejected before writing.

## Velocity fields and integration

`velocity-fields-oracle.cpp` invokes the unmodified pinned native
`DiscreteField`, `EnrightField`, `VelocitySampler` and `VelocityIntegrator`.
The 64-profile fixture `openvdb-13.0.1-velocity-fields.json` has SHA-256
`A26682FF405EBB1B0D804259E26DC98E08A12DC439BF8805469F6F1BBC2A4081`.
Its 16 Enright profiles cover 512 world/time samples plus integer-coordinate
queries, both precisions, periodic phases, NaN and infinity. Its 48 grid profiles
cover float/double vectors, all three interpolation orders, collocated/staggered
sampling, identity, uniform affine scale/translation, general affine and nonlinear
frustum transforms. Across 24 positions per grid profile, three time steps and
RK orders zero through four, 34,560 integration results compare float and double
location precision independently. Activity matches exactly; numeric tolerances are
2e-6 for float and 5e-13 for double, relative to max(1, abs(expected)).

The 83 tests additionally verify captured-tree/transform semantics, copy rebinding,
integer-query bypass of interpolation, staggered activity union, divergence-free
and periodic Enright behavior, analytic linear-field RK convergence, large double
locations retained by float integration, argument validation, span/ref updates and
zero steady-state sampling/integration allocations.

The public API is `IVelocityField`, `DiscreteField<TVector>`,
`EnrightField<TScalar>`, `VelocitySampler<TVector>` and
`VelocityIntegrator<TVector>`. Vector grids require Vec3f or Vec3d; Enright scalar
parameters require float or double. The common field interface widens native
scalar-rounded components to Vec3d. Field velocities are already in the world
frame. Accessor-backed instances are not thread-safe; copies obtain independent
accessors. Discrete fields retain their original tree and transform references.
Velocity samplers retain the original accessor but read the grid's current
transform; their copies acquire the grid's current tree. Intermediate RK positions,
time steps and displacements use velocity-component precision, with the resulting
displacement added to the original location at location precision. Enright's
transform getter returns a new identity transform, matching its native by-value API.

Linear oracle transforms explicitly preserve AffineMap representation through the
native public transform reader: the native Transform(map) constructor automatically
simplifies diagonal matrices. This matches the managed Transform's current generic
affine representation. Specialized native Scale/ScaleTranslate/UniformScale maps
remain a precision-coverage gap: arithmetic rearrangement changes cell selection
or support activity at exact boundaries. The original UniformScaleTranslateMap
samples are retained in `reference/velocity-specialized-map-boundaries.json` as
known-limitation evidence, not equivalence fixtures. For scale 0.375 and translation
(1.25,-2.5,0.75), such differences occur at index-space band corners and half-cell
boundaries. The velocity implementation does not silently claim specialized-map
parity or change transform arithmetic to mask this distinction.
