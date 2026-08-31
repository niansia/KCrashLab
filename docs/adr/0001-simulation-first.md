# ADR-0001: Simulation-first control plane

- Status: Accepted
- Date: 2026-08-31

## Context

The current host is Windows 11 Home. A hypervisor is present, but the supported Hyper-V management module and a disposable kernel-test VM are unavailable. Windows SDK directories exist, but WDK driver build targets are not verified.

## Decision

KCrashLab v1 implements ordinary campaigns against a deterministic `SimulatedLabBackend`. All simulation output is labeled `SIMULATED`, and `HyperVLabBackend` remains a fail-closed contract skeleton. Track B source may be built only through the explicit environment-protected workflow and may execute only through the destructive-lab G1 script after its private gates pass; neither path can fall back to the host.

## Consequences

The project can validate event sourcing, idempotency, timeout classification, evidence integrity, signatures, replay, minimization, real-profile policy, WinDbg parsing, and sanitized evidence semantics without a kernel lab. It cannot claim real BSOD recovery, Driver Verifier behavior, or a Windows kernel finding until the G1 script produces a sanitized bundle from the pinned environment and that bundle verifies offline.
