# Implementation status

Updated: 2026-08-31

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
- Versioned experiment provenance: distinct source-commit/artifact time semantics, deterministic timestamp policy, Git state, source-tree digest, definition digest, Case IR version, and engine version.
- Fuzz termination telemetry distinguishes budget completion from scheduler-limit termination and records duplicate/empty scheduling work.
- Case Schema/runtime parity tests cover lineage hashes, mutation/field property limits, and the complete checked-in sample operation allowlist.
- Exact .NET SDK pinning plus Windows 2022 and Ubuntu 24.04 simulation CI lanes.
- Fault injection for ambiguous journal commits, duplicate/colliding events, CAS interruption/corruption, manifest loss/truncation, and invalid virtual-clock jumps.
- Fail-closed clean reviewer packaging using `git archive`, ZIP inspection, and separate recorded-results packaging.
- Safe CI that builds and tests simulation only; dependency audit treats NuGet advisories as errors.
- Track B source boundary: a safe-default repository-owned KMDF target, explicit lab-fault build property, deterministic Case IR compiler, fixed device path, driver hash pinning, write-through guest dispatch journal, strict real-lab profile validation, and dump-stability state tracking. This source has not yet been WDK/Hyper-V validated.
- One-command Track B G1 host automation with exact VM/checkpoint checks, private-switch enforcement, clean restore, heartbeat/reboot observation, one discovery plus three cold replays, stable dump acquisition, WinDbg batch triage, exact-signature voting, and `finally` recovery.
- Real WinDbg parser and sanitized REAL_LAB evidence builder/verifier; public bundles require 3/3 matching signatures and record—but do not publish—the raw dump hash.

## Last checked-in simulation baseline

- Release build: zero warnings and zero errors.
- The initial simulation preview recorded 49 passing tests. New Track B tests require a fresh Windows/.NET run before this count is updated; repository text must not imply that an unavailable runner executed them.
- All eight controller scenarios reach the expected classification.
- Evidence corruption and untracked files are rejected.
- Fuzz campaign corruption and untracked files are rejected; identical campaigns produce identical manifests.
- Safe state seed discovers the known synthetic signature at execution 47 with the checked-in 256-execution fixture.
- The checked-in E1 v1 record is legacy provenance, not evidence for the implemented v2 policy ablation; canonical v2 results are pending regeneration from a committed tree.
- Recorded E2 expressiveness validation: stateful 20/20 discoveries versus single-call 0/20 under paired synthetic trials; by construction, the finding requires a three-operation chain.
- Duplicate JSON keys, unknown contract members, invalid state transitions, unsafe paths, and real-backend acquisition fail closed.
- Generated case, scenario, capability, environment, finding, run, fuzz campaign, and E1 experiment documents pass their JSON Schemas.

## Not implemented yet

- Similarity-assisted clustering and manual split/merge audit records.
- SQLite metadata for CAS references and retention garbage collection.
- Controller resume from every intermediate evidence-production state.
- A genuine sanitized G1 evidence bundle from the owner's pinned Windows/WDK/Hyper-V lab. Source completeness and CI syntax/build validation do not substitute for this runtime evidence.
