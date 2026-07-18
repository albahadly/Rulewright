# Rulewright Architecture

## Layering

```
Rulewright.Json.SystemText ──┐
Rulewright.Json.Newtonsoft ──┤ (adapters: JSON text → neutral DOM)
                             ▼
                 Rulewright.Serialization      Rulewright.Core
                 (DOM, parser, validator,  ──► (domain model, results,
                  canonical hash)               IRuleFunction)
                             ▲
                             │
                 Rulewright.Execution
                 (compiler, interpreter, cache,
                  RulewrightBuilder / RulewrightEngine)
```

- **Core** is pure domain model — no I/O, no JSON, zero dependencies, immutable after
  construction.
- **Serialization** defines a minimal neutral JSON tree (`RuleJsonValue`) plus
  `IRuleJsonReader`. Adapter packages translate their library's DOM into it; that is
  the *only* thing an adapter does. This keeps `netstandard2.0` consumers free to pick
  System.Text.Json, Newtonsoft, or anything else.
- **Execution** owns everything that runs: the expression-tree compiler, the
  dictionary-fact interpreter, the compiled-delegate cache, and the public
  builder/engine API.

## The evaluation pipeline

1. **Parse once** — `LoadRuleSet(json)`: adapter → neutral DOM → structural
   validation (JSON-pointer errors) → immutable `RuleSet`.
2. **Prepare once** — per rule: content hash, pre-sorted evaluation order
   (priority desc, document order for ties), prematerialized action outputs,
   pre-order condition-node index (for tracing).
3. **Compile once per fact type** — first `Evaluate<TFact>` compiles the rule into
   two delegates (see *Tracing*) and caches them.
4. **Execute many** — subsequent evaluations are direct delegate invocations.

## Compiled-delegate cache

- Key: `(fact CLR type, rule content hash)` in a `ConcurrentDictionary` — safe for
  concurrent readers, lock-free on the hot path.
- The content hash (`RuleHasher`) is a SHA-256 over a canonical rendering of exactly
  the compilation-relevant content: the **condition tree and actions**, with sorted
  keys and invariant number formatting. It is insensitive to whitespace, key order,
  `layout`, `id`, `description`, `priority`, and `enabled` — none of which change the
  compiled delegate — so canvas edits and metadata changes never trigger recompiles,
  while any semantic change always does. Two rules with identical bodies share one
  compiled delegate.

## Compilation strategy

`RuleExpressionCompiler` builds one `Expression<Func<TFact, bool>>` per rule:

- **Field paths** become chained member accesses with block-scoped locals, so each
  segment is evaluated exactly once. Reference-type and `Nullable<T>` links get null
  guards that short-circuit into the operator's null semantics (below) instead of
  throwing `NullReferenceException`.
- **Constants are converted at compile time** to the field's exact CLR type
  (`ValueConverter`): JSON `18` (long) becomes `int 18` for an `int` field; ISO
  strings become `DateTime`/`DateTimeOffset`/`TimeSpan`/`Guid`; strings become enum
  members. Value-type comparisons therefore run unboxed. Conversion failures surface
  as `RuleCompilationException` with the rule id and field path.
- **Numeric comparisons**: if the constant converts exactly to the field type, the
  comparison runs in that type (fast path). Otherwise (`int` field vs `10.5`) both
  sides widen once to `decimal` (or `double` when binary floating point is involved).
  Exact equality against a non-representable constant folds to a compile-time
  `false`/`true`.
- **Strings** are ordinal: `Contains`/`StartsWith`/`EndsWith` with
  `StringComparison.Ordinal`, ordering via `Comparer<string>` (ordinal). `MatchesRegex`
  embeds a `RegexOptions.Compiled` regex constructed at rule-compile time.
- **`In`/`NotIn`** build a typed `HashSet<T>` once at compile time and emit a
  `Contains` call.
- **Custom functions** are looked up in the registry at compile time and embedded as
  constants — the compiled delegate calls `IRuleFunction.Evaluate` directly, no
  per-evaluation name resolution.

## Null semantics

Identical across compiled and interpreted paths, exercised by shared tests:

| Situation | Result |
|---|---|
| `IsNull` on null field/path | `true` |
| `IsNotNull` on null field/path | `false` |
| `Equals` with `null` comparand, null field | `true` |
| `NotEquals` with non-null comparand, null field | `true` |
| `NotIn` with null field | `true` |
| Any other operator with a null field/path | `false` |
| `custom` with a null path | function is called with `null` fieldValue |

Missing members on **typed** facts are compile-time errors; missing keys on
**dictionary** facts resolve to null (there is no compile-time shape to check).

## Tracing with zero disabled-cost

Each rule compiles **two** delegates from the same expression builder:

- the fast path: `Func<TFact, bool>` — no tracing artifacts at all;
- the traced path: `Func<TFact, bool?[], bool>` — every condition node's boolean
  outcome is additionally written into a slot array (`results[i] = (bool?)node`).

Nodes are indexed by a pre-order walk (`ConditionNodeIndexer`) shared by the compiler,
the interpreter, and `ConditionTraceBuilder`, which re-hydrates the `bool?[]` into a
`ConditionTraceNode` tree after evaluation. Slots left `null` mean "short-circuited,
never evaluated" and surface as `Passed == null`. `EvaluationOptions.EnableTrace`
selects which delegate runs — tracing off costs nothing beyond an untraced call.

## Interpreter fallback

Dictionary facts (`IDictionary<string, object>`, nested dictionaries, POCOs inside
dictionaries via cached reflection) cannot be typed at compile time, so
`RuleInterpreter` walks the condition tree directly with `RuntimeComparisons`
mirroring the compiled semantics. Results report
`CompilationMode.Interpreted` — the throughput difference is documented and measured
in the benchmark suite rather than hidden.

## Thread safety

- `RulewrightEngine` is immutable after `Build()`; the delegate cache is a
  `ConcurrentDictionary`.
- `LoadedRuleSet`, `RuleSet`, and every result type are immutable.
- Compiled delegates and interpreter state are stateless per call; per-evaluation
  buffers (`bool?[]`, output dictionaries) are method-local.
- `IRuleFunction` implementations are shared across concurrent evaluations and must
  be thread-safe (documented on the interface).

## Multi-targeting

Libraries target `netstandard2.0;net8.0`. Everything is written against the
netstandard2.0 API surface; C# language features used are syntax-only (no
`IsExternalInit`, no `System.Index`). The net48 leg is enforced three ways: the test
projects run on net48, the `Rulewright.Sample.NetFramework48` smoke test runs in CI,
and the adapter's netstandard2.0 build takes the System.Text.Json 8.x package only
for that target (net8.0 uses the in-box copy).
