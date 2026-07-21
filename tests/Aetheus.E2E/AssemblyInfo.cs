// SPDX-License-Identifier: EUPL-1.2
using NUnit.Framework;

// The development Blazor static-asset server used by ylaunch is deliberately lightweight. Parallel
// browser bootstraps have intermittently dropped individual fingerprinted WASM responses, which then
// strands otherwise unrelated tests on a spinner. Production concurrency belongs to load tests; this
// functional suite must execute each isolated browser scenario against a stable application state.
[assembly: LevelOfParallelism(1)]
