using System.Diagnostics;
using System.Net.Sockets;
using YPM.Core.Models;
using YPM.Core.Services;

namespace YPM.UI.Services;

public sealed class NodeBackendHostService : IBackendHostService
{
    private readonly ApiOptions _options;
    private Process? _process;

    public NodeBackendHostService(ApiOptions options)
    {
        _options = options;
    }

    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var uri = new Uri(_options.BaseUrl);

        // Force kill any leftover node processes from previous runs.
        KillOrphanedNodeProcesses();

        // Also kill any process occupying the target port.
        try
        {
            await KillProcessOnPortAsync(uri.Port, cancellationToken);
        }
        catch
        {
            // Best-effort — if we can't kill the old process we still try to start a new one.
        }

        var baseDirectory = AppContext.BaseDirectory;
        var nodeExePath = Path.Combine(baseDirectory, "node-runtime", "node.exe");
        var backendDirectory = Path.Combine(baseDirectory, "backend");
        var startupScript = Path.Combine(backendDirectory, "start-ypm-api.js");

        if (!File.Exists(nodeExePath))
        {
            LogError($"Bundled node.exe not found at: {nodeExePath}. BaseDirectory: {baseDirectory}");
            throw new FileNotFoundException("Bundled node.exe was not found.", nodeExePath);
        }

        if (!File.Exists(startupScript))
        {
            LogError($"Backend startup script not found at: {startupScript}. BaseDirectory: {baseDirectory}");
            throw new FileNotFoundException("Bundled backend startup script was not found.", startupScript);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = nodeExePath,
            Arguments = $"\"{startupScript}\"",
            WorkingDirectory = backendDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.Environment["PORT"] = uri.Port.ToString();
        startInfo.Environment["HOST"] = uri.Host;

        _process = new Process
        {
            StartInfo = startInfo,
        };

        var stderr = new StringWriter();
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderr.WriteLine(e.Data);
        };

        if (!_process.Start())
        {
            LogError("Process.Start() returned false for node.exe.");
            throw new InvalidOperationException("Failed to start the bundled API process.");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_process.HasExited)
            {
                var errorOutput = stderr.ToString();
                LogError($"Bundled API exited early with code {_process.ExitCode}. stderr: {errorOutput}");
                throw new InvalidOperationException($"Bundled API exited early with code {_process.ExitCode}.");
            }

            if (await IsPortOpenAsync(uri.Host, uri.Port, cancellationToken))
            {
                return;
            }

            await Task.Delay(300, cancellationToken);
        }

        throw new TimeoutException($"Bundled API did not start listening on {_options.BaseUrl} within 20 seconds.");
    }

    private static void LogError(string message)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Fusion");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "startup_error.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] NodeBackendHost: {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    /// <summary>
    /// Kills any leftover node.exe processes from previous runs.
    /// </summary>
    private static void KillOrphanedNodeProcesses()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("node"))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Process may have exited or we lack permissions — skip.
                }
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task KillProcessOnPortAsync(int port, CancellationToken cancellationToken)
    {
        string stdout;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                },
            };

            process.Start();
            stdout = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            return;
        }

        var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.Contains($":{port}", StringComparison.Ordinal) || !line.EndsWith("LISTENING", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var pidString = parts[^1];
            if (!int.TryParse(pidString, out var pid) || pid <= 0)
            {
                continue;
            }

            try
            {
                using var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/PID {pid} /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (killer is not null)
                {
                    await killer.WaitForExitAsync(cancellationToken);
                }
            }
            catch
            {
            }
        }

        // Wait briefly for the port to actually be released.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await IsPortOpenAsync("127.0.0.1", port, cancellationToken))
            {
                return;
            }
            await Task.Delay(200, cancellationToken);
        }
    }
}
