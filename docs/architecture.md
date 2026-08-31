# Architecture

KCrashLab Research Preview v0.1 separates control-plane policy from target execution through `ILabBackend`. The controller owns lifecycle and evidence policy; a backend owns a disposable execution lease. The only executable backend in this revision is deterministic user-mode simulation.

```text
safe Case IR → mutation operators → policy-driven fuzz engine
      │                                  │
      ├── canonical SHA-256 identity     ├── corpus admission policy
      ├── parent + mutation lineage      ├── parent selection policy
      └── bounded schedule               ├── operator selection policy
                                         └── candidate selection policy
                                                    │
CLI → Controller → append-only SQLite journal       │
          │                                         │
          ├→ ILabBackend → simulation → feedback ───┘
          ├→ triage parser → Signature v1 → exact cluster
          └→ minimizer → replay vote → CAS + manifest + report
```

## Campaign state and recovery

The campaign journal is append-only. Resumption rehydrates the aggregate and continues from the last committed state. Dispatch receipts and event IDs are deterministic so a delivery acknowledged after an ambiguous commit is not dispatched twice. Infrastructure failures are a separate result class and never count as target findings.

Fault-injection tests exercise duplicate and colliding events, interrupted and corrupt content-addressed writes, missing/truncated manifests, ambiguous journal commits, and invalid virtual-clock jumps.

## Policy-driven fuzz engine

All experimental arms use `PolicyDrivenFuzzEngine`. Its four explicit extension points are:

- `ICorpusAdmissionPolicy`
- `IParentSelectionPolicy`
- `IOperatorSelectionPolicy`
- `ICandidateSelectionPolicy`

E1 crosses keep-all versus novelty-only admission with uniform versus energy-ranked parent selection. Operator and candidate selection stay uniform. Mutation operators, candidate enumeration, safe seed, budget, and evaluator are shared.

Random choices do not consume one mutable PRNG stream. Each choice is derived from SHA-256 over `(campaign seed, scheduling iteration, decision lane)`. Parent, operator, and candidate lanes therefore cannot perturb one another merely by consuming a different number of random values. The same seed reproduces the same execution log.

Novelty is synthetic semantic feedback, not kernel coverage. The model emits observable state/path elements; no claim is made that this approximates production driver coverage.

## Experiment interpretation

E1 is a single-target 2×2 scheduling-policy ablation. It can describe interactions within the checked-in deterministic seed suite, but it cannot establish general scheduler superiority, real-driver performance, or statistical significance.

E2 wraps the same mutation operators with a sequence-length cap. Its ground truth requires three operations, so the study is an expressiveness validation: a single-call representation cannot encode the trigger. It is not treated as the primary empirical benchmark.

## Evidence and provenance

Reviewer-facing summaries record the controlled parameters, source-tree digest, experiment-definition digest, Case IR version, engine version, recording time, and source commit. The source-tree digest covers source, tests, schemas, samples, CI, and documentation while excluding generated results to avoid a circular digest.

Each evidence directory also has an exact-byte SHA-256 manifest. Verification rejects modified, missing, and untracked files, then recomputes semantic invariants from the JSON/CSV records.

Semantic case identity excludes the top-level lineage envelope while stored case bytes retain it. Equivalent inputs deduplicate even when separate mutation paths reach them; the evidence manifest still hashes the exact stored bytes. See [canonical case identity](canonical-case-identity.md).

## Real-lab boundary

`HyperVLabBackend` is contract-only and returns `BLOCKED_BY_ENVIRONMENT`. It may become executable only after the WDK, disposable VM, immutable checkpoint, dump, Driver Verifier recovery, and kill-switch gates in [ADR-0001](adr/0001-simulation-first.md) and [lab safety](lab-safety.md) are satisfied. It must never fall back to the daily-use host.
