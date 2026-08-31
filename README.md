# KCrashLab

**An evidence-first research platform for deterministic, reproducible Windows driver reliability experiments.**

[![Simulation CI](https://github.com/niansia/KCrashLab/actions/workflows/ci.yml/badge.svg)](https://github.com/niansia/KCrashLab/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

KCrashLab studies the parts of low-level failure research that are easy to overlook: deterministic input generation, recovery after interruption, exact failure identity, trigger minimization, cold replay, provenance, and independently verifiable evidence.

The default workflow runs entirely in user mode against a synthetic state machine. A separately gated Windows-lab track exists for the repository-owned target, but it is never selected by ordinary campaign commands.

> [!IMPORTANT]
> **All evidence currently committed to this repository is simulated.** The presence of Windows-lab source code does not prove that a driver was loaded, a system failure occurred, a checkpoint was restored, or a real vulnerability was found. KCrashLab does not target third-party drivers and does not generate exploits.

## Project status

| Area | Status | What the status means |
|---|:---:|---|
| Deterministic simulation | ✅ | Implemented and exercised by repository tests and recorded synthetic artifacts |
| Resumable campaign controller | ✅ | Append-only journal, recovery, terminal-state handling, and idempotent evidence production |
| Case mutation and corpus scheduling | ✅ | Deterministic policies, stateful sequences, semantic feedback, and explicit scheduler telemetry |
| Triage, replay, and minimization | ✅ | Exact signatures, replay voting, hierarchical reduction, and lineage-preserving cases |
| Evidence verification | ✅ | Content hashes plus semantic consistency checks across summaries, cases, reports, and raw tables |
| Controlled Windows-lab source | 🧪 | Gated implementation is present; no runtime result is committed |
| Third-party target support | — | Intentionally out of scope |

`✅` means implemented in the public simulation path. `🧪` means source is available for review but runtime evidence is absent. `—` means intentionally unsupported.

## Why this project exists

Finding a failure is only the beginning. A credible research workflow must also answer:

1. Can the exact input be identified independently of file formatting?
2. Can an interrupted campaign resume without inventing or losing state?
3. Can the same failure be reproduced from a clean baseline?
4. Can the trigger be reduced while preserving its exact signature?
5. Can another reviewer verify the result without trusting the producer?
6. Are simulated observations clearly separated from real-machine observations?

KCrashLab turns those questions into explicit contracts, tests, artifacts, and failure-closed gates.

## System overview

```text
environment probe
    → canonical Case IR
    → deterministic mutation and scheduling
    → semantic observation and corpus admission
    → exact signature and finding deduplication
    → minimization and replay voting
    → evidence bundle and manifest
    → offline structural + semantic verification
```

The controller communicates through `ILabBackend`. The simulation backend is the only backend available to ordinary campaigns. The Windows-lab path has a separate entry point, private profile, explicit confirmation boundary, fixed repository target, and clean-checkpoint replay policy.

## Technical highlights

### Canonical Case IR

Inputs are normalized into a versioned JSON representation. Semantic identity is a SHA-256 digest of canonical content, while lineage metadata remains available for audit. Equivalent inputs therefore deduplicate even when they were reached through different mutation paths.

### Deterministic scheduling

Parent, operator, and candidate decisions use independent seed-derived decision lanes. Candidate caps use `HASH_RANKED_V1`: the complete valid candidate set is ranked from the campaign seed, operator identifier, and semantic case identifier before truncation. This avoids favoring whichever fields happen to be enumerated first.

Campaign summaries expose the termination reason, scheduling iterations and limit, duplicate-candidate skips, empty polls, candidate-selection rule, and per-operator cap. A scheduler-limited run cannot be silently reported as a fully consumed execution budget or proof of global search-space exhaustion.

### Exact failure identity

Findings are grouped by a versioned signature derived from normalized triage fields rather than by a generic failure label. Replay uses explicit voting, and minimization is accepted only while the target signature remains unchanged.

### Resumability and idempotence

Campaign transitions are recorded in an append-only SQLite journal. Restarting the controller reconstructs state and resumes the unfinished stage. Evidence publication is idempotent, so recovery does not create multiple authoritative bundles for one campaign.

### Evidence beyond checksums

Every bundle includes a SHA-256 manifest, but verification does not stop there. KCrashLab also checks cross-file identities, summary bounds, trial counts, paired seeds, claims, canonical cases, CSV rows, reports, and provenance fields. A self-consistent hash over inconsistent research data is rejected.

## Reproduce the simulation path

### Requirements

- Windows, Linux, or macOS for the simulation path
- .NET SDK `8.0.100` exactly, as pinned by [`global.json`](global.json)
- No administrator rights, WDK, Hyper-V, or VM

### Build and test

```powershell
dotnet --version
dotnet restore KCrashLab.sln
dotnet build KCrashLab.sln --configuration Release --no-restore
dotnet test KCrashLab.sln --configuration Release --no-build
```

### Run a deterministic campaign

```powershell
dotnet run --project src/KCrashLab.Cli --configuration Release -- `
  campaign run `
  --scenario dump-ready `
  --case samples/cases/state-original.case.json `
  --output artifacts/demo

dotnet run --project src/KCrashLab.Cli --configuration Release -- `
  evidence verify artifacts/demo
```

### Run deterministic discovery

```powershell
dotnet run --project src/KCrashLab.Cli --configuration Release -- `
  fuzz run `
  --seed samples/cases/state-safe-seed.case.json `
  --budget 256 `
  --campaign-seed 20260831 `
  --output artifacts/fuzz

dotnet run --project src/KCrashLab.Cli --configuration Release -- `
  fuzz verify artifacts/fuzz
```

### Run the controlled experiments

```powershell
dotnet run --project src/KCrashLab.Cli --configuration Release -- `
  experiment e1 `
  --seed samples/cases/state-safe-seed.case.json `
  --budget 256 `
  --trials 20 `
  --base-seed 20260831 `
  --output artifacts/e1

dotnet run --project src/KCrashLab.Cli --configuration Release -- `
  experiment e2 `
  --seed samples/cases/state-reset-seed.case.json `
  --budget 512 `
  --trials 20 `
  --base-seed 20260831 `
  --output artifacts/e2

dotnet run --project src/KCrashLab.Cli --configuration Release -- `
  experiment verify artifacts/e1

dotnet run --project src/KCrashLab.Cli --configuration Release -- `
  experiment verify artifacts/e2
```

E1 is a paired 2×2 ablation of corpus admission and parent selection. E2 changes only the maximum sequence length, making it an expressiveness check for a known multi-step synthetic condition—not a general performance claim. See the [E1](docs/experiments/E1-mutation-strategy.md) and [E2](docs/experiments/E2-stateful-sequences.md) experiment records for controls and interpretation limits.

## Recorded artifacts and provenance

Reviewer-facing synthetic artifacts are stored under:

- [`results/recorded/g3`](results/recorded/g3) — deterministic discovery;
- [`results/recorded/e1`](results/recorded/e1) — policy ablation;
- [`results/recorded/e2`](results/recorded/e2) — stateful versus single-call experiment.

Local `artifacts/*` directories are disposable outputs and are not authoritative. A recorded result is accepted only after generation from a clean commit, semantic verification, and an independent deterministic rerun with a matching manifest. Provenance records the source revision, source-tree digest, Case IR version, engine version, experiment-definition digest, and timestamp policy.

Historical artifacts do not retroactively inherit stronger guarantees from newer code. Read [`docs/status.md`](docs/status.md) before citing a result.

## Controlled Windows-lab track

The repository also contains a separately gated implementation for the project-owned test target:

- a safe-default KMDF build and a distinct opt-in lab build;
- a fixed device interface and allowlisted request contract;
- a Windows guest dispatcher with a durable attempt journal;
- a versioned private profile and fail-closed validation;
- exact VM/checkpoint/private-network checks;
- one discovery attempt followed by three clean-checkpoint replays;
- dump-readiness checks, WinDbg parsing, exact signatures, and sanitized public evidence construction.

This track is not run by normal CI and is not runtime-verified by the checked-in repository. Its entry point requires an explicitly prepared, disposable, isolated owner-controlled environment. The full prerequisites and publication boundary are documented in the [controlled-lab runbook](docs/track-b-runbook.md) and [publication checklist](docs/publication-checklist.md).

## Repository layout

| Path | Responsibility |
|---|---|
| [`src/KCrashLab.Contracts`](src/KCrashLab.Contracts) | Versioned backend, case, experiment, and evidence contracts |
| [`src/KCrashLab.Domain`](src/KCrashLab.Domain) | Canonicalization, scheduling, mutation, signatures, replay, and minimization |
| [`src/KCrashLab.Storage`](src/KCrashLab.Storage) | SQLite journal and content-addressed storage |
| [`src/KCrashLab.Simulation`](src/KCrashLab.Simulation) | Virtual clock, scripted backend, synthetic target, and environment gates |
| [`src/KCrashLab.Controller`](src/KCrashLab.Controller) | Resumable orchestration and artifact production |
| [`src/KCrashLab.GuestAgent`](src/KCrashLab.GuestAgent) | Windows-only dispatcher for the fixed project-owned contract |
| [`drivers/KCrashLab.Target`](drivers/KCrashLab.Target) | Repository-owned KMDF target source |
| [`schemas`](schemas) | JSON Schema 2020-12 contracts |
| [`tests`](tests) | Unit, component, contract, recovery, and golden-fixture tests |
| [`docs`](docs) | Architecture, methods, safety boundaries, claims, and experiment records |

## Research integrity and limitations

- `SIMULATED` and `REAL_LAB` are separate evidence classes and may not be conflated.
- No checked-in artifact currently establishes a real-machine result.
- Synthetic discovery latency is not a benchmark of real target performance.
- E2 is a controlled sanity experiment, not a universal claim about stateful methods.
- Manifests establish integrity after production; they do not prove that the producing machine was honest.
- Raw memory captures and unreviewed diagnostic output are private by default.
- Third-party targeting, exploitability assessment, and exploit generation are outside project scope.

The mapping from every public claim to its supporting artifact is maintained in the [research claims and evidence ledger](docs/research-claims.md).

## Documentation

- [Architecture](docs/architecture.md)
- [Implementation status](docs/status.md)
- [Threat model](docs/threat-model.md)
- [Lab safety policy](docs/lab-safety.md)
- [Reviewer packaging guide](docs/packaging.md)
- [Security policy](SECURITY.md)
- [Citation metadata](CITATION.cff)

## License

Licensed under the [Apache License 2.0](LICENSE).
