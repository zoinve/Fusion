namespace YPM.Core.Services;

public interface IBackendHostService : IAsyncDisposable
{
    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
}
