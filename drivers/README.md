# Repository-owned KMDF target

`KCrashLab.Target` is the Track B v0.2 target used only inside a disposable, isolated Windows VM. It exposes four fixed buffered IOCTLs: echo, reset state, set mode, and submit record. The normal build validates all lengths and never intentionally crashes.

The opt-in `KclLabFaults=true` MSBuild property compiles a deterministic stateful bugcheck oracle. It exists to validate recovery, dump acquisition, triage, and replay—not to model exploitability. The fault is reachable only after `RESET_STATE → SET_MODE(2) → SUBMIT_RECORD(declared_len > payload length)`.

```powershell
# Safe default
msbuild drivers/KCrashLab.Target/KCrashLab.Target.vcxproj /p:Configuration=Release /p:Platform=x64

# Disposable VM laboratory variant only
msbuild drivers/KCrashLab.Target/KCrashLab.Target.vcxproj /p:Configuration=Release /p:Platform=x64 /p:KclLabFaults=true
```

Do not install either variant on a daily-use host. The device is exclusive and restricted to SYSTEM/Administrators, and it publishes the repository-owned interface GUID `{4fd15d37-1f06-4e50-a823-376ad418f196}` for inventory. The guest agent additionally requires an exact controller-supplied SHA-256 for `KCrashLabTarget.sys` before it opens the fixed `\\.\KCrashLabTarget` compatibility path.
