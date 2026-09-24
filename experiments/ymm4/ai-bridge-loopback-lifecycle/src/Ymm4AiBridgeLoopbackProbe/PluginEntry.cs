using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using YukkuriMovieMaker.Plugin;

namespace Ymm4AiBridgeLoopbackProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "Chat Native Work Lab — YMM4 AI Bridge Loopback Lifecycle";

    public void SetCulture(CultureInfo cultureInfo) => BridgeProbe.ObserveCallback(cultureInfo);
}

internal static class BridgeProbe
{
    private static readonly object Gate = new();
    private static int callbackCount;
    private static int listenerStartCount;
    private static int shutdownStarted;
    private static TcpListener? listener;
    private static CancellationTokenSource? cancellation;
    private static string? outputDirectory;

    public static void ObserveCallback(CultureInfo cultureInfo)
    {
        var root = Environment.GetEnvironmentVariable("CNWL_YMM4_AI_BRIDGE_OUTPUT");
        if (string.IsNullOrWhiteSpace(root)) return;

        var callback = Interlocked.Increment(ref callbackCount);
        var fullRoot = Path.GetFullPath(root);
        Directory.CreateDirectory(fullRoot);

        lock (Gate)
        {
            outputDirectory ??= fullRoot;
            WriteCallbacks(cultureInfo, callback);

            if (listener is not null)
            {
                WriteStartup(cultureInfo);
                return;
            }

            cancellation = new CancellationTokenSource();
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Interlocked.Increment(ref listenerStartCount);

            if (Application.Current is { } application)
                application.Exit += (_, _) => Shutdown("application-exit");

            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown("process-exit");

            WriteStartup(cultureInfo);
            _ = AcceptLoopAsync(listener, cancellation.Token);
        }
    }

    private static async Task AcceptLoopAsync(TcpListener activeListener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var client = await activeListener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, new UTF8Encoding(false), leaveOpen: true);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\n",
                };

                var line = await reader.ReadLineAsync().WaitAsync(token).ConfigureAwait(false);
                await writer.WriteLineAsync(string.Equals(line, "PING", StringComparison.Ordinal) ? "PONG" : "UNKNOWN").ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            WriteText("listener-error.txt", ex.ToString());
        }
    }

    private static void Shutdown(string reason)
    {
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0) return;

        try
        {
            cancellation?.Cancel();
            listener?.Stop();
        }
        finally
        {
            WriteText("shutdown.txt",
                $"reason={reason}\n" +
                $"callbacks={Volatile.Read(ref callbackCount)}\n" +
                $"listener_start_count={Volatile.Read(ref listenerStartCount)}\n" +
                $"process_id={Environment.ProcessId}\n");
        }
    }

    private static void WriteCallbacks(CultureInfo cultureInfo, int callback)
    {
        WriteText("callbacks.txt",
            $"callbacks={callback}\n" +
            $"culture={cultureInfo.Name}\n" +
            $"thread_id={Environment.CurrentManagedThreadId}\n");
    }

    private static void WriteStartup(CultureInfo cultureInfo)
    {
        if (listener?.LocalEndpoint is not IPEndPoint endpoint) return;

        WriteText("startup.txt",
            $"address={endpoint.Address}\n" +
            $"port={endpoint.Port}\n" +
            $"callbacks={Volatile.Read(ref callbackCount)}\n" +
            $"listener_start_count={Volatile.Read(ref listenerStartCount)}\n" +
            $"culture={cultureInfo.Name}\n" +
            $"process_id={Environment.ProcessId}\n" +
            $"application_present={Application.Current is not null}\n");
    }

    private static void WriteText(string fileName, string body)
    {
        var root = outputDirectory;
        if (string.IsNullOrWhiteSpace(root)) return;

        try
        {
            File.WriteAllText(Path.Combine(root, fileName), body, new UTF8Encoding(false));
        }
        catch
        {
            // Evidence writing must never crash the host.
        }
    }
}
