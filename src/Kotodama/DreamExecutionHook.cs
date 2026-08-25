namespace Kotodama;

internal interface IDreamExecutionHook
{
    Task AfterStagingAsync(CancellationToken cancellationToken);

    Task AfterUpdateAsync(CancellationToken cancellationToken);
}

internal sealed class NoOpDreamExecutionHook : IDreamExecutionHook
{
    internal static NoOpDreamExecutionHook Instance { get; } = new();

    private NoOpDreamExecutionHook() { }

    public Task AfterStagingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AfterUpdateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
