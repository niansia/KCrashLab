# Implementation status

Updated: 2026-09-01

## Working now

- Capability probe with `AVAILABLE`, `UNAVAILABLE`, `UNVERIFIED`, and `BLOCKED` states.
- Case IR v1 validation, canonical JSON, and stable SHA-256 case IDs.
- Typed campaign lifecycle backed by an append-only SQLite journal.
- Resume from `RUNNING` without a second dispatch.
- `ILabBackend` plus eight deterministic, virtual-time simulator scenarios.
- The ordinary-campaign Hyper-V backend always fails closed; destructive Track B execution is available only through the explicit gated G1 command.
- Versioned synthetic triage parser, stack normalization, signature v1, and exact grouping primitive.
- Sequence delta debugging demonstrated from 14 to 3 operations.
- Hierarchical minimization reduces the same fixture from 552 to 188 canonical bytes by shrinking schedule, optional fields, strings, and integers while preserving the signature.
- Deterministic 3/3 replay policy with infrastructure errors excluded from the vote.
- Replay voting can be used as a probabilistic minimization oracle for flaky synthetic targets.
- Content-addressed artifact copy, SHA-256 manifest, semantic bundle verifier, and static report.
- Deterministic boundary scalar, payload block, and sequence insert/delete/swap mutation operators with lineage and seed-keyed hash-ranked candidate caps that do not privilege early enumeration positions.
- Energy-ranked novelty corpus driven by synthetic semantic feedback.
- Exact-signature finding deduplication and deterministic fuzz campaign runner.
- Verifiable fuzz artifacts containing corpus, per-execution metrics, coverage, findings, manifest, and static report.
- Paired E1 2×2 policy ablation crossing keep-all/novelty-only admission with uniform/energy-ranked parent selection; operator and candidate selection remain identical across arms.
- Censoring-aware E1 Kaplan–Meier discovery curves plus paired factorial contrasts, raw CSV, claims limits, and manifest verification.
- Paired E2 stateful-versus-single-call experiment with sequence length as the only changed variable.
- Versioned experiment provenance: distinct source-commit/artifact time semantics, deterministic timestamp policy, clean `HEAD == git_commit` binding for canonical records, `GIT_TREE_SHA256_V1` object-database source digest, definition digest, Case IR version, and engine version. M1 additionally records and verifies canonical simulator-environment, scenario-fixture, minimizer, and replay-policy digests.
- Deterministic evidence text uses LF, UTF-8 without BOM, invariant culture, and ordinal ordering; generated JSON, CSV, HTML, reports, and manifests are contract-tested against platform newline drift.
- Fuzz termination telemetry distinguishes budget completion from scheduler-limit termination and records duplicate/empty scheduling work.
- Draft 2020-12 structural Case Schema/runtime parity tests execute a real schema validator over checked-in cases, randomized property order, and generated valid/invalid boundaries for depth, integers, Unicode rune lengths, lineage, fields, mutation metadata, operations, and schedules. Cross-field schedule-length equality and the byte-size envelope are tested separately as runtime semantic and transport invariants. Raw byte parsing rejects malformed UTF-8 before canonicalization.
- Exact .NET SDK pinning, locked NuGet dependency graphs, immutable Action revisions, and Windows 2022 plus Ubuntu 24.04 simulation CI lanes.
- Fault injection for ambiguous journal commits, duplicate/colliding events, CAS interruption/corruption, manifest loss/truncation, and invalid virtual-clock jumps.
- Fail-closed clean reviewer packaging using `git archive`, ZIP inspection, and separate recorded-results packaging.
- Safe CI that builds and tests simulation only; dependency audit treats NuGet advisories as errors.
- Track B source boundary: a safe-default repository-owned KMDF target, explicit lab-fault build property, deterministic Case IR compiler, fixed device path, driver hash pinning, write-through guest dispatch journal, strict real-lab profile validation, and dump-stability state tracking. This source has not yet been WDK/Hyper-V validated.
- One-command Track B G1 host automation with exact VM/checkpoint checks, private-switch enforcement, clean restore, heartbeat/reboot observation, one discovery plus three cold replays, stable dump acquisition, WinDbg batch triage, exact-signature voting, and `finally` recovery.
- Real WinDbg parser and sanitized REAL_LAB evidence builder/verifier; public bundles require 3/3 matching signatures and record—but do not publish—the raw dump hash.

## Last checked-in simulation baseline

- Evidence source commit: `55c31b25902157cddc9014bb9fdaa598bede40a2`; `GIT_TREE_SHA256_V1` source-tree digest: `bc0d06a2759e2429b0fec5683f3102e4644628f30b3fe6032a21a233d1333338`. Recorded evidence is committed in its child evidence/release revision to avoid circular self-reference; exact reruns must check out the source commit.
- Release build: zero warnings and zero errors.
- Latest protected-main CI baseline: 66 tests passed on Windows 2022, and the cross-platform simulation lane passed on Ubuntu 24.04. The final evidence-freeze source passes 73 tests locally with exact .NET 8.0.100 on Windows; both canonical evidence runs produced byte-identical manifests. This does not constitute WDK/Hyper-V Track B runtime validation.
- All eight controller scenarios reach the expected classification.
- Evidence corruption and untracked files are rejected.
- Fuzz campaign corruption and untracked files are rejected; identical campaigns produce identical manifests.
- The checked-in G3 current-policy record discovers the known synthetic signature at execution 211 in its 256-execution fixture, with 99 semantic feedback elements and 92 retained corpus cases.
- Recorded E1 v2 four-arm outcomes are 14/20 keep-all energy-ranked, 7/20 keep-all uniform, 18/20 novelty-only energy-ranked, and 12/20 novelty-only uniform discoveries. These are descriptive paired-seed observations, not a general superiority claim.
- Recorded E2 expressiveness validation: stateful 12/20 discoveries versus single-call 0/20 under paired synthetic trials; by construction, only the stateful arm can represent the required three-operation chain.
- The recorded minimization/replay bundle reduces the known synthetic trigger from 14 to 3 operations and 552 to 188 canonical bytes while preserving the exact signature in 3/3 simulated replays.
- Duplicate JSON keys, unknown contract members, invalid state transitions, unsafe paths, and real-backend acquisition fail closed.
- Generated case, scenario, capability, environment, finding, run, fuzz campaign, and E1 experiment documents pass their JSON Schemas.

## Not implemented yet

- Similarity-assisted clustering and manual split/merge audit records.
- SQLite metadata for CAS references and retention garbage collection.
- Controller resume from every intermediate evidence-production state.
- A genuine sanitized G1 evidence bundle from the owner's pinned Windows/WDK/Hyper-V lab. Source completeness and CI syntax/build validation do not substitute for this runtime evidence.
