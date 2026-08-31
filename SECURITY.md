# Security policy

KCrashLab defaults to user-mode simulation. Its Track B deterministic bugcheck oracle is not a general driver target and may run only in the repository owner's disposable, isolated VM. Do not use the project to probe third-party drivers or devices, and do not load the lab driver on a daily-use host.

## Supported scope

- Synthetic user-mode targets supplied by this repository.
- Scripted simulator fixtures and canned WinDbg-like text.
- Offline evidence verification.
- Repository-owned KMDF target through the gated Track B G1 command in an authorized disposable lab.
- Sanitized real-lab evidence verification without publishing raw kernel memory.

## Prohibited scope

- Commercial or third-party driver testing without written authorization.
- Exploit generation, payload development, privilege escalation, persistence, or evasion.
- Host-kernel fuzzing or enabling a real backend without a disposable, recoverable, isolated lab.
- Publishing memory dumps by default; dumps can contain secrets and personal data.

If an unexpected third-party issue is encountered, stop testing, preserve only the minimum necessary evidence privately, and follow coordinated disclosure.

## Reporting an issue

Security fixes are provided for the latest tagged release and the current `main` branch. Older snapshots are not supported.

Report a vulnerability in KCrashLab itself through [GitHub Private Vulnerability Reporting](../../security/advisories/new). Do not open a public issue containing a dump, proof of concept, device identifier, secret, personal data, or details of an unexpected third-party vulnerability. If Private Vulnerability Reporting is unavailable, publish no technical details; retain the minimum evidence needed to establish dates and integrity until a coordinated channel is agreed.

Public releases must follow the [evidence publication checklist](docs/publication-checklist.md). A digest or sanitized derived report is preferred over releasing raw kernel memory.
