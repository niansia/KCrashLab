# E1 simulated mutation-strategy experiment

Date: 2026-09-01

## Question

Within the same valid Case IR mutation envelope, what are the separate descriptive effects of corpus-admission feedback and parent scheduling on discovery of the known `kcl.state` synthetic signature under a fixed execution budget?

E1 v2 is a 2×2 policy ablation. It crosses keep-all versus novelty-only corpus admission with uniform versus energy-ranked parent selection. Operator selection and candidate selection are uniform in every arm; all arms use the same boundary scalar, payload block, and sequence insert/delete/swap operators. This avoids attributing the effect of several simultaneously changed scheduling decisions to semantic novelty alone.

## Fixed design

- Safe seed: `samples/cases/state-safe-seed.case.json`.
- Target: deterministic user-mode `kcl.state` model.
- Strategies: `KEEP_ALL_UNIFORM_V2`, `KEEP_ALL_ENERGY_RANKED_V2`, `NOVELTY_ONLY_UNIFORM_V2`, and `NOVELTY_ONLY_ENERGY_RANKED_V2`.
- Budget: 256 executions per arm and trial.
- Trials: 20 paired campaign seeds from 20260831 through 20260850.
- Outcome: first exact matching signature; a no-finding trial is right-censored at its actual completed execution count (256 for every recorded trial).
- Quantiles: linear interpolation, calculated among successful trials only.
- Integrity: summary, raw CSV, and static report are covered by one SHA-256 manifest and an offline semantic verifier.

```powershell
git checkout --detach 55c31b25902157cddc9014bb9fdaa598bede40a2

dotnet run --project src/KCrashLab.Cli --configuration Release -- experiment e1 `
  --seed samples/cases/state-safe-seed.case.json `
  --budget 256 `
  --trials 20 `
  --base-seed 20260831 `
  --recorded-at SOURCE_COMMIT_TIME `
  --output artifacts/evidence-freeze-rerun/e1

dotnet run --project src/KCrashLab.Cli --configuration Release -- experiment verify artifacts/evidence-freeze-rerun/e1
```

Exact evidence reproduction requires the clean source commit above. Compare the resulting manifest bytes with `results/recorded/e1/manifest.sha256` from the evidence/release revision; executing from that child revision correctly produces different Git provenance.

## Recorded descriptive result

| Strategy | Discoveries | Censored | Successful first finding median [Q1, Q3] |
|---|---:|---:|---:|
| `KEEP_ALL_ENERGY_RANKED_V2` | 14/20 | 6 | 99 [68, 127.25] |
| `KEEP_ALL_UNIFORM_V2` | 7/20 | 13 | 41 [17, 162.5] |
| `NOVELTY_ONLY_ENERGY_RANKED_V2` | 18/20 | 2 | 83 [56.5, 121.25] |
| `NOVELTY_ONLY_UNIFORM_V2` | 12/20 | 8 | 130.5 [104.5, 170.25] |

The checked-in v2 bundle uses engine `1.4.0-sim`, source commit `55c31b25902157cddc9014bb9fdaa598bede40a2`, `GIT_TREE_SHA256_V1` source-tree digest `bc0d06a2759e2429b0fec5683f3102e4644628f30b3fe6032a21a233d1333338`, current policy IDs and definition-digest semantics, and `SOURCE_COMMIT_TIME`. It passes the offline semantic verifier.

The v2 report includes `1 − Kaplan–Meier survival` for every arm and paired contrasts for admission at each parent policy and parent policy at each admission policy. No p-value is reported because the deterministic seed suite is not claimed to be a random sample from a wider target population. The experiment does not establish external validity, real-driver performance, kernel coverage effectiveness, or a general advantage across targets.

## Reproducibility correction made during the study

An initial dry run revealed that the novelty engine recorded campaign seeds without using them to change scheduling, so its nominal trials repeated one path. That result was rejected. Parent, operator, and candidate policy decisions now use independent SHA-256-derived decision lanes keyed by campaign seed and scheduling iteration. Mutation candidate caps use a separate seed-keyed `HASH_RANKED_V1` ranking over the complete valid candidate set. Same-seed execution logs remain identical, while different seeds are contract-tested to produce different orders.

The v2 artifact's `raw.csv` preserves all 80 trial outcomes, including every censored trial. `raw.csv`, `survival.csv`, and the `factorial_contrasts` section in `summary.json` are deterministically derived and cross-checked by the verifier. Re-running the exact command from the pinned source commit must produce a byte-identical manifest before the record is accepted.
