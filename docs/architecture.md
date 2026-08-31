# Architecture

KCrashLab v1 separates policy from execution through `ILabBackend`. The controller owns campaign state and evidence policy; the backend owns a disposable execution lease. The only executable v1 backend is deterministic simulation.

```text
safe Case IR → mutation operators → semantic feedback → novelty corpus
      │                                      │
      └──────── deterministic lineage ───────┘
                         │
CLI → Controller ────────┼→ SQLite event journal
          │              ├→ exact Signature v1 dedup
          │              └→ fuzz artifacts + manifest + static report
          ├→ ILabBackend → SimulatedLabBackend → synthetic events/artifacts
          │                    └→ virtual clock (no test sleeps)
          ├→ Triage parser → Signature v1 → exact cluster
          └→ Evidence builder → CAS + manifest → static report
```

The campaign journal is append-only. Resumption rehydrates the aggregate and continues from the last committed state. Dispatch receipts and event IDs are deterministic so duplicate delivery can be recognized. Infrastructure failures never count as target findings.

The fuzzing path begins with a validated non-failing case. Boundary scalar, payload block, and sequence insert/delete/swap operators produce canonical descendants with explicit lineage. The synthetic target returns semantic coverage elements rather than kernel coverage. Cases with new feedback or a signature enter the energy-ranked corpus. An explicit SplitMix64 stream seeded by the campaign rotates operator and candidate order: the same seed reproduces one execution log, while paired experiment seeds explore different schedules.

The E1 baseline uses the same operators and safe seed but selects the parent, operator, and candidate uniformly at random from a deterministic PRNG. Its feedback is measured but does not affect selection. This holds validity knowledge constant and isolates the effect of novelty-guided queueing.

E2 wraps the same mutation operators with a sequence-length policy. Paired single-call and stateful trials differ only in the cap, so discovery differences are not confounded by separate grammars or feedback implementations. Repeated scheduler selections with no unseen candidate terminate as explicit search-space exhaustion.

Reviewer-facing experiment summaries include provenance. The definition digest binds all controlled parameters; the source-tree digest covers source, tests, schemas, samples, CI, and documentation while deliberately excluding generated results to avoid a circular hash. Exact artifact bytes remain protected by each result manifest.

Semantic case identity deliberately excludes the top-level lineage envelope, while the stored case bytes retain it. This lets equivalent inputs deduplicate even if two mutation paths reach them; the artifact manifest independently hashes the exact stored bytes. The complete rule is documented in [canonical case identity](canonical-case-identity.md).

The future `HyperVLabBackend` is contract-only and returns `BLOCKED_BY_ENVIRONMENT`. It may become executable only after all Track B gates in [ADR-0001](adr/0001-simulation-first.md) are satisfied.
