# Threat model

## Trusted

- The local controller process and policy configuration.
- The evidence root selected by the user after path validation.
- Repository-owned simulator fixtures after their hashes are recorded.

## Untrusted

- Case files, backend messages, artifact names and bytes, triage text, and manifests.
- Any future guest or driver output.

## Required defenses

- Strict input size, depth, operation-count, and string-length limits.
- Canonical serialization before computing a case ID.
- Relative-path normalization, traversal rejection, and reparse-point rejection.
- Atomic blob commit followed by hash verification.
- Versioned parsers that retain raw input and expose parse confidence.
- Exact signatures remain immutable; similarity can only suggest a merge.
- Missing kernel-lab capabilities block execution instead of selecting the host.

## Claims policy

A simulated failure is evidence about control-plane behavior, not evidence of a kernel defect or exploitability. Root cause and exploitability remain `NOT_CLAIMED` and `NOT_ASSESSED` unless a future, separately governed investigation establishes them.

