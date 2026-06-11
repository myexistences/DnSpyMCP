using DotNetInspectorMcp.Domain;
using DotNetInspectorMcp.Endpoints;
using DotNetInspectorMcp.Communication;
using DotNetInspectorMcp.Models;

namespace DotNetInspectorMcp.Hosting;

internal sealed class McpServerHost : IDisposable
{
    private readonly AssemblyAnalyzer _analyzer;
    private readonly StdioJsonRpc _rpc;
    private readonly McpServer _server;

    private McpServerHost(AssemblyAnalyzer analyzer, StdioJsonRpc rpc, McpServer server)
    {
        _analyzer = analyzer;
        _rpc = rpc;
        _server = server;
    }

    public static McpServerHost CreateDefault()
    {
        var rpc = new StdioJsonRpc();
        var analysis = new AssemblyAnalyzer();
        var context = new ToolContext(analysis);
        var registry = ToolRegistry.From(typeof(AssemblyTools));
        var server = new McpServer(rpc, registry, context);

        return new McpServerHost(analysis, rpc, server);
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
        => _server.RunAsync(cancellationToken);

    public void Dispose()
    {
        _analyzer.Dispose();
        _rpc.Dispose();
    }
}
