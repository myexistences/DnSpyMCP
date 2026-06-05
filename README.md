<div align="center">

# 🔬 DnSpy MCP Server

[![GitHub stars](https://img.shields.io/github/stars/myexistences/DnSpyMCP?style=for-the-badge&logo=github&color=gold)](https://github.com/myexistences/DnSpyMCP/stargazers)
[![GitHub forks](https://img.shields.io/github/forks/myexistences/DnSpyMCP?style=for-the-badge&logo=github&color=blue)](https://github.com/myexistences/DnSpyMCP/network/members)
[![GitHub issues](https://img.shields.io/github/issues/myexistences/DnSpyMCP?style=for-the-badge&logo=github&color=red)](https://github.com/myexistences/DnSpyMCP/issues)
[![GitHub last commit](https://img.shields.io/github/last-commit/myexistences/DnSpyMCP?style=for-the-badge&logo=github&color=green)](https://github.com/myexistences/DnSpyMCP/commits)
[![GitHub license](https://img.shields.io/github/license/myexistences/DnSpyMCP?style=for-the-badge&color=purple)](https://github.com/myexistences/DnSpyMCP/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![Version](https://img.shields.io/badge/version-2.0.0-orange?style=for-the-badge)](https://github.com/myexistences/DnSpyMCP/releases)

**A powerful MCP server for .NET reverse engineering, game hacking, and Il2Cpp dump analysis.**

*19 AI-optimized tools • Cross-References • Network Reversing • Il2Cpp Support • TCP Proxy Analysis*

[![Visitors](https://api.visitorbadge.io/api/visitors?path=myexistences%2FDnSpyMCP&countColor=%23263759&style=for-the-badge)](https://visitorbadge.io/status?path=myexistences%2FDnSpyMCP)

</div>

---

## Features

- **19 MCP Tools** for deep .NET assembly analysis, patching, and game reversing
- Fully async, non-blocking architecture
- Il2Cpp / Unity game dump optimized (offset search, RVA lookup, dummy detection)
- Cross-reference scanning (method callers, field references)
- Network/TCP proxy reverse engineering (packet handler discovery, crypto usage scanning)
- Hardcoded secret extraction (IPs, URLs, API keys)
- Single-file publish (win-x64 / linux-x64)
- MCP stdio transport (Claude Code / Codex / Cursor / OpenCode compatible)

## Project Layout

```
src/DotNetInspectorMcp/
├── Program.cs                          # Entry point with graceful shutdown
├── Hosting/McpServerHost.cs            # Dependency wiring
├── Models/McpServer.cs                 # MCP request router
├── Communication/StdioJsonRpc.cs       # Stdio JSON-RPC transport
├── Domain/AssemblyAnalyzer.cs          # Core analysis engine
├── Domain/ResourceRegistry.cs          # MCP resources
├── Endpoints/AssemblyTools.cs          # All tool implementations
├── Endpoints/ToolRegistry.cs           # Reflection-based tool discovery
├── Endpoints/Attributes.cs             # [McpTool] / [ToolParam] attributes
├── Endpoints/ToolCallResult.cs         # Tool result model
└── Endpoints/ToolContext.cs            # DI context for tools
```

## Tools (19 total)

### Assembly Inspection
| Tool | Description |
|------|-------------|
| `list_types` | List all types. Supports `gameCodeOnly` filter to hide Unity/System noise. |
| `decompile_type` | Decompile a full type to C#. |
| `decompile_method` | Decompile a specific method. Auto-detects Il2Cpp dummy bodies. |
| `get_method_il` | Get raw IL instructions for a method. |
| `search_members` | Search types, methods, fields, properties by name. |
| `list_methods` | List all methods of a type with tokens. |
| `find_string_references` | Find all string literal usages across the assembly. |

### Game Dump / Il2Cpp Analysis
| Tool | Description |
|------|-------------|
| `analyze_type` | Full class layout: fields with offsets, properties, methods with RVAs. |
| `get_method_rva` | Get the Il2Cpp RVA for a specific method. |
| `search_by_offset` | Search by hex (`0x16D0`) or decimal offset to find fields/methods. |

### Deep Reverse Engineering
| Tool | Description |
|------|-------------|
| `find_method_callers` | Cross-reference: find all methods that call a target method. |
| `find_field_references` | Cross-reference: find all methods that read/write a target field. |
| `find_derived_types` | Find all classes inheriting from a base class or interface. |
| `lookup_token` | Resolve a raw hex token (e.g., `0x06001234`) to its Type/Method/Field. |

### Network / TCP Proxy Reversing
| Tool | Description |
|------|-------------|
| `find_network_handlers` | Heuristic scan for Socket/TcpClient/Packet handlers with RVAs. |
| `find_crypto_usage` | Heuristic scan for AES/RSA/Encrypt/Decrypt methods. |
| `scan_secrets` | Extract hardcoded IPs, URLs, WebSocket endpoints, and API keys. |

### Patching
| Tool | Description |
|------|-------------|
| `patch_replace_string_literal` | Replace a string literal at a specific IL offset. Always creates backup. |
| `patch_nop_instructions` | NOP one or more IL instructions. Always creates backup. |

### Navigation
| Tool | Description |
|------|-------------|
| `format_inspector_jump` | Build step-by-step navigation from metadata tokens. |

## Resources

- `inspector://assemblies` — list cached assemblies
- `inspector://assembly?path=<path>&view=summary` — assembly summary
- `inspector://assembly?path=<path>&view=types` — all type names

## Build

```bash
dotnet build src/DotNetInspectorMcp/DotNetInspectorMcp.csproj -c Release
```

## Run

```bash
dotnet run --project src/DotNetInspectorMcp/DotNetInspectorMcp.csproj -c Release
```

## Publish (single-file)

```powershell
./publish.ps1 -Runtime win-x64
./publish.ps1 -Runtime linux-x64
```

## MCP Client Configuration

### Claude Code / Cursor / Codex

```json
{
  "mcpServers": {
    "dnspy-mcp": {
      "command": "dotnet",
      "args": [
        "<PATH_TO>/DnSpyMCP.dll"
      ]
    }
  }
}
```

### OpenCode (Windows)

Config file: `%USERPROFILE%\.config\opencode\opencode.json`

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "dnspy-mcp": {
      "type": "local",
      "enabled": true,
      "command": [
        "dotnet",
        "<PATH_TO>/DnSpyMCP.dll"
      ]
    }
  }
}
```

### Using Published .exe (no dotnet runtime needed)

```json
{
  "mcpServers": {
    "dnspy-mcp": {
      "command": "<PATH_TO>/DnSpyMCP-win-x64.exe",
      "args": []
    }
  }
}
```

> Replace `<PATH_TO>` with the actual path to your built DLL or published EXE.

## Quick Start

1. Build or publish
2. Add config to your MCP client (see above)
3. Restart your MCP client
4. Ask the AI to call `list_types` with your target assembly path

## Example Workflows

### Find what modifies a health field
```
1. search_members → find "health" field
2. find_field_references → see all methods that read/write it
3. decompile_method → inspect the logic
4. get_method_rva → get the RVA for Frida hooking
```

### TCP Proxy / Packet Interception
```
1. find_network_handlers → find Send/Receive methods
2. find_crypto_usage → find encryption before packets are sent
3. scan_secrets → extract server IPs and endpoints
4. get_method_rva → get RVAs for Frida hooks
```

### Il2Cpp Game Analysis
```
1. list_types (gameCodeOnly=true) → see only game scripts
2. analyze_type → get full memory layout with offsets
3. search_by_offset 0x16D0 → find what field is at that offset
4. find_method_callers → trace who calls a specific function
```
