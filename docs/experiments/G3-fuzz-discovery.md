# G3 deterministic fuzz-discovery record

Date: 2026-08-31

G3 demonstrates discovery mechanics against the repository-owned user-mode `kcl.state` model. It is not a kernel-crash benchmark and does not compare strategies.

## Reproduction

```powershell
dotnet run --project src/KCrashLab.Cli --configuration Release -- fuzz run `
  --seed samples/cases/state-safe-seed.case.json `
  --strategy novelty `
  --budget 256 `
  --campaign-seed 20260831 `
  --recorded-at 2026-08-31T00:00:00Z `
  --output results/recorded/g3

dotnet run --project src/KCrashLab.Cli --configuration Release -- fuzz verify results/recorded/g3
```

The CLI `novelty` alias selects `NOVELTY_ONLY_ENERGY_V2`.

## Recorded result

| Metric | Value |
|---|---:|
| Executions | 256 |
| First matching synthetic signature | execution 175 |
| Semantic feedback elements | 77 |
| Retained corpus cases | 88 |
| Raw synthetic failure observations | 48 |
| Exact signatures | 1 |
| Manifest entries | 94 |
| Offline verification | passed |

Signature: `c16b0923faeafce6e8196416e81c29d76ea01fa379e562fa7c785b373f6cf1db`

The 48 raw failure observations are repeated executions of one exact signature, not 48 independent discoveries. The first-execution value is deterministic only for the recorded source, seed case, policy set, mutation set, campaign seed, and budget.

## Preserved evidence

- `summary.json`: controlled parameters, counts, claims limits, findings, and provenance.
- `coverage.json`: sorted synthetic semantic-feedback elements.
- `metrics.csv`: every execution, parent, operator, novelty, admission decision, result, and signature.
- `corpus/*.case.json`: retained canonical inputs with mutation lineage.
- `findings/<signature>/`: first trigger and finding metadata.
- `report/index.html`: static report with the mandatory simulation banner.
- `manifest.sha256`: exact-byte integrity for every other file.

The verifier checks both the manifest and semantic relationships among the summary, metrics, coverage, corpus, and findings. A separately generated rerun with the same source and parameters must produce a byte-identical manifest before the record is accepted.
