---
name: verify
description: How to build, launch, and drive the Rulewright v2 drag-and-drop flow builder (custom canvas, dark theme) in a real browser.
---

# Verifying Rulewright.Sample.BlazorBuilder.v2

Blazor WebAssembly. As of the current design, the canvas is a **hand-rolled** node-graph editor
(no third-party library — earlier Drawflow-based iteration was fully replaced), styled to match
a supplied dark-theme mockup (copper/teal accents, JetBrains Mono + IBM Plex Sans). Same
"no server" caveat as v1: curl only proves the static shell serves, not that anything rendered —
always drive it in a real browser.

## Launch

```bash
cd samples/Rulewright.Sample.BlazorBuilder.v2
dotnet run --urls http://127.0.0.1:5312
```

Same Windows file-lock gotcha as the other samples: kill any leftover `dotnet run` via
`Get-NetTCPConnection -LocalPort 5312` / `Stop-Process -Force` before rebuilding.

## Architecture (matters for debugging)

The canvas/palette/wires/inspector/drawer/modal are **entirely JS-owned state**
(`wwwroot/js/rule-canvas.js`, a single IIFE exposing `window.rulewrightFlowBuilder.init(dotNetRef)`)
— there is no C# node/draft model like v1/the old v2 had. `Pages/Canvas.razor` is just the page
shell (static HTML matching the mockup) plus two `[JSInvokable]` bridge methods:
`ValidateRule(ruleJson)` and `EvaluateRule(ruleJson, factJson)`, both taking/returning JSON
strings, backed by the real `RulewrightEngine` (`Services/EngineService.cs`, unchanged from
before). JS builds the rule JSON client-side from its own node/connection graph
(`buildConditionTree`/`buildRuleJson` in rule-canvas.js) and calls these via
`dotNetRef.invokeMethodAsync(...)`.

**Trace-highlight mechanism**: `buildConditionTree` returns both the JSON condition tree AND a
parallel "id tree" of the same shape (node id in place of the JSON body). Since
`Rulewright.Execution.ConditionTraceBuilder` produces a `ConditionTraceNode` tree that mirrors
the parsed condition 1:1 (same order, same nesting), `applyTraceHighlight` zips the id tree and
the trace tree positionally — no string-matching heuristics. If node/wire pass-fail highlighting
ever looks wrong after a change to condition-building order, check that `buildConditionTree`'s
recursion order still exactly matches what gets sent to the engine.

## Known-good smoke flow (verified working, zero console errors)

1. Load `/`, wait for `#canvasWrap`. A seeded demo graph renders: Fact Input → Compare
   (`Customer.Age > 18`) → Set Output (`Discount = 10`), connected with orthogonal wires.
2. `#btnExport` → opens the bottom drawer's JSON tab; `#jsonOut` shows real rule-schema JSON
   (`{id, description, priority, enabled, condition, actions}`) — matches Rulewright's actual
   schema, not a mockup approximation.
3. `#btnValidate` → calls the real `RuleSetValidator` via the JSInvokable bridge; the Validation
   tab and a toast both reflect the real result.
4. Click a node (`.node`) → right-side Node Inspector shows its fields (Compare: field/operator/
   value; Set Output: target/value); editing them (`input`/`change` events) live-updates the
   node's on-canvas summary and the JSON tab.
5. Double-click a palette item (`.palette-item[data-type="..."]`) → drops a new node near canvas
   center; drag-and-drop from the palette onto the canvas works too (`dragstart`/`drop`).
6. `#btnTest` → modal pre-filled with the current sample fact JSON; `#testRun` calls the real
   `EvaluateRule` bridge. Verified both outcomes: a fact that satisfies `Customer.Age > 18`
   shows a green "✓ Rule fired — Discount=10" banner, green node/wire highlighting, and a Trace
   tab entry `Customer.Age GreaterThan 18 — passed`; a failing fact shows the red "✗ Rule did not
   fire" banner instead.
7. Selecting a node and pressing `Delete` removes it (and its connections); dragging from a
   node's output port (`.port-out`) to another node's input port (`.port-in`) wires them;
   clicking a wire selects it (delete-affordance `×` glyph at the wire's midpoint, or `Delete`
   key) removes it.

All of the above passed clean (zero console errors) in the current design.

## Examples picker + full schema coverage (added later, same design)

The toolbar's `#exampleSelect` dropdown (populated from `wwwroot/examples/manifest.json`, same
19 files as v1) fetches an example, then `importRuleJson(rule)` in rule-canvas.js clears the
canvas and rebuilds it as nodes: condition tree (Compare/Custom Function/AND/OR/NOT), all four
action types with a Then/Else branch each, and — when present — computed value-expressions
(`literal`/`field`/`op`) as chained `Literal`/`Field Ref`/`Expression` nodes wired into a
Compare's **Field (expr)** pin (the ONLY place a condition allows a computed value — its
right-hand comparison `value` must stay a constant, `RuleSetValidator` rejects an object there)
or an Action's **Value (expr)** pin. `rules`-set docs import only rule #1 with a toast noting
`N` total; `decisionTable` docs show a toast and leave the canvas untouched (not supported).

**Two real bugs found via this import path, both fixed** — worth re-checking after any change
to `importRuleJson`/`importValueExpr`/`importCondition`:
1. **Double-quoted string literals**: import populated a text field with `JSON.stringify(value)`,
   but `parseValue()` (the field's own reader) treats bare unquoted text as a string — so a
   string got wrapped in quotes that then round-tripped as `""gold""`. Fixed via a
   `valueToFieldText()` helper that only JSON-stringifies non-string values.
2. **Extra unwired operand/input slots** (e.g. 3 real operands rendering as 6 slots): import
   pre-grew `node.inputs` to the target length before calling `addConnection()` N times, but
   `addConnection()` *also* auto-appends a fresh empty slot on every call for a dynamic-input
   node (that's how manual click-to-wire keeps exactly one trailing empty slot) — the two growth
   mechanisms compounded. Fixed by removing the pre-growth entirely; sequential
   `addConnection(child, node, i)` calls for `i = 0, 1, 2, ...` naturally reproduce the correct
   "N filled + 1 trailing empty" invariant on their own. (After the fix, a node with 3 wired
   operands correctly shows 4 port rows — 3 filled + 1 empty invitation slot — not 3; that's
   correct, not a regression.)

Verified via Playwright: all 19 examples import and validate clean (zero console errors);
`07-computed-values.json`'s two `concat`/`multiply` expressions round-trip and evaluate for
real (`DiscountAmount=20`, `Message="Thanks Ada, you saved 10%!"` — confirms both the operand-
count fix and a related whitespace-preservation fix in `parseValue`, which previously trimmed
meaningful trailing spaces like `"Thanks "` out of string constants); `12-custom-function.json`
round-trips its `addToOutput`/`appendToOutput` actions and AND-grouped custom-function leaf
correctly; `08-arithmetic-operators.json` (26 nodes) loads without crashing; changing an
existing action's type via the Inspector's new Type dropdown (e.g. `setOutput` →
`addToOutput`) updates the exported JSON immediately.
