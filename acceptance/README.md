# Fluorine independent acceptance tests

`TESTS.md` is written from the spec only (`spec/semantics-cs.md` first, then `spec/semantics.md`). It is not derived from any Grok Build output, so Grok Build cannot make the list fit its own decisions. `SPEC-GAPS.md` lists the places where the spec is unclear. Do not count a test of a gap as either pass or fail until Werner decides.

## Checking a Grok Build run

1. **Map.** For every test in the build, find the TESTS.md ID it covers. Write the mapping in `runs/<run>/MAP.md` as `build test name -> ID(s)`. A build test may cover several IDs.
2. **Missing IDs.** Any ID with no mapped build test is *missing*. Either the build adds the test, or we run our own version against the build's public API.
3. **Verify.** For each mapped pair, check that the build test asserts the **Expected** column, not something weaker. Then run it, and set Status to `pass`, `fail`, `weak` (it asserts less than Expected), or `blocked (gap Gn)`. Keep per-run results in `runs/<run>/RESULTS.md`, and copy the latest Status here.
4. **Unspecified behaviour.** Flag every build test that maps to no ID, or that asserts more than Expected. Examples are exact log messages, a particular exception type where the spec allows two, member names the spec doesn't give, timing, `ExecutionCount` in release, behaviour listed as a non-goal in TESTS.md (moved out of the spec), and WinForms reattach after shutdown. Classify each one as:
   - *spec gap*: it is a real question, so add it to SPEC-GAPS.md and send it to Spec Clerk;
   - *invented behaviour*: it must not be a requirement, so the test is removed or kept as an implementation test outside the acceptance suite.
5. Use only the public API. Wait with the Barrier helper (see the TESTS.md harness), never with sleeps or a library flush, which is a non-goal.

## Maintenance

- IDs are stable. A changed rule updates its row. A removed rule marks the row `retired`. A new rule gets the next free `-xx` number.
- When the spec changes, check the diff of semantics*.md against the IDs of each changed section.
