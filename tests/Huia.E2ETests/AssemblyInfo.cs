using Xunit;

// Disable parallel test execution across test collections because multiple fixtures
// spin up out-of-process dev servers and mock IDPs bound to fixed ports.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
