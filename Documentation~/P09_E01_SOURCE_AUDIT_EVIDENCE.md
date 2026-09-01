# P09-E01 — Executable source audit and regression policy

Status: **PASS** on the migration branch. This gate prevents the package from silently
reintroducing the architecture that P02–P08 removed. It does not publish a package.

## Commands

From the repository root:

```bash
python3 tools/navigation-source-audit.py
python3 tools/navigation-source-audit.py --jitter-root Assets
python3 tools/test-navigation-source-audit.py
```

`tools/publish-package.sh` runs the source-only form before creating its subtree.
The consumer inventory form is intentionally separate: canonical Jitter is installed
under the consumer `Assets` tree and remains absent from the Custom Navigation package.

## Scope and exclusions

The executable scope is `Runtime`, `Server~`, `Samples~`, and `Tests`. The only excluded
trees are generated build output (`Server~/bin`, `Server~/obj`, corresponding test
`bin`/`obj`) and the `Server~/lib` vendor boundary. Linked Runtime files in every server
`.csproj` are resolved and counted, so the generated server projection cannot escape the
audited package or reference a missing shared file.

| Rule | Enforced invariant |
|---|---|
| `CN001` | Legacy coordinate DTO names cannot return. |
| `CN002` | Unity coordinate dependencies cannot enter Runtime/server core. |
| `CN003` | `Mathf`, `MathF`, platform math, and scalar finite checks require a classified non-simulation boundary; simulation uses `StableMath`. |
| `CN004` | Coordinate/route simulation state in Runtime/server cannot be declared as direct `float`/`double`. |
| `CN005` | Exactly one `NavigationPathFingerprint` implementation exists. |
| `CN006` | No package-owned Jitter DLL exists; consumer mode requires exactly one canonical DLL with the approved SHA-256. |
| `CN007` | Component-wise `JVector`/`RcVec3f` conversion exists only in `NavigationDotRecastAdapter`. |
| `CN008` | Server linked-source projection stays inside the package and points to existing Runtime files. |

Every allowlist item in `tools/navigation-source-audit-rules.json` contains a category,
owner, reason, path selector, and line pattern. An entry that matches nothing fails the
audit, preventing dead or accidentally broadened policy. The only legacy waiver is the
existing server request-boundary `float.IsFinite`; it is explicitly owned and scheduled
for replacement by `NavigationJitterValidation` in the next compatibility cleanup.

## Fixture contract

`tools/source-audit-fixtures.json` contains one clean fixture and one isolated negative
fixture for every rule. The fixture runner requires the diagnostic to contain the exact
rule id, relative path, and line. A generic nonzero process exit is not accepted as proof.

## Recorded P09 evidence

```text
P09_SOURCE_AUDIT_OK files=62 projections=23 allowlisted=51 jitter=source-only
P09_SOURCE_AUDIT_OK files=62 projections=23 allowlisted=51 jitter=checked
P09_SOURCE_AUDIT_FIXTURES_OK positive=1 negatives=8
```

The checked consumer Jitter identity is SHA-256
`944666bbe73dfce5ffc5bfb18569fb0004f50e767dcbb8b471dde15242023ca6`.
