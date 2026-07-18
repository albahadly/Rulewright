# Contributing to Rulewright

Thanks for your interest! Issues, discussions, and pull requests are all welcome.

## Development setup

- Install the [.NET 8+ SDK](https://dotnet.microsoft.com/download). On Windows,
  .NET Framework 4.8 (in-box on Windows 10/11) lets you run the full test matrix.
- Build & test:

  ```
  dotnet build Rulewright.slnx
  dotnet test  Rulewright.slnx              # net8.0 + net48 on Windows
  dotnet test  Rulewright.slnx -f net8.0    # net8.0 only (Linux/macOS)
  ```

- Benchmarks (Release only):

  ```
  dotnet run -c Release --project tests/Rulewright.Benchmarks -- --filter '*Evaluation*'
  ```

## Ground rules

- **`Rulewright.Core` stays zero-dependency** and every library keeps compiling for
  `netstandard2.0`. Use only syntax-level C# features there (no `init`, no records,
  no `System.Index/Range`).
- **The JSON schema is a contract.** Changes to `docs/schema/rule-schema.json`, the
  validator, or observable evaluation semantics must update the golden-file fixtures
  (`tests/Rulewright.Execution.Tests/Fixtures`) deliberately — a fixture change is a
  reviewable, intentional contract change.
- **Compiled and interpreted paths stay in parity.** If you change operator
  semantics, change both `RuleExpressionCompiler` and `RuleInterpreter`/
  `RuntimeComparisons`, and cover both in tests.
- **Public members carry XML docs.** The build treats warnings as errors, including
  missing docs.
- New operators or actions need: schema update, validator rules, parser mapping,
  compiler + interpreter implementations, tests for both paths, and README docs.

## Pull requests

1. Fork, branch from `main`, keep changes focused.
2. Add or update tests — bug fixes need a failing-before test.
3. `dotnet build` and `dotnet test` must pass with zero warnings.
4. Describe *why* in the PR body, not just what.

## Reporting bugs

Please include: the rule JSON, the fact (shape and values), expected vs actual
result, and whether the run was `Compiled` or `Interpreted`
(`result.CompilationMode`). A failing golden-file fixture makes a perfect repro.
