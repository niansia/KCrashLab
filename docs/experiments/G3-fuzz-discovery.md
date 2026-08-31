# G3 deterministic fuzz discovery record

Date: 2026-08-31

This record demonstrates the Phase 3 mechanics against the synthetic `kcl.state` target. It is not a kernel-crash benchmark and does not compare strategies.

## Reproduction command

```powershell
dotnet run --project src/KCrashLab.Cli --configuration Release -- fuzz run `
  --seed samples/cases/state-safe-seed.case.json `
  --budget 256 `
  --campaign-seed 20260831 `
  --recorded-at 2026-08-31T00:00:00Z `
  --output results/recorded/g3

dotnet run --project src/KCrashLab.Cli --configuration Release -- fuzz verify results/recorded/g3
```

## Recorded result

| Metric | Value |
|---|---:|
| Executions | 256 |
| First matching synthetic signature | execution 47 |
| Semantic feedback elements | 70 |
| Selected corpus cases | 149 |
| Raw synthetic failure observations | 110 |
| Exact signatures | 1 |
| Manifest entries | 155 |
| Offline verification | passed |

Signature: `c16b0923faeafce6e8196416e81c29d76ea01fa379e562fa7c785b373f6cf1db`

The raw failure count is not presented as 110 discoveries. Exact signature grouping reduces it to one synthetic finding. The first-execution value is deterministic only for this code, safe seed, operator ordering, budget, and campaign seed.

## Preserved evidence

- `summary.json`: campaign parameters, counts, limitations, and finding index.
- `coverage.json`: sorted synthetic semantic feedback elements.
- `metrics.csv`: every execution, lineage parent, operator, novelty, corpus decision, result, and signature.
- `corpus/*.case.json`: canonical retained inputs with mutation lineage.
- `findings/<signature>/`: first trigger and finding metadata.
- `report/index.html`: static human-readable report with a mandatory simulation banner.
- `manifest.sha256`: exact-byte integrity for every other file.

`summary.json` also records the source-tree and experiment-definition SHA-256 digests, Case IR version, engine version, recording time, and Git state. The current canonical record says `UNCOMMITTED` because this workspace did not yet have a first commit; the source-tree digest still binds the result to the exact source inputs. It must be regenerated after a real commit if a commit-bound release is desired.

The next E1/E2 study must run multiple independent seed trials and retain censored no-finding trials. It must not generalize this single deterministic fixture into a grammar-versus-random performance claim.
