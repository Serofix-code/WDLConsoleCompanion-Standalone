# SDK

This directory is the future stable, reusable SDK boundary. The existing trainer contains useful components, but they remain experimental and build-coupled. Code should move here only after its public API, ownership, error behavior, tests, and supported-build contract are documented.

Planned modules include process/memory abstractions, signature definitions, guarded pointer traversal, database models, and read-only query APIs. No game binaries are required or permitted.
