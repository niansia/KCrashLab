# E2 stateful-sequence experiment

Date: 2026-08-31

## Question

Can the stateful Case IR express and discover a cross-operation synthetic failure that a single-call input model cannot express?

This is an expressiveness/sanity validation. Because the target's ground truth requires a three-operation prerequisite chain, the single-call arm cannot represent the trigger; the result must not be presented as a general empirical performance advantage for stateful fuzzing.

## Controlled design

Both modes use the same one-operation safe seed, semantic feedback, novelty corpus, mutation operators, campaign seeds, and execution budget. The only independent variable is maximum sequence length:

- `SINGLE_CALL`: at most 1 operation.
- `STATEFUL`: at most 6 operations.

The known synthetic signature requires `RESET_STATE → SET_MODE(2) → SUBMIT_RECORD(declared_len > payload length)`. This ground truth is used only for offline evaluation; it is not passed to the scheduler.

```powershell
dotnet run --project src/KCrashLab.Cli --configuration Release -- experiment e2 `
  --seed samples/cases/state-reset-seed.case.json `
  --budget 512 `
  --trials 20 `
  --base-seed 20260831 `
  --recorded-at SOURCE_COMMIT_TIME `
  --output results/recorded/e2

dotnet run --project src/KCrashLab.Cli --configuration Release -- experiment verify results/recorded/e2
```

## Recorded descriptive result

| Mode | Maximum operations | Discoveries | Censored | Successful first finding median [Q1, Q3] |
|---|---:|---:|---:|---:|
| Single call | 1 | 0/20 | 20 | n/a |
| Stateful | 6 | 20/20 | 0 | 149 [146, 151.25] |

Paired outcomes: 20 stateful-only, 0 single-call-only, 0 both, and 0 neither.

The result demonstrates expressiveness against this specific synthetic prerequisite chain. It does not establish real-driver effectiveness, statistical significance, or kernel crash discovery. Single-call runs may stop below the nominal budget when their finite valid search space is exhausted; the raw artifact preserves actual execution counts rather than pretending idle scheduler polls are executions.
