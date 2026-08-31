# E2 stateful-sequence experiment

Date: 2026-09-01

## Question

Can the stateful Case IR express and discover a cross-operation synthetic failure that a single-call input model cannot express?

This is an expressiveness/sanity validation. Because the target's ground truth requires a three-operation prerequisite chain, the single-call arm cannot represent the trigger; the result must not be presented as a general empirical performance advantage for stateful fuzzing.

## Controlled design

Both modes use the same one-operation safe seed, semantic feedback, novelty corpus, mutation operators, campaign seeds, and execution budget. The only independent variable is maximum sequence length:

- `SINGLE_CALL`: at most 1 operation.
- `STATEFUL`: at most 6 operations.

The known synthetic signature requires `RESET_STATE → SET_MODE(2) → SUBMIT_RECORD(declared_len > payload length)`. This ground truth is used only for offline evaluation; it is not passed to the scheduler.

```powershell
git checkout --detach 55c31b25902157cddc9014bb9fdaa598bede40a2

dotnet run --project src/KCrashLab.Cli --configuration Release -- experiment e2 `
  --seed samples/cases/state-reset-seed.case.json `
  --budget 512 `
  --trials 20 `
  --base-seed 20260831 `
  --recorded-at SOURCE_COMMIT_TIME `
  --output artifacts/evidence-freeze-rerun/e2

dotnet run --project src/KCrashLab.Cli --configuration Release -- experiment verify artifacts/evidence-freeze-rerun/e2
```

Exact evidence reproduction requires the clean source commit above. Compare the resulting manifest bytes with `results/recorded/e2/manifest.sha256` from the evidence/release revision; executing from that child revision correctly produces different Git provenance.

## Recorded descriptive result

| Mode | Maximum operations | Discoveries | Censored | Successful first finding median [Q1, Q3] |
|---|---:|---:|---:|---:|
| Single call | 1 | 0/20 | 20 | n/a |
| Stateful | 6 | 12/20 | 8 | 229.5 [139.25, 302.25] |

Paired outcomes: 12 stateful-only, 0 single-call-only, 0 both, and 8 neither.

The checked-in record uses engine `1.4.0-sim`, source commit `55c31b25902157cddc9014bb9fdaa598bede40a2`, `GIT_TREE_SHA256_V1` source-tree digest `bc0d06a2759e2429b0fec5683f3102e4644628f30b3fe6032a21a233d1333338`, current definition-digest semantics, and `SOURCE_COMMIT_TIME`. It passes the offline semantic verifier.

The result demonstrates expressiveness against this specific synthetic prerequisite chain. It does not establish real-driver effectiveness, statistical significance, or kernel crash discovery. Single-call runs may stop below the nominal budget after reaching the scheduler-iteration limit with repeated empty or duplicate selections; this does not prove global search-space exhaustion. The raw artifact preserves actual execution counts rather than treating idle scheduler polls as executions.
