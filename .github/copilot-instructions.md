# Copilot instructions for Rulewright

Rulewright is a JSON-driven business rule engine for .NET: parse a rule document, validate it
against `docs/schema/rule-schema.json`, compile or interpret it, evaluate facts against it. It
targets `netstandard2.0` + `net8.0` (multi-targeted) so it runs on both modern .NET and .NET
Framework 4.8.

## Repo layout

- `src/Rulewright.Core` — domain model (`Rule`, `ConditionNode`, `ValueExpression`, `RuleAction`,
  results/trace types, `IRuleFunction`). **Zero dependencies**, must stay netstandard2.0-only
  syntax (no `init`, no records, no `System.Index`/`Range`).
- `src/Rulewright.Serialization` — JSON-neutral DOM (`RuleJsonValue`) ↔ domain mapping
  (`RuleSetParser`), structural validation (`RuleSetValidator`, JSON-pointer errors), content
  hashing (`RuleHasher`), and `RuleSchemaCatalog` (the vocabulary as structured metadata for
  UI/tooling).
- `src/Rulewright.Execution` — `RulewrightBuilder`/`RulewrightEngine`: compiles rules to
  expression-tree delegates per fact type (cached by content hash) with an interpreter fallback
  for dictionary facts; tracing is a separate compiled delegate, opt-in.
- `src/Rulewright.Json.SystemText`, `src/Rulewright.Json.NewtonsoftJson` — `IRuleJsonReader`
  adapters; pick either, `Rulewright.Serialization` has no JSON library dependency itself.
- `src/Rulewright.Extensions.Functions` — built-in `custom`-operator predicates +
  registration/assembly-scan helpers.
- `tests/` — one test project per `src/` library (xUnit v3, both TFMs), plus
  `Rulewright.Benchmarks` (BenchmarkDotNet, includes an NRules/RulesEngine comparison).
- `examples/` — 19 canonical rule-schema JSON documents, each with a golden-file fixture in
  `tests/Rulewright.Execution.Tests/Fixtures`; also the demo content the Blazor samples load.
- `samples/` — `ConsoleApp`, `AspNetCore`, `NewtonsoftJson`, `Functions`, `DecisionTable`,
  `NetFramework48`, and two Blazor WebAssembly **visual rule builders**:
  `Rulewright.Sample.BlazorBuilder` (form/tree-based editor) and
  `Rulewright.Sample.BlazorBuilder.v2` (a hand-rolled drag-and-drop node canvas — all its
  interactive state lives in `wwwroot/js/rule-canvas.js`; the Razor page is just the page shell
  plus two `[JSInvokable]` bridge methods to the real engine for Validate/Test).
- `docs/schema/rule-schema.json` — the JSON Schema contract; `docs/architecture.md` — design
  writeup.

## Ground rules (from CONTRIBUTING.md — do not violate these)

- **`Rulewright.Core` stays zero-dependency**, and every `src/` library keeps compiling for
  `netstandard2.0`.
- **The JSON schema is a contract.** A change to `docs/schema/rule-schema.json`, the validator,
  or observable evaluation semantics must come with a deliberate, reviewable update to the
  golden-file fixtures — never regenerate them silently to make a test pass.
- **Compiled and interpreted paths must stay in parity.** Any change to operator/expression
  semantics touches BOTH `RuleExpressionCompiler` and `RuleInterpreter`/`RuntimeComparisons`, with
  tests covering both paths (typed/compiled fact + dictionary/interpreted fact).
- **Public members require XML doc-comments.** The build has `TreatWarningsAsErrors` on,
  including missing-docs warnings — an undocumented public member fails the build, not just a
  lint pass.
- **A new operator or action type is a multi-file change**: schema update, validator rule, parser
  mapping (JSON name ↔ domain enum), compiler implementation, interpreter implementation, tests
  for both paths, README/docs update. Missing any one of these is an incomplete PR.

## Build & test

```
dotnet build Rulewright.slnx
dotnet test  Rulewright.slnx              # net8.0 + net48 (Windows)
dotnet test  Rulewright.slnx -f net8.0    # net8.0 only (Linux/macOS, no net48 runtime)
```

Both must be clean (0 warnings, 0 failures) before considering a change done. For a Blazor
sample change, also run it (`dotnet run --project samples/<project>`) and exercise the change in
a real browser — these are WASM apps; a clean build does not mean the UI behaves correctly, and
neither project has a browser-automation test suite. Check `samples/<project>/.claude/skills/`
for a project-specific verify recipe (launch command, key selectors, known gotchas) before
inventing one from scratch.

## Conventions

- Null semantics, operator behavior, and hashing rules are deliberately spelled out in
  `docs/architecture.md` — read it before changing evaluation semantics rather than guessing.
- `RuleHasher`'s content hash covers only condition + actions (not id/description/priority/
  enabled/layout) — it's the compiled-delegate cache key; don't fold metadata fields into it.
- The `layout` key on a rule is opaque, engine-ignored, reserved for a visual builder's canvas
  positions. Don't give it evaluation meaning.
- Prefer small, focused PRs mirroring one roadmap item at a time (see README's Roadmap section
  for what's shipped vs. planned) over broad refactors.
- Commit messages: Conventional Commits (`feat:`, `fix:`, `chore:`, …), concise subject, `-`
  bulleted body. This repo does not use AI attribution trailers in commits.

## What not to do

- Don't add a runtime dependency to `Rulewright.Core`.
- Don't special-case behavior differently between the compiled and interpreted evaluation paths.
- Don't change `docs/schema/rule-schema.json` or fixture files without updating the other side of
  that contract in the same change.
- Don't suppress `TreatWarningsAsErrors` or add `#pragma warning disable` to work around a missing
  XML doc — write the doc.
