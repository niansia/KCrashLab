# Track B controlled-lab runbook

Track B source is intentionally unusable until a Windows operator replaces every placeholder in `samples/real-lab-profile.template.json`. Validate the resulting private profile before installing or dispatching anything:

```powershell
dotnet run --project src/KCrashLab.Cli -- lab validate-profile C:\KCrashLabPrivate\real-lab-profile.json
```

The validator requires canonical VM/checkpoint GUIDs, a non-placeholder driver SHA-256, the fixed repository driver/device names, a dedicated exchange root, immutable/disposable/isolated lab attestations, an authorization reference, and bounded watchdog timers. A failure returns exit code 2 and `BLOCKED_BY_PROFILE`.

## Safe build and checkpoint preparation

1. Build `KCrashLabTarget.sys` without `KclLabFaults`; record its SHA-256 and confirm safe ECHO and malformed-length rejection.
2. Build the lab variant separately with `/p:KclLabFaults=true`; never reuse the same output directory or hash allowlist entry.
3. Install only in the isolated VM, enable complete or kernel dump policy, verify dump free space, configure Driver Verifier for `KCrashLabTarget.sys`, and enable the Hyper-V Guest Service Interface used by `Copy-VMFile`.
4. Place the guest agent and lab driver in the VM, then power down and create the immutable clean checkpoint.
5. Record the VM ID, checkpoint ID, guest build string (`OsName|OsVersion|WindowsBuildLabEx`), lab driver hash, and private authorization reference in the profile.

## G1 automated acceptance sequence

`scripts/track-b-g1.ps1` is the fail-closed host control plane. It requires PowerShell 7, Hyper-V PowerShell, PowerShell Direct credentials, a WDK-built lab driver, a private profile, a command-line WinDbg (`cdb.exe` is recommended), an offline symbol directory, and a clean Git commit. It records a deterministic symbol-tree digest, refuses non-private Hyper-V switches, and restores the pinned checkpoint in `finally`.

```powershell
$credential = Get-Credential
./scripts/track-b-g1.ps1 `
  -ProfilePath C:\KCrashLabPrivate\real-lab-profile.json `
  -CasePath samples\cases\kmdf-state-crash.case.json `
  -MinimizedCasePath samples\cases\kmdf-state-crash.case.json `
  -OutputPath D:\KCrashLabExchange\g1 `
  -WinDbgPath 'C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe' `
  -SymbolPath D:\KCrashLabPrivate\symbols `
  -GuestCredential $credential `
  -Confirm:$false
```

The checked-in trigger is already the minimal three-operation prerequisite chain, so the original and minimized paths are identical for G1. A future discovery campaign may supply distinct paths without changing the evidence contract.

The command performs one discovery run with the original case, then three cold replay runs with the minimized case:

1. Restore the pinned checkpoint and verify the observed VM/checkpoint identity again.
2. Start the guest agent with the exact case, driver path/hash, and write-through journal path.
3. Confirm the final durable journal record identifies the operation dispatched before the guest stopped responding.
4. Wait until `MEMORY.DMP` has a positive unchanged length for the configured stability interval and can be opened exclusively.
5. Hash the dump before copying it, retain raw WinDbg batch output, and restore the clean checkpoint.
6. Require the same exact signature in all three attempts, not merely three bugchecks.
7. Restore the clean checkpoint even if acquisition or parsing fails.
8. Generate `private-acquisition.json`, then build and immediately verify a sanitized `public-evidence` bundle without the dump.

The script writes raw dumps, journals, and unreviewed debugger output below the private output root. Do not place that root inside the repository. Only `public-evidence` is eligible for publication after completing the publication checklist.

G1 changes from `NOT ASSESSED` to `OBSERVED` only after this command completes on the pinned lab and its public bundle passes `lab evidence verify`. The presence of the script or a successful WDK build is not real-kernel evidence.

```powershell
dotnet run --project src/KCrashLab.Cli --configuration Release -- `
  lab evidence verify D:\KCrashLabExchange\g1\public-evidence
```
