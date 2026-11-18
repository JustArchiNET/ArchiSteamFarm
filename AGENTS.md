# AGENTS.md

## Scope

This repository contains ArchiSteamFarm (ASF), a cross-platform C#/.NET application for Steam account and card-farming automation. Apply these instructions to the whole repository unless a nested repository or submodule has its own instructions.

`ASF-ui` and `wiki` are Git submodules with separate repositories. Make UI or wiki edits only when the task explicitly requires them. Do not edit `ASF-ui/dist`, `ASF-ui/node_modules`, generated localization files, or any generated build output. Submodule commits and parent gitlink updates require explicit publication scope.

## Working Method

- Start by checking `git status`, existing local changes, and any relevant `AGENTS.md` instructions before searching or editing.
- Search for existing helpers, patterns, interfaces, tests, and resource names before implementing new logic.
- Ask for clarification when the requested scope, compatibility policy, migration behavior, or Steam/ToS implications are ambiguous.
- Keep changes small and coherent. Do not mix unrelated refactors, formatting cleanup, dependency upgrades, or generated artifact updates into the task.
- Do not commit, push, reset, clean, or create/restore changelists unless the user explicitly asks. Preserve the user's active branch and pending worktree state.
- Do not run live Steam/bot integration actions or start the application against real local bot configuration unless the task explicitly requires and the user has provided a safe environment for it. Prefer isolated unit tests and temporary state.

## Maintaining This File

- Update `AGENTS.md` automatically when work reveals durable repository knowledge, an important project convention, a non-obvious build/test workflow, or a decision/context supplied by the user that future AI sessions need.
- Include decisions that affect implementation or validation, especially compatibility constraints, preferred commands, scope boundaries, security requirements, and preferred handling of submodules or generated files.
- Do not wait for a separate documentation request; add or revise the relevant section during the work or before finishing it.
- Keep entries concise, repository-wide, reusable, and accurate. Do not store task-specific status, temporary paths, secrets, credentials, personal data, or unresolved speculation.
- Verify repository evidence before encoding a convention. If user-provided context conflicts with existing code or documentation, follow the most specific user instruction and record the decision if it is durable.

## Repository Sources of Truth

- `.editorconfig` and `ArchiSteamFarm.slnx.DotSettings` define C# formatting and naming rules.
- `Directory.Build.props` defines the target framework (`net11.0`), language version, analyzers, signing, trimming, and Release warning policy.
- `Directory.Packages.props` contains centrally managed NuGet package versions.
- `ArchiSteamFarm.slnx` lists all projects.
- Package references should use central package management: add or update versions in `Directory.Packages.props`, then reference the package in the relevant `.csproj` without duplicating a version.
- `.github/CONTRIBUTING.md` documents project scope, contribution, license, AI-disclosure, and code-quality requirements.
- `.github/workflows/ci.yml` is the authoritative normal CI build/test matrix.
- `.github/workflows/publish.yml` is the authoritative packaging, trimming, signing, UI, plugin, and release flow.
- `ArchiSteamFarm/Localization/Strings.resx` is the only source `.resx` file that should be edited for main ASF UI strings. Plugin source strings live in their plugin `Localization/Strings.resx` files. Translated/generated `.resx` files are managed externally.

Read the existing implementation and nearby tests before changing behavior. Preserve compatibility with ASF's public IPC API, plugin interfaces, JSON/config formats, command syntax, and Steam integration.

## Project Map

- `ArchiSteamFarm/` - executable application and core implementation.
- `ArchiSteamFarm.Tests/` - MSTest 4 test project.
- `ArchiSteamFarm.OfficialPlugins.*` - bundled or included official plugin projects.
- `ArchiSteamFarm.CustomPlugins.*` - custom-plugin examples and test/plugin reference implementations.
- `ArchiSteamFarm.OfficialPlugins.ItemsMatcher/`, `MobileAuthenticator/`, `SteamTokenDumper/` - plugins bundled in normal distributions.
- `ArchiSteamFarm.OfficialPlugins.Monitoring/` - included separately published plugin.
- `ASF-ui/` - Vue 2/Webpack web UI submodule.
- `wiki/` - documentation submodule.
- `overlay/` - variant-specific runtime files and launcher/service scripts.
- `resources/` - icons, signing public key, and release assets.
- `tools/` - auxiliary tools such as zip executable handling.

## Setup and Builds

Initialize submodules recursively when the task needs their source:

```sh
git submodule update --init --recursive
```

The environment requires .NET SDK 11 and the repo targets .NET 11. The CI matrix is also cross-platform, so avoid platform-specific assumptions unless the change is explicitly platform-specific.

Preferred build and test commands:

```sh
dotnet build -c Debug -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --nologo
dotnet test ArchiSteamFarm.Tests -c Debug -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --filter TestCategory!=Manual --nologo
```

Use `DebugFast` while iterating on behavior to get fast build/test feedback. It disables analyzers and is intended for reaching a correct implementation quickly, not for final verification:

```sh
dotnet build -c DebugFast -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --nologo
dotnet test ArchiSteamFarm.Tests -c DebugFast -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --filter TestCategory!=Manual --nologo
```

Once the implementation is working, switch back to `Debug` and verify analyzer warnings, then use `Release` for warning-as-error, trimming, or signing-sensitive validation.

For Release validation:

```sh
dotnet build -c Release -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --nologo
dotnet test ArchiSteamFarm.Tests -c Release -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --filter TestCategory!=Manual --nologo
```

Prefer a targeted project/test first, then the full test project when the change affects shared behavior:

```sh
dotnet test ArchiSteamFarm.Tests --filter "FullyQualifiedName~ArchiSteamFarm.Tests.TestClassName"
```

`./cc.sh` performs the repository's broader publish pipeline (UI deploy, tests, executable publish, and plugin builds). It defaults to pulling submodules and creates `out/`; use options explicitly for task-local validation:

```sh
./cc.sh --no-pull --no-clean
```

Do not run with `--clean` unless broad cleanup of generated `out/` is explicitly intended.

For UI changes:

```sh
npm ci --prefix ASF-ui --no-audit --no-fund --no-progress --prefer-offline
npm run-script deploy --prefix ASF-ui
```

`npm run lint --prefix ASF-ui` invokes ESLint with `--fix` and can modify files; use it only when intentionally applying/validating formatting.

## C# Rules

- All C# files use the repository Apache 2.0 header and end with a newline.
- Use tabs for indentation, file-scoped namespaces, and project naming/casing from `.editorconfig`.
- Keep nullable annotations accurate. Guard public/internal entry points with `ArgumentNullException.ThrowIfNull(...)` and `ThrowIfNullOrEmpty(...)` where established.
- Validate enums with `Enum.IsDefined(...)` and `InvalidEnumArgumentException` in test-style and public parsing paths where applicable.
- Use `ConfigureAwait(false)` consistently for library/application async continuations.
- Prefer existing helpers, collection types, logging, serialization, and Steam/IPC abstractions over introducing a new mechanism.
- Preserve cancellation tokens through async call chains and handle `OperationCanceledException` appropriately.
- Use existing NLog/`ArchiLogger` patterns instead of `Console.WriteLine` or ad-hoc logging.
- Avoid blocking async methods with `.Result`, `.Wait()`, or thread-affinity assumptions.
- Use `Task.CompletedTask` or `Task.FromResult(...)` for fully synchronous implementations.
- Do not suppress analyzer warnings unless there is a clear false-positive or documented compatibility reason. Release builds treat warnings as errors.
- C# release configuration disables overflow checks and enables trimming; changes must remain compatible with partial trimming and native/single-file variants.
- Preserve source compatibility for public plugin interfaces and public IPC request/response types.
- Keep all JSON property names and config keys stable unless the task explicitly includes a migration. If migration is needed, follow the existing `SerializableFile` and migration patterns.

## Tests

- Tests use MSTest 4 in `ArchiSteamFarm.Tests`.
- Use `[TestMethod]`, `[DataRow]`, and `[TestCategory("Manual")]` consistently. Manual tests are excluded in normal CI and must not be made part of the automatic suite unless they become deterministic.
- `TestBase` provides cancellation and web-browser test helpers.
- Test classes use `ExecutionScope.MethodLevel` parallelization; preserve isolation and avoid shared mutable global state.
- Add or update the smallest targeted test for a behavior change, then run the complete test project if practical.
- Use platform-specific attributes such as `OSCondition` for OS-dependent behavior.
- Do not add network-dependent tests to the normal suite. Existing manual or live-service tests are the exceptions.

## Plugin and IPC Changes

- Plugin discovery uses `IPlugin` exports and `System.Composition`; implement only the plugin interfaces actually needed.
- Official plugins must remain version-compatible with ASF. Custom plugins are explicitly marked/modded and can differ.
- Plugin callbacks may run during partial initialization: check nulls and optional global state, and preserve cancellation.
- Plugin code must clean up bot references and disposal state in destroy/dispose callbacks.
- IPC controllers and request/response DTOs may be exposed by ASP.NET/OpenAPI and ASF-ui. Keep routes, JSON shapes, authorization, and Swagger metadata intentional and backward-compatible.
- Web and third-party HTTP calls should use ASF's `WebBrowser`/bot `ArchiWebHandler` patterns as appropriate.

## UI and Submodule Changes

- UI source is in `ASF-ui/src`; it is a Vue 2 application using Vuex, Vue Router, Axios, Webpack, and ESLint.
- UI changes that depend on IPC fields or routes must be coordinated with the corresponding ASF controller/DTO/OpenAPI changes.
- Coordinate submodule changes with the parent repository explicitly. Do not update a submodule gitlink or push submodule work unless the task requests repository/submodule publication. Do not commit generated `ASF-ui/dist` or `node_modules`.
- Wiki changes belong to the `wiki` submodule, not parent repository documentation files.

## Localization

- Edit the neutral main `ArchiSteamFarm/Localization/Strings.resx` and plugin source `Strings.resx` files only.
- Do not edit localized/generated `.resx` variants or localization workflow outputs manually.
- UI translations follow ASF-ui's separate submodule workflow.
- Use meaningful resource names and preserve existing public strings when only formatting or localization metadata changes.

## Generated and Runtime State

Do not hand-edit or commit:

- `bin/`, `obj/`, `out/`, `ArchiSteamFarm/bin/`, `ArchiSteamFarm/obj/`
- `ASF-ui/dist/`, `ASF-ui/node_modules/`
- `ArchiSteamFarm/config/`, `ArchiSteamFarm/log.txt`, `ArchiSteamFarm/logs/`, `ArchiSteamFarm/debug/`
- local `.bak`, database, coverage, test-result, or publish artifacts

The `config/` directory and runtime database/log files are local state. Preserve any pre-existing worktree changes unless the task explicitly owns them.

## Secrets and Security

Never add private signing keys, Steam credentials, bot config secrets, API tokens, or real database values to source or commits.

- `resources/ArchiSteamFarm.snk` is private build signing material and must not be created or populated manually.
- `ArchiSteamFarm.OfficialPlugins.SteamTokenDumper/SharedInfo.cs` contains a build placeholder, normally replaced from `STEAM_TOKEN_DUMPER_TOKEN` only in secret-backed publishing.
- Existing local configs, logs, and databases may contain sensitive data; do not read, print, summarize, or include them in patches unless the task explicitly requires it.
- Do not weaken authentication, IPC ban controls, trade/Steam safety logic, or plugin isolation without explicit task scope.

## Change Safety and Validation

- Inspect `git status` before and after edits; do not revert, clean, or move unrelated changes.
- Make surgical changes with focused tests and avoid unrelated formatting/refactoring.
- Prefer focused builds/tests while developing. Use `./cc.sh` or publish workflow parity for final cross-project, UI, plugin, variant, or packaging changes rather than as the default iteration command.
- Use `dotnet format` only when a targeted formatter invocation is necessary; prefer the configured IDE cleanup/import/reformat behavior.
- For C# changes, run at least the relevant test or build command, and run Release validation for warning, trimming, or signing-sensitive work.
- For plugin changes, verify affected plugin project builds and normal plugin discovery/post-build copy behavior.
- For UI changes, run the deploy build and, only if needed, the fix-mode lint command.
- For documentation-only changes, no build/test run is required unless repository documentation checks exist.
- Before reporting complete, verify that no generated, secret, submodule, or unrelated files were included in the change.

## Community and AI Contributions

Follow `.github/CONTRIBUTING.md`: ASF has a strict scope and Steam ToS policy. GitHub issues and PRs are development-focused, not technical support. All meaningful AI assistance must be disclosed in community-facing issues, discussions, PRs, and messages, naming the tool when possible. The submitter remains accountable for correctness.
