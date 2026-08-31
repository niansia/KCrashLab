# Canonical case identity

Case IR v1 has two related hashes with different purposes.

## Semantic case ID

`case_id` is SHA-256 over canonical JSON after removing only the top-level `parent_case_id` and `mutation` properties. Object properties are ordered by ordinal name, array order is preserved, integers use their canonical JSON representation, and no insignificant whitespace is emitted.

Lineage is excluded because it describes how a case was reached, not what the target executes. Two mutation paths that produce the same target, seed, operations, and schedule therefore share one case ID and are not executed twice within a campaign.

## Stored-byte integrity

The saved corpus document keeps its `parent_case_id` and mutation operator parameters. The evidence manifest hashes those exact bytes separately. This means lineage tampering changes the manifest hash even though it does not change semantic target identity.

## Versioning rule

Changing either canonicalization or the fields included in identity requires a new Case IR major schema version. Readers must fail closed on unsupported major versions; old IDs must never be silently recomputed under a new rule.
