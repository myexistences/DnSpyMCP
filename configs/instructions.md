# DnSpy MCP — AI Agent Usage Guide

## Overview
DnSpy MCP is a .NET reverse engineering MCP server designed for game hacking, Il2Cpp dump analysis, and TCP proxy reversing. It provides 22 tools that operate on .NET assemblies (.dll/.exe files).

## Critical: Assembly Path
**Every tool requires `assemblyPath`** — the full path to a .NET assembly file.

For Il2Cpp Unity games, point to:
```
<project>/Dump/DummyDll/Assembly-CSharp.dll
```

You can also set the `DNSPY_DEFAULT_ASSEMBLY` environment variable to avoid repeating the path.

If you don't know which DLL contains a type, use `search_workspace` with the DummyDll folder path.

---

## Recommended Workflows

### 1. Finding & Analyzing a Type
```
search_members(query="PlayerHealth", assemblyPath="...")
  → find the type name
analyze_type(typeFullName="PlayerHealth", assemblyPath="...")
  → get full memory layout (fields + offsets + methods + RVAs)
get_type_layout(typeFullName="PlayerHealth", assemblyPath="...")
  → get C-struct format for Frida/cheat engine
```

### 2. Cross-Reference Analysis (Who Uses What?)
```
find_field_references(typeFullName="BaseProfileInfo", fieldName="Health", assemblyPath="...")
  → find all methods that read/write the Health field
find_method_callers(typeFullName="NetworkManager", methodName="SendPacket", assemblyPath="...")
  → find all methods that call SendPacket
```

### 3. TCP Proxy / Network Reversing
```
find_network_handlers(assemblyPath="...")
  → find Socket/TcpClient/Packet handler methods with RVAs
find_crypto_usage(assemblyPath="...")
  → find encryption/decryption methods
scan_secrets(assemblyPath="...")
  → extract hardcoded IPs, URLs, API keys
```

### 4. Il2Cpp Game Modding
```
list_types(gameCodeOnly=true, assemblyPath="...")
  → filter out Unity/System noise
search_by_offset(offsetQuery="0x16D0", assemblyPath="...")
  → find what field/method is at a given offset
get_method_rva(typeFullName="...", methodName="...", assemblyPath="...")
  → get the RVA for native hooking in Frida
```

### 5. Bridging dump.cs to DummyDll
```
resolve_dump_line(dumpCsPath="path/to/dump.cs", lineNumber=789108, assemblyPath="...Assembly-CSharp.dll")
  → resolves a dump.cs line number to a type/member and cross-references the DummyDll
```

### 6. Multi-DLL Discovery
```
search_workspace(directoryPath="path/to/DummyDll", query="NetworkManager")
  → searches ALL .dll files in the folder for matching types/members
```

---

## Important Notes

- **Il2Cpp DummyDlls have NO real method bodies.** `decompile_method` will show stubs with `NotImplementedException`. This is expected — use `get_method_rva` to get the RVA for hooking in native code (GameAssembly.dll).
- **Field offsets from `analyze_type`** are the actual memory offsets used for reading/writing in memory at runtime.
- **`search_by_offset`** uses numeric comparison, so `0x4142DB4` and `0x04142DB4` will both match correctly.
- **`get_type_layout`** outputs a C-struct format ready for copy-paste into cheat engine or Frida scripts.
- **Patching tools** (`patch_replace_string_literal`, `patch_nop_instructions`) always create a backup before modifying files.

---

## Tool Quick Reference (22 tools)

| Tool | Purpose |
|------|---------|
| `list_types` | List all types, with optional `gameCodeOnly` filter |
| `search_members` | Search types, methods, fields by name |
| `search_workspace` | Search ALL DLLs in a folder |
| `decompile_type` | Decompile a full type to C# |
| `decompile_method` | Decompile a specific method |
| `get_method_il` | Get raw IL instructions |
| `list_methods` | List methods of a type |
| `find_string_references` | Find string literal usages |
| `analyze_type` | Full class layout (fields, offsets, methods, RVAs) |
| `get_type_layout` | C-struct memory layout for cheat development |
| `get_method_rva` | Get Il2Cpp RVA for a method |
| `search_by_offset` | Find field/method by hex or decimal offset |
| `find_method_callers` | Cross-ref: who calls a method |
| `find_field_references` | Cross-ref: who reads/writes a field |
| `find_derived_types` | Find all subclasses of a type |
| `lookup_token` | Resolve metadata token to type/method/field |
| `find_network_handlers` | Find TCP/Socket/Packet handlers |
| `find_crypto_usage` | Find encryption/decryption usage |
| `scan_secrets` | Extract hardcoded IPs, URLs, API keys |
| `resolve_dump_line` | Bridge dump.cs line numbers to DummyDll types |
| `patch_replace_string_literal` | Replace a string literal in IL |
| `patch_nop_instructions` | NOP IL instructions |
| `format_inspector_jump` | Build navigation instructions from tokens |
