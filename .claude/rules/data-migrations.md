---
paths:
  - "src/Lumen.Data/**"
---

# Data and migrations

- `MigrationRunner` refuses a database newer than the code (the DB is at version 14; check `src/Lumen.Data/Migrations` for the current one).
- A branch with a migration breaks every other branch on the owner's PC once he runs it: **merge the migration PR first** or rebase the others on it. Say so in the PR.
- Logic stays testable and OS-agnostic (`Lumen.Data` references only `Lumen.Core`).
- Secrets: DPAPI `CurrentUser` through the secret store; never logged or synced (security rule 1).
