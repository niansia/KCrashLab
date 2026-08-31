# Public evidence release checklist

Use this checklist before publishing a recorded result, reviewer package, screenshot, demo, or release. Kernel dumps and debugger output may contain credentials, paths, usernames, memory contents, and other personal or secret data.

## Provenance and reproducibility

- [ ] The source tree is committed and the bundle names that commit.
- [ ] The working tree was clean when the experiment started.
- [ ] The experiment definition, seeds, budgets, versions, and timestamps are recorded.
- [ ] Censored trials, negative results, parser failures, and infrastructure errors are retained.
- [ ] The documented command reproduces the artifact or explains unavoidable nondeterminism.
- [ ] Offline manifest verification succeeds from a fresh extraction.
- [ ] The corresponding entry in the research claims ledger is accurate.

## Claim hygiene

- [ ] Simulation and Windows-lab evidence are visibly separated.
- [ ] The report distinguishes a crash, a confirmed exact-signature replay, a root cause, and exploitability.
- [ ] Conclusions do not exceed the target population or experimental design.
- [ ] Legacy or exploratory results are labeled and not mixed into confirmatory results.
- [ ] Protocol deviations and conflicts of interest are disclosed.

## Sensitive-data review

- [ ] No dump is published by default.
- [ ] Raw dumps and debugger logs are scanned for secrets, tokens, usernames, hostnames, paths, network identifiers, and unrelated memory content.
- [ ] VM names, checkpoint names, device identifiers, and internal network details are generalized where they are not required for reproduction.
- [ ] Redaction is performed on a derived copy; the private raw blob hash remains in the evidence chain.
- [ ] The release lists every withheld or redacted artifact and why it was withheld.
- [ ] Screenshots and videos are reviewed frame by frame for transient secrets and notifications.

## Authorization and disclosure

- [ ] The target is repository-owned or written authorization is archived privately.
- [ ] The work complies with `SECURITY.md` and the lab safety policy.
- [ ] Any unexpected third-party issue has been removed from the public bundle and moved to coordinated disclosure.
- [ ] Publication does not include exploit code, weaponization guidance, or third-party proprietary material.

The checklist is a release control, not proof that an artifact is harmless. When uncertain, withhold the artifact and publish its digest, metadata, and a sanitized derived report instead.
