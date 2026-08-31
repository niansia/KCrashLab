# E1 simulated mutation-strategy experiment

Date: 2026-08-31

## Question

Within the same valid Case IR mutation envelope, does semantic-novelty scheduling reach the known `kcl.state` synthetic signature more consistently than uniform-random parent/operator/candidate selection under a fixed execution budget?

This is a narrower and fairer v1 adaptation of the blueprint's grammar-aware-versus-random experiment. Both strategies use the same boundary scalar, payload block, and sequence insert/delete/swap operators. The comparison isolates scheduling feedback; it does not compare grammar-aware inputs with arbitrary raw bytes.

## Fixed design

- Safe seed: `samples/cases/state-safe-seed.case.json`.
- Target: deterministic user-mode `kcl.state` model.
- Strategies: `NOVELTY_GUIDED_DETERMINISTIC_V1` and `UNIFORM_RANDOM_VALID_MUTATION_V1`.
- Budget: 256 executions per strategy and trial.
- Trials: 20 paired campaign seeds from 20260831 through 20260850.
- Outcome: first exact matching signature; no finding at execution 256 is right-censored.
- Quantiles: linear interpolation, calculated among successful trials only.
- Integrity: summary, raw CSV, and static report are covered by one SHA-256 manifest and an offline semantic verifier.

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

| Strategy | Discoveries | Censored | Discovery rate | First finding median [Q1, Q3]* |
|---|---:|---:|---:|---:|
| Novelty-guided deterministic | 20/20 | 0 | 100% | 70 [59.5, 71] |
| Uniform-random valid mutation | 5/20 | 15 | 25% | 40 [26, 54] |

*The first-finding statistic includes successful trials only. The random baseline's lower median must not be read as better typical time-to-finding because 75% of its trials are censored and absent from that median. Discovery/censoring must be considered first.

The observed fixture supports the engineering expectation that retaining semantic novelty makes discovery more reliable on this synthetic state machine. It does not establish statistical significance, external validity, real-driver performance, kernel coverage effectiveness, or a general advantage across targets. Those claims remain explicitly `NOT_ASSESSED` or `NOT_CLAIMED` in the artifact.

The canonical report now includes `1 − Kaplan–Meier survival` as a step curve. All 15 random no-finding trials remain in the risk set until the execution-256 censoring boundary. The paired descriptive table records 5 both-discovered, 15 novelty-only, 0 random-only, and 0 neither outcomes. No p-value is reported because this deterministic seed suite is not claimed to be a random sample from a wider target population.

## Reproducibility correction made during the study

An initial dry run revealed that the novelty engine recorded campaign seeds without using them to change scheduling, so its nominal trials repeated one path. That result was rejected. The final engine uses an explicit SplitMix64 schedule to rotate operator and candidate order; same-seed execution logs remain identical, while different seeds are contract-tested to produce different orders.

The artifact's `raw.csv` preserves all 40 trial outcomes, including every censored trial; `survival.csv` is deterministically derived and cross-checked by the verifier. Re-running the exact command must produce a byte-identical manifest before the record is accepted.
