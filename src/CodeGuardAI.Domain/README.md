# Domain policies

- Entity identifiers are supplied by the caller and must be non-empty `Guid` values. The Domain does not choose an ID generator.
- Every persisted timestamp is a `DateTimeOffset`, uses a `Utc` suffix, and must have offset `+00:00`. The caller supplies time, keeping the Domain deterministic.
- Entity state is exposed through getters. Construction and state changes go through invariant-preserving methods; there are no public setters.
- `ReviewRun` can move from `Running` to exactly one terminal state: `Completed` or `Failed`. Cancellation is persisted as `Failed` with error code `Cancelled`.
- Invalid construction arguments fail immediately with argument exceptions. An invalid state transition returns `false` without changing the entity.
- The Domain project has no framework, persistence, Infrastructure, logging, or I/O dependency. Persistence mapping belongs outside this assembly.
