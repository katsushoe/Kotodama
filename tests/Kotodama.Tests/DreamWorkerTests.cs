using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class DreamWorkerTests
{
    [Fact]
    public void GetInterval_WhenUnset_UsesDefault()
    {
        Environment.SetEnvironmentVariable("KOTODAMA_DREAM_INTERVAL_SECONDS", null);
        DreamWorker.GetInterval().Should().Be(TimeSpan.FromSeconds(DreamWorker.DefaultIntervalSeconds));
    }

    [Fact]
    public void GetInterval_WhenConfigured_UsesPositiveSeconds()
    {
        try
        {
            Environment.SetEnvironmentVariable("KOTODAMA_DREAM_INTERVAL_SECONDS", "15");
            DreamWorker.GetInterval().Should().Be(TimeSpan.FromSeconds(15));
        }
        finally
        {
            Environment.SetEnvironmentVariable("KOTODAMA_DREAM_INTERVAL_SECONDS", null);
        }
    }
}
