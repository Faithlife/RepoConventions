# Test Runtime Parallelization Plan

## Goal

Reduce the wall-clock time for `RepoConventions.Tests` while preserving the integration-heavy coverage that exercises temporary git repositories, PowerShell convention scripts, and the in-process CLI.

## Current Observations

- `tests/RepoConventions.Tests/AssemblyInfo.cs` already enables NUnit parallel execution with `[assembly: Parallelizable(ParallelScope.All)]` and `[assembly: LevelOfParallelism(8)]`.
- Most tests invoke `RepoConventionsCli.InvokeAsync` in-process through duplicated `CliInvocation` helpers.
- `src/RepoConventions/RepoConventionsCli.cs` redirects `Console.Out` and `Console.Error` globally around `ParseResult.InvokeAsync`. That process-wide mutation is unsafe when multiple tests invoke the CLI concurrently, even when each test passes its own `StringWriter` instances.
- Two tests in `ConventionExecutionTests` mutate `GITHUB_ACTIONS` through `TemporaryEnvironmentVariable` and are marked `[NonParallelizable]`.
- `System.CommandLine` 2.0.5 has `InvocationConfiguration.Output` and `InvocationConfiguration.Error`, and `ParseResult.InvokeAsync` accepts an `InvocationConfiguration`, so the CLI output path can be made instance-local without changing package versions.

## Plan

### 1. Establish a Timing Baseline

- Run the current test project several times with the same build output to separate build time from test execution time:
  - `dotnet test tests/RepoConventions.Tests/RepoConventions.Tests.csproj --no-restore --no-build`
  - `dotnet test tests/RepoConventions.Tests/RepoConventions.Tests.csproj --no-restore --no-build -- NUnit.NumberOfTestWorkers=1`
  - `dotnet test tests/RepoConventions.Tests/RepoConventions.Tests.csproj --no-restore --no-build -- NUnit.NumberOfTestWorkers=8`
- Capture per-test duration data with a TRX or NUnit result file so slow tests and fixtures are visible before changing anything.
- Record the baseline in the eventual PR description rather than committing generated test-result artifacts.

### 2. Remove Global Console Redirection

- Replace `RepoConventionsCli.InvokeParseResultAsync`'s `Console.SetOut` and `Console.SetError` block with a per-invocation `InvocationConfiguration`:
  - `Output = standardOutput`
  - `Error = standardError`
- Continue passing `standardOutput` and `standardError` directly into command actions, since the repo already uses those writers for application output.
- Keep explicit flushing after invocation so current tests continue to read complete `StringWriter` contents.
- Add or adjust tests around root help and parse errors if needed, because those are the paths most likely to depend on `System.CommandLine`'s configured output/error writers.

### 3. Make GitHub Actions Detection Injectable

- Move the direct `Environment.GetEnvironmentVariable("GITHUB_ACTIONS")` read out of `ConventionRunner.IsRunningInGitHubActions`.
- Prefer one of these small injection shapes:
  - Add a `bool IsGitHubActions` or `bool UseGitHubActionsGroupMarkers` property to `ConventionRunnerSettings`, computed by the CLI from the environment in production.
  - Or add an environment-reader delegate to the internal CLI/runner settings if future environment-dependent behavior is likely.
- Update the two group-marker tests to pass the desired value explicitly instead of mutating the process environment.
- Remove `TemporaryEnvironmentVariable` and the two `[NonParallelizable]` attributes once no tests mutate process-wide environment variables.

### 4. Consolidate CLI Test Invocation Helpers

- Introduce a shared test helper for invoking `RepoConventionsCli` with isolated output/error writers and optional test hooks such as remote repository URL resolution, fake external command execution, and GitHub Actions mode.
- Replace the duplicated nested `CliInvocation` helpers in `AddCommandTests`, `GitRepoTests`, `ConventionExecutionTests`, and `OpenPrTests`.
- This is mostly cleanup, but it lowers the chance that future CLI tests accidentally reintroduce global state or skip the new injectable settings.

### 5. Tune Parallelism After Isolation

- Re-run the baseline commands with worker counts such as 1, 4, 8, and 12.
- Keep the default at the fastest stable value on Windows. The current value of 8 may already be best, but the suite does a lot of git and PowerShell process work, so higher worker counts might become slower from disk and process-start contention.
- If command-line worker tuning is preferable to assembly attributes, consider moving the worker count into a checked-in `.runsettings` file or the build script so local and CI runs use the same setting.

### 6. Optional Follow-Up: Reduce Process Work Without Losing Coverage

- After parallelism is safe, inspect the per-test timing report for tests that spend most of their time launching `pwsh` or running repeated git setup.
- Keep representative end-to-end tests that run real PowerShell scripts and real git commands.
- For tests whose assertion is about RepoConventions orchestration rather than PowerShell or git integration, consider narrow injection points for convention script execution or repository state so those cases can avoid spawning extra processes.
- Treat this as a second pass only if the safe-parallelism changes do not meet the runtime target.

## Acceptance Criteria

- No production or test code uses `Console.SetOut` or `Console.SetError` for CLI test capture.
- No tests mutate `GITHUB_ACTIONS` or any other process-wide environment variable.
- The two currently nonparallel group-marker tests can run in parallel with the rest of the suite.
- Repeated `dotnet test tests/RepoConventions.Tests/RepoConventions.Tests.csproj --no-restore --no-build -- NUnit.NumberOfTestWorkers=8` runs pass.
- The full repository validation still passes with `./build.ps1 test` before the final implementation commit.
- The PR notes include before/after wall-clock timings and the chosen NUnit worker count.

## Risks

- `System.CommandLine` help or parse-error output may behave slightly differently when using `InvocationConfiguration`; cover this with focused tests before relying on the speedup.
- Higher test parallelism could expose independent filesystem or git temp-directory races. Keep failures actionable by preserving per-test temporary directories and avoiding shared test artifacts.
- Too many workers may make the suite slower on machines with limited disk throughput, so the final worker count should be measured rather than assumed.
