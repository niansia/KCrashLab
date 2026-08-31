# Research claims and evidence ledger

This ledger is the reviewer-facing boundary between implemented mechanisms, observations, and future hypotheses. A claim may move to a stronger state only when its acceptance evidence is checked in or linked from a versioned release. Architecture diagrams and planned interfaces are not evidence.

## Claim states

- **VERIFIED**: repeatable from the repository and covered by an automated verifier or test.
- **OBSERVED**: recorded evidence exists, but external validity or independent replication is not established.
- **LEGACY**: retained for provenance but superseded by a changed experimental design.
- **NOT ASSESSED**: no qualifying evidence has been collected.
- **OUT OF SCOPE**: intentionally excluded from the project.

## Current ledger

| ID | Claim | State | Acceptance evidence | Important limitation |
|---|---|---|---|---|
| C-01 | Case IR has deterministic canonical identities. | VERIFIED | Canonicalization tests and versioned Case IR schema. | User-mode contracts only. |
| C-02 | Campaign recovery avoids a second dispatch after an ambiguous resume point. | VERIFIED | SQLite resume and fault-injection tests. | Does not yet cover every evidence-production state. |
| C-03 | Evidence manifests detect modified, missing, and untracked files. | VERIFIED | Evidence and fuzz-artifact verifier tests. | Integrity is not authenticity; manifests are not signed. |
| C-04 | The synthetic finding can be minimized while preserving an exact signature and 3/3 replay vote. | OBSERVED | `results/recorded/g3` and its SHA-256 manifest. | One deterministic synthetic target; no kernel inference. |
| C-05 | Corpus admission and parent scheduling have separable effects in E1. | NOT ASSESSED | Requires a committed, verified four-arm E1 v2 bundle. | Existing E1 v1 evidence changed several factors and is LEGACY. |
| C-06 | Stateful Case IR can represent a synthetic trigger that a one-operation cap cannot. | OBSERVED | `results/recorded/e2` and the E2 protocol. | Expressiveness sanity check, not a general performance comparison. |
| C-07 | KCrashLab can recover a Windows VM after a real driver-induced crash and collect a stable dump. | NOT ASSESSED | Requires a successful `track-b-g1.ps1` run and verified sanitized bundle. | Automation source exists, but no real-lab evidence is checked in. |
| C-08 | A real finding reproduces from an immutable clean checkpoint with the same signature. | NOT ASSESSED | Three cold replays, checkpoint identity, raw debugger output, and verified bundle. | A repeated bugcheck alone is insufficient. |
| C-09 | Any finding is exploitable or security-impacting. | NOT ASSESSED | Requires a separately governed investigation. | Crashability is not exploitability. |
| C-10 | KCrashLab is suitable for third-party driver testing. | OUT OF SCOPE | None. | Repository-owned synthetic targets only. |

## Rules for changing the ledger

1. State the claim before running the experiment and define a falsifiable acceptance rule.
2. Preserve negative, censored, and infrastructure-error outcomes; do not silently drop failed trials.
3. Pin the source commit, experiment-definition digest, environment, seed schedule, Case IR version, and engine version.
4. Keep raw acquisition output immutable. Derived parsers may be replaced, but their output must link to the raw blob hash.
5. Separate target failures from lab failures. An unavailable heartbeat, missing dump, parser failure, or checkpoint mismatch cannot be counted as a confirmed target finding.
6. Report effect sizes and denominators before interpreting successful trials. Do not infer real-driver behavior from synthetic targets.
7. Record deviations from the preregistered protocol in the final report. If a design changes materially, assign a new experiment version instead of overwriting the old claim.

## Evidence required for the first real-lab claim

The first transition of C-07 or C-08 away from `NOT ASSESSED` requires all of the following in one sanitized release bundle:

- repository-owned target driver source and exact binary SHA-256;
- safe/vulnerable build-mode declaration and disposable-lab attestation;
- VM identity, immutable checkpoint identity, guest build, Driver Verifier settings, and dump policy;
- original Case IR, durable `case_id`/`attempt_id` dispatch journal, and raw watchdog observations;
- original dump hash and raw WinDbg batch output retained privately or published only after redaction review;
- parser version, exact signature, minimized Case IR, and three cold-replay outcomes;
- evidence manifest verification output and an explicit list of withheld sensitive artifacts.

Passing this checklist demonstrates a reproducible research workflow. It does not by itself establish root cause, exploitability, or impact.
