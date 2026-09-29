# Persistence policies

- `CodeGuardDbContext` is registered with scoped lifetime and uses the validated `DatabaseOptions.ConnectionString` value.
- `Project -> ReviewRun` and `ReviewRun -> Finding/TestCase/AIModelRun/ToolExecution` use database cascade delete for referential cleanup.
- Cascade configuration is not authorization to delete a project. No project-delete use case is exposed in this phase; any future deletion must be authorized and coordinated by an Application service before calling the DbContext.
- Entity mappings are explicit and live in `Configurations`. The Domain and Application projects do not depend on EF Core or Npgsql.
- Sensitive-data logging is intentionally not enabled. Configuration failures identify only the missing key and never include its value.
- Migrations, database creation, and connectivity checks are outside this phase.
