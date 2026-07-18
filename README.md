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
| **Action** | Writes a `value` (constant or computed) to the outputs at `target`. `setOutput` replaces, `addToOutput` sums, `appendToOutput` collects into a list. |

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

### Actions — constant and computed values

A `setOutput` action writes a `value` into the result's outputs. That value is a **bare
scalar** (a constant) or an **expression** computed from the fact at evaluation time — one
key, and a constant is simply the simplest expression:

```json
"actions": [
  { "type": "setOutput", "target": "Tier", "value": "gold" },
  { "type": "setOutput", "target": "Discount",
    "value": { "op": "multiply", "operands": [ { "field": "Order.Total" }, 0.1 ] } },
  { "type": "setOutput", "target": "Message",
    "value": { "op": "concat", "operands": [ "Thanks, ", { "field": "Customer.Name" }, "!" ] } }
]
```

Values are **pure data** — a closed operator vocabulary, never embedded code — so a UI can
generate them and a reviewer can diff them, exactly like conditions. A value is a bare scalar
(a literal), `{ "field": "<dotted path>" }`, `{ "literal": <scalar> }`, or
`{ "op": "<operator>", "operands": [ … ] }`:

| Category | Operators |
|---|---|
| Arithmetic | `add`, `subtract`, `multiply`, `divide`, `modulo`, `negate` |
| String | `concat` |
| Null | `coalesce` (first non-null operand) |

`add`/`multiply`/`concat`/`coalesce` take two or more operands; `subtract`/`divide`/`modulo`
take exactly two (order significant); `negate` takes one. Evaluation is **total** — it never
throws on data: any null operand propagates to a null result (except `coalesce`), a
non-numeric operand to an arithmetic operator yields null, and division or modulo by zero
yields null. Arithmetic runs in `decimal` unless a floating-point operand forces `double`, so
`divide` is never silently integer-truncated. Computed outputs are compiled to delegates and
cached per fact type just like conditions (constant-only rules keep their pre-materialized
outputs and allocate nothing per firing); field references in expressions are checked against
typed facts at compile time.

**Action types.** The action `type` decides how the value combines with what fired rules have
already written to that `target`, applied in priority order across the whole evaluation:

| Type | Effect |
|---|---|
| `setOutput` | Replaces the value at `target`. |
| `addToOutput` | Adds numerically — a running total across fired rules. |
| `appendToOutput` | Appends to a list at `target` — collected across fired rules. |

```json
"actions": [
  { "type": "addToOutput", "target": "RiskScore",
    "value": { "op": "multiply", "operands": [ { "field": "Order.Total" }, 0.1 ] } },
  { "type": "appendToOutput", "target": "Reasons", "value": "high-value order" }
]
```

The accumulators are **null-tolerant**: a value that resolves to null (and, for `addToOutput`,
any non-numeric value) contributes nothing rather than wiping the running result — so one stray
rule can't destroy a total. Each fired rule's own `Outputs` snapshot reflects the result *at
that rule's firing* (`appendToOutput` copies the list, so earlier snapshots stay frozen).

### Decision tables

For logic that reads naturally as a grid, a **decision table** is a compact authoring form.
Each input column maps a cell to a condition; each output column maps a cell to an action:

```json
{
  "decisionTable": {
    "hitPolicy": "first",
    "inputs": [
      { "field": "Customer.Tier", "operator": "Equals" },
      { "field": "Order.Total",   "operator": "GreaterThanOrEqual" }
    ],
    "outputs": [ { "target": "Discount" }, { "target": "Label" } ],
    "rows": [
      { "when": ["VIP", 100],  "then": [20, "vip-big-order"] },
      { "when": ["VIP", null], "then": [10, "vip"] },
      { "when": [null, null],  "then": [0,  "standard"] }
    ]
  }
}
```

A table **expands into ordinary rules at load time** — one rule per row — so it runs through
the exact same compiled/interpreted path with no special casing, and the same schema, tracing,
and hashing apply. A `null` input cell is a **wildcard**; an all-wildcard row is a **catch-all**.
Input columns default to `Equals` and take any comparison operator (`In` cells are arrays);
output columns default to `setOutput` and may use `addToOutput`/`appendToOutput`. A `null`
output cell skips that output for the row. Two hit policies:

- **`collect`** (default) — every matching row applies its actions in row order (pairs with
  `addToOutput` for scoring tables).
- **`first`** — only the first matching row applies. Encoded by ANDing each row with the
  negation of every earlier row's condition, so exactly one row fires under normal evaluation.

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

- **v1** — core engine: compiled evaluation, interpreter fallback, tracing,
  schema + validator, benchmarks, net48 proof.
- **v2 (current)** — *"rules do more."* Computed action expressions (arithmetic,
  `concat`, `coalesce` over fact fields), accumulating action types
  (`addToOutput`/`appendToOutput`), and decision-table authoring have shipped. Also on
  the track: Newtonsoft adapter, function catalog/discovery API, and the
  NRules/RulesEngine benchmark comparison in `docs/benchmarks.md`.
- **v3** — Blazor drag-and-drop rule builder emitting/consuming this exact schema.
  From v1 on, changes to the `layout` contract or the JSON Schema are treated as
  breaking changes.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). By participating you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md).

## License

[MIT](LICENSE)
