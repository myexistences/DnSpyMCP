using System;
using System.Threading.Tasks;

namespace DotNetInspectorMcp.Endpoints;

internal static partial class AssemblyTools
{
    // ── 1. trace_field_consumers ───────────────────────────────────────────

    [McpTool("trace_field_consumers", "Trace the full call graph of methods that read or write a specific field. Answers 'Who ultimately uses this field?'")]
    public static async Task<ToolCallResult> TraceFieldConsumers(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Target type full name.", Required = true)]
        string typeFullName,
        [ToolParam("Target field name.", Required = true)]
        string fieldName,
        [ToolParam("Max recursion depth for tracing call graph. Default: 3.")]
        int depth = 3)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the full type name.") is { } err2) return err2;
        if (ValidateRequired(fieldName, "fieldName", "Provide the field name.") is { } err3) return err3;

        try
        {
            var text = await ctx.Analysis.TraceFieldConsumersAsync(assemblyPath, typeFullName, fieldName, depth);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                typeFullName,
                fieldName,
                depth,
                trace = text
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── 2. get_struct_layout (C++ format with padding) ───────────────────

    [McpTool("get_struct_layout", "Export a type as a C/C++ struct with precise memory padding (_pad) automatically calculated for cheat development.")]
    public static async Task<ToolCallResult> GetStructLayout(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Type full name.", Required = true)]
        string typeFullName,
        [ToolParam("Format. Default: 'cpp'.")]
        string format = "cpp")
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the full type name.") is { } err2) return err2;

        try
        {
            var text = await ctx.Analysis.GetStructLayoutAsync(assemblyPath, typeFullName, format);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                typeFullName,
                format,
                structLayout = text
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── 3. find_enum_values ──────────────────────────────────────────────

    [McpTool("find_enum_values", "Find and resolve enum values. Search by magic number, hex value, partial name, or dump a specific enum.")]
    public static async Task<ToolCallResult> FindEnumValues(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Query to search for (e.g., '0x2000', '8192', 'KOL'). Leave empty to dump full enumName.")]
        string query = "",
        [ToolParam("Optional exact or partial enum type name to restrict search (e.g., 'CIKPIKDDDKE').")]
        string? enumName = null)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;

        if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(enumName))
            return new ToolCallResult("Error: Provide either a query or an enumName.", new { error = true });

        try
        {
            var text = await ctx.Analysis.FindEnumValuesAsync(assemblyPath, query, enumName);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                query,
                enumName,
                results = text
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── 4. find_ui_bindings ──────────────────────────────────────────────

    [McpTool("find_ui_bindings", "Scan a class to find all Unity UI component references (Image, Text, TMPro) and list methods that modify them.")]
    public static async Task<ToolCallResult> FindUiBindings(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Type full name of the UI script (e.g., UIHudKillNotificationItem).", Required = true)]
        string typeFullName)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the full type name.") is { } err2) return err2;

        try
        {
            var text = await ctx.Analysis.FindUiBindingsAsync(assemblyPath, typeFullName);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                typeFullName,
                bindings = text
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── 5. search_by_inheritance ─────────────────────────────────────────

    [McpTool("search_by_inheritance", "Find all classes that inherit from a base type and optionally filter by field names.")]
    public static async Task<ToolCallResult> SearchByInheritance(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Base type full name (e.g., MonoBehaviour).", Required = true)]
        string baseType,
        [ToolParam("Optional text the field name must contain (e.g., 'badge').")]
        string? fieldContains = null,
        [ToolParam("Max inheritance depth. Default: 2.")]
        int maxDepth = 2)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(baseType, "baseType", "Provide the base type full name.") is { } err2) return err2;

        try
        {
            var text = await ctx.Analysis.SearchByInheritanceAsync(assemblyPath, baseType, fieldContains, maxDepth);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                baseType,
                fieldContains,
                maxDepth,
                results = text
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── 6. diff_types ────────────────────────────────────────────────────

    [McpTool("diff_types", "Compare two types side-by-side to see field offset and signature differences.")]
    public static async Task<ToolCallResult> DiffTypes(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Type A full name.", Required = true)]
        string typeA,
        [ToolParam("Type B full name.", Required = true)]
        string typeB)
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeA, "typeA", "Provide the first type full name.") is { } err2) return err2;
        if (ValidateRequired(typeB, "typeB", "Provide the second type full name.") is { } err3) return err3;

        try
        {
            var text = await ctx.Analysis.DiffTypesAsync(assemblyPath, typeA, typeB);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                typeA,
                typeB,
                diff = text
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // ── 7. resolve_method_signature (C++ Typedef Generator) ──────────────

    [McpTool("resolve_method_signature", "Generate a C++ function pointer typedef matching a game method's signature for MinHook or Frida.")]
    public static async Task<ToolCallResult> ResolveMethodSignature(
        ToolContext ctx,
        [ToolParam("Path to the target .NET assembly.", Required = true)]
        string assemblyPath,
        [ToolParam("Type full name.", Required = true)]
        string typeFullName,
        [ToolParam("Method name.", Required = true)]
        string methodName,
        [ToolParam("Format type. Default: 'cpp_hook'.")]
        string format = "cpp_hook")
    {
        assemblyPath = ResolveAssemblyPath(ctx, assemblyPath);
        if (ValidateRequired(assemblyPath, "assemblyPath", AssemblyPathHint) is { } err1) return err1;
        if (ValidateRequired(typeFullName, "typeFullName", "Provide the type full name.") is { } err2) return err2;
        if (ValidateRequired(methodName, "methodName", "Provide the method name.") is { } err3) return err3;

        try
        {
            var text = await ctx.Analysis.ResolveMethodSignatureAsync(assemblyPath, typeFullName, methodName, format);
            return new ToolCallResult(text, new
            {
                assemblyPath,
                typeFullName,
                methodName,
                format,
                signature = text
            });
        }
        catch (Exception ex)
        {
            return new ToolCallResult($"Error: {ex.Message}\n{ex.StackTrace}", new { error = true, message = ex.Message, stackTrace = ex.StackTrace });
        }
    }
}
