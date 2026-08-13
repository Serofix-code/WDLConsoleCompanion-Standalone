# Confidence classification

Confidence records the strength of evidence for a specific claim on a specific build.

- `confirmed`: reproduced with direct runtime evidence and validation on a named build.
- `strongly_inferred`: multiple observations agree, but an important contract remains incomplete.
- `inferred`: plausible from limited strings, call sites, or correlations.
- `unknown`: intentionally unresolved.

Confidence is not inherited across builds and does not mean a write is safe. Evidence references must be reviewable without distributing proprietary material.
