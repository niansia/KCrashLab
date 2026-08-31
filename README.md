# KCrashLab

KCrashLab is a simulation-tested control plane for reproducible Windows kernel-driver fuzzing research. The default v1 workflow runs entirely in user mode: it validates environment capabilities, exercises deterministic failure scenarios, resumes campaigns from an append-only journal, performs novelty-guided mutation from a safe seed, minimizes synthetic triggers, and produces hash-verifiable evidence bundles. Track B source is present but reachable only through an explicit destructive-lab command.

> **Checked-in evidence status: SIMULATION ONLY — no checked-in result claims a real kernel crash or Hyper-V checkpoint run. Track B execution remains an explicit private-lab operation.**

| Capability | Simulation track | Windows lab track |
|---|:---:|:---:|
| Campaign state machine | ✅ | 🧪 |
| Deterministic mutation and stateful sequences | ✅ | 🚧 |
| Replay voting and minimization | ✅ | 🧪 |
| Content-addressed, verifiable evidence | ✅ | 🧪 |
| Hyper-V checkpoint recovery | simulated | 🧪 |
| KMDF IOCTL execution | — | 🧪 |
| Kernel dump acquisition and WinDbg triage | — | 🧪 |

`✅` means verified by checked-in evidence, `🧪` means the complete gated implementation is present but awaits owner-lab runtime evidence, `🚧` means not implemented for that track, and `—` means not applicable. Third-party driver fuzzing and exploit generation are out of scope, not roadmap items.

The project deliberately fails closed when a disposable kernel lab, Hyper-V management, or matching WDK driver targets are unavailable. Ordinary campaigns never activate a partial real backend; Track B is reachable only through the explicit G1 command and never falls back to kernel-facing work on the host.

## Why this project

Driver fuzzing is not only input mutation. A credible platform must recover after failures, distinguish target failures from infrastructure failures, reproduce the same signature from a clean baseline, minimize the trigger, and preserve enough evidence for someone else to audit the conclusion. KCrashLab makes those control-plane properties testable without pretending a Windows 11 Home workstation is a safe kernel lab.

```text
probe → safe seed → deterministic mutation → semantic feedback → corpus
      → exact signature → minimize/replay → evidence → offline verification
```

## Quick start

Prerequisite: a .NET 8 SDK accepted by `global.json`. No administrator rights, WDK, Hyper-V, or VM is required for simulation.

```powershell
dotnet restore KCrashLab.sln
dotnet test KCrashLab.sln
dotnet run --project src/KCrashLab.Cli -- lab probe --output artifacts/capability-report.json
dotnet run --project src/KCrashLab.Cli -- campaign run --scenario dump-ready --case samples/cases/state-original.case.json --output artifacts/demo
dotnet run --project src/KCrashLab.Cli -- evidence verify artifacts/demo
dotnet run --project src/KCrashLab.Cli -- fuzz run --seed samples/cases/state-safe-seed.case.json --budget 256 --campaign-seed 20260831 --output artifacts/fuzz
dotnet run --project src/KCrashLab.Cli -- fuzz verify artifacts/fuzz
dotnet run --project src/KCrashLab.Cli -- experiment e1 --seed samples/cases/state-safe-seed.case.json --budget 256 --trials 20 --base-seed 20260831 --output artifacts/e1
dotnet run --project src/KCrashLab.Cli -- experiment verify artifacts/e1
dotnet run --project src/KCrashLab.Cli -- experiment e2 --seed samples/cases/state-reset-seed.case.json --budget 512 --trials 20 --base-seed 20260831 --output artifacts/e2
dotnet run --project src/KCrashLab.Cli -- experiment verify artifacts/e2
```

With the checked-in seed and campaign seed, the simulated fuzzer deterministically reaches the known state-machine signature at execution 47. The output preserves every selected corpus case and its lineage, per-execution metrics, semantic feedback, exact-signature findings, a static report, and a SHA-256 manifest. The finding pipeline then shrinks the 14-operation fixture to 3 operations and 552 canonical bytes to 188 while retaining a 3/3 replay match. These are reproducibility fixtures, not real crash benchmarks.

E1 is implemented as a 2×2 policy ablation: keep-all versus novelty-only corpus admission, crossed with uniform versus energy-ranked parent selection. All four arms share the same operator selection and candidate selection policies, valid mutation envelope, seed, budget, and paired campaign seeds. This design separates the two scheduler factors instead of attributing differences between two bundled engines to semantic novelty alone. Canonical v2 results must be regenerated with a committed tree before any performance numbers are claimed.

Mutation candidate caps are experimental parameters rather than enumeration accidents: every operator defines its complete valid candidate universe, then `HASH_RANKED_V1` deterministically ranks candidates from the campaign seed, operator ID, and semantic case ID before applying the cap. Campaign summaries also expose scheduler iterations, duplicate skips, empty polls, the scheduling limit, and the termination reason, so a short run cannot be silently presented as budget exhaustion.

E1 preserves a Kaplan–Meier cumulative discovery curve and paired factorial contrasts so censored trials remain part of the analysis. E2 changes only the maximum sequence length: the single-call cap is 1 and the stateful cap is 6. Because the fixture requires a three-operation prerequisite chain, E2 is an expressiveness/sanity validation—not evidence that stateful fuzzing generally outperforms single-call fuzzing.

Canonical reviewer-facing results live under `results/recorded/g3`, `results/recorded/e1`, and `results/recorded/e2`; numbered `artifacts/*-vN` directories are transient local runs and are not cited as authoritative evidence. New canonical summaries record the source commit time separately from artifact creation time and may use the deterministic `SOURCE_COMMIT_TIME` policy. They also bind the Git state, source-tree digest, explicit mutation/candidate parameters, Case IR version, and engine version. Legacy v1 records remain readable but do not acquire stronger provenance retroactively.

## Repository map

- `src/KCrashLab.Contracts`: versioned backend and evidence contracts.
- `src/KCrashLab.Domain`: canonical case IDs, mutation operators, novelty corpus, lifecycle rules, signatures, replay, and minimization.
- `src/KCrashLab.Storage`: SQLite event journal and content-addressed evidence storage.
- `src/KCrashLab.Simulation`: virtual clock, scripted backend, synthetic target, blocked ordinary-campaign real backend, and Track B profile/dump gates.
- `src/KCrashLab.Controller`: resumable campaign orchestration and evidence production.
- `src/KCrashLab.GuestAgent`: Windows-only, hash-pinned Case IR to allowlisted IOCTL dispatcher with a write-through attempt journal.
- `drivers/KCrashLab.Target`: repository-owned KMDF safe target plus an explicit disposable-lab bugcheck-oracle build mode.
- `schemas`: JSON Schema 2020-12 contracts.
- `tests`: unit, component, contract, and golden-fixture tests.

See [architecture](docs/architecture.md), [threat model](docs/threat-model.md), [lab safety policy](docs/lab-safety.md), [Track B runbook](docs/track-b-runbook.md), [reviewer packaging](docs/packaging.md), the [G3 discovery record](docs/experiments/G3-fuzz-discovery.md), [E1 experiment record](docs/experiments/E1-mutation-strategy.md), and [E2 experiment record](docs/experiments/E2-stateful-sequences.md).
The exact implemented/not-yet-implemented boundary is tracked in [implementation status](docs/status.md).

Reviewers can audit every public assertion through the [research claims and evidence ledger](docs/research-claims.md). Before publishing any real-lab material, maintainers must also complete the [public evidence release checklist](docs/publication-checklist.md), including dump/log redaction and coordinated-disclosure review.

## Track B controlled Windows lab

Track B now includes a safe-default KMDF target, hash-pinned guest dispatcher, strict private lab profile, a discovery-plus-three-cold-replays Hyper-V G1 automation command, real WinDbg parsing, and a sanitized real-evidence verifier. None of those source files is evidence that a kernel run occurred. The Windows Lab column remains 🧪 until a checked-in sanitized bundle from the pinned lab verifies successfully.

The complete destructive-lab command and prerequisites are documented in the [Track B runbook](docs/track-b-runbook.md). Its lifecycle is:

```text
validate profile/VM/checkpoint/network → restore → boot/heartbeat → hash-pinned dispatch
→ observe reboot → stable exclusive dump → WinDbg → exact signature → restore
→ repeat 3/3 → sanitized bundle without dump → offline verify
```

## Scope statement

This repository contains no exploit generation, privilege-escalation chain, third-party driver targeting, host kernel fuzzing, or deliberately vulnerable kernel driver. Simulated findings must always carry `execution_mode: SIMULATED` and the report banner `SIMULATED — NOT A REAL KERNEL CRASH`.

## License

Licensed under the [Apache License 2.0](LICENSE).
