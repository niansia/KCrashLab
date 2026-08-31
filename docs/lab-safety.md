# Lab safety policy

Track A is safe to run on an ordinary workstation because it is user-mode simulation and requires no elevation.

Track B is blocked until all of the following are independently verified:

1. Matching Windows SDK and WDK driver build targets.
2. A disposable Windows Pro/Enterprise lab VM isolated from daily data and production networks.
3. Working Hyper-V management and an immutable checkpoint identity.
4. Tested dump-space, test-signing, Driver Verifier recovery, and kill-switch runbooks.

Failure of any gate must produce `BLOCKED_BY_ENVIRONMENT`. A real backend must not fall back to the host, another VM, or an arbitrary device interface.

## Track B execution controls

Before each real-lab run, the controller must capture the verified VM and checkpoint identities, target device-interface GUID, repository-owned driver hash, guest build, dump destination, and an operator-supplied lab authorization record. A mismatch after restore aborts the run before dispatch.

The host-side kill switch must remain available independently of the guest heartbeat. Loss of controller state, ambiguous VM identity, checkpoint mutation, evidence-root exhaustion, or repeated recovery failure stops the campaign; none of these conditions may be reclassified as a target crash.

Real-lab evidence is private by default. Publication requires the [public evidence release checklist](publication-checklist.md), and a kernel dump must never be placed in a public evidence bundle merely to make the bundle self-contained.
