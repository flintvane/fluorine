# Build-test map

Flagged: 28

Each row is one `[Fact]` in `tests/Fluorine.Tests` (154). Mapping uses the test name and its assert statements. Flag is `unspecified behaviour` when the test maps to no acceptance ID, or when an assert goes beyond the Expected column of the IDs it covers.

| build test | IDs covered | flag |
| --- | --- | --- |
| Rule_1_1_RegisterRemembersReaders | cs1.1-01 | |
| Rule_1_2_RegisterTHoldsTypedValue | cs1.7-01 | |
| Rule_1_3_RecordReadSubscribesCurrentWhenRecording | cs1.1-01 | |
| Rule_1_4_TriggerReactionsTriggersConnected | cs1.4-01 | |
| Rule_1_5_RecordReadAndTriggerFromAnyThread | cs1.5-01 | |
| Rule_1_6_EmptyConstructorUsesDefaultT | cs1.6-01 | |
| Rule_1_7_InitialConstructorDoesNotTrigger | cs1.7-01, cs1.7-02 | |
| Rule_1_8_ValueGetterRecordsReadSetterWrites | cs1.8-01, cs1.8-02 | |
| Rule_1_9_NameIsSettableAndNoNameConstructor | cs1.9-01 | unspecified behaviour: asserts Name reads back "alpha" and "beta"; cs1.9-01 only requires that set does not throw and that no string constructor exists |
| Rule_1_10_NameIsRetained | — | unspecified behaviour: no acceptance ID; asserts Name is still "kept" |
| Rule_1_11_RegistersCanBeInheritedAndCoreMembersAreNotVirtual | cs1.11-01 | |
| Rule_1_12_BackingValueIsProtected | cs1.12-01 | |
| Rule_1_13_ConstructorsAndValueFromAnyThread | cs1.13-01 | |
| Rule_1_14_SetValueReturnsWhetherChanged | cs1.14-01, cs1.14-02 | |
| Rule_1_15_SetValueOutOldValueDoesNotRecordRead | cs1.15-01, cs1.15-02, cs1.15-03 | |
| Rule_1_16_EqualityIsObjectEqualsNotIEquatableAlone | cs1.16-01, cs1.16-02 | |
| Rule_1_17_EqualityExceptionReachesCallerNothingStored | cs1.17-01 | |
| Rule_1_18_LargeStructAssignmentDoesNotTear | cs1.18-01 | |
| Rule_2_1_ReactorQueueUsesSchedulerAndDisposedDrops | cs2.1-01, cs2.1-02 | |
| Rule_2_4_ReactorOnShuttingDownSchedulerIsDisposed | cs2.4-01, cs2.4-02 | |
| Rule_2_5_NameIsSettableNoNameConstructor | cs2.5-01 | unspecified behaviour: asserts Reactor.Name reads back "r"; cs2.5-01 only requires that set works and that the only public constructors are Reactor() and Reactor(Scheduler) |
| Rule_2_6_ReactionHasNoPublicConstructor | cs2.6-01 | |
| Rule_2_7_AddQueuesFirstRunAndReturnsReaction | cs2.7-01 | unspecified behaviour: asserts Reaction.Name is "n"; that read-back is cs2.7-02 and only applies in diagnostics mode |
| Rule_2_8_ReactionNeverRunsInline | cs2.8-01 | |
| Rule_2_9_AddOnDisposedReturnsNull | cs2.9-01 | |
| Rule_2_10_ReactionAlwaysHasAReactor | — | unspecified behaviour: no acceptance ID; asserts a reaction is non-null under "always has a reactor" |
| Rule_2_11_DisposingReactorDisposesReactions | cs2.11-01 | |
| Rule_2_12_DisposingReactionRemovesItFromReactor | cs2.12-01 | |
| Rule_2_13_NullLambdaIsEmptyAndFixedAtCreation | cs2.13-01 | unspecified behaviour: waits until ExecutionCount >= 1; cs2.13-01 only requires a non-null reaction, no exception, and nothing logged |
| Rule_2_14_LambdaCanBeCalledOutsideReaction | cs2.14-01 | |
| Rule_2_15_TriggerQueuesAndNeverThrowsOnDisposed | cs2.15-01, cs2.15-03 | |
| Rule_2_16_NameIsTheNameGivenToAdd | cs2.16-01 | unspecified behaviour: asserts Name is "named" with no diagnostics mode; cs2.16-01 checks that read-back only in diagnostics mode |
| Rule_2_17_DisposeNullsLambdaAndDetaches | cs2.17-01 | |
| Rule_2_18_CurrentIsRunningReaction | cs2.18-01, cs2.18-02 | |
| Rule_2_19_PauseRecordingIncrementsPauseCount | cs2.19-01 | |
| Rule_2_21_PauseOutsideReactionHasNoEffect | cs2.21-01 | |
| Rule_2_22_RecordingTrueOnlyWhenUnpausedReaction | cs2.22-01, cs2.22-02 | |
| Rule_2_23_PauseCountEndsWithTheRunningReaction | cs2.23-01 | |
| Rule_2_24_PauseWrappersAreReentrant | cs2.24-01, cs2.24-02, cs2.24-04 | |
| Rule_3_1_SchedulerIsAbstract | cs3.1-01 | |
| Rule_3_2_QueueReactionNeverInlineNeverThrowsAfterShutdownSilent | cs3.2-01, cs3.2-02 | |
| Rule_3_5_RunRecordsReadsThenDisconnectsUnread | cs3.5-01 | |
| Rule_3_6_CurrentIsThreadStaticAndClearedAfterShutdown | cs3.6-01, cs3.6-02, cs3.6-04, cs3.13-03 | |
| Rule_3_7_BaseClassHasNoRun | cs3.7-01 | |
| Rule_3_9_SchedulersAreAnnouncedToReactive | cs3.9-01 | unspecified behaviour: asserts membership in Scheduler.SnapshotAll; cs3.9-01 only requires both schedulers finished |
| Rule_3_10_OneSchedulerServesManyReactors | cs3.10-01 | |
| Rule_3_11_ReactiveShutdownInformsAllThenWaits | cs3.11-01 | unspecified behaviour: asserts internal IsFinished; cs3.11-01 requires drained work and ForThread null |
| Rule_3_12_SchedulersDoNotInterfere | cs3.12-01 | |
| Rule_3_13_ForThreadAndAttachOtherTypeThrows | cs3.13-01, cs3.13-04 | |
| Rule_3_14_SchedulerOfAThreadStaysTheSame | cs3.14-01 | |
| Rule_3_15_AtMostOneReactionPerThread | cs3.15-01 | |
| Rule_4_1_NoPublicConstructorAndNoAttachToCurrent | cs4.1-01 | |
| Rule_4_2_CreateNewRunsInitialThenLoop | cs4.2-01, cs4.2-02, cs4.2-04 | cs4.2-04: the original exception is delivered once through UnhandledException; CreateNew returns the scheduler and the loop keeps running |
| Rule_4_3_QueueNeverBlocksAndShutdownEndsThread | cs4.3-01, cs4.3-02 | |
| Rule_5_1_NoPublicConstructor | cs5.1-01 | |
| Rule_5_2_CreateNewRunsInitialThenDispatcher | cs5.2-01 | |
| Rule_5_3_AttachToCurrentReturnsExisting | cs5.3-01, cs5.3-02 | |
| Rule_5_4_QueueDoesNotBlockAndShutdownFollows38 | cs5.4-01 | |
| Rule_6_1_AttachToCurrentRequiresWinFormsContext | cs6.1-01, cs6.1-02, cs6.1-03 | |
| Rule_6_2_UserContextIsNotSupported | cs6.2-01 | |
| Rule_6_3_QueueNeverBlocksAndShutdownDoesNotEndLoop | cs6.3-01, cs6.3-02 | |
| Rule_7_1_UnhandledExceptionEventAndConsoleFallback | cs7.1-01, cs7.1-02 | |
| Rule_7_2_ReactiveExceptionConstructorsAndInner | cs7.2-01 | |
| Rule_7_3_ProgrammingErrorsAreLogged | cs7.3-01 | |
| Rule_7_4_ExecutionCountIncreasesAndDoesNotAffectBehaviour | cs7.4-01, cs7.4-02 | |
| Rule_7_5_DiagnosticsModeIsImplementationDefined | — | unspecified behaviour: no acceptance ID; asserts Name reads back "diag" |
| Rule_1_1_EventLoopProcessesOneAtATime | s1.2-01, s11.7-01 | |
| Rule_1_2_ReactionPinnedToCreatingScheduler | cs2.2-01 | |
| Rule_1_3_RegistersAreNotPinned | cs1.13-01 | |
| Rule_1_4_TriggerQueuesAndCoalesces | s1.4-01 | |
| Rule_1_5_DifferentSchedulersRunInParallel | s1.5-01 | |
| Rule_1_6_RunIsSynchronous | — | unspecified behaviour: no acceptance ID; requires synchronous order "12" |
| Rule_1_8_TriggeringNeverThrows | s1.8-01 | |
| Rule_1_9_TrackingIsPerThread | s1.9-01 | |
| Rule_3_1_ObjectEquals | cs1.16-01 | |
| Rule_3_1a_DefaultValue | cs1.6-01 | |
| Rule_3_1b_CompareThenStoreThenTrigger | s3.1b-01 | |
| Rule_3_1c_EqualityThrowStoresNothing | cs1.17-01 | |
| Rule_3_2_NoChangeNoTrigger | cs1.8-03 | |
| Rule_4_1_RunsAtLeastOnceAfterLastModification | s4.1-01 | |
| Rule_4_2_IntermediateValuesMayBeSkipped | — | unspecified behaviour: no acceptance ID; requires an intermediate value to be skipped (last == 3), which the spec only permits |
| Rule_4_4_QueuedAtMostOnceAndTriggerDuringRunQueuesOneMore | s4.4-01 | |
| Rule_4_5_ManualRegisterTriggerIgnoresEquality | cs1.4-02 | |
| Rule_4_6_ManualReactionTrigger | s4.6-01, cs2.15-01 | |
| Rule_6_1_SelfWriteRetriggers | s6.1-01 | |
| Rule_6_3_PausingAppliesToReadsOnly | s6.3-01 | |
| Rule_7_1_SubscribeBeforeValueReturned | cs1.8-01 | |
| Rule_7_2_StoreThenTrigger | cs1.8-02, s7.2-01 | |
| Rule_7_3_DependencyMaintenance | cs3.5-01 | |
| Rule_7_3a_PausedReadDoesNotKeepSubscription | s7.3a-02 | |
| Rule_7_4_ReactionNeverRunsReentrantly | s7.4-01 | |
| Rule_7_5_TriggeringDoesNotSubscribe | s7.5-01 | |
| Rule_8_1_ExceptionEndsRunKeepsReadsUpToThrow | cs3.5-02 | |
| Rule_8_2_ReactionDoesNotStopTheLoop | s8.2-01 | |
| Rule_9_1_PausedReadsDoNotSubscribe | cs1.3-02, cs2.19-01 | |
| Rule_9_2_PauseIsReentrant | cs2.24-04 | |
| Rule_9_3_UnbalancedPauseEndsWithReaction | cs2.23-01 | |
| Rule_10_1_PlainSynchronousRun | — | unspecified behaviour: no acceptance ID; waits until a run's depth is positive |
| Rule_11_2_ReactorCapturesCreatingThreadScheduler | cs2.2-01 | |
| Rule_11_5_SameLoopFirstRunAfterCreatorFinishes | s11.5-01 | |
| Rule_11_7_TwoReactionsOnSameLoopNeverConcurrent | s11.7-01 | |
| Rule_11_9_FirstComeFirstServed | s11.9-01 | |
| Rule_11_10_WritesTakeEffectImmediately | s11.10-01 | |
| Rule_11_11_WritesOnOneLoopSeenTogether | s11.11-01 | |
| Rule_11_15_ShutdownDrainsThenDisposesAndRefusesNew | cs3.8-02, cs3.8-04, s11.15-01 | |
| Rule_11_16_ShutdownFromAnyThreadAndNotFromReaction | cs3.8-05 | unspecified behaviour: asserts internal IsFinished; cs3.8-05 only requires that Shutdown returns and the scheduler is finished |
| Rule_11_17_ReactiveShutdownAll | cs3.11-01 | unspecified behaviour: asserts internal IsFinished on both schedulers; cs3.11-01 requires drained work and ForThread null |
| Rule_12_5_DisposeRemovesSubscriptions | cs2.17-01 | |
| Rule_12_6_QueuedRunDoesNotExecuteOriginalLambda | cs2.17-04, s12.6b-01 | |
| Rule_12_8_ValuelessRegisterIsPureSignal | cs1.1-01, cs1.4-01 | |
| Rule_12_9_NullActionIsNoAction | s12.9-01, cs2.13-01 | |
| Rule_12_12_AddDuringDisposeCreatesNothing | cs2.9-01, cs2.4-01 | |
| Rule_13_1_NoTearing | cs1.18-01 | |
| Rule_13_3_QueuedAtMostOnceFromManyThreads | s13.3-01 | |
| Rule_13_8_WritersDoNotWaitForReaction | s13.8-01 | |
| Rule_15_1_UnhandledFromReactionIsLogged | cs7.2-02 | |
| Rule_5_1_GlitchableAcrossRegisters | — | unspecified behaviour: no acceptance ID; glitching across registers is only permitted, not required |
| Rule_5_3_AggregateValueIsAtomic | — | unspecified behaviour: no acceptance ID; asserts an aggregate register value is (2, 2) |
| Rule_5_4_DerivedValueAfterSchedulerTurn | s5.4-01 | |
| Rule_6_2_NoCycleDetectionEndlessRetriggerIsPossible | — | unspecified behaviour: no acceptance ID; cycle detection is a non-goal, and the test requires a retrigger loop to reach 5 |
| Rule_6_4_CreatingReactionsInsideAReaction | s6.4-01 | |
| Rule_11_1_ReactionStaysOnItsThread | s11.1-01 | |
| Rule_11_3_UserWorkIsNotAReaction | cs3.3-03 | |
| Rule_11_6_OtherLoopMayRunBeforeCreatorFinishes | — | unspecified behaviour: no acceptance ID; requires the other loop to run before the creator finishes, which the spec only permits |
| Rule_11_8_NoTwoReactionsInsideEachOther | s7.4-01 | |
| Rule_11_12_NoCrossThreadWriteBundle | — | unspecified behaviour: no acceptance ID; cross-thread write bundling is only a missing guarantee, and the test pins values 1 and 2 |
| Rule_11_13_NoAtomicMultiRegisterWrite | — | unspecified behaviour: no acceptance ID; multi-register atomicity is only a missing guarantee, and the test pins a value of 1 |
| Rule_12_1_StrongReferencesBothWays | — | unspecified behaviour: no acceptance ID; asserts the reaction still runs after other references are dropped |
| Rule_12_2_ReactionBelongsToOneReactor | — | unspecified behaviour: no acceptance ID; asserts a reaction is non-null under "belongs to one reactor" |
| Rule_12_3_ReactorDisposeDisposesAll | cs2.11-01 | |
| Rule_12_4_ReactorAssociatedWithOneScheduler | cs2.2-03 | |
| Rule_12_7_DisposingReactorAppliesToEveryReaction | cs2.11-01 | |
| Rule_12_10_LambdaCannotBeChanged | cs2.13-02 | |
| Rule_12_11_SameCodeAsOrdinaryCallRecordsNothing | cs2.14-01 | |
| Rule_13_2_RegisterCriticalSectionSerializesSubscribeAndTrigger | — | unspecified behaviour: no acceptance ID; register critical-section serialisation is not an acceptance row |
| Rule_13_7_LambdaDoesNotRunUnderQueueLock | s13.7-01 | |
| Rule_15_3_ExecutionCounter | cs7.4-01 | |
| Rule_15_4_NamedReactionsAndRegisters | — | unspecified behaviour: no acceptance ID; asserts Register.Name "reg" and Reaction.Name "rx" outside diagnostics mode |
| Rule_8_1_AsyncLambdaIsOrdinaryAfterAwait | cs8.1-01, cs8.1-02 | |
| Rule_8_2_ShutdownDetachesWithoutQueuedRun | cs3.8-02, s11.15-01 | |
| Rule_7_1_HandlerThrowWritesWarningAndContinues | cs7.1-03, cs7.1-04 | |
