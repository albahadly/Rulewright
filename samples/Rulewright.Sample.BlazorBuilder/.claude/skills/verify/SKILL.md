---
name: verify
description: How to build, launch, and drive the Rulewright Blazor WASM rule builder in a real browser.
---

# Verifying Rulewright.Sample.BlazorBuilder

This is a Blazor WebAssembly standalone app — there is no server-side logic to
curl; everything (parsing, validation, evaluation) runs client-side in the
browser's WASM runtime. curl/HTTP checks only prove the static files serve —
you must load it in an actual browser to observe behavior.

## Launch

```bash
cd samples/Rulewright.Sample.BlazorBuilder
dotnet run --urls http://127.0.0.1:5311
```

Wait for `Now listening on: http://127.0.0.1:5311`, then `curl -s -o /dev/null
-w "%{http_code}" http://127.0.0.1:5311/` should return 200 (page shell only —
this does NOT prove the WASM app rendered; see below).

**Windows lock gotcha**: a prior `dotnet run` left running will lock the
shared `Rulewright.*.dll` outputs and break `dotnet build` on the whole
solution with `MSB3021`/`MSB3027` copy errors. Find and kill it first:
`powershell -Command "Get-NetTCPConnection -LocalPort 5311 | Select-Object OwningProcess"`
then `Stop-Process -Id <pid> -Force`.

## Drive it (no browser tool in this environment — use Playwright via npx)

No headless-browser tool is wired into the harness here. `npx playwright`
and `npx playwright install chromium` work (no `--with-deps`, that needs
sudo/apt and isn't available on Windows anyway). Set up once per session in
the scratchpad:

```bash
cd <scratchpad>/pw && npm init -y && npm install playwright && npx playwright install chromium
```

Then a small Node script driving `chromium.launch()` → `page.goto(...)` →
`page.waitForSelector('#raw-json')` → interact via `page.locator(...)`. Key
selectors in the current UI: `#raw-json` (JSON textarea), `#example-select`
(example dropdown, `<option value>` = filename e.g. `06-nested-logic.json`),
`#fact-json` (fact textarea), `button:has-text("Evaluate")`,
`button:has-text("Apply")`, `.validation-panel`, `.evaluation-result`,
`.trace-view`.

**Always attach a `page.on('pageerror', ...)` / `page.on('console', ...)`
listener before `goto`.** A crashed Blazor render (e.g. an unhandled
exception in `WebAssemblyRenderer`) still returns HTTP 200 for the page
shell and the WASM boot log looks normal — the only sign is a
`console.error` from `Microsoft.AspNetCore.Components.WebAssembly.Rendering.WebAssemblyRenderer`
with a stack trace, and every `#`-id selector will then time out waiting to
become visible. That combination (selectors never appear + a renderer
console.error) is a real app bug, not a flaky wait — read the console
capture before assuming a timing issue.

## Known-good smoke flow

1. Load `/`, wait for `#raw-json`. Validation panel should read "Document is
   valid." for the default starter document.
2. Fill `#fact-json` with `{"Order":{"Total":150}}`, click Evaluate. Starter
   rule should fire with `Discount = 10` in both fired-rules and final
   outputs, trace shows the condition passed.
3. `page.selectOption('#example-select', '06-nested-logic.json')` — raw JSON
   updates, validation stays valid. Good regression case: this example has
   real AND/OR/NOT nesting, so evaluating it against 2-3 different facts
   (qualifies via the AND/NOT branch, disqualified by weight, qualifies via
   the OR branch's tier check) is a strong end-to-end check of both the
   condition-tree logic and the trace view — not just a smoke test.
4. Break the raw JSON (delete a closing brace), click Apply — validation
   panel should show a parse error, Evaluate button disabled, no crash.
5. Fix to parseable-but-schema-invalid JSON (e.g. drop `"id"`), click Apply —
   validation panel should show a JSON-pointer-anchored structural error
   (e.g. `(root) — 'id' is required.`), Evaluate still disabled.

All of the above passed clean (no console errors) as of the Phase B
milestone (JSON-only shell: raw JSON view, example picker, validation panel,
fact editor, evaluate/trace panel — no visual condition/action tree editor
yet, that's Phase C/D).

## Phase C: driving the visual condition-tree editor

Selectors: `.condition-group` (a group node), `.condition-leaf` (a leaf
node's inputs), `.condition-group-header select` (AND/OR/NOT picker),
`.condition-group-actions button:has-text("+ Condition")` /
`:has-text("+ Group")`, group's own delete = `button:has-text("Remove
group")` (rendered in the group's own header), leaf's own delete =
`.remove-btn` next to that leaf's inputs.

**Selector gotcha that cost real debugging time**: `.condition-node:has(.condition-leaf)`
matches every ANCESTOR group too, not just leaf nodes — `:has()` checks
descendants at any depth, and a group's `.condition-node` wraps its entire
subtree including nested leaves. A test that indexed into that locator to
find "the newly added leaf" was picking up group wrapper nodes interleaved
with real leaves, and looked like a leaf-removal bug (wrong node vanishing)
that was actually a test-script bug. Use
`.condition-node:not(:has(.condition-group))` to get leaf-only nodes.

**Document order matches visual nesting for the "+ Condition"/"+ Group"
and "Remove group" buttons** — a group's header (and its own Remove
button) renders before its children in the markup, so `.first()` on any of
those locators reliably means "the outermost/first group encountered",
not "the last one added". Verified: add a leaf to a specific nested group,
find it by its empty `field` input, remove it via its own `.remove-btn` —
document round-trips back to byte-for-byte the same structure as before
the add (confirmed against `examples/06-nested-logic.json`'s AND/OR/NOT
tree, including through a full JSON round-trip since every edit rebuilds
the whole draft tree from the serialized document, not just a partial
patch).

## Phase D: action list + value-expression editor

Selectors: `.action-list` (one per `actions`/`else`, in that order —
`.action-list button:has-text("+ Action")` / `page.locator('.action-list').nth(0|1)`
to disambiguate), `.action-editor` (one row per action; its own first
`select` is the action-type picker, first `input` is the target),
`.value-expr` (one per expression node — the literal/field/operator kind
picker is `.value-expr > select`, i.e. the DIRECT-child select, since a
nested operator's own operands are also `.value-expr` and would otherwise
match too broadly), `.operand-list .value-expr` for an operator's operands
specifically.

Full flow verified: switch a `setOutput` value from a literal to a
`multiply` operator expression, set operands to `{field: Order.Total}` and
literal `0.2`, evaluate with `Order.Total=250` → `Discount=50.0` (correct,
and picked "Interpreted" compilation mode as expected for dictionary
facts). Add/remove a second action (`appendToOutput`), add an `else`
action and confirm it fires (`Branch: Else`) when the condition fails.
Zero console errors throughout. Also swept all 19 files in `examples/` via
the example picker: every one validates clean and loads without a console
error; the visual condition/action editors correctly show for single-rule
documents and correctly fall back to "edit via raw JSON" for `rules` sets
and `decisionTable` documents (by design — out of scope for this
milestone's tree editor).
