# Implementation status

Updated: 2026-08-31

Release label: Research Preview v0.1

## Implemented

- Read-only host capability probe with `AVAILABLE`, `UNAVAILABLE`, `UNVERIFIED`, and `BLOCKED` states.
- Case IR v1 validation, canonical JSON, stable SHA-256 identities, and explicit mutation lineage.
- Typed campaign lifecycle backed by an append-only SQLite event journal.
- Resume from `RUNNING` after an ambiguous commit without a second dispatch.
- `ILabBackend`, eight deterministic simulator scenarios, and a virtual clock with no test sleeps.
- Contract-only Hyper-V backend that always fails closed.
- Versioned synthetic triage parser, stack normalization, Signature v1, and exact grouping.
- Sequence and hierarchical minimization with signature-preserving replay.
- Deterministic replay voting with infrastructure failures excluded from the denominator.
- Content-addressed artifact storage, exact-byte manifest, static report, and semantic verifier.
- Boundary scalar, payload block, sequence insert/delete/swap operators, and sequence-length policy.
- Shared policy-driven fuzz engine with explicit corpus-admission, parent, operator, and candidate policies.
- Independent SHA-256-derived decision lanes for reproducible scheduling.
- Four-arm E1 2×2 ablation, right censoring, Kaplan–Meier curve, paired contrasts, raw CSV, and claims limits.
- E2 single-call/stateful expressiveness validation.
- Versioned provenance binding source, experiment definition, Case IR, engine, recording time, and source commit.
- Fault injection for journal ambiguity/collision, CAS interruption/corruption, manifest loss/truncation, and clock bounds.
- Fail-closed reviewer packaging through `git archive` with generated results packaged separately.
- Windows CI for restore, build, dependency audit, tests, safe end-to-end runs, artifact verification, and packaging.

## Verified for this release

- Release build: zero warnings and zero errors.
- Tests: 49 passed, 0 failed.
- NuGet audit: no known vulnerable direct or transitive package reported by configured sources.
- All eight simulator scenarios reach their expected classification.
- Duplicate JSON keys, unknown members, unsafe paths, corrupt evidence, and real-backend acquisition fail closed.
- G3: 256/256 simulated executions, first known signature at execution 175, 48 raw failure observations grouped into one exact signature.
- E1: four 20-trial policy arms complete with all no-finding trials retained as right-censored observations.
- E2: single-call 0/20 versus stateful 11/20 on a structurally stateful synthetic trigger; treated only as expressiveness evidence.
- The 14-operation minimization fixture reduces to 3 operations and 188 canonical bytes with a 3/3 replay match.
- Canonical G3, E1, and E2 artifacts pass hash and semantic verification.
- Identically parameterized reruns produce byte-identical manifests.

## Not implemented

- Additional synthetic targets with coarse or delayed feedback.
- Similarity-assisted clustering and auditable manual split/merge records.
- SQLite reference metadata and retention garbage collection for content-addressed artifacts.
- Resume from every intermediate evidence-production state.
- KMDF test targets, IOCTL harness, Driver Verifier orchestration, real dump acquisition, and checkpoint replay.
- Kernel coverage collection or evaluation against real drivers.

Track B remains blocked by environment and is not part of Research Preview v0.1 completion.
