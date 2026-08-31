# Lab safety policy

Track A is safe to run on an ordinary workstation because it is user-mode simulation and requires no elevation.

Track B is blocked until all of the following are independently verified:

1. Matching Windows SDK and WDK driver build targets.
2. A disposable Windows Pro/Enterprise lab VM isolated from daily data and production networks.
3. Working Hyper-V management and an immutable checkpoint identity.
4. Tested dump-space, test-signing, Driver Verifier recovery, and kill-switch runbooks.

Failure of any gate must produce `BLOCKED_BY_ENVIRONMENT`. A real backend must not fall back to the host, another VM, or an arbitrary device interface.

