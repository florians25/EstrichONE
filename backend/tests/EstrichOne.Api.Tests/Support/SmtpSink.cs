// Integrationstests laufen unter Linux (CI); kurze Aufrufe ohne Abbruch-Token.
#pragma warning disable xUnit1051, CA1416
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EstrichOne.Api.Tests.Support;

/// <summary>Minimaler SMTP-Server für Tests (EHLO, AUTH PLAIN, MAIL, RCPT, DATA). Die CI prüft zusätzlich gegen Mailpit.</summary>
public sealed class SmtpSink : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public SmtpSink(string? requiredUser = null, string? requiredPassword = null)
    {
        RequiredUser = requiredUser;
        RequiredPassword = requiredPassword;
        _listener.Start();
        _loop = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public string? RequiredUser { get; }
    public string? RequiredPassword { get; }
    public List<(string From, List<string> To, string Data)> Messages { get; } = [];

    public async Task<int> WaitForAsync(int count, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            lock (Messages)
            {
                if (Messages.Count >= count)
                {
                    return Messages.Count;
                }
            }

            await Task.Delay(100);
        }

        lock (Messages)
        {
            return Messages.Count;
        }
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 smtp-sink ESMTP");
            string from = string.Empty;
            var to = new List<string>();
            var authenticated = RequiredUser is null;
            while (await reader.ReadLineAsync() is { } line)
            {
                var upper = line.ToUpperInvariant();
                if (upper.StartsWith("EHLO", StringComparison.Ordinal) || upper.StartsWith("HELO", StringComparison.Ordinal))
                {
                    await writer.WriteLineAsync("250-smtp-sink");
                    await writer.WriteLineAsync("250 AUTH PLAIN");
                }
                else if (upper.StartsWith("AUTH PLAIN", StringComparison.Ordinal))
                {
                    var payload = line.Length > 11 ? line[11..].Trim() : await AskAsync(writer, reader);
                    var parts = Encoding.UTF8.GetString(Convert.FromBase64String(payload)).Split('\0');
                    authenticated = parts.Length == 3 && parts[1] == RequiredUser && parts[2] == RequiredPassword;
                    await writer.WriteLineAsync(authenticated ? "235 ok" : "535 5.7.8 Authentication failed");
                }
                else if (upper.StartsWith("MAIL FROM:", StringComparison.Ordinal))
                {
                    if (!authenticated)
                    {
                        await writer.WriteLineAsync("530 5.7.0 Authentication required");
                        continue;
                    }

                    from = line[10..].Trim().Split(' ')[0].Trim('<', '>');
                    to = [];
                    await writer.WriteLineAsync("250 ok");
                }
                else if (upper.StartsWith("RCPT TO:", StringComparison.Ordinal))
                {
                    to.Add(line[8..].Trim().Trim('<', '>'));
                    await writer.WriteLineAsync("250 ok");
                }
                else if (upper == "DATA")
                {
                    await writer.WriteLineAsync("354 go");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                    {
                        data.AppendLine(dataLine);
                    }

                    lock (Messages)
                    {
                        Messages.Add((from, to, data.ToString()));
                    }

                    await writer.WriteLineAsync("250 queued");
                }
                else if (upper == "QUIT")
                {
                    await writer.WriteLineAsync("221 bye");
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("250 ok");
                }
            }
        }
    }

    private static async Task<string> AskAsync(StreamWriter writer, StreamReader reader)
    {
        await writer.WriteLineAsync("334 ");
        return await reader.ReadLineAsync() ?? string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }
}
