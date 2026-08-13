# Database format

Research records live in `database/records/*.json`. Each file contains an array of records. Required fields are stable `id`, `kind`, `name`, `summary`, `confidence`, `status`, `builds`, and `evidence`.

Large runtime catalogs remain at their application source paths and are indexed by `database/catalog.json`. This prevents two copies from drifting.

Generated `docs/generated/RESEARCH_INDEX.md` is deterministic. Run `python tools/generate_docs.py` after changing records.
