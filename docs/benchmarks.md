# Benchmarks

The benchmark suite lives in `tests/Rulewright.Benchmarks` (BenchmarkDotNet) and
covers, from the first working version of the engine:

- **`EvaluationBenchmarks`** — warm-path throughput at 1 / 100 / 10,000 rules:
  compiled typed facts (baseline), compiled with tracing enabled, and the
  dictionary-fact interpreter.
- **`ColdStartBenchmarks`** — cold `LoadRuleSet` (parse + validate + hash), cold
  load + first compiled evaluation, and a warm cached evaluation as baseline.

Run them in Release mode:

```
dotnet run -c Release --project tests/Rulewright.Benchmarks -- --filter '*Evaluation*'
dotnet run -c Release --project tests/Rulewright.Benchmarks -- --filter '*ColdStart*'
```

## Published results

Pending — results tables for the current release, plus the comparison harness
against NRules and Microsoft RulesEngine (same rule set, same fact shape), are a
roadmap item. Numbers will be published here per release once the comparison
harness lands, and CI will gain a regression gate that fails when a benchmark
regresses beyond a defined threshold.
