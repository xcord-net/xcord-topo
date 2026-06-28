using Xunit.Sdk;

namespace XcordTopo.Tests.Integration.Helpers;

/// <summary>
/// Condition-based wait helper - replaces fixed <c>Task.Delay(...)</c> sleeps
/// that flake under load or on slow runners. Polls a predicate until it returns
/// true or the timeout elapses, failing the test (XunitException) on timeout
/// instead of silently passing a stale assertion.
/// </summary>
public static class WaitHelper
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Polls <paramref name="predicate"/> until it returns true. Throws an
    /// <see cref="XunitException"/> (renders as a test failure) if the
    /// condition is not met within <paramref name="timeout"/>.
    /// </summary>
    public static async Task UntilAsync(
        Func<bool> predicate,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        string label = "condition")
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        var delay = interval ?? DefaultInterval;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;
            await Task.Delay(delay);
        }
        throw new XunitException(
            $"WaitHelper.UntilAsync '{label}' timed out after {(timeout ?? DefaultTimeout).TotalSeconds:0.###}s");
    }

    /// <summary>
    /// Async-predicate overload - awaits <paramref name="predicate"/> on each poll.
    /// </summary>
    public static async Task UntilAsync(
        Func<Task<bool>> predicate,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        string label = "condition")
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        var delay = interval ?? DefaultInterval;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
                return;
            await Task.Delay(delay);
        }
        throw new XunitException(
            $"WaitHelper.UntilAsync '{label}' timed out after {(timeout ?? DefaultTimeout).TotalSeconds:0.###}s");
    }
}
