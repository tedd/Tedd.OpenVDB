# AttributeSet native conformance

The oracle pins OpenVDB 13.0.1 commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` and emits 144 profiles.

- 96 descriptor profiles verify insertion order, attribute types/codecs, typed defaults, renames, group offsets, drops, metadata ordering, and exact native bytes.
- 48 set profiles, each written with and without transient arrays, verify construction defaults, uniform/expanded arrays, flags, groups, split metadata/data passes, and exact native bytes.
- 53 independent tests cover malformed streams, bounds, allocation budgets, copy-on-write ownership, resize rules, concurrency, disposal, and runtime codec dispatch.

Reproduce with `reference/Build-AttributeSetReference.ps1`. The managed set tracks sharing between managed AttributeSet owners. Raw AttributeArray references cannot participate in a CLR reference-count query, so `IsShared` deliberately reports tracked set ownership.
