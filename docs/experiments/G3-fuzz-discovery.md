# G3 deterministic fuzz-discovery record

Date: 2026-09-01

G3 demonstrates discovery mechanics against the repository-owned user-mode `kcl.state` model. It is not a kernel-crash benchmark and does not compare strategies.

## Reproduction

```powershell
git checkout --detach 55c31b25902157cddc9014bb9fdaa598bede40a2

dotnet run --project src/KCrashLab.Cli --configuration Release -- fuzz run `
  --seed samples/cases/state-safe-seed.case.json `
  --strategy novelty `
  --budget 256 `
  --campaign-seed 20260831 `
  --recorded-at SOURCE_COMMIT_TIME `
  --output artifacts/evidence-freeze-rerun/g3

dotnet run --project src/KCrashLab.Cli --configuration Release -- fuzz verify artifacts/evidence-freeze-rerun/g3
```

Exact evidence reproduction requires the clean source commit above. Compare the resulting manifest bytes with `results/recorded/g3/manifest.sha256` from the evidence/release revision; executing from that child revision correctly produces different Git provenance.

The CLI `novelty` alias selects `NOVELTY_ONLY_ENERGY_RANKED_V2`.

## Recorded result

| Metric | Value |
|---|---:|
| Executions | 256 |
| First matching synthetic signature | execution 211 |
| Semantic feedback elements | 99 |
| Retained corpus cases | 92 |
| Raw synthetic failure observations | 32 |
| Exact signatures | 1 |
| Manifest entries | 98 |
| Offline verification | passed |

Signature: `c16b0923faeafce6e8196416e81c29d76ea01fa379e562fa7c785b373f6cf1db`

The 32 raw failure observations are repeated executions of one exact signature, not 32 independent discoveries. The first-execution value is deterministic only for the recorded source, seed case, policy set, mutation set, campaign seed, and budget.

The record uses engine `1.4.0-sim`, source commit `55c31b25902157cddc9014bb9fdaa598bede40a2`, `GIT_TREE_SHA256_V1` source-tree digest `bc0d06a2759e2429b0fec5683f3102e4644628f30b3fe6032a21a233d1333338`, versioned scheduler policy `MAX_4096_OR_BUDGET_X_OPERATOR_COUNT_X_32_V1`, current definition-digest semantics, and `SOURCE_COMMIT_TIME`.

## Preserved evidence

- `summary.json`: controlled parameters, counts, claims limits, findings, and provenance.
- `coverage.json`: sorted synthetic semantic-feedback elements.
- `metrics.csv`: every execution, parent, operator, novelty, admission decision, result, and signature.
- `corpus/*.case.json`: retained canonical inputs with mutation lineage.
- `findings/<signature>/`: first trigger and finding metadata.
- `report/index.html`: static report with the mandatory simulation banner.
- `manifest.sha256`: exact-byte integrity for every other file.

The verifier checks both the manifest and semantic relationships among the summary, metrics, coverage, corpus, and findings. A separately generated rerun with the same source and parameters must produce a byte-identical manifest before the record is accepted.
