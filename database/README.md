# Database

`records/*.json` contains evidence-bearing research records validated against `schemas/research-record.schema.json`. `catalog.json` indexes larger runtime catalogs used by the companion without duplicating them.

Record IDs use `kind.namespace.name`, for example `signature.operative.manager_capture`. Related records reference those stable IDs. Generated output belongs in `docs/generated/` and must not be edited manually.
