# KCrashLab

KCrashLab is a simulation-tested control plane for reproducible Windows kernel-driver fuzzing research. The current v1 runs entirely in user mode: it validates environment capabilities, exercises deterministic failure scenarios, resumes campaigns from an append-only journal, performs novelty-guided mutation from a safe seed, minimizes synthetic triggers, and produces hash-verifiable evidence bundles.

> **Current status: SIMULATION ONLY — no real kernel crash, vulnerable driver, or Hyper-V checkpoint is used.**

The project deliberately fails closed when a disposable kernel lab, Hyper-V management, or matching WDK driver targets are unavailable. A future real-lab backend must implement the same lifecycle contract; it must never fall back to running kernel-facing work on the host.

## Why this project

Driver fuzzing is not only input mutation. A credible platform must recover after failures, distinguish target failures from infrastructure failures, reproduce the same signature from a clean baseline, minimize the trigger, and preserve enough evidence for someone else to audit the conclusion. KCrashLab makes those control-plane properties testable without pretending a Windows 11 Home workstation is a safe kernel lab.

```text
probe → safe seed → deterministic mutation → semantic feedback → corpus
      → exact signature → minimize/replay → evidence → offline verification
```

## Quick start

Prerequisite: .NET 8 SDK or newer. No administrator rights, WDK, Hyper-V, or VM is required for simulation.

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

The checked-in E1 method compares novelty-guided scheduling with uniform-random selection while holding the valid mutation envelope, seed case, and per-trial budget constant. In the recorded 20 paired synthetic trials, novelty-guided scheduling found the signature in 20/20 trials; the valid-mutation random baseline found it in 5/20, with 15 right-censored trials. This is a single-target descriptive result, not a claim about real drivers or statistical significance.

E1 also preserves a Kaplan–Meier cumulative discovery curve and paired outcomes so censored trials remain part of the analysis. E2 changes only the maximum sequence length: the single-call cap is 1 and the stateful cap is 6. The recorded synthetic result is 0/20 versus 20/20 discoveries because the known signature requires a three-operation prerequisite chain.

Canonical reviewer-facing results live under `results/recorded/g3`, `results/recorded/e1`, and `results/recorded/e2`; numbered `artifacts/*-vN` directories are transient local runs and are not cited as authoritative evidence. Every canonical summary records an explicit timestamp, Git state, source-tree digest, experiment-definition digest, Case IR version, and engine version.

## Repository map

- `src/KCrashLab.Contracts`: versioned backend and evidence contracts.
- `src/KCrashLab.Domain`: canonical case IDs, mutation operators, novelty corpus, lifecycle rules, signatures, replay, and minimization.
- `src/KCrashLab.Storage`: SQLite event journal and content-addressed evidence storage.
- `src/KCrashLab.Simulation`: virtual clock, scripted backend, synthetic target, and blocked real-backend skeleton.
- `src/KCrashLab.Controller`: resumable campaign orchestration and evidence production.
- `schemas`: JSON Schema 2020-12 contracts.
- `tests`: unit, component, contract, and golden-fixture tests.

See [architecture](docs/architecture.md), [threat model](docs/threat-model.md), [lab safety policy](docs/lab-safety.md), [reviewer packaging](docs/packaging.md), the [G3 discovery record](docs/experiments/G3-fuzz-discovery.md), [E1 experiment record](docs/experiments/E1-mutation-strategy.md), and [E2 experiment record](docs/experiments/E2-stateful-sequences.md).
The exact implemented/not-yet-implemented boundary is tracked in [implementation status](docs/status.md).

## Scope statement

This repository contains no exploit generation, privilege-escalation chain, third-party driver targeting, host kernel fuzzing, or deliberately vulnerable kernel driver. Simulated findings must always carry `execution_mode: SIMULATED` and the report banner `SIMULATED — NOT A REAL KERNEL CRASH`.

## License

No license has been selected yet. Until one is added, normal copyright restrictions apply.
