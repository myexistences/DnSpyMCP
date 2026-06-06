# AGENT.md — DnSpy MCP Server v2.3.0

This document explains **every major aspect** of this MCP server so another agent (or human) can operate, extend, and troubleshoot it safely.

---

## 1) Purpose

`DnSpyMCP` is a local MCP (Model Context Protocol) server for .NET reverse engineering, game hacking, and network analysis.

It provides 30 tools to:
- inspect assemblies (`list_types`, `list_methods`, `search_members`, `analyze_type`)
- decompile code (`decompile_type`, `decompile_method`)
- inspect IL (`get_method_il`, `find_string_references`)
- game dump analysis (`search_by_offset`, `get_method_rva`, `get_struct_layout`, `find_enum_values`, `find_ui_bindings`, `get_class_pointer`)
- cross-references (`find_method_callers`, `find_field_references`, `trace_field_consumers`, `find_derived_types`, `search_by_inheritance`)
- token resolution (`lookup_token`)
- network reversing (`find_network_handlers`, `find_crypto_usage`, `scan_secrets`)
- multi-assembly search (`search_workspace`)
- class comparison & signatures (`diff_types`, `resolve_method_signature`)
- dump.cs bridge (`resolve_dump_line`)
- generate navigation instructions (`format_inspector_jump`)
- patch binaries (`patch_replace_string_literal`, `patch_nop_instructions`)

It does **not** require dnSpy at runtime. It uses:
- `dnlib` — for reading/writing .NET assemblies and IL
- `ICSharpCode.Decompiler` — for C# decompilation

---

## 2) MCP transport + protocol

Transport: **stdio JSON-RPC**.

Supported framing:
- LSP-style headers: `Content-Length: N\r\n\r\n<json>`
- Line framed JSON (single JSON message per line, or multi-line with balanced braces)

Implemented MCP methods:
- `initialize`
- `notifications/initialized`
- `ping`
- `tools/list`
- `tools/call`
- `resources/list`
- `resources/read`

Protocol version used: `2024-11-05`.

---

## 3) Project layout

```
src/DotNetInspectorMcp/
├── Program.cs                       — entry point with graceful Ctrl+C shutdown
├── Hosting/McpServerHost.cs         — dependency wiring, IDisposable host
├── Models/McpServer.cs              — request router
├── Communication/StdioJsonRpc.cs    — stdio read/write + framing, IDisposable
├── Domain/AssemblyAnalyzer.cs       — core analysis engine, IDisposable
├── Domain/ResourceRegistry.cs       — MCP resources
├── Endpoints/AssemblyTools.cs       — all 22 tool implementations
├── Endpoints/ToolRegistry.cs        — reflection-based tool discovery
├── Endpoints/Attributes.cs          — [McpTool] and [ToolParam] attributes
├── Endpoints/ToolCallResult.cs      — tool result model
└── Endpoints/ToolContext.cs         — DI context record
```

---

## 4) Tool registration model

Tools are discovered via reflection:
- Static methods in `Endpoints/AssemblyTools.cs`
- Marked with `[McpTool(name, description)]`
- Parameters described with `[ToolParam(description, Required = true/false)]`

`ToolRegistry` generates MCP JSON schema from C# method signatures.
Parameters with `Required = true` are always included in the JSON schema `required` array.

First parameter of every tool must be `ToolContext`.
Methods can return `ToolCallResult` (sync) or `Task<ToolCallResult>` (async).

---

## 5) Error handling & null guards

All tool handlers validate required parameters **before** calling into the analyzer.
If `assemblyPath` (or any required param) is null/empty, the tool returns a friendly
`ToolCallResult` with an actionable error message instead of throwing an exception.

Default assembly path auto-discovery:
1. Checks `DNSPY_DEFAULT_ASSEMBLY` environment variable
2. Scans CWD for `DummyDll/Assembly-CSharp.dll`, `Managed/Assembly-CSharp.dll`, etc.

---

## 6) Tool reference (30 tools)

### Assembly Inspection
1. `list_types` — list types with optional `gameCodeOnly` filter
2. `decompile_type` — decompile full type to C#
3. `decompile_method` — decompile method, auto-detects Il2Cpp dummies
4. `get_method_il` — raw IL listing
5. `search_members` — search by name across types, methods, fields, properties
6. `list_methods` — list methods of a type
7. `find_string_references` — find string literals in IL

### Game Dump / Il2Cpp
8. `analyze_type` — full class layout (fields + offsets + properties + methods + RVAs)
9. `get_struct_layout` — advanced C++ struct export with explicit `_pad` bytes
10. `get_type_layout` — basic C-struct layout 
11. `get_method_rva` — Il2Cpp RVA for a method
12. `search_by_offset` — find field/method by hex or decimal offset
13. `find_enum_values` — find/resolve enum values by magic number
14. `find_ui_bindings` — scan class for Unity UI refs & their modifiers
15. `get_class_pointer` — get static `TypeInfo` pointer from `script.json` for memory scanning

### Cross-References & Deep Reversing
16. `trace_field_consumers` — full call graph from field access (who ultimately uses it)
16. `find_method_callers` — who calls a target method
17. `find_field_references` — who reads/writes a target field
18. `find_derived_types` — inheritance tree scan
19. `search_by_inheritance` — filtered inheritance search
20. `lookup_token` — resolve raw hex token to Type/Method/Field/Property
21. `diff_types` — compare field offsets/signatures between two classes
22. `resolve_method_signature` — generate C++ typedef for hooking

### Network Reversing
23. `find_network_handlers` — heuristic scan for TCP/Socket/Packet logic
24. `find_crypto_usage` — heuristic scan for AES/RSA/encryption
25. `scan_secrets` — extract hardcoded IPs, URLs, API keys

### Multi-Assembly & Bridge
26. `search_workspace` — search ALL .dll files in a directory
27. `resolve_dump_line` — bridge dump.cs line numbers to DummyDll types

### Patching
28. `patch_replace_string_literal` — replace string at IL offset (backup first)
29. `patch_nop_instructions` — NOP instructions at IL offset (backup first)

### Navigation
- `format_inspector_jump` — build navigation steps from tokens

---

## 7) Resources

`resources/list` returns:
- `inspector://assemblies`
- Per-cached-assembly resources for summary/types

`resources/read` supports:
- `inspector://assemblies`
- `inspector://assembly?path=<...>&view=summary`
- `inspector://assembly?path=<...>&view=types`

Cache is populated when tools load assemblies by `assemblyPath`.

---

## 8) Lifecycle & disposal

- `Program.cs` registers `Console.CancelKeyPress` and `AppDomain.ProcessExit`
- On shutdown, `McpServerHost.Dispose()` is called
- This disposes `AssemblyAnalyzer` (releases all loaded `ModuleDefMD` handles)
- And disposes `StdioJsonRpc` (releases `SemaphoreSlim`)

---

## 9) Typical agent workflows

### A) Il2Cpp game field analysis
1. `list_types` with `gameCodeOnly=true`
2. `analyze_type` for full memory layout
3. `get_type_layout` for C-struct export
4. `search_by_offset` to find what's at `0x16D0`
5. `find_field_references` to trace who modifies it

### B) TCP Proxy / Packet hooking
1. `find_network_handlers` to locate Send/Receive
2. `find_crypto_usage` to find encryption layer
3. `scan_secrets` to extract server IPs/endpoints
4. `get_method_rva` for Frida hook targets

### C) Multi-DLL discovery
1. `search_workspace` to find which DLL contains a type
2. `analyze_type` to deep dive into the matching type
3. `find_field_references` / `find_method_callers` for cross-references

### D) Patch popup text
1. `find_string_references` with literal fragment
2. `patch_replace_string_literal` with new text
3. Verify by re-running `find_string_references` on patched file

### E) Bridge dump.cs
1. `resolve_dump_line` with dump.cs path and line number
2. Optionally provide `assemblyPath` for full DummyDll cross-reference

---

## 10) Build, run, publish

Build: `dotnet build src/DotNetInspectorMcp/DotNetInspectorMcp.csproj -c Release`
Run: `dotnet run --project src/DotNetInspectorMcp/DotNetInspectorMcp.csproj -c Release`
Publish: `./publish.ps1 -Runtime win-x64` or `./publish.ps1 -Runtime linux-x64`

---

## 11) Known caveats

- Patching can break signatures/strong-name expectations in some apps.
- NOP patching can break control flow if applied blindly.
- Always validate patched binaries in isolated test environments.
- Heuristic network/crypto scanners may have false positives on large assemblies.
- Il2Cpp DummyDlls have no real method bodies — decompilation shows stubs.
- `resolve_dump_line` uses regex-based parsing; may not handle all dump.cs formatting edge cases.

---

## 12) Extension guidance

When adding tools:
1. Add async method in `Domain/AssemblyAnalyzer.cs`
2. Add `[McpTool]` endpoint in `Endpoints/AssemblyTools.cs`
3. Mark required parameters with `[ToolParam(description, Required = true)]`
4. Add null guards with `ValidateRequired()` before calling the analyzer
5. Ensure token-rich output (TypeDef, MethodDef, RVA, etc.)
6. Update this AGENT.md + README.md + configs/instructions.md
