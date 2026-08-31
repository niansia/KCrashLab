# E1 simulated mutation-strategy experiment

Date: 2026-08-31

## Question

Within the same valid Case IR mutation envelope, what are the separate descriptive effects of corpus-admission feedback and parent scheduling on discovery of the known `kcl.state` synthetic signature under a fixed execution budget?

E1 v2 is a 2×2 policy ablation. It crosses keep-all versus novelty-only corpus admission with uniform versus energy-ranked parent selection. Operator selection and candidate selection are uniform in every arm; all arms use the same boundary scalar, payload block, and sequence insert/delete/swap operators. This avoids attributing the effect of several simultaneously changed scheduling decisions to semantic novelty alone.

## Fixed design

- Safe seed: `samples/cases/state-safe-seed.case.json`.
- Target: deterministic user-mode `kcl.state` model.
- Strategies: `KEEP_ALL_UNIFORM_V2`, `KEEP_ALL_ENERGY_RANKED_V2`, `NOVELTY_ONLY_UNIFORM_V2`, and `NOVELTY_ONLY_ENERGY_RANKED_V2`.
- Budget: 256 executions per arm and trial.
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
  --recorded-at SOURCE_COMMIT_TIME `
  --output results/recorded/e1

dotnet run --project src/KCrashLab.Cli --configuration Release -- experiment verify results/recorded/e1
```

## Recorded descriptive result

The checked-in `results/recorded/e1` directory is a legacy v1 two-engine record and is retained only for provenance. It does **not** answer the v2 ablation question and must not be cited as evidence that semantic novelty alone caused its 20/20 versus 5/20 result. A canonical v2 table will be added only after all four arms are run from a committed source tree and the generated bundle passes `experiment verify`.

The v2 report includes `1 − Kaplan–Meier survival` for every arm and paired contrasts for admission at each parent policy and parent policy at each admission policy. No p-value is reported because the deterministic seed suite is not claimed to be a random sample from a wider target population. The experiment does not establish external validity, real-driver performance, kernel coverage effectiveness, or a general advantage across targets.

## Reproducibility correction made during the study

An initial dry run revealed that the novelty engine recorded campaign seeds without using them to change scheduling, so its nominal trials repeated one path. That result was rejected. Parent, operator, and candidate policy decisions now use independent SHA-256-derived decision lanes keyed by campaign seed and scheduling iteration. Mutation candidate caps use a separate seed-keyed `HASH_RANKED_V1` ranking over the complete valid candidate set. Same-seed execution logs remain identical, while different seeds are contract-tested to produce different orders.

The v2 artifact's `raw.csv` preserves all 80 trial outcomes, including every censored trial; `survival.csv` and `contrasts.csv` are deterministically derived and cross-checked by the verifier. Re-running the exact command must produce a byte-identical manifest before the record is accepted.
