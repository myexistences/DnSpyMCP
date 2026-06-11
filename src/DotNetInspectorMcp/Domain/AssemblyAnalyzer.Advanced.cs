using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using dnlib.DotNet;

namespace DotNetInspectorMcp.Domain;

internal sealed partial class AssemblyAnalyzer
{
    // ── 1. trace_field_consumers ───────────────────────────────────────────

    public Task<string> TraceFieldConsumersAsync(string assemblyPath, string typeFullName, string fieldName, int maxDepth) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var targetType = FindType(module, typeFullName);
        var targetField = targetType.Fields.FirstOrDefault(f => f.Name == fieldName) 
            ?? throw new InvalidOperationException($"Field not found: {fieldName}");

        var sb = new StringBuilder();
        sb.AppendLine($"--- Trace for {typeFullName}.{fieldName} (Depth: {maxDepth}) ---");

        // Step 1: Find immediate consumers
        var immediateConsumers = new List<MethodDef>();
        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody || method.Body is null) continue;

                foreach (var instr in method.Body.Instructions)
                {
                    if (instr.Operand is dnlib.DotNet.IField operandField)
                    {
                        if (operandField.Name == targetField.Name && operandField.DeclaringType.FullName == targetField.DeclaringType.FullName)
                        {
                            immediateConsumers.Add(method);
                            break; // No need to add the same method twice
                        }
                    }
                }
            }
        }

        if (immediateConsumers.Count == 0)
            return $"No consumers found for {typeFullName}.{fieldName}";

        // Step 2: Trace call graph up to depth
        var visited = new HashSet<MethodDef>();
        foreach (var consumer in immediateConsumers)
        {
            TraceCallGraphRecursive(module, consumer, sb, 0, maxDepth, visited);
        }

        return sb.ToString();
    });

    private void TraceCallGraphRecursive(ModuleDefMD module, MethodDef method, StringBuilder sb, int currentDepth, int maxDepth, HashSet<MethodDef> visited)
    {
        var indent = new string(' ', currentDepth * 4);
        var rva = GetMethodAddressRva(method);
        var action = currentDepth == 0 ? "reads/writes field" : "calls";
        sb.AppendLine($"{indent}-> {method.DeclaringType.FullName}.{method.Name} ({action}) // {(rva != null ? "RVA: " + rva : "No RVA")}");

        if (currentDepth >= maxDepth) return;
        if (!visited.Add(method)) return; // Prevent infinite loops

        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            foreach (var caller in type.Methods)
            {
                if (!caller.HasBody || caller.Body is null) continue;

                foreach (var instr in caller.Body.Instructions)
                {
                    if (instr.Operand is dnlib.DotNet.IMethod operandMethod)
                    {
                        if (operandMethod.Name == method.Name && operandMethod.DeclaringType.FullName == method.DeclaringType.FullName)
                        {
                            TraceCallGraphRecursive(module, caller, sb, currentDepth + 1, maxDepth, visited);
                            break;
                        }
                    }
                }
            }
        }
    }

    // ── 2. get_struct_layout (C++ format with padding) ───────────────────

    public Task<string> GetStructLayoutAsync(string assemblyPath, string typeFullName, string format) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var type = FindType(module, typeFullName);

        var sb = new StringBuilder();
        sb.AppendLine($"// Type: {type.FullName} (C++ Struct Layout)");

        sb.AppendLine($"struct {SanitizeCName(type.Name)} {{");

        var fields = new List<(uint Offset, string CType, string Name, uint Size)>();

        // Il2Cpp object header (Assuming 64-bit pointers)
        var baseType = type.BaseType?.FullName;
        if (baseType == "System.Object" || baseType?.Contains("MonoBehaviour") == true || baseType?.Contains("ScriptableObject") == true)
        {
            fields.Add((0x00, "void*", "klass", 8));
            fields.Add((0x08, "void*", "monitor", 8));
        }

        foreach (var field in type.Fields)
        {
            if (field.IsStatic) continue;

            var offsetStr = GetFieldOffset(field);
            uint offset = 0;
            if (offsetStr != null && offsetStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                uint.TryParse(offsetStr[2..], System.Globalization.NumberStyles.HexNumber, null, out offset);
            else if (offsetStr != null)
                uint.TryParse(offsetStr, out offset);
            else if (field.FieldOffset.HasValue)
                offset = field.FieldOffset.Value;

            var cType = MapToCType(field.FieldType.FullName);
            var size = GetTypeSize(field.FieldType.FullName);
            fields.Add((offset, cType, field.Name, size));
        }

        fields = fields.OrderBy(f => f.Offset).ToList();

        uint currentOffset = 0;
        int padCount = 0;

        foreach (var field in fields)
        {
            if (field.Offset > currentOffset)
            {
                var padSize = field.Offset - currentOffset;
                sb.AppendLine($"    char _pad{padCount++}[0x{padSize:X2}];");
                currentOffset = field.Offset;
            }

            var padding = new string(' ', Math.Max(1, 20 - field.CType.Length));
            sb.AppendLine($"    {field.CType}{padding}{SanitizeCName(field.Name)}; // 0x{field.Offset:X2}");
            currentOffset += field.Size;
        }

        sb.AppendLine("};");
        return sb.ToString();
    });

    private static uint GetTypeSize(string dotnetType)
    {
        return dotnetType switch
        {
            "System.Boolean" => 1,
            "System.Byte" => 1,
            "System.SByte" => 1,
            "System.Int16" => 2,
            "System.UInt16" => 2,
            "System.Int32" => 4,
            "System.UInt32" => 4,
            "System.Single" => 4,
            "System.Int64" => 8,
            "System.UInt64" => 8,
            "System.Double" => 8,
            _ => 8 // Assume pointers for classes/strings/arrays
        };
    }

    // ── 3. find_enum_values ──────────────────────────────────────────────

    public Task<string> FindEnumValuesAsync(string assemblyPath, string query, string? enumName) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var results = new List<string>();

        foreach (var type in module.GetTypes().Where(t => t.IsEnum))
        {
            bool typeMatches = !string.IsNullOrEmpty(enumName) && type.FullName.Contains(enumName, StringComparison.OrdinalIgnoreCase);

            foreach (var field in type.Fields)
            {
                if (!field.IsStatic || !field.HasConstant) continue;

                var valStr = field.Constant.Value?.ToString() ?? "0";
                uint valNum = 0;
                uint.TryParse(valStr, out valNum);
                var hexVal = $"0x{valNum:X}";

                bool valueMatches = !string.IsNullOrEmpty(query) && 
                    (field.Name.String.Contains(query, StringComparison.OrdinalIgnoreCase) || 
                     valStr == query || 
                     hexVal.Equals(query, StringComparison.OrdinalIgnoreCase));

                if (typeMatches || valueMatches)
                {
                    results.Add($"Enum: {type.FullName} | Field: {field.Name} = {valStr} ({hexVal})");
                }
            }
        }

        return results.Count == 0 ? "No enum values found matching the criteria." : string.Join(Environment.NewLine, results);
    });

    // ── 4. find_ui_bindings ──────────────────────────────────────────────

    public Task<string> FindUiBindingsAsync(string assemblyPath, string typeFullName, string[]? customUiTypes = null) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var type = FindType(module, typeFullName);
        var results = new List<string>();

        results.Add($"--- UI Bindings for {typeFullName} ---");

        var uiFields = type.Fields.Where(f => 
            f.FieldType.FullName.Contains("UnityEngine.UI") || 
            f.FieldType.FullName.Contains("TMPro") ||
            (customUiTypes != null && customUiTypes.Any(c => f.FieldType.FullName.Contains(c, StringComparison.OrdinalIgnoreCase)))).ToList();

        if (uiFields.Count == 0)
        {
            return $"No UI fields found in {typeFullName}.";
        }

        results.Add("UI Fields:");
        foreach (var field in uiFields)
        {
            var offset = GetFieldOffset(field) ?? "Unknown Offset";
            results.Add($"  - {field.Name} ({field.FieldType.FullName}) | Offset: {offset}");
        }

        results.Add("\nMethods that modify UI:");
        foreach (var method in type.Methods)
        {
            if (!method.HasBody || method.Body is null) continue;

            var modifiedFields = new HashSet<string>();

            for (int i = 0; i < method.Body.Instructions.Count; i++)
            {
                var instr = method.Body.Instructions[i];
                if (instr.OpCode.Code == dnlib.DotNet.Emit.Code.Ldfld && instr.Operand is dnlib.DotNet.IField fieldRef)
                {
                    if (uiFields.Any(f => f.Name == fieldRef.Name))
                    {
                        // Look ahead for a Callvirt that sets a property (like set_text, set_sprite)
                        for (int j = i + 1; j < Math.Min(i + 5, method.Body.Instructions.Count); j++)
                        {
                            var nextInstr = method.Body.Instructions[j];
                            if (nextInstr.OpCode.Code == dnlib.DotNet.Emit.Code.Callvirt && nextInstr.Operand is dnlib.DotNet.IMethod callMethod)
                            {
                                if (callMethod.Name.String.StartsWith("set_"))
                                {
                                    modifiedFields.Add($"{fieldRef.Name}.{callMethod.Name}");
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (modifiedFields.Count > 0)
            {
                var rva = GetMethodAddressRva(method);
                results.Add($"  - {method.Name} () | RVA: {rva}");
                foreach (var mod in modifiedFields)
                {
                    results.Add($"      -> calls {mod}");
                }
            }
        }

        return string.Join(Environment.NewLine, results);
    });

    // ── 5. search_by_inheritance ─────────────────────────────────────────

    public Task<string> SearchByInheritanceAsync(string assemblyPath, string baseTypeFullName, string? fieldContains, int maxDepth) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var results = new List<string>();
        
        var baseType = FindType(module, baseTypeFullName);

        // Build derived map
        var derivedMap = new Dictionary<string, List<TypeDef>>();
        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            var bType = type.BaseType?.FullName;
            if (bType != null)
            {
                if (!derivedMap.ContainsKey(bType)) derivedMap[bType] = new List<TypeDef>();
                derivedMap[bType].Add(type);
            }
        }

        var matchingTypes = new HashSet<TypeDef>();
        SearchDerivedRecursive(derivedMap, baseTypeFullName, 0, maxDepth, matchingTypes);

        foreach (var type in matchingTypes)
        {
            if (string.IsNullOrEmpty(fieldContains))
            {
                results.Add($"Class: {type.FullName}");
            }
            else
            {
                var matchingFields = type.Fields.Where(f => f.Name.String.Contains(fieldContains, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matchingFields.Count > 0)
                {
                    results.Add($"Class: {type.FullName}");
                    foreach (var f in matchingFields)
                    {
                        var offset = GetFieldOffset(f) ?? "Unknown";
                        results.Add($"  -> Field: {f.Name} ({f.FieldType.FullName}) | Offset: {offset}");
                    }
                }
            }
        }

        return results.Count == 0 ? "No classes found matching criteria." : string.Join(Environment.NewLine, results);
    });

    private void SearchDerivedRecursive(Dictionary<string, List<TypeDef>> derivedMap, string currentTypeName, int depth, int maxDepth, HashSet<TypeDef> results)
    {
        if (depth >= maxDepth) return;
        if (!derivedMap.TryGetValue(currentTypeName, out var derivedTypes)) return;

        foreach (var type in derivedTypes)
        {
            results.Add(type);
            SearchDerivedRecursive(derivedMap, type.FullName, depth + 1, maxDepth, results);
        }
    }

    // ── 6. diff_types ────────────────────────────────────────────────────

    public Task<string> DiffTypesAsync(string assemblyPath, string typeA, string typeB) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var tA = FindType(module, typeA);
        var tB = FindType(module, typeB);

        var sb = new StringBuilder();
        sb.AppendLine($"--- Diff: {typeA} vs {typeB} ---");

        var fieldsA = tA.Fields.ToDictionary(f => f.Name.String, f => f);
        var fieldsB = tB.Fields.ToDictionary(f => f.Name.String, f => f);

        sb.AppendLine("\n[Fields Only in A]:");
        foreach (var f in fieldsA.Values.Where(f => !fieldsB.ContainsKey(f.Name)))
            sb.AppendLine($"  - {f.Name} ({f.FieldType.FullName})");

        sb.AppendLine("\n[Fields Only in B]:");
        foreach (var f in fieldsB.Values.Where(f => !fieldsA.ContainsKey(f.Name)))
            sb.AppendLine($"  - {f.Name} ({f.FieldType.FullName})");

        sb.AppendLine("\n[Field Differences]:");
        foreach (var fA in fieldsA.Values)
        {
            if (fieldsB.TryGetValue(fA.Name, out var fB))
            {
                var offA = GetFieldOffset(fA);
                var offB = GetFieldOffset(fB);
                if (offA != offB || fA.FieldType.FullName != fB.FieldType.FullName)
                {
                    sb.AppendLine($"  - {fA.Name}: A({fA.FieldType.FullName} @ {offA}) vs B({fB.FieldType.FullName} @ {offB})");
                }
            }
        }

        return sb.ToString();
    });

    // ── 7. resolve_method_signature (C++ Typedef Generator) ──────────────

    public Task<string> ResolveMethodSignatureAsync(string assemblyPath, string typeFullName, string methodName, string format) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var type = FindType(module, typeFullName);
        var method = FindMethod(type, methodName, null);

        if (format != "cpp_hook")
            return "Only format='cpp_hook' is supported currently.";

        var retType = MapToCType(method.ReturnType.FullName);
        var args = new List<string>();

        if (!method.IsStatic)
        {
            args.Add("void* instance");
        }

        foreach (var param in method.Parameters)
        {
            if (param.IsHiddenThisParameter) continue;
            var cType = MapToCType(param.Type.FullName);
            args.Add($"{cType} {SanitizeCName(param.Name)}");
        }

        var argStr = string.Join(", ", args);
        var rva = GetMethodAddressRva(method) ?? "Unknown";

        var sb = new StringBuilder();
        sb.AppendLine($"// {typeFullName}.{methodName} | RVA: {rva}");
        sb.AppendLine($"typedef {retType}(*t{method.Name})({argStr});");

        return sb.ToString();
    });

    // ── 8. get_class_pointer ─────────────────────────────────────────────

    public Task<string> GetClassPointerAsync(string scriptJsonPath, string typeFullName) => Task.Run(() =>
    {
        var normalizedPath = System.IO.Path.GetFullPath(scriptJsonPath);
        if (!System.IO.File.Exists(normalizedPath))
            throw new System.IO.FileNotFoundException($"script.json not found: {normalizedPath}");

        using var fs = System.IO.File.OpenRead(normalizedPath);
        using var doc = System.Text.Json.JsonDocument.Parse(fs);

        if (!doc.RootElement.TryGetProperty("ScriptMetadata", out var metadataArray) || metadataArray.ValueKind != System.Text.Json.JsonValueKind.Array)
            return "Error: Could not find 'ScriptMetadata' array in script.json. Is this a valid Il2CppDumper output?";

        var targetName = typeFullName + "_TypeInfo";
        
        foreach (var item in metadataArray.EnumerateArray())
        {
            if (item.TryGetProperty("Name", out var nameProp) && nameProp.GetString() == targetName)
            {
                if (item.TryGetProperty("Address", out var addressProp))
                {
                    if (addressProp.TryGetUInt64(out var address))
                    {
                        return $"[TypeInfo Class Pointer] {typeFullName} -> Hex Offset: 0x{address:X} (Decimal: {address})";
                    }
                }
            }
        }

        return $"Class pointer for '{typeFullName}' not found in script.json. Note: Only classes that are statically referenced by the game will have a TypeInfo pointer generated in global-metadata.";
    });
}
