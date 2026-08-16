# Architecture

The repository separates factual research from executable experiments:

1. `database/` stores claims, compatibility, confidence, and evidence.
2. `sdk/` defines reusable, game-independent interfaces as they stabilize.
3. `tools/` validates and searches records without the game.
4. `src/WDLConsoleCompanion/` is an experimental consumer and research instrument.
5. `research/` records unresolved work and reproduction methods.

Runtime writes use expected-byte checks, unique pattern matches, pointer validation, read-back verification, and reversible cleanup where a feature supports it. A research record can exist without executable support.
