# RepoConventions Tech Spec

This document tracks the implementation details that still need decisions.

## Open Questions

- [ ] How does the CLI choose the target repository and conventions file? Current directory only, or explicit path arguments?

Current directory only. We can add arguments in the future if we need them.

- [ ] What is the exact command surface? Are `--commit` and `--open-pr` mutually exclusive? Do we also need `--help`, `--version`, `--verbose`, or `--dry-run`?

Open PR implies commit. Help and version sound good. No verbose or dry run just yet.

- [ ] What does no-argument validation include: YAML shape only, or also recursive convention resolution, existence checks, and cycle detection?

I've changed my mind on no argument validation. Let's just show usage if there are no arguments for now.

- [ ] What exit codes should distinguish invalid config, dirty repo, convention failure, auth/network failure, and PR failure?

Different failure codes don't seem necessary to me. For now.

- [ ] When conventions leave file changes, does the CLI auto-commit after each convention or once at the end?

After each convention.

- [ ] What commit message format should auto-created commits use?

`Apply convention <name>.`

The name is the name of the convention directory or of the repository if the convention is at the root.

- [ ] If a convention fails after earlier changes or commits, what rollback behavior is required?

Please restate this open question with my options.

- [ ] Which PowerShell host should run `convention.ps1`: `pwsh`, Windows PowerShell, or either?

pwsh

- [ ] Should the JSON input for `convention.ps1` contain anything besides `settings`?

no

- [ ] How are composite conventions resolved: declaration order, nested settings behavior, cycle handling, and validation when both `convention.yml` and `convention.ps1` exist?

Declaration order. Nested settings will use syntax like GitHub Actions, but for now, composite convention settings are ignored. When a cycle is detected, just skip over the convention entry that would cause it. Let's allow conventions to have both and apply the composite convention before running the script.

- [ ] How are remote conventions fetched: git clone, archive download, or GitHub API? Do we need caching?

Git clone is fine.

- [ ] How is authentication provided for private convention repos and for PR creation?

Use gh for PR creation. Assume git and gh are both already authenticated as desired.

- [ ] What branch naming, push target, and PR title/body should `--open-pr` use?

Restate the question with one or more proposals.

- [ ] What should happen if a matching branch or open PR already exists?

If an open PR already exists, it should apply the conventions to the code in that PR.

- [ ] What console output is required for progress, no-op runs, and failures? Do we need a machine-readable mode?

Convention script output should not be captured, i.e. it should just flow to the caler. When running under GitHub Actions, display each convention run in its own group. Aside from that, restate the question with one or more proposals.
