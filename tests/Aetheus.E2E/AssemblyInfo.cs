// SPDX-License-Identifier: EUPL-1.2
using NUnit.Framework;

// The development Blazor static-asset server used by ylaunch is deliberately lightweight. Parallel
// browser bootstraps have intermittently dropped individual fingerprinted WASM responses, which then
// strands otherwise unrelated tests on a spinner. Production concurrency belongs to load tests; this
// functional suite must execute each isolated browser scenario against a stable application state.
//
// PLAN-007 lot 6: fixtures may now run side by side, but only as many at once as there are workers.
// LevelOfParallelism(1) keeps one worker by default, so ylaunch and any run that does not ask for
// more stay sequential exactly as before. QA can ask for more through NUnit.NumberOfTestWorkers
// (AETHEUS_E2E_WORKERS in aetheus-qa.yaml, passed by run-qa-e2e-suite.sh), which takes precedence.
// Fixtures that share state across tests keep [NonParallelizable].
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(1)]
