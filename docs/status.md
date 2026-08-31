# Implementation status

Updated: 2026-08-31

## Working now

- Capability probe with `AVAILABLE`, `UNAVAILABLE`, `UNVERIFIED`, and `BLOCKED` states.
- Case IR v1 validation, canonical JSON, and stable SHA-256 case IDs.
- Typed campaign lifecycle backed by an append-only SQLite journal.
- Resume from `RUNNING` without a second dispatch.
- `ILabBackend` plus eight deterministic, virtual-time simulator scenarios.
- A future Hyper-V backend skeleton that always fails closed.
- Versioned synthetic triage parser, stack normalization, signature v1, and exact grouping primitive.
- Sequence delta debugging demonstrated from 14 to 3 operations.
- Hierarchical minimization reduces the same fixture from 552 to 188 canonical bytes by shrinking schedule, optional fields, strings, and integers while preserving the signature.
- Deterministic 3/3 replay policy with infrastructure errors excluded from the vote.
- Replay voting can be used as a probabilistic minimization oracle for flaky synthetic targets.
- Content-addressed artifact copy, SHA-256 manifest, semantic bundle verifier, and static report.
- Deterministic boundary scalar, payload block, and sequence insert/delete/swap mutation operators with lineage.
- Energy-ranked novelty corpus driven by synthetic semantic feedback.
- Exact-signature finding deduplication and deterministic fuzz campaign runner.
- Verifiable fuzz artifacts containing corpus, per-execution metrics, coverage, findings, manifest, and static report.
- Paired E1 experiment runner comparing novelty-guided scheduling with a uniform-random valid-mutation baseline; censored trials, median/IQR, raw CSV, claims limits, and manifest verification are preserved.
- Censoring-aware E1 Kaplan–Meier discovery curve plus paired outcome summary.
- Paired E2 stateful-versus-single-call experiment with sequence length as the only changed variable.
- Versioned experiment provenance: recording time, Git state, source-tree digest, definition digest, Case IR version, and engine version.
- Fault injection for ambiguous journal commits, duplicate/colliding events, CAS interruption/corruption, manifest loss/truncation, and invalid virtual-clock jumps.
- Fail-closed clean reviewer packaging using `git archive`, ZIP inspection, and separate recorded-results packaging.
- Safe CI that builds and tests simulation only; dependency audit treats NuGet advisories as errors.

## Verified locally

- Release build: zero warnings and zero errors.
- Tests: 49 passed, 0 failed.
- All eight controller scenarios reach the expected classification.
- Evidence corruption and untracked files are rejected.
- Fuzz campaign corruption and untracked files are rejected; identical campaigns produce identical manifests.
- Safe state seed discovers the known synthetic signature at execution 47 with the checked-in 256-execution fixture.
- Recorded E1 run: novelty-guided 20/20 discoveries versus valid-mutation random 5/20 under paired 256-execution synthetic trials; no real-driver or significance claim is made.
- Recorded E2 run: stateful 20/20 discoveries versus single-call 0/20 under paired synthetic trials; the finding requires a three-operation chain.
- Duplicate JSON keys, unknown contract members, invalid state transitions, unsafe paths, and real-backend acquisition fail closed.
- Generated case, scenario, capability, environment, finding, run, fuzz campaign, and E1 experiment documents pass their JSON Schemas.

## Not implemented yet

- Similarity-assisted clustering and manual split/merge audit records.
- SQLite metadata for CAS references and retention garbage collection.
- Controller resume from every intermediate evidence-production state.
- Track B KMDF, Driver Verifier, real dump acquisition, and Hyper-V checkpoint replay. Track B remains blocked by environment and is not part of v1 completion.
