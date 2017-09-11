## Concurrent Patterns

A library intended to abstract away common patterns
In async code while maintaining good performance.

#### AsyncPoller
Schedule a repeatable task with a regular interval between runs.

#### AsyncJob
A callback which will run unless actively delayed or executed immediately.

#### AsyncTaskQueue
A dumbed down producer-consumer pattern.