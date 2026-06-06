using System.Text.Json;
using DotNetInspectorMcp.Domain;
using DotNetInspectorMcp.Endpoints;
using DotNetInspectorMcp.Communication;

namespace DotNetInspectorMcp.Models;

internal sealed class McpServer(StdioJsonRpc rpc, ToolRegistry tools, ToolContext toolContext)
{
    private const string SupportedProtocolVersion = "2024-11-05";
    private readonly ResourceRegistry _resources = new(toolContext.Analysis);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var raw = await rpc.ReadMessageAsync(cancellationToken);
            if (raw is null)
                break;

            JsonDocument? doc = null;
            JsonElement? extractedId = null;
            try
            {
                doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                // Extract id early so we can use it in the catch block without re-parsing
                extractedId = TryExtractId(root);

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in root.EnumerateArray())
                        await ProcessRequestAsync(entry, cancellationToken);
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    await ProcessRequestAsync(root, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // Let cancellation propagate for graceful shutdown
            }
            catch (Exception ex)
            {
                if (extractedId is JsonElement requestId)
                    await rpc.WriteErrorAsync(requestId, -32000, ex.Message, cancellationToken);
            }
            finally
            {
                doc?.Dispose();
            }
        }
    }

    private async Task ProcessRequestAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var method = request.GetProperty("method").GetString();
        var hasId = request.TryGetProperty("id", out var id);
        var @params = request.TryGetProperty("params", out var value) ? value : default;

        object? result;
        switch (method)
        {
            case "initialize":
                result = BuildInitializeResult(@params);
                break;
            case "notifications/initialized":
                result = null;
                break;
            case "ping":
                result = new { };
                break;
            case "tools/list":
                result = new { tools = tools.GetDefinitions() };
                break;
            case "tools/call":
                result = await HandleToolCallAsync(@params);
                break;
            case "resources/list":
                result = new { resources = _resources.ListResources() };
                break;
            case "resources/read":
                result = await HandleResourceReadAsync(@params);
                break;
            default:
                throw new InvalidOperationException($"Method not found: {method}");
        }

        if (!hasId)
            return;

        await rpc.WriteResultAsync(id, result ?? new { }, cancellationToken);
    }

    private object BuildInitializeResult(JsonElement @params)
    {
        var protocolVersion = SupportedProtocolVersion;
        if (@params.ValueKind == JsonValueKind.Object &&
            @params.TryGetProperty("protocolVersion", out var clientVersion) &&
            clientVersion.ValueKind == JsonValueKind.String)
        {
            protocolVersion = clientVersion.GetString() ?? protocolVersion;
        }

        return new
        {
            protocolVersion,
            capabilities = new
            {
                tools = new
                {
                    listChanged = false
                },
                resources = new
                {
                    listChanged = true,
                    subscribe = false
                }
            },
            serverInfo = new
            {
                name = "dnspy-mcp",
                version = "2.3.0"
            }
        };
    }

    private async Task<object> HandleToolCallAsync(JsonElement @params)
    {
        var toolName = @params.GetProperty("name").GetString()
            ?? throw new InvalidOperationException("Missing tool name.");
        var arguments = @params.TryGetProperty("arguments", out var args) ? args : default;

        try
        {
            var output = await tools.InvokeAsync(toolName, arguments, toolContext);
            return new
            {
                content = new[]
                {
                    new { type = "text", text = output.Text }
                },
                structuredContent = output.StructuredContent
            };
        }
        catch (Exception ex)
        {
            return new
            {
                content = new[]
                {
                    new { type = "text", text = ex.Message }
                },
                isError = true
            };
        }
    }

    private async Task<object> HandleResourceReadAsync(JsonElement @params)
    {
        var uri = @params.GetProperty("uri").GetString()
            ?? throw new InvalidOperationException("Missing resource uri.");

        var text = await _resources.ReadResourceAsync(uri);
        return new
        {
            contents = new[]
            {
                new
                {
                    uri,
                    mimeType = "text/plain",
                    text
                }
            }
        };
    }

    private static JsonElement? TryExtractId(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
            return root.TryGetProperty("id", out var id) ? id.Clone() : null;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in root.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out var id))
                    return id.Clone();
            }
        }

        return null;
    }
}
