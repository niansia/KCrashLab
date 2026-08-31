# ADR-0001: Simulation-first control plane

- Status: Accepted
- Date: 2026-08-31

## Context

The current host is Windows 11 Home. A hypervisor is present, but the supported Hyper-V management module and a disposable kernel-test VM are unavailable. Windows SDK directories exist, but WDK driver build targets are not verified.

## Decision

KCrashLab v1 implements the control plane against a deterministic `SimulatedLabBackend`. All output is labeled `SIMULATED`. `HyperVLabBackend` remains a fail-closed contract skeleton. No driver is built, installed, or loaded on the host.

## Consequences

The project can validate event sourcing, idempotency, timeout classification, evidence integrity, signatures, replay, and minimization now. It cannot claim real BSOD recovery, kernel coverage, Driver Verifier results, or a Windows kernel finding.

