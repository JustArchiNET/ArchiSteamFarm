# AGENTS.md

## Scope

ASF is a cross-platform C#/.NET Steam automation application. These rules apply repository-wide unless a nested repository or submodule has more specific instructions.

`ASF-ui` and `wiki` are separate submodule repositories. Do not edit generated UI output or submodule internals unless the task explicitly covers them.

## Working Method

- Check `git status`, existing changes, and relevant instructions before searching or editing.
- Reuse existing helpers, patterns, interfaces, tests, resource names, and serialization code.
- Preserve public IPC API, plugin interfaces, JSON/config formats, command syntax, and Steam compatibility unless the task explicitly changes them.
- Keep changes small and coherent. Ask for clarification on ambiguous scope, migration, Steam/ToS, or compatibility decisions.
- Do not commit, push, reset, clean, or create/restore changelists unless explicitly requested.
- Prefer isolated tests and temporary state. Do not run live Steam/bot actions against real local configuration without explicit task scope and safe credentials/state.

## Maintain This File

- Add durable repository knowledge, important decisions, stable workflows, and user-provided constraints that future sessions need.
- Update `AGENTS.md` during the work or before finishing; do not wait for a separate documentation request.
- Keep entries concise and reusable. Verify conventions against repository evidence.
- Never store task status, temporary paths, secrets, credentials, personal data, or unresolved speculation.
- Follow the most specific current user instruction; record it in this file only when it is durable.

## Sources of Truth

- `.editorconfig` and `ArchiSteamFarm.slnx.DotSettings`: C# formatting and naming.
- `Directory.Build.props`: target framework, language/analyzers, signing, trimming, and warnings.
- `Directory.Packages.props`: centrally managed NuGet versions.
- `ArchiSteamFarm.slnx`: projects. `ArchiSteamFarm/` is core; `ArchiSteamFarm.Tests/` is MSTest; plugin projects are `ArchiSteamFarm.OfficialPlugins.*` and `ArchiSteamFarm.CustomPlugins.*`.
- `.github/CONTRIBUTING.md`: scope, issues/PRs, license, and AI disclosure.
- `.github/workflows/ci.yml` and `publish.yml`: authoritative CI and release behavior.

## Builds and Tests

Environment: .NET SDK 11, `net11.0`, cross-platform CI.

```sh
# Fast iteration
dotnet build -c DebugFast -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --nologo
dotnet test ArchiSteamFarm.Tests -c DebugFast -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --filter TestCategory!=Manual --nologo

# Analyzer verification
dotnet build -c Debug -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --nologo
dotnet test ArchiSteamFarm.Tests -c Debug -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --filter TestCategory!=Manual --nologo

# Warning-as-error/trimming/signed validation
dotnet build -c Release -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --nologo
dotnet test ArchiSteamFarm.Tests -c Release -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --filter TestCategory!=Manual --nologo
```

Use `DebugFast` for fast iteration; it disables analyzers. Return to `Debug`, then `Release` as appropriate.
Prefer a targeted build/test first. Use `dotnet test ArchiSteamFarm.Tests --filter "FullyQualifiedName~ArchiSteamFarm.Tests.TestClassName"` for a focused MSTest class.
Use `./cc.sh --no-pull --no-clean` for broader publish validation. Do not use `--clean` unless broad cleanup is explicitly intended.

For UI source changes:

```sh
npm ci --prefix ASF-ui --no-audit --no-fund --no-progress --prefer-offline
npm run-script deploy --prefix ASF-ui
```

## C# and Tests

- Preserve tabs, file-scoped namespaces, `.editorconfig` naming, Apache 2.0 file headers, and final newlines.
- Keep nullable annotations accurate; guard public/internal inputs consistently. Preserve cancellation tokens and use `ConfigureAwait(false)`.
- Use existing NLog/`ArchiLogger`, serialization, collection, Steam, IPC, and plugin abstractions; avoid `.Result`, `.Wait()`, and ad-hoc logging.
- Validate enums and use the established argument/null/invalid-operation exceptions.
- Release treats warnings as errors. Do not suppress analyzers unless justified; account for trimming and single-file/native variants.
- Package versions belong in `Directory.Packages.props`; projects reference packages without version duplication.
- MSTest 4 uses method-level parallelization and `TestBase` helpers. Keep tests isolated; use `[DataRow]`, `[TestCategory("Manual")]`, and `OSCondition` consistently.
- Manual/network/live-service tests stay outside normal CI unless they become deterministic.

## Plugins, IPC, UI, and Localization

- Plugin discovery uses `[Export(typeof(IPlugin))]` and `System.Composition`. Implement only required interfaces and clean up bot/disposal references.
- Official plugins must remain version-compatible. Preserve plugin callback cancellation and partial-initialization null checks.
- IPC request/response types, routes, JSON shapes, authorization, and Swagger metadata require backward-compatible or explicitly migrated changes.
- UI source is under `ASF-ui/src` (Vue 2/Webpack/ESLint). Coordinate IPC-dependent UI changes and submodule gitlinks explicitly.
- Edit only neutral source strings: `ArchiSteamFarm/Localization/Strings.resx` and plugin `Localization/Strings.resx`.
- Do not hand-edit localized `.resx` variants or generated UI/wiki/localization output.

## Generated State and Secrets

Do not hand-edit or commit `bin/`, `obj/`, `out/`, `ASF-ui/dist/`, `ASF-ui/node_modules/`, local config/log/debug/database state, or publish/test artifacts.

Never add private signing keys, Steam credentials, bot secrets, API tokens, or real database values. Do not read or print existing local sensitive state unless explicitly required.

## Validation and Final State

- Inspect status before and after changes; preserve unrelated work.
- Run targeted tests/builds first, `Debug` for analyzer verification, and `Release` for warning/trimming/signing-sensitive work.
- For plugin changes, verify affected project builds and plugin copy/discovery behavior.
- For UI changes, run deploy/build and lint only when needed; lint can modify files.
- Documentation-only changes need no build unless repository documentation checks exist.
- Before completion, verify there are no generated, secret, submodule, or unrelated files in the change.

## Community

Follow `.github/CONTRIBUTING.md`: strict scope and Steam/ToS policy, development-focused GitHub issues/PRs, Apache 2.0 licensing, and disclosure of meaningful AI assistance.
