# Tooling, git, releases and the cloud sandbox

Read when you hit one of these topics; the rules that apply to every session are in CLAUDE.md.

## Git

- **GitHub SSH can fail intermittently** ("Permission denied (publickey)" or a 500). Retry once; otherwise push over HTTPS with gh's credentials for that command only: `git -c credential.helper= -c "credential.helper=!gh auth git-credential" push https://github.com/axelprz/lumen.git <ref>`.

## Releases (only when the owner asks)

Annotated tag on `main`'s merge commit, e.g. `git tag -a v0.2.0-beta.5 -m "…" <sha>` and `git push origin <tag>`. `release.yml` builds and publishes (≈3 min, 6 assets). A `-` in the tag makes it a pre-release; versions come from the tag (`0.0.0-dev` locally). Tag `v1.0.0` only on the owner's word; the default update channel flips to stable by itself at 1.0.

## Cloud sandbox (Linux)

- The WPF app **cannot run** there; it builds (`EnableWindowsTargeting`). Say so in the PR and give the owner the manual test.
- .NET SDK: `dotnet-install.sh --version 10.0.100 --install-dir ~/.dotnet` (needs `dot.net` and `builds.dotnet.microsoft.com`; set `DOTNET_ROOT` and `PATH`).
- Baseline: 26 failures in `Lumen.Core.Tests` there (`RoutineSafety/Runner/Review/Service` tests with `C:\` paths); they pass on Windows CI. Any other failure is yours.
- GitHub through the MCP tools (no `gh`). `vpk` can validate packaging (`dotnet tool install --tool-path … vpk --version 1.2.161`, needs `zstd`).

## Tooling pitfalls

- **Edit scripts:** write Python scripts to the scratchpad with the Write tool and run them; long Bash heredocs with quotes break, and Bash collapses `\\` (Windows paths, JSON escapes). Each replacement should assert it matched exactly once.
- **Line endings:** `.cs`, `.xaml`, `.csproj`, `.props`, `.resx` are CRLF; Markdown and YAML are LF in the repo but CRLF in this working copy. Normalize before editing (`\r\n` → `\n`, edit, write back with the file's own ending).
- **Cutting code by text search:** a pattern like `"    }\n"` also matches deeper indentation; after removing a block, look at the result and build.
