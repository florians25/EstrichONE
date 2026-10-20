using System.Diagnostics;
using System.Text;

namespace EstrichOne.Hosting.Docker;

/// <summary>
/// Startet ein Programm ohne Shell: Argumente werden einzeln über <see cref="ProcessStartInfo.ArgumentList"/>
/// übergeben, dadurch ist keine Shell-Injection möglich – unabhängig vom Inhalt der Argumente.
/// </summary>
public static class ProcessRunner
{
    public static async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        Action<string>? output,
        CancellationToken cancellationToken,
        string? workingDirectory = null,
        int maxCapture = 4 * 1024 * 1024)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Fortschrittsbalken und Farben von Compose abschalten; Ausgabe zeilenweise lesbar.
        startInfo.Environment["COMPOSE_ANSI"] = "never";
        startInfo.Environment["COMPOSE_PROGRESS"] = "plain";
        startInfo.Environment["NO_COLOR"] = "1";

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var sync = new Lock();
        void Collect(StringBuilder target, string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (sync)
            {
                if (target.Length < maxCapture)
                {
                    target.AppendLine(line);
                }
            }

            output?.Invoke(line);
        }

        process.OutputDataReceived += (_, e) => Collect(stdout, e.Data);
        process.ErrorDataReceived += (_, e) => Collect(stderr, e.Data);

        try
        {
            if (!process.Start())
            {
                return new CommandResult(-1, string.Empty, $"{fileName} konnte nicht gestartet werden.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return new CommandResult(-1, string.Empty, $"{fileName} konnte nicht gestartet werden: {exception.Message}");
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // bereits beendet
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                return new CommandResult(-2, stdout.ToString(), stderr + $"Zeitüberschreitung nach {timeout.TotalSeconds:0} Sekunden.");
            }
        }

        // Restliche Ausgabe der asynchronen Leser abwarten.
        process.WaitForExit();
        lock (sync)
        {
            return new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
    }
}
