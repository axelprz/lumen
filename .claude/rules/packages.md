---
paths:
  - "**/*.csproj"
  - "Directory.Packages.props"
  - "Directory.Build.props"
  - "THIRD-PARTY-NOTICES.txt"
  - ".github/workflows/release.yml"
---

# Packages

- Pinned in `Directory.Packages.props`; versions at least ~2 weeks old; NuGet audit as errors.
- A dependency not in ARCHITECTURE §2 needs the owner's OK first.
- **Every new package must be added to `THIRD-PARTY-NOTICES.txt` and `AboutViewModel`** (a repo test fails otherwise; read license and copyright from the package's `.nuspec`).
- The `vpk` version in `release.yml` must match Velopack (1.2.161).
