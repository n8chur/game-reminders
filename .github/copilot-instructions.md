# Copilot review instructions

Prioritize correctness and data preservation over stylistic suggestions. In particular, look for:

- any path that completes, deletes, overwrites, or loses a reminder without explicit **Dismiss**;
- non-idempotent or race-prone filesystem behavior under iCloud synchronization;
- malformed or unsupported JSON that can remain silently pending or corrupt authoritative state;
- unstable identity, alias collisions, or multiple games mapped ambiguously to one executable;
- process start/exit races, missed launches, duplicate popups, and excessive background resource use;
- UI close, shutdown, and exception paths that accidentally change reminder state;
- reminder message content written to logs;
- accidental requirements for elevation, game injection, or anti-cheat interaction.

Respect the product invariants and validation commands in `/AGENTS.md`. Flag conflicts with `docs/MVP_SPECIFICATION.md`, but distinguish later-milestone work from regressions in the current milestone.
