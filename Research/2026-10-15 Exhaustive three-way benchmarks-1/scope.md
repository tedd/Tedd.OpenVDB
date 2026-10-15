# Exhaustive public-API benchmark status

The managed declaration inventory is complete: 311 externally visible types and 4,103 callable definitions. All 4,103 emitted definitions match the independent Roslyn source inspection; there are no unmatched definitions, ambiguities, or inspection compilation diagnostics.

This is an API inventory, not benchmark coverage. No exhaustive three-way timing cases have been executed. The cached-access experiments have separate evidence directories.

The inventory includes constructors, overloads, operators, property accessors, public synthesized members, and abstract/interface contracts. Inherited `System.Object` methods, fields, and implicit struct zero-initialization are not separate callable definitions. The `CompilerGeneratedAttribute` count and Roslyn's synthesized-declaration count describe different properties and need not agree.

Generic declarations require an explicit finite type/input matrix. Span/ref signatures require statically typed callers to avoid reflection costs or unsupported invocation. Abstract/interface contracts require concrete implementations. Native reference-returning APIs and managed copying APIs require explicit semantic adapters; an unavailable equivalent must remain a coverage gap.

The pending scope distinction is the full Tedd.OpenVDB API with native equivalents/N/A versus the union of both libraries' public APIs. Before broad timing, the native prototype's secondary-cache invalidation around accessor `addTile` requires a sanitizer reproduction and resolution. Stock and modified native cores must then be rebuilt separately with matching headers and flags; a modified accessor header linked to the same stock core is insufficient for a broad ABI comparison.

`managed-api-inventory.json` contains source locations, stable signatures/documentation IDs, parameter/return modifiers, generic constraints, and explicit unmapped native/benchmark fields. `status.json` records remaining work. `raw/` records source hashes and successful inventory builds. The inventory program only inspects metadata/source; it never invokes library members or initializes library objects.
