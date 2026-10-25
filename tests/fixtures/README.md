# Test fixtures

Sample store files for fixture-driven provider tests, one folder per store: `steam/`, `epic/`, `gog/`, ...

Rules:

- Fixtures must be **synthetic or anonymized**. Never commit a real library, SteamID, account name, token or path that identifies a person.
- Keep each fixture minimal: just enough to exercise the parser case it belongs to.
- When a fixture models a format you could not verify on a real machine, say so in the test with a `// VERIFY:` comment.
