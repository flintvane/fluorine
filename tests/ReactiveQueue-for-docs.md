# ReactiveQueue — note for the doc drafter

The implementation is `src/Fluorine/Collections/ReactiveQueue.cs`. The tests that pin it are `tests/Fluorine.Tests/ReactiveQueueTests.cs`. This note says what those tests already decided, so the topic page and the spec section can follow them.

Suggested place in the guide: topic 18, after reactive lists, shifting derived values and everything after it by one. It is a data queue. It is not the scheduler work queue.

There is no `Version`, no `Value`, no `Diff`, no `Count`, no `Contains`, and no enumeration. `ReactiveQueue<T>` derives from `Register`, not from `Register<T>`. `ThereIsNoPicture` locks that shape. Do not invent a picture API to match the set, dictionary, array, or list.

## Contract the tests lock

- `Push` adds at the tail. `Pop` and `TryPop` take from the head. Order is FIFO, including duplicates and null. The same reference can sit in the queue twice and both pops return that instance (`PushPopAndTryPopAreFifo`).
- `TryPop` returns false when empty and sets the out value to `default(T)`. That includes a stored `0`: false means empty, true means a zero was stored. `Pop` on an empty queue throws `InvalidOperationException` with the message `The queue is empty.`
- `new ReactiveQueue<T>()` and `new ReactiveQueue<T>(IEnumerable<T>)`. A null sequence throws `ArgumentNullException`. Construction stores the items and triggers nothing. No scheduler is required to create, push, or pop (`ConstructorStoresTheSequenceAndRejectsNull`).
- Elements are not compared. `Equals` and `GetHashCode` are never called (`EqualityIsNotConsulted`). An equal value is still another element, and pushing it onto an empty queue wakes a waiting pop (`AnEqualElementIsStillANewHead`). Nothing in `equality.md` about stable hash codes applies. One sentence there is enough: a queue does not compare its elements.
- `Pop` and `TryPop` are the reactive reads. Both subscribe to whether a new element is available at the head, including a pop that finds the queue empty. `Push` does not subscribe.
- A push notifies listeners only when the queue was empty, because that element is the new head. A push behind an existing head does not notify. The later element is delivered when a pop uncovers it.
- A successful pop notifies listeners only when another element is then at the head. Popping the last element does not notify: nothing new is available.
- One pop per run therefore takes a buffered queue one element per run and stops after the last element. There is no extra empty run (`OnePopPerRunTakesBufferedItemsWithoutAnEmptyFollowUp`, `WaitingTryPopRerunsWhenPushMakesAHead`).
- A drain loop (`while (TryPop)`) takes every element in the run that finds them. If one of those pops uncovers a successor, that notification schedules one follow-up run, and that run observes an empty head. Triggers coalesce, so the follow-up is one run, not one per element. A second pump does nothing. A later push of several items does the same thing again: one draining run plus one empty follow-up (`DrainLoopFollowsUpOnceWhenAPopUncoversTheNextHead`). This follow-up is specified. It is what a one-pop reaction uses as its next run, seen from a loop that already took the uncovered element.
- Subscription is per run, same as any other register. A run that does not call `Pop` or `TryPop` drops the watch, and later pushes do not wake it. The items stay in order (`SkippingPopDropsTheHeadWatch`). `Dispose` does the same (`DisposingTheReactionDropsTheHeadWatch`).
- `Reaction.Pause` around a pop still takes the element and does not subscribe. A later push does not wake that reaction (`PausedPopConsumesAndDoesNotSubscribe`). The other `Pause` shapes use the same recording flag; one test covers the rule.
- `Pop` on an empty queue subscribes and then throws. Inside a reaction the exception reaches `Reactive.UnhandledException` unwrapped, as `InvalidOperationException`, not `ReactiveException`. A later push reruns the reaction, `Pop` returns that element, and it does not throw again (`PopOnEmptySubscribesThenThrows`).
- A pop from outside a reaction is an ordinary consumer. It removes the head the reaction would have taken (`OutsidePopRemovesTheHeadTheReactionWouldHaveTaken`). Two reactions that each pop at most one item per run share the items: each pushed value is taken once (`CompetingPopsShareTheItems`). Which reaction takes which item follows scheduler order and is not part of the contract.
- Push and pop are safe from any thread. One reaction keeps up with producers on other threads: nothing is lost, nothing is delivered twice, and each producer's own pushes stay in order (`AReactionKeepsUpWithProducersOnOtherThreads`). A struct element is copied whole under the queue lock (`ConcurrentPushAndPopDoesNotTearAStruct`).
- Order inside one run matters. `TryPop` that finds empty, then `Push`, subscribes first; the push sees a listener and schedules the follow-up, which takes that element (`PushFromASubscribedEmptyPopRerunsAndDeliversThatElement`). `Push` before the reaction has popped in that run notifies nobody, because `Push` itself does not subscribe; if the same run then pops the element, no follow-up is queued (`PushBeforeAnyPopDoesNotRescheduleThatRun`).

## Pages to touch when drafting

- New topic page, plus the previous/next lines, `docs/index.md`, `README.md`, and `mkdocs.yml`.
- The closing list in `docs/topics/signal-registers.md`.
- One sentence in `docs/topics/equality.md`.
- A new section in `spec/semantics-cs.md`, after the list rules. The sentences above are the behaviour to specify.

## Not worth a new test

- No `Peek`, `Clear`, `Count`, indexer, or comparer. Absence is the API.
- A reaction that pushes on every run and also pops can queue itself forever, the same cycle as writing a register it reads. The two push-order tests already show when a push does and does not schedule a follow-up.
- `TriggerReactions` is still public because the queue is a `Register`. Callers use `Push`, `Pop`, and `TryPop`.
