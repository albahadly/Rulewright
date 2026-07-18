# Rulewright

A high-performance, JSON-driven business rule engine for .NET. Rules are plain JSON
documents; evaluation is compiled expression trees — parse once, compile once, execute
millions of times.

[![CI](https://github.com/rulewright/rulewright/actions/workflows/ci.yml/badge.svg)](https://github.com/rulewright/rulewright/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

```json
{
  "id": "discount-rule-01",
  "description": "VIP or high-value customers get 10% off",
  "priority": 10,
  "condition": {
    "type": "group",
    "operator": "AND",
    "rules": [
      { "field": "Customer.Age", "operator": "GreaterThan", "value": 18 },
      {
        "type": "group",
        "operator": "OR",
        "rules": [
          { "field": "Order.Total", "operator": "GreaterThanOrEqual", "value": 100 },
          { "field": "Customer.IsVip", "operator": "Equals", "value": true }
        ]
      }
    ]
  },
  "actions": [
    { "type": "setOutput", "target": "Discount", "value": 10 },
    { "type": "setOutput", "target": "DiscountReason", "value": "VIP or high-value order" }
  ]
}
```

```csharp
var engine = new RulewrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .RegisterFunction("IsBusinessDay", (fieldValue, value) => /* ... */ true)
    .Build();

LoadedRuleSet ruleSet = engine.LoadRuleSet(json);   // parse + validate once

var result = engine.Evaluate(ruleSet, customerOrder, new EvaluationOptions
{
    EnableTrace = true,
    StopOnFirstMatch = false,
});

foreach (FiredRule fired in result.FiredRules)
    Console.WriteLine($"{fired.RuleId} -> {string.Join(", ", fired.Outputs)}");
```

## Why Rulewright?

- **Compiled, not interpreted.** Rules compile to delegates via expression trees:
  parse once, compile once per fact type, then evaluations are direct delegate calls —
  no reflection, no string parsing, no boxing of value-type comparisons. Compiled
  delegates are cached by a **content hash** of the rule (not its id), so editing a
  rule's body always invalidates the cache, while reformatting or moving canvas nodes
  never does.
- **One package, .NET Framework 4.8 through .NET 8+.** The core targets
  `netstandard2.0` with **zero dependencies**; a `net8.0` build lights up newer BCL
  paths. A .NET Framework 4.8 sample project builds and runs in CI — the compatibility
  claim is executed, not asserted.
- **Bring your own JSON library.** Parsing is abstracted behind `IRuleJsonReader`
  with adapter packages (`Rulewright.Json.SystemText` today, Newtonsoft planned), so
  the core never forces a JSON dependency on your app.
- **Built for auditability.** Opt-in execution traces record which rules fired and
  which condition nodes passed, failed, or were short-circuited — with **zero
  overhead when disabled** (separate compiled fast path, not a runtime flag check).
- **Designed for a visual future.** The schema keeps logic and presentation apart:
  the `layout` key is reserved for drag-and-drop canvas tools and ignored by the
  engine. A formal JSON Schema (`docs/schema/rule-schema.json`) plus a standalone
  validator with JSON-pointer errors are the contract a future rule-builder UI
  plugs into.

### Why not NRules or Microsoft RulesEngine?

| | Rulewright | NRules | MS RulesEngine |
|---|---|---|---|
| Rule format | JSON documents | C# fluent DSL | JSON with lambda-expression strings |
| Evaluation | Compiled expression trees | Rete network | Parsed/compiled C# expression strings |
| Inference / chaining | No (v1 non-goal) | Yes | No |
| netstandard2.0 + .NET FX 4.8 | Yes, zero-dep core | Yes | Partial |
| Layout metadata for UI round-trip | First-class (`layout`) | n/a | n/a |
| Execution trace | Opt-in, per-node | Events | Partial |

NRules is the right tool for forward-chaining inference over a working memory.
Microsoft RulesEngine embeds C# expression *strings* in JSON, which means arbitrary
code in rule files and runtime string compilation. Rulewright's rules are pure data:
a closed, validatable operator vocabulary that a UI can safely generate and a
reviewer can safely diff.

## Install

```
dotnet add package Rulewright.Execution
dotnet add package Rulewright.Json.SystemText
```

*(Not yet published to NuGet — build from source for now; see below.)*

## Concepts

| Term | Meaning |
|---|---|
| **Rule** | `id` + condition tree + actions (+ `priority`, `enabled`, ignored `layout`). |
| **Condition** | A leaf (`field` / `operator` / `value`) or a group (`AND` / `OR` / `NOT` over children). |
| **Fact** | The object a rule set is evaluated against: a typed POCO (compiled path) or an `IDictionary<string, object>` (interpreted path). |
| **Action** | v1: `setOutput` — writes a constant into the result's outputs. |

### Operators

Comparison `Equals`, `NotEquals`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`,
`LessThanOrEqual` · String (ordinal) `Contains`, `StartsWith`, `EndsWith`,
`MatchesRegex` · Collection `In`, `NotIn` · Null `IsNull`, `IsNotNull` · Logical
`AND`, `OR`, `NOT` · Extensible `custom` + `name`, resolved against functions
registered on the builder **at compile time**.

### Field resolution

`"field": "Customer.Age"` resolves a dotted path against the fact:

- **Typed facts**: compile-time member access (`Expression.PropertyOrField` chains).
  A missing member is a load/compile-time `RuleCompilationException`, not a runtime
  surprise. Navigation is null-safe: a null intermediate applies the operator's null
  semantics instead of throwing.
- **Dictionary facts**: nested dictionaries, with cached-reflection fallback for POCOs
  stored inside dictionaries. Missing keys resolve to null. The result reports
  `CompilationMode.Interpreted` so the slower path is visible, never silent.

### Null semantics (both paths, by design)

A null field value (or null anywhere along the path) makes every operator return
`false`, except: `IsNull` → `true`, `NotEquals` (non-null comparand) → `true`,
`NotIn` → `true`, and `Equals` with a `null` comparand → `true`.

## Performance

The benchmark suite (`tests/Rulewright.Benchmarks`, BenchmarkDotNet) measures
compiled vs interpreted throughput at 1 / 100 / 10,000 rules and cold-load vs
warm-cache cost. Run it with:

```
dotnet run -c Release --project tests/Rulewright.Benchmarks -- --filter '*'
```

Published results and a comparison harness against NRules and Microsoft RulesEngine
land in `docs/benchmarks.md` (roadmap).

## Building from source

```
git clone https://github.com/rulewright/rulewright.git
cd rulewright
dotnet build Rulewright.slnx
dotnet test  Rulewright.slnx            # runs on net8.0 and net48 (Windows)
dotnet run --project samples/Rulewright.Sample.ConsoleApp
```

Requires the .NET 8+ SDK. On Windows, the test suite and the
`Rulewright.Sample.NetFramework48` smoke test also exercise .NET Framework 4.8.

## Packages

| Package | Contents |
|---|---|
| `Rulewright.Core` | Domain model: `Rule`, conditions, results, `IRuleFunction`. Zero dependencies. |
| `Rulewright.Serialization` | JSON ↔ domain mapping, structural validator (JSON-pointer errors), content hashing. |
| `Rulewright.Execution` | Expression-tree compiler, interpreter fallback, delegate cache, `RulewrightBuilder` / `RulewrightEngine`. |
| `Rulewright.Json.SystemText` | `IRuleJsonReader` adapter for System.Text.Json + `JsonElement` fact helpers. |
| `Rulewright.Json.NewtonsoftJson` | *(scaffold — planned)* Newtonsoft.Json adapter. |
| `Rulewright.Extensions.Functions` | *(scaffold — planned)* function catalog & discovery. |

## Non-goals for v1

Called out explicitly so expectations are clear:

- **No forward-chaining / RETE-style inference.** One fact set in, one result out —
  stateless, single-pass evaluation. Rule outputs never feed other rules' inputs.
- **No persistence layer.** Storing rule JSON is your application's concern; this
  library parses, validates, compiles, and executes.
- **No UI in this phase.** The Blazor drag-and-drop builder is a separate future
  project — the schema's `layout` key and the JSON Schema validator built here are
  the contract it will depend on.

## Roadmap

- **v1 (current)** — core engine: compiled evaluation, interpreter fallback, tracing,
  schema + validator, benchmarks, net48 proof.
- **v2** — Newtonsoft adapter, function catalog/discovery API, rule versioning,
  rule-set-level short-circuit strategies, pluggable fact providers, hot-reload of
  rule JSON, NRules/RulesEngine benchmark comparison in `docs/benchmarks.md`.
- **v3** — Blazor drag-and-drop rule builder emitting/consuming this exact schema.
  From v1 on, changes to the `layout` contract or the JSON Schema are treated as
  breaking changes.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). By participating you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md).

## License

[MIT](LICENSE)
