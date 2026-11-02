---
paths:
  - "src/Lumen.Providers/**"
---

# Providers

- A provider never throws out of a scan: it returns partial results plus diagnostics (security rule 7).
- Read-only toward stores (security rule 2). Store facts in ARCHITECTURE §4 are starting points: unverified ones get fixture tests, a `// VERIFY:` comment and a PR note. GOG Galaxy, Xbox (GDK), EA, Ubisoft and Battle.net were verified by the owner on 2026-11-02.
- Playnite (`JosefNemec/PlayniteExtensions`) is a good, safe reference for store behavior.
- Network hosts are limited to the list in security rule 4; a new host needs the owner's approval first.
