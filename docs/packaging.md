# Reviewer packaging

Do not compress the working directory. It can contain ignored build outputs, local databases, private dumps, and transient experiment runs even when `.gitignore` is correct.

After creating at least one commit and confirming the worktree is clean, produce separate source and recorded-results packages:

```powershell
./scripts/package.ps1 `
  -OutputPath artifacts/KCrashLab-source.zip `
  -RecordedResultsOutputPath artifacts/KCrashLab-recorded-results.zip
```

The script fails closed when `HEAD` is absent or the worktree is dirty. The source archive is created with `git archive`; `.gitattributes` excludes `results/`. It then inspects the ZIP and rejects build directories, `.git`, transient artifacts, dumps, and compiled native or managed binaries. Recorded research results are packaged separately from `results/recorded`.

Use `-Force` only to replace the two exact ZIP paths supplied to the script. The script never recursively deletes a directory.
