# E1 simulated 2×2 policy ablation

Date: 2026-08-31

Experiment contract: `E1_POLICY_ABLATION_2X2_V2`

## Question

Within one valid Case IR mutation envelope, how do corpus admission and parent selection interact when searching for the known `kcl.state` synthetic signature under a fixed execution budget?

This revision replaces the earlier two-engine comparison. The earlier design changed admission, parent selection, operator selection, candidate selection, and enumeration behavior together, so it could compare complete strategies but could not isolate novelty. Its result is superseded and is not cited as component-level evidence.

## Factorial design

Two factors are crossed:

| Factor | Level A | Level B |
|---|---|---|
| Corpus admission | keep every executed case | retain only semantic novelty or a signature |
| Parent selection | uniform | energy-ranked |

All four arms share:

- `PolicyDrivenFuzzEngine` and the same evaluator;
- the boundary scalar, payload block, and sequence insert/delete/swap operators;
- the same deterministic candidate-enumeration implementation and uniform operator/candidate selection policies;
- safe seed `samples/cases/state-safe-seed.case.json`;
- 256 executions per trial;
- 20 paired campaign seeds, 20260831 through 20260850;
- the first exact matching signature as the event;
- right censoring when no signature is found by the fixed budget.

Parent, operator, and candidate choices use independent SHA-256-derived decision lanes. A policy cannot change later operator/candidate choices merely by consuming a different number of PRNG values.

## Reproduction

```powershell
dotnet run --project src/KCrashLab.Cli --configuration Release -- experiment e1 `
  --seed samples/cases/state-safe-seed.case.json `
  --budget 256 `
  --trials 20 `
  --base-seed 20260831 `
  --recorded-at 2026-08-31T00:00:00Z `
  --output results/recorded/e1

dotnet run --project src/KCrashLab.Cli --configuration Release -- experiment verify results/recorded/e1
```

## Recorded descriptive result

| Admission | Parent | Discoveries | Censored | Discovery rate | Successful-trial first finding median [Q1, Q3]* |
|---|---|---:|---:|---:|---:|
| Keep all | Uniform | 5/20 | 15 | 25% | 121 [91, 210] |
| Keep all | Energy | 14/20 | 6 | 70% | 111 [81, 127.75] |
| Novelty only | Uniform | 16/20 | 4 | 80% | 129 [87.25, 184.75] |
| Novelty only | Energy | 13/20 | 7 | 65% | 125 [97, 173] |

*Quantiles use linear interpolation among successful trials only. They must not be compared without the discovery and censoring counts.

Planned paired contrasts:

| Contrast | Both | Left only | Right only | Neither |
|---|---:|---:|---:|---:|
| Novelty vs keep-all at uniform parent | 3 | 13 | 2 | 2 |
| Novelty vs keep-all at energy parent | 11 | 2 | 3 | 4 |
| Energy vs uniform at keep-all admission | 5 | 9 | 0 | 6 |
| Energy vs uniform at novelty admission | 11 | 2 | 5 | 2 |

## Interpretation boundary

The checked-in target shows an interaction, not one uniformly dominant component. Novelty-only admission increases discovery count under uniform parent selection (16 versus 5), but not under energy parent selection (13 versus 14). Energy selection increases discovery count under keep-all admission (14 versus 5), but not under novelty-only admission (13 versus 16).

This is valuable as a design diagnostic: it rejects the simplistic claim that either novelty admission or energy selection is independently superior on every configuration. It does not establish statistical significance, external validity, real-driver performance, or kernel-coverage effectiveness. Those claims remain `NOT_ASSESSED` or `NOT_CLAIMED` in the artifact.

`raw.csv` preserves all 80 trial outcomes. `survival.csv` is derived from the raw trials and checked by the verifier. The static report shows `1 − Kaplan–Meier survival`, retaining no-finding trials in the risk set through the censoring boundary.
