# Architecture

KCrashLab separates policy from execution through `ILabBackend`. The controller owns campaign state and evidence policy; the backend owns a disposable execution lease. Automated fuzz campaigns remain simulation-only. Track B uses a separately gated destructive-lab command so an incomplete real backend can never become an implicit fallback.

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

The fuzzing path begins with a validated non-failing case. Boundary scalar, payload block, and sequence insert/delete/swap operators produce canonical descendants with explicit lineage. The synthetic target returns semantic coverage elements rather than kernel coverage. Cases with new feedback or a signature enter the energy-ranked corpus. Parent, operator, and candidate decisions use independent SHA-256-derived lanes keyed by campaign seed and scheduling iteration. Candidate caps separately use `HASH_RANKED_V1` over the complete valid candidate set. The same seed reproduces one execution log, while paired experiment seeds explore different schedules.

E1 uses one policy-driven engine for all four arms of a 2×2 ablation: keep-all versus novelty-only corpus admission, crossed with uniform versus energy-ranked parent selection. Operator and candidate selection are uniform in every arm, and paired arms share operators, safe seed, budget, and campaign seed. This holds the mutation envelope and candidate-generation path constant while exposing the separate descriptive effects of admission and parent scheduling.

E2 wraps the same mutation operators with a sequence-length policy. Paired single-call and stateful trials differ only in the cap, so discovery differences are not confounded by separate grammars or feedback implementations. Repeated scheduler selections with no unseen candidate end at the explicit scheduler-iteration limit; this is not claimed as proof that the global search space was exhausted.

Reviewer-facing experiment summaries include provenance. The definition digest binds all controlled parameters; the source-tree digest covers source, tests, schemas, samples, CI, and documentation while deliberately excluding generated results to avoid a circular hash. Exact artifact bytes remain protected by each result manifest.

Semantic case identity deliberately excludes the top-level lineage envelope, while the stored case bytes retain it. This lets equivalent inputs deduplicate even if two mutation paths reach them; the artifact manifest independently hashes the exact stored bytes. The complete rule is documented in [canonical case identity](canonical-case-identity.md).

`HyperVLabBackend` remains contract-only and returns `BLOCKED_BY_ENVIRONMENT`; normal campaign commands therefore cannot accidentally select a host or arbitrary VM. The explicit Track B boundary is `scripts/track-b-g1.ps1`: it validates a private profile, exact VM/checkpoint identities and private networking, restores and boots the VM, uses PowerShell Direct to start the hash-pinned guest dispatcher, detects a reboot, waits for an exclusively readable stable dump, runs WinDbg, restores in `finally`, and repeats from the checkpoint three times.

```text
private profile + explicit credential + case + cdb.exe
  → exact VM/checkpoint/private-switch gate
  → restore/start/heartbeat → Copy-VMFile → guest agent → KMDF
  → reboot → stable dump → private copy/hash → raw WinDbg → Signature v1
  → finally restore × 3 → 3/3 vote
  → REAL_LAB evidence builder → sanitized manifest bundle (no dump)
```

This split is intentional: checked-in automation can be reviewed and built on ordinary CI, while only an environment-protected, manually dispatched self-hosted Windows runner may build Track B native variants. A real-kernel claim still requires the sanitized output of the destructive run; source code and CI success alone are insufficient.
