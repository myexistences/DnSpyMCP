using DotNetInspectorMcp.Hosting;

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

using var host = McpServerHost.CreateDefault();

try
{
    await host.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // Graceful shutdown — expected on Ctrl+C
}
