// SPDX-License-Identifier: GPL-3.0-or-later

using Xunit;

// Several integration-style tests override the process-wide plugin configuration and use the
// same persisted budget path. Keep them deterministic rather than racing across test classes.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
