# M1 simulated minimization and replay record

Date: 2026-09-01

M1 closes the evidence boundary between fuzz discovery and post-discovery reduction. It starts from the checked-in 14-operation `state-original` synthetic trigger, minimizes against the exact signature, and applies the deterministic 3/3 simulated replay policy.

## Reproduction

```powershell
git checkout --detach 55c31b25902157cddc9014bb9fdaa598bede40a2

dotnet run --project src/KCrashLab.Cli --configuration Release -- campaign run `
  --scenario dump-ready `
  --case samples/cases/state-original.case.json `
  --recorded-at SOURCE_COMMIT_TIME `
  --output artifacts/evidence-freeze-rerun/m1

dotnet run --project src/KCrashLab.Cli --configuration Release -- `
  evidence verify artifacts/evidence-freeze-rerun/m1/finding
```

Exact evidence reproduction requires the clean source commit above. Compare the resulting manifest bytes with `results/recorded/minimization-replay/manifest.sha256` from the evidence/release revision; executing from that child revision correctly produces different Git provenance.

## Recorded result

| Metric | Value |
|---|---:|
| Original operations | 14 |
| Minimized operations | 3 |
| Original canonical bytes | 552 |
| Minimized canonical bytes | 188 |
| Minimization oracle attempts | 53 |
| Replay vote | 3/3 matching |
| Manifest entries | 14 |
| Offline verification | passed |

The exact signature is `c16b0923faeafce6e8196416e81c29d76ea01fa379e562fa7c785b373f6cf1db`. The recorded bundle was produced from source commit `55c31b25902157cddc9014bb9fdaa598bede40a2` with `GIT_TREE_SHA256_V1` source-tree digest `bc0d06a2759e2429b0fec5683f3102e4644628f30b3fe6032a21a233d1333338` and engine `1.4.0-sim`.

`decision.json` schema v2 carries machine-readable source and experiment provenance, including the scenario-fixture digest, experiment-definition digest, Case IR version, campaign seed, maximum minimization attempts, minimizer-definition digest, and replay-policy-definition digest. `environment.json` schema v2 records only the versioned simulator backend, virtual epoch, and scenario-fixture identity; host OS, Hyper-V, SDK, and WDK state cannot change the canonical manifest. The verifier recomputes the definition digests and cross-checks the environment against the provenance. This is deterministic simulator evidence, not a kernel crash, clean-checkpoint VM replay, root-cause analysis, or exploitability evidence.
