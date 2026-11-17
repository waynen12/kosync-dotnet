using Xunit;

// Tests share process-wide environment variables (ADMIN_PASSWORD,
// SELF_REGISTRATION_ENABLED) to drive the app deterministically - run
// sequentially to avoid cross-test interference.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
