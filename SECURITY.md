# Security policy

KCrashLab v1 is a user-mode simulation project. Do not use it to probe third-party drivers or devices, and do not load deliberately vulnerable drivers on a daily-use host.

## Supported scope

- Synthetic user-mode targets supplied by this repository.
- Scripted simulator fixtures and canned WinDbg-like text.
- Offline evidence verification.
- Contract-only future backend interfaces that fail closed.

## Prohibited scope

- Commercial or third-party driver testing without written authorization.
- Exploit generation, payload development, privilege escalation, persistence, or evasion.
- Host-kernel fuzzing or enabling a real backend without a disposable, recoverable, isolated lab.
- Publishing memory dumps by default; dumps can contain secrets and personal data.

If an unexpected third-party issue is encountered, stop testing, preserve only the minimum necessary evidence privately, and follow coordinated disclosure.

