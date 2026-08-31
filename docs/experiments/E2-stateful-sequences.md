# E2 stateful expressiveness validation

Date: 2026-08-31

Experiment contract: `E2_STATEFUL_VS_SINGLE_CALL_V1`

## Question

Can the stateful Case IR express and reach a cross-operation synthetic failure that a single-call representation cannot encode?

This is an expressiveness and end-to-end sanity validation. It is not the primary empirical scheduler benchmark because the ground truth structurally requires three operations.

## Controlled design

Both modes use the same one-operation safe seed, synthetic semantic feedback, fuzz engine, policies, mutation operators, campaign seeds, and execution budget. The only changed parameter is maximum sequence length:

- `SINGLE_CALL`: at most 1 operation.
- `STATEFUL`: at most 6 operations.

The known signature requires `RESET_STATE → SET_MODE(2) → SUBMIT_RECORD(declared_len > payload length)`. The signature is used for offline outcome evaluation and is not passed to the scheduler.

## Reproduction

```powershell
dotnet run --project src/KCrashLab.Cli --configuration Release -- experiment e2 `
  --seed samples/cases/state-reset-seed.case.json `
  --budget 512 `
  --trials 20 `
  --base-seed 20260831 `
  --recorded-at 2026-08-31T00:00:00Z `
  --output results/recorded/e2

dotnet run --project src/KCrashLab.Cli --configuration Release -- experiment verify results/recorded/e2
```

## Recorded descriptive result

| Mode | Maximum operations | Discoveries | Censored | Successful first finding median [Q1, Q3] |
|---|---:|---:|---:|---:|
| Single call | 1 | 0/20 | 20 | n/a |
| Stateful | 6 | 11/20 | 9 | 181 [100, 216.5] |

Paired outcomes: 11 stateful-only, 0 single-call-only, 0 both, and 9 neither.

The result verifies the intended representation boundary: the single-call mode cannot encode the prerequisite chain, while the stateful mode can and reaches it for some campaign seeds. It does not establish real-driver effectiveness, statistical significance, or kernel-crash discovery capability.

Runs may stop below the nominal budget when no unseen valid candidate remains. The artifact records actual executions rather than counting idle scheduler polls as target executions.
