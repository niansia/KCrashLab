# KCrashLab

[![simulation-ci](https://github.com/niansia/KCrashLab/actions/workflows/ci.yml/badge.svg)](https://github.com/niansia/KCrashLab/actions/workflows/ci.yml)

**Research Preview v0.1 · Reproducible control-plane research for Windows kernel-driver fuzzing**

KCrashLab is a simulation-tested research prototype for the difficult parts around a kernel fuzzer: deterministic case identity, policy-controlled mutation, crash-signature grouping, resumable orchestration, trigger minimization, replay voting, evidence integrity, and failure recovery.

> **Current execution mode: SIMULATED.** This repository does not contain a vulnerable kernel driver, does not load a driver, and does not claim a real Windows kernel crash. Track B remains fail-closed until a disposable and recoverable Windows lab is available.

## Research question

A useful fuzzing result is more than “the target stopped.” It must answer:

- Can the campaign resume after an ambiguous failure without dispatching the same case twice?
- Can two observations be grouped by an exact, versioned signature instead of a filename?
- Can the trigger be minimized while preserving the same signature under replay?
- Can a reviewer verify the artifact without trusting the machine that produced it?
- Can scheduling policies be compared without silently changing the mutation engine at the same time?

KCrashLab turns those questions into executable contracts and tamper-evident artifacts.

```text
safe Case IR
    │
    ├─ canonical identity + mutation lineage
    ▼
policy-driven fuzz engine ── semantic feedback ── corpus scheduler
    │                                              │
    └──────────── deterministic execution log ─────┘
                           │
                           ▼
exact signature ── minimize ── replay vote ── evidence manifest
                           │
                           └─ offline semantic verification
```

## Capability matrix

| Capability | Simulation track | Real kernel lab |
|---|---|---|
| Case IR v1, canonical JSON, SHA-256 case IDs | Implemented and tested | Reusable contract |
| Boundary, payload, and stateful sequence mutations | Implemented | Harness integration pending |
| 2×2 corpus-admission / parent-selection ablation | Implemented on one synthetic target | Not evaluated |
| Append-only SQLite campaign journal and resume | Implemented with fault injection | Backend integration pending |
| Versioned triage and exact signature grouping | Implemented with synthetic WinDbg-like input | Real dump parser pending |
| Hierarchical minimization and replay voting | Implemented | VM checkpoint replay pending |
| CAS, SHA-256 manifest, static report, semantic verifier | Implemented | Reusable contract |
| Hyper-V lease/checkpoint lifecycle | Contract only; fails closed | Blocked by environment |
| KMDF targets, IOCTL harness, Driver Verifier, dump capture | Not included | Not implemented |
| Kernel coverage or real-driver performance claims | Not claimed | No evidence yet |

## Recorded evidence

Canonical reviewer-facing outputs live in [`results/recorded`](results/recorded). Every set contains a SHA-256 manifest and a semantic verifier; every summary binds the experiment definition, Case IR version, engine version, source-tree digest, recording time, and source commit.

### G3 — deterministic discovery mechanics

One 256-execution `kcl.state` campaign using `NOVELTY_ONLY_ENERGY_V2`:

| Metric | Recorded value |
|---|---:|
| First exact signature | execution 175 |
| Semantic feedback elements | 77 |
| Retained corpus cases | 88 |
| Raw synthetic failures | 48 |
| Exact signatures | 1 |

The 48 raw observations are not presented as 48 bugs. Exact-signature grouping reduces them to one known synthetic finding.

### E1 — 2×2 policy ablation

All four arms use the same engine, safe seed, operators, candidate enumeration, per-trial budget, and deterministic decision-stream construction. Only corpus admission and parent selection are crossed:

| Corpus admission | Parent selection | Discoveries / 20 | Censored |
|---|---|---:|---:|
| Keep all | Uniform | 5 | 15 |
| Keep all | Energy | 14 | 6 |
| Novelty only | Uniform | 16 | 4 |
| Novelty only | Energy | 13 | 7 |

The result shows an interaction on this fixture: novelty admission helps under uniform parent selection, while the energy scheduler changes that relationship. It does **not** support a universal “novelty is better” or “energy is better” claim. Censored trials remain in the Kaplan–Meier analysis and raw paired outcomes.

### E2 — stateful expressiveness validation

The target signature structurally requires a three-operation prerequisite chain. With a 512-execution budget across 20 paired seeds, the single-call cap found 0/20 and the stateful cap found 11/20; nine stateful trials were censored. This validates that the stateful representation can express and sometimes reach the chain. It is a controlled sanity check, not a general performance benchmark.

The separate minimization fixture shrinks from 14 to 3 operations and from 552 to 188 canonical bytes while retaining a 3/3 matching replay vote.

## Quick start

Prerequisite: a .NET 10 SDK. The projects target `net8.0`; simulation requires no administrator privileges, WDK, Hyper-V, VM, or driver installation.

```powershell
git clone https://github.com/niansia/KCrashLab.git
cd KCrashLab
dotnet restore KCrashLab.sln
dotnet test KCrashLab.sln -c Release
```

Probe the current machine without changing it:

```powershell
dotnet run --project src/KCrashLab.Cli -c Release -- `
  lab probe --output artifacts/capability-report.json
```

Run and verify the safe end-to-end fixture:

```powershell
dotnet run --project src/KCrashLab.Cli -c Release -- `
  campaign run --scenario dump-ready `
  --case samples/cases/state-original.case.json `
  --output artifacts/demo

dotnet run --project src/KCrashLab.Cli -c Release -- `
  evidence verify artifacts/demo/finding
```

Run deterministic fuzz discovery:

```powershell
dotnet run --project src/KCrashLab.Cli -c Release -- `
  fuzz run --seed samples/cases/state-safe-seed.case.json `
  --strategy novelty --budget 256 --campaign-seed 20260831 `
  --output artifacts/fuzz

dotnet run --project src/KCrashLab.Cli -c Release -- `
  fuzz verify artifacts/fuzz
```

Run the two recorded experiment designs:

```powershell
dotnet run --project src/KCrashLab.Cli -c Release -- `
  experiment e1 --seed samples/cases/state-safe-seed.case.json `
  --budget 256 --trials 20 --base-seed 20260831 `
  --output artifacts/e1

dotnet run --project src/KCrashLab.Cli -c Release -- `
  experiment e2 --seed samples/cases/state-reset-seed.case.json `
  --budget 512 --trials 20 --base-seed 20260831 `
  --output artifacts/e2

dotnet run --project src/KCrashLab.Cli -c Release -- experiment verify artifacts/e1
dotnet run --project src/KCrashLab.Cli -c Release -- experiment verify artifacts/e2
```

## Design properties

- **Fail closed:** an unavailable real-lab prerequisite produces `BLOCKED_BY_ENVIRONMENT`; it never falls back to the host.
- **Deterministic decisions:** parent, operator, and candidate decisions use independent SHA-256-derived lanes keyed by campaign seed and scheduling iteration.
- **Crash ≠ infrastructure failure:** timeouts, corrupt artifacts, and agent loss do not become findings.
- **Exact-byte evidence:** manifests reject modified, missing, and untracked files.
- **Semantic verification:** verifiers recompute experiment invariants instead of checking hashes alone.
- **Fault-injected recovery:** tests cover ambiguous commits, duplicate/colliding events, interrupted/corrupt CAS writes, manifest loss, and invalid virtual-clock jumps.
- **Claims discipline:** every simulated report carries a mandatory simulation banner and explicit non-claims.

## Repository layout

| Path | Responsibility |
|---|---|
| [`src/KCrashLab.Contracts`](src/KCrashLab.Contracts) | Versioned Case IR, campaign, fuzzing, experiment, and evidence contracts |
| [`src/KCrashLab.Domain`](src/KCrashLab.Domain) | Canonical identity, policy engine, mutation, signatures, replay, minimization |
| [`src/KCrashLab.Storage`](src/KCrashLab.Storage) | SQLite journal, content-addressed storage, evidence manifests |
| [`src/KCrashLab.Simulation`](src/KCrashLab.Simulation) | Capability probe, deterministic fixtures, synthetic target, virtual clock |
| [`src/KCrashLab.Controller`](src/KCrashLab.Controller) | Resumable orchestration and artifact production/verification |
| [`schemas`](schemas) | JSON Schema 2020-12 contracts |
| [`tests`](tests) | Unit, component, contract, golden-fixture, and fault-injection tests |
| [`docs`](docs) | Architecture, safety, threat model, ADR, and experiment records |

## Reproduce the checked-in claims

The exact commands and interpretation boundaries are documented in:

- [G3 deterministic discovery](docs/experiments/G3-fuzz-discovery.md)
- [E1 2×2 policy ablation](docs/experiments/E1-mutation-strategy.md)
- [E2 stateful expressiveness](docs/experiments/E2-stateful-sequences.md)
- [Architecture](docs/architecture.md)
- [Implementation status](docs/status.md)
- [Threat model](docs/threat-model.md)
- [Lab safety policy](docs/lab-safety.md)

## Safety and scope

KCrashLab is limited to repository-owned synthetic targets and offline evidence. Do not use it to probe third-party drivers or devices. It contains no exploit generation, privilege-escalation chain, persistence, evasion, host-kernel fuzzing, or deliberately vulnerable kernel driver. Memory dumps are excluded from source packages because they can contain secrets and personal data.

See [`SECURITY.md`](SECURITY.md) before extending Track B.

## Roadmap

1. Add multiple synthetic targets with deliberately coarse feedback and preregistered ablations.
2. Add similarity-assisted clustering with auditable manual split/merge records.
3. Complete intermediate-state evidence-production resume and CAS retention metadata.
4. Build Track B only inside a disposable Windows Pro/Enterprise VM with immutable checkpoint, WDK, Driver Verifier recovery, dump-space validation, and a tested kill switch.

## License

No open-source license has been granted. Normal copyright restrictions apply. A license should be selected explicitly before accepting reuse or external contributions.
