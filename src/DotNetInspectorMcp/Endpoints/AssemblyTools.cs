using DotNetInspectorMcp.Domain;

namespace DotNetInspectorMcp.Endpoints;

internal static class AssemblyTools
{
    // ── Shared validation helper ──────────────────────────────────────────
    private static ToolCallResult? ValidateRequired(string? value, string paramName, string hint)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return null;

        return new ToolCallResult(
            $"Error: {paramName} is required. {hint}",
            new { error = true, message = $"{paramName} is required", hint });
    }

    private static string ResolveAssemblyPath(ToolContext ctx, string? assemblyPath)
    {
        if (!string.IsNullOrWhiteSpace(assemblyPath))
            return assemblyPath;

        var defaultPath = ctx.Analysis.DefaultAssemblyPath;
        if (defaultPath != null)
            return defaultPath;

        return assemblyPath ?? string.Empty; // Will be caught by null guard
    }

    private const string AssemblyPathHint =
        "Provide the full path to a .NET assembly (.dll/.exe). " +
        "For Il2Cpp Unity games, use the DummyDll/Assembly-CSharp.dll path. " +
        "You can also set the DNSPY_DEFAULT_ASSEMBLY environment variable.";

    // ── Assembly Inspection ───────────────────────────────────────────────

    [McpTool("list_types", "List all types in a .NET assembly.")]
    public static async Task<ToolCallResult> ListTypes(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly (.dll/.exe).", Required = true)]
        string assemblyPath,
        [ToolParam("Optional namespace filter (exact match).")]
        string? @namespace = null,
        [ToolParam("Include nested types in output.")]
        bool includeNested = true,
        [ToolParam("If true, filters out System, UnityEngine, and other standard namespaces to show only game code.")]
        bool gameCodeOnly = false)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err) return err;

        try
        {
            var text = await ctx.Analysis.ListTypesAsync(assemblyPath, @namespace, includeNested, gameCodeOnly);
            var types = await ctx.Analysis.GetTypesAsync(assemblyPath, @namespace, includeNested, gameCodeOnly);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                @namespace,
                includeNested,
                gameCodeOnly,
                count = types.Length,
                types
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [McpTool("decompile_type", "Decompile a type from an assembly into C#.")]
    public static async Task<ToolCallResult> DecompileType(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Type full name. Supports dnlib and reflection format.", Required = true)]
        string typeFullName)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the full type name (e.g. 'MyNamespace.MyClass'). Use search_members to find types.") is { } err2) return err2;

        try
        {
            var code = await ctx.Analysis.DecompileTypeAsync(assemblyPath, typeFullName);
            return new ToolCallResult(code, new
            {
                assemblyPath,
                typeFullName,
                language = "csharp",
                code
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [McpTool("decompile_method", "Decompile one method into C#. Supports overload selection.")]
    public static async Task<ToolCallResult> DecompileMethod(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Type full name.", Required = true)]
        string typeFullName,
        [ToolParam("Method name.", Required = true)]
        string methodName,
        [ToolParam("Optional parameter type list to pick a specific overload (e.g. [\"System.String\",\"System.Int32\"]).")]
        string[]? parameterTypeNames = null)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the full type name. Use search_members to find it.") is { } err2) return err2;
        if (ValidateRequired(methodName, "methodName", "Provide the method name. Use list_methods to see available methods.") is { } err3) return err3;

        try
        {
            var code = await ctx.Analysis.DecompileMethodAsync(assemblyPath, typeFullName, methodName, parameterTypeNames);
            return new ToolCallResult(code, new
            {
                assemblyPath,
                typeFullName,
                methodName,
                parameterTypeNames,
                language = "csharp",
                code
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new
            {
                error = true,
                message = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    [McpTool("get_method_il", "Get raw IL for one method. Supports overload selection.")]
    public static async Task<ToolCallResult> GetMethodIl(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Type full name.", Required = true)]
        string typeFullName,
        [ToolParam("Method name.", Required = true)]
        string methodName,
        [ToolParam("Optional parameter type list to pick a specific overload.")]
        string[]? parameterTypeNames = null)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the full type name.") is { } err2) return err2;
        if (ValidateRequired(methodName, "methodName", "Provide the method name.") is { } err3) return err3;

        try
        {
            var il = await ctx.Analysis.GetMethodIlAsync(assemblyPath, typeFullName, methodName, parameterTypeNames);
            return new ToolCallResult(il, new
            {
                assemblyPath,
                typeFullName,
                methodName,
                parameterTypeNames,
                language = "il",
                il
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new
            {
                error = true,
                message = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    [McpTool("search_members", "Search matching type/member names inside an assembly.")]
    public static async Task<ToolCallResult> SearchMembers(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Case-insensitive search text.", Required = true)]
        string query,
        [ToolParam("Max number of results (default 500).")]
        int maxResults = 500)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(query, "query", "Provide a search string (e.g. 'Health', 'Damage', 'Player').") is { } err2) return err2;

        try
        {
            var text = await ctx.Analysis.SearchMembersAsync(assemblyPath, query, maxResults);
            var lines = text == "No matches found." ? Array.Empty<string>() : text.Split(Environment.NewLine);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                query,
                maxResults,
                count = lines.Length,
                results = lines
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [McpTool("list_methods", "List methods for a given type with signatures (helps overload targeting).")]
    public static async Task<ToolCallResult> ListMethods(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Type full name.", Required = true)]
        string typeFullName)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the full type name. Use search_members to find it.") is { } err2) return err2;

        try
        {
            var text = await ctx.Analysis.ListMethodsAsync(assemblyPath, typeFullName);
            var lines = text == "No methods found." ? Array.Empty<string>() : text.Split(Environment.NewLine);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                typeFullName,
                count = lines.Length,
                methods = lines
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [McpTool("find_string_references", "Find methods that reference a given string literal in IL (ldstr).")]
    public static async Task<ToolCallResult> FindStringReferences(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("String text to search for (path fragment, filename, token, etc.).", Required = true)]
        string text,
        [ToolParam("Case-sensitive search. Default: false.")]
        bool caseSensitive = false,
        [ToolParam("Max number of matches to return. Default: 500.")]
        int maxResults = 500)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(text, "text", "Provide the string literal text to search for.") is { } err2) return err2;

        try
        {
            var output = await ctx.Analysis.FindStringReferencesAsync(assemblyPath, text, caseSensitive, maxResults);
            var lines = output == "No string references found." ? Array.Empty<string>() : output.Split(Environment.NewLine);
            return new ToolCallResult(output, new
            {
                assemblyPath,
                text,
                caseSensitive,
                maxResults,
                count = lines.Length,
                matches = lines
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── Patching ──────────────────────────────────────────────────────────

    [McpTool("patch_replace_string_literal", "Patch one IL string literal at a method token + IL offset. Always creates a backup first.")]
    public static async Task<ToolCallResult> PatchReplaceStringLiteral(
        ToolContext ctx,
        [ToolParam("Path to target assembly (.exe/.dll).", Required = true)]
        string assemblyPath,
        [ToolParam("MethodDef token (example: 0x060005C1).", Required = true)]
        string methodDefToken,
        [ToolParam("IL offset (example: IL_01D2).", Required = true)]
        string ilOffset,
        [ToolParam("New literal text.", Required = true)]
        string newText,
        [ToolParam("Patch in place (default false).")]
        bool inPlace = false,
        [ToolParam("Output path when not inPlace. Optional.")]
        string? outputPath = null)
    {
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(methodDefToken, "methodDefToken", "Provide a MethodDef token (e.g. 0x060005C1). Use list_methods or search_members to find it.") is { } err2) return err2;
        if (ValidateRequired(ilOffset, "ilOffset", "Provide the IL offset (e.g. IL_01D2). Use get_method_il to find it.") is { } err3) return err3;
        if (ValidateRequired(newText, "newText", "Provide the replacement string text.") is { } err4) return err4;

        try
        {
            var result = await ctx.Analysis.PatchReplaceStringLiteralAsync(assemblyPath, methodDefToken, ilOffset, newText, inPlace, outputPath);
            return new ToolCallResult(result, new
            {
                assemblyPath,
                methodDefToken,
                ilOffset,
                newText,
                inPlace,
                outputPath,
                backupAlwaysCreated = true,
                result
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [McpTool("patch_nop_instructions", "Patch one or more IL instructions to NOP starting at an IL offset. Always creates a backup first.")]
    public static async Task<ToolCallResult> PatchNopInstructions(
        ToolContext ctx,
        [ToolParam("Path to target assembly (.exe/.dll).", Required = true)]
        string assemblyPath,
        [ToolParam("MethodDef token (example: 0x060005C1).", Required = true)]
        string methodDefToken,
        [ToolParam("Start IL offset (example: IL_01DD).", Required = true)]
        string ilOffset,
        [ToolParam("Number of instructions to NOP.")]
        int count = 1,
        [ToolParam("Patch in place (default false).")]
        bool inPlace = false,
        [ToolParam("Output path when not inPlace. Optional.")]
        string? outputPath = null)
    {
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(methodDefToken, "methodDefToken", "Provide a MethodDef token. Use list_methods or search_members to find it.") is { } err2) return err2;
        if (ValidateRequired(ilOffset, "ilOffset", "Provide the start IL offset. Use get_method_il to find it.") is { } err3) return err3;

        try
        {
            var result = await ctx.Analysis.PatchNopInstructionsAsync(assemblyPath, methodDefToken, ilOffset, count, inPlace, outputPath);
            return new ToolCallResult(result, new
            {
                assemblyPath,
                methodDefToken,
                ilOffset,
                count,
                inPlace,
                outputPath,
                backupAlwaysCreated = true,
                result
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── Game Dump / Il2Cpp Analysis ───────────────────────────────────────

    [McpTool("analyze_type", "Get the exact memory layout, fields, properties, methods, and RVAs of a class.")]
    public static async Task<ToolCallResult> AnalyzeType(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Type full name.", Required = true)]
        string typeFullName)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the full type name. Use search_members to find it.") is { } err2) return err2;

        try
        {
            var code = await ctx.Analysis.AnalyzeTypeAsync(assemblyPath, typeFullName);
            return new ToolCallResult(code, new
            {
                assemblyPath,
                typeFullName,
                analysis = code
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [McpTool("get_method_rva", "Get the Il2Cpp RVA (AddressAttribute) for a specific method.")]
    public static async Task<ToolCallResult> GetMethodRva(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Type full name.", Required = true)]
        string typeFullName,
        [ToolParam("Method name.", Required = true)]
        string methodName,
        [ToolParam("Optional parameter type list to pick a specific overload.")]
        string[]? parameterTypeNames = null)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the full type name.") is { } err2) return err2;
        if (ValidateRequired(methodName, "methodName", "Provide the method name.") is { } err3) return err3;

        try
        {
            var result = await ctx.Analysis.GetMethodRvaAsync(assemblyPath, typeFullName, methodName, parameterTypeNames);
            return new ToolCallResult(result, new
            {
                assemblyPath,
                typeFullName,
                methodName,
                parameterTypeNames,
                result
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [McpTool("search_by_offset", "Search for a field or method by its memory offset or RVA. Supports hex (0x16D0) or decimal (5840). Uses numeric comparison for reliable matching.")]
    public static async Task<ToolCallResult> SearchByOffset(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("The offset to search for (e.g., 0x16D0 or 5840).", Required = true)]
        string offsetQuery)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(offsetQuery, "offsetQuery", "Provide a hex (0x16D0) or decimal (5840) offset to search for.") is { } err2) return err2;

        try
        {
            var text = await ctx.Analysis.SearchByOffsetAsync(assemblyPath, offsetQuery);
            var lines = text.StartsWith("No matches") || text.StartsWith("Invalid") ? Array.Empty<string>() : text.Split(Environment.NewLine);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                offsetQuery,
                count = lines.Length,
                results = lines
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── Deep Reverse Engineering ──────────────────────────────────────────

    [McpTool("find_method_callers", "Find all methods that call a specific target method (Cross-References).")]
    public static async Task<ToolCallResult> FindMethodCallers(
        ToolContext ctx,
        [ToolParam("Path to target assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Target type full name.", Required = true)]
        string typeFullName,
        [ToolParam("Target method name.", Required = true)]
        string methodName)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the full type name.") is { } err2) return err2;
        if (ValidateRequired(methodName, "methodName", "Provide the method name.") is { } err3) return err3;

        try
        {
            var text = await ctx.Analysis.FindMethodCallersAsync(assemblyPath, typeFullName, methodName);
            var lines = text.StartsWith("No callers") ? Array.Empty<string>() : text.Split(Environment.NewLine);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                typeFullName,
                methodName,
                count = lines.Length,
                results = lines
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [McpTool("find_field_references", "Find all methods that read or write to a specific target field (Cross-References).")]
    public static async Task<ToolCallResult> FindFieldReferences(
        ToolContext ctx,
        [ToolParam("Path to target assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Target type full name.", Required = true)]
        string typeFullName,
        [ToolParam("Target field name.", Required = true)]
        string fieldName)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the full type name.") is { } err2) return err2;
        if (ValidateRequired(fieldName, "fieldName", "Provide the field name. Use analyze_type to see available fields.") is { } err3) return err3;

        try
        {
            var text = await ctx.Analysis.FindFieldReferencesAsync(assemblyPath, typeFullName, fieldName);
            var lines = text.StartsWith("No references") ? Array.Empty<string>() : text.Split(Environment.NewLine);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                typeFullName,
                fieldName,
                count = lines.Length,
                results = lines
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [McpTool("find_derived_types", "Find all classes that inherit from or implement a target base class/interface.")]
    public static async Task<ToolCallResult> FindDerivedTypes(
        ToolContext ctx,
        [ToolParam("Path to target assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Base type or interface full name.", Required = true)]
        string baseTypeFullName)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(baseTypeFullName, "baseTypeFullName", "Provide the full type name of the base class or interface.") is { } err2) return err2;

        try
        {
            var text = await ctx.Analysis.FindDerivedTypesAsync(assemblyPath, baseTypeFullName);
            var lines = text.StartsWith("No derived") ? Array.Empty<string>() : text.Split(Environment.NewLine);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                baseTypeFullName,
                count = lines.Length,
                results = lines
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [McpTool("lookup_token", "Resolve a raw hexadecimal metadata token (e.g., 0x06001234) into its corresponding Type/Method/Field/Property.")]
    public static async Task<ToolCallResult> LookupToken(
        ToolContext ctx,
        [ToolParam("Path to target assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("The metadata token in hex format (e.g., 0x06001234).", Required = true)]
        string tokenText)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(tokenText, "tokenText", "Provide a metadata token in hex (e.g. 0x06001234).") is { } err2) return err2;

        try
        {
            var result = await ctx.Analysis.LookupTokenAsync(assemblyPath, tokenText);
            return new ToolCallResult(result, new
            {
                assemblyPath,
                tokenText,
                result
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── Network / TCP Proxy Reversing ─────────────────────────────────────

    [McpTool("find_network_handlers", "Heuristically scan the game for network packet handling, TcpClient, Sockets, and serialization methods.")]
    public static async Task<ToolCallResult> FindNetworkHandlers(
        ToolContext ctx,
        [ToolParam("Path to target assembly.", Required = true)]
        string assemblyPath)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err) return err;

        try
        {
            var result = await ctx.Analysis.FindNetworkHandlersAsync(assemblyPath);
            var lines = result.StartsWith("No network") ? Array.Empty<string>() : result.Split(Environment.NewLine);
            return new ToolCallResult(result, new
            {
                assemblyPath,
                count = lines.Length,
                results = lines
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [McpTool("find_crypto_usage", "Heuristically scan the game for Cryptography usage (AES, RSA, MD5, Encrypt/Decrypt methods) used to hide packets.")]
    public static async Task<ToolCallResult> FindCryptoUsage(
        ToolContext ctx,
        [ToolParam("Path to target assembly.", Required = true)]
        string assemblyPath)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err) return err;

        try
        {
            var result = await ctx.Analysis.FindCryptoUsageAsync(assemblyPath);
            var lines = result.StartsWith("No crypto") ? Array.Empty<string>() : result.Split(Environment.NewLine);
            return new ToolCallResult(result, new
            {
                assemblyPath,
                count = lines.Length,
                results = lines
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [McpTool("scan_secrets", "Scan all string literals in the game to extract hardcoded Server IP Addresses, Websocket URLs, and API Keys.")]
    public static async Task<ToolCallResult> ScanSecrets(
        ToolContext ctx,
        [ToolParam("Path to target assembly.", Required = true)]
        string assemblyPath)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err) return err;

        try
        {
            var result = await ctx.Analysis.ScanSecretsAsync(assemblyPath);
            var lines = result.StartsWith("No secrets") ? Array.Empty<string>() : result.Split(Environment.NewLine);
            return new ToolCallResult(result, new
            {
                assemblyPath,
                count = lines.Length,
                results = lines
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── Multi-Assembly Search ─────────────────────────────────────────────

    [McpTool("search_workspace", "Search ALL .dll files in a directory for matching types, methods, fields, and properties. Useful when you don't know which DLL contains a type.")]
    public static async Task<ToolCallResult> SearchWorkspace(
        ToolContext ctx,
        [ToolParam("Path to folder containing .dll files (e.g., the DummyDll directory).", Required = true)]
        string directoryPath,
        [ToolParam("Case-insensitive search text.", Required = true)]
        string query,
        [ToolParam("Max results per DLL (default 50).")]
        int maxResultsPerDll = 50,
        [ToolParam("Search subdirectories recursively (default false).")]
        bool recursive = false)
    {
        if (ValidateRequired(directoryPath, "directoryPath", "Provide the path to a folder containing .dll files (e.g. the DummyDll directory).") is { } err1) return err1;
        if (ValidateRequired(query, "query", "Provide a search string (e.g. 'Health', 'Damage', 'Player').") is { } err2) return err2;

        try
        {
            var result = await ctx.Analysis.SearchWorkspaceAsync(directoryPath, query, maxResultsPerDll, recursive);
            var lines = result.StartsWith("No matches") ? Array.Empty<string>() : result.Split(Environment.NewLine);
            return new ToolCallResult(result, new
            {
                directoryPath,
                query,
                maxResultsPerDll,
                recursive,
                count = lines.Length,
                results = lines
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── Dump.cs Bridge ────────────────────────────────────────────────────

    [McpTool("resolve_dump_line", "Given a dump.cs file and line number, resolve the type/member and optionally look it up in the DummyDll assembly.")]
    public static async Task<ToolCallResult> ResolveDumpLine(
        ToolContext ctx,
        [ToolParam("Path to dump.cs file.", Required = true)]
        string dumpCsPath,
        [ToolParam("The line number to resolve (1-indexed).", Required = true)]
        int lineNumber,
        [ToolParam("Optional: path to the DummyDll assembly to cross-reference. If provided, returns full analysis.")]
        string? assemblyPath = null)
    {
        if (ValidateRequired(dumpCsPath, "dumpCsPath", "Provide the path to the dump.cs file.") is { } err1) return err1;
        if (lineNumber <= 0)
            return new ToolCallResult("Error: lineNumber must be a positive integer.", new { error = true, message = "lineNumber must be positive" });

        assemblyPath = string.IsNullOrWhiteSpace(assemblyPath) ? ResolveAssemblyPath(ctx, null) : assemblyPath;

        try
        {
            var result = await ctx.Analysis.ResolveDumpLineAsync(dumpCsPath, lineNumber, assemblyPath);
            return new ToolCallResult(result, new
            {
                dumpCsPath,
                lineNumber,
                assemblyPath,
                result
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── C-Struct Layout ───────────────────────────────────────────────────

    [McpTool("get_type_layout", "Get a C-struct-style memory layout of a type, ready for cheat engine, Frida scripts, or memory reading.")]
    public static async Task<ToolCallResult> GetTypeLayout(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Type full name.", Required = true)]
        string typeFullName)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the full type name. Use search_members to find it.") is { } err2) return err2;

        try
        {
            var layout = await ctx.Analysis.GetTypeLayoutAsync(assemblyPath, typeFullName);
            return new ToolCallResult(layout, new
            {
                assemblyPath,
                typeFullName,
                layout
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── Navigation ────────────────────────────────────────────────────────

    [McpTool("format_inspector_jump", "Build step-by-step DotNet Inspector navigation instructions from metadata tokens.")]
    public static ToolCallResult FormatInspectorJump(
        ToolContext ctx,
        [ToolParam("Path to assembly to open in Inspector.", Required = true)]
        string assemblyPath,
        [ToolParam("Optional TypeDef token (example: 0x02000058).")]
        string? typeDefToken = null,
        [ToolParam("Optional MethodDef token (example: 0x060005C1).")]
        string? methodDefToken = null,
        [ToolParam("Optional FieldDef token (example: 0x04000012).")]
        string? fieldDefToken = null,
        [ToolParam("Optional PropertyDef token (example: 0x17000001).")]
        string? propertyDefToken = null,
        [ToolParam("Optional IL offset (example: IL_01D2 or 01D2).")]
        string? ilOffset = null)
    {
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err) return err;

        try
        {
            var normalizedType = NormalizeToken(typeDefToken);
            var normalizedMethod = NormalizeToken(methodDefToken);
            var normalizedField = NormalizeToken(fieldDefToken);
            var normalizedProperty = NormalizeToken(propertyDefToken);
            var normalizedIl = NormalizeIlOffset(ilOffset);

            var lines = new List<string>
            {
                "DotNet Inspector navigation plan:",
                $"1) Open assembly: {assemblyPath}",
                "2) Use metadata token navigation/search and jump to the token(s) below:"
            };

            if (normalizedType is not null) lines.Add($"   - TypeDef: {normalizedType}");
            if (normalizedMethod is not null) lines.Add($"   - MethodDef: {normalizedMethod}");
            if (normalizedField is not null) lines.Add($"   - FieldDef: {normalizedField}");
            if (normalizedProperty is not null) lines.Add($"   - PropertyDef: {normalizedProperty}");

            if (normalizedType is null && normalizedMethod is null && normalizedField is null && normalizedProperty is null)
                lines.Add("   - (No metadata token provided)");

            if (normalizedIl is not null)
            {
                lines.Add("3) Open the method body (IL view) and jump/scroll to offset:");
                lines.Add($"   - {normalizedIl}");
            }

            lines.Add("Tip: tokens from find_string_references/search_members/list_methods are directly reusable here.");

            var text = string.Join(Environment.NewLine, lines);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                typeDefToken = normalizedType,
                methodDefToken = normalizedMethod,
                fieldDefToken = normalizedField,
                propertyDefToken = normalizedProperty,
                ilOffset = normalizedIl,
                steps = lines
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    private static string? NormalizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var t = token.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            t = t[2..];

        if (!uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"Invalid token format: {token}");

        return $"0x{value:X8}";
    }

    private static string? NormalizeIlOffset(string? il)
    {
        if (string.IsNullOrWhiteSpace(il))
            return null;

        var x = il.Trim().ToUpperInvariant();
        if (x.StartsWith("IL_", StringComparison.Ordinal))
            x = x[3..];

        if (!int.TryParse(x, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidOperationException($"Invalid IL offset: {il}");

        return $"IL_{value:X4}";
    }
}
