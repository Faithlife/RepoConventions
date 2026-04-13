# RepoConventions Tech Spec

This document tracks the implementation details that still need decisions.

## Open Questions

- [ ] How does the CLI choose the target repository and conventions file? Current directory only, or explicit path arguments?
- [ ] What is the exact command surface? Are `--commit` and `--open-pr` mutually exclusive? Do we also need `--help`, `--version`, `--verbose`, or `--dry-run`?
- [ ] What does no-argument validation include: YAML shape only, or also recursive convention resolution, existence checks, and cycle detection?
- [ ] What exit codes should distinguish invalid config, dirty repo, convention failure, auth/network failure, and PR failure?
- [ ] When conventions leave file changes, does the CLI auto-commit after each convention or once at the end?
- [ ] What commit message format should auto-created commits use?
- [ ] If a convention fails after earlier changes or commits, what rollback behavior is required?
- [ ] Which PowerShell host should run `convention.ps1`: `pwsh`, Windows PowerShell, or either?
- [ ] Should the JSON input for `convention.ps1` contain anything besides `settings`?
- [ ] How are composite conventions resolved: declaration order, nested settings behavior, cycle handling, and validation when both `convention.yml` and `convention.ps1` exist?
- [ ] How are remote conventions fetched: git clone, archive download, or GitHub API? Do we need caching?
- [ ] How is authentication provided for private convention repos and for PR creation?
- [ ] What branch naming, push target, and PR title/body should `--open-pr` use?
- [ ] What should happen if a matching branch or open PR already exists?
- [ ] What console output is required for progress, no-op runs, and failures? Do we need a machine-readable mode?