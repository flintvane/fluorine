# Questions

Rows and build tests that cannot be brought into line without changing the spec, the library, or an existing test. The tests named here are unchanged.

## Build tests that contradict the spec

`cs1.10` and `cs2.16`: `Register.Name` and `Reaction.Name` need not be retained when diagnostics mode is off. `cs7.5`: what diagnostics mode is, and how it is switched on, is implementation-defined. This build documents no switch. A conforming implementation that does not retain names would fail these tests:

- `Rule_1_9_NameIsSettableAndNoNameConstructor` reads `Name` back as `"alpha"` and `"beta"`. `cs1.9-01` only requires that the set does not throw and that no string constructor exists.
- `Rule_1_10_NameIsRetained` requires `Name` to stay `"kept"`.
- `Rule_2_7_AddQueuesFirstRunAndReturnsReaction` requires `Reaction.Name == "n"`. That read-back is `cs2.7-02`, and only in diagnostics mode.
- `Rule_2_16_NameIsTheNameGivenToAdd` requires `Name == "named"` with no diagnostics mode.
- `Rule_7_5_DiagnosticsModeIsImplementationDefined` requires `Register.Name == "diag"`.
- `Rule_15_4_NamedReactionsAndRegisters` requires `Register.Name == "reg"` and `Reaction.Name == "rx"`.

`cs7.4`: `Reaction.ExecutionCount` may be compiled only into debug builds. These tests require the member, so they would not compile against a release library that omits it:

- `Rule_2_13_NullLambdaIsEmptyAndFixedAtCreation` waits until `ExecutionCount >= 1`.
- `Rule_7_4_ExecutionCountIncreasesAndDoesNotAffectBehaviour`.
- `Rule_15_3_ExecutionCounter`.

## Acceptance rows with no public test

`cs4.2-07` and `cs5.2-06` say the bring-up failure is not testable through the public API: there is no public way to make `CreateNew` fail, and no fault-injection point. `acceptance/SPEC-GAPS.md` assigns no G number to that gap (`G6` is whether `ThreadScheduler` has `Run()`, and `G7` was the initial-action exception, which the spec now resolves). Each ID has one skipped test. The skip reason is `not testable through the public API (spec gap)`, because there is no `Gn` to cite. They are not asserted.
