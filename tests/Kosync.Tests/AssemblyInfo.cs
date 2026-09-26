using Xunit;

// Tests share process-wide environment variables (ADMIN_PASSWORD,
// REGISTRATION_DISABLED) to drive the app deterministically - run
// sequentially to avoid cross-test interference.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
