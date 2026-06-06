using System.Collections.Concurrent;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

namespace DotNetInspectorMcp.Domain;

internal sealed partial class AssemblyAnalyzer : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<LoadedAssembly>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private string? _defaultAssemblyPath;
    private bool _defaultAssemblyChecked;

    public IReadOnlyList<string> GetCachedAssemblyPaths() => _cache.Keys.OrderBy(x => x).ToArray();

    /// <summary>
    /// Auto-discovered default assembly path.
    /// Checks DNSPY_DEFAULT_ASSEMBLY env var first, then scans CWD for common DummyDll locations.
    /// </summary>
    public string? DefaultAssemblyPath
    {
        get
        {
            if (_defaultAssemblyChecked)
                return _defaultAssemblyPath;

            _defaultAssemblyChecked = true;

            // 1. Check environment variable
            var envPath = Environment.GetEnvironmentVariable("DNSPY_DEFAULT_ASSEMBLY");
            if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            {
                _defaultAssemblyPath = Path.GetFullPath(envPath);
                return _defaultAssemblyPath;
            }

            // 2. Auto-discover common locations from CWD
            var cwd = Environment.CurrentDirectory;
            var candidates = new[]
            {
                "DummyDll/Assembly-CSharp.dll",
                "Dump/DummyDll/Assembly-CSharp.dll",
                "Il2CppDumper/DummyDll/Assembly-CSharp.dll",
                "Managed/Assembly-CSharp.dll",
                "Assembly-CSharp.dll"
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(cwd, candidate);
                if (File.Exists(fullPath))
                {
                    _defaultAssemblyPath = Path.GetFullPath(fullPath);
                    return _defaultAssemblyPath;
                }
            }

            return null;
        }
    }

    public Task<string> GetAssemblySummaryAsync(string assemblyPath) => Task.Run(() =>
    {
        var asm = GetOrLoad(assemblyPath);
        var module = asm.Module;
        var allTypes = module.GetTypes().Where(t => !t.IsGlobalModuleType).ToArray();
        var methodCount = allTypes.Sum(t => t.Methods.Count);

        var lines = new[]
        {
            $"path: {asm.Path}",
            $"module: {module.Name}",
            $"runtime: {module.RuntimeVersion}",
            $"types: {allTypes.Length}",
            $"methods: {methodCount}"
        };

        return string.Join(Environment.NewLine, lines);
    });

    public Task<string[]> GetTypesAsync(string assemblyPath, string? namespaceFilter, bool includeNested, bool gameCodeOnly = false) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        
        var query = module.GetTypes()
            .Where(t => !t.IsGlobalModuleType)
            .Where(t => includeNested || !t.IsNested)
            .Where(t => string.IsNullOrWhiteSpace(namespaceFilter) || string.Equals(t.Namespace, namespaceFilter, StringComparison.Ordinal));

        if (gameCodeOnly)
        {
            query = query.Where(t => 
                string.IsNullOrWhiteSpace(t.Namespace) || 
                (!t.Namespace.StartsWith("System") &&
                 !t.Namespace.StartsWith("Microsoft") &&
                 !t.Namespace.StartsWith("UnityEngine") &&
                 !t.Namespace.StartsWith("Unity.") &&
                 !t.Namespace.StartsWith("Mono") &&
                 !t.Namespace.StartsWith("JetBrains") &&
                 !t.Namespace.StartsWith("TMPro") &&
                 !t.Namespace.StartsWith("Cinemachine")));
        }

        return query
            .Select(t => t.FullName)
            .OrderBy(t => t)
            .ToArray();
    });

    public async Task<string> ListTypesAsync(string assemblyPath, string? namespaceFilter, bool includeNested, bool gameCodeOnly = false)
    {
        var types = await GetTypesAsync(assemblyPath, namespaceFilter, includeNested, gameCodeOnly);
        return types.Length == 0 ? "No types found." : string.Join(Environment.NewLine, types);
    }

    public Task<string> AnalyzeTypeAsync(string assemblyPath, string typeFullName) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var type = FindType(module, typeFullName);
        
        var sb = new StringBuilder();
        var baseType = type.BaseType?.FullName;
        var interfaces = type.Interfaces.Select(i => i.Interface.FullName).ToList();
        
        sb.AppendLine($"// Type: {type.FullName}");
        if (baseType != null) sb.AppendLine($"// Base: {baseType}");
        if (interfaces.Any()) sb.AppendLine($"// Interfaces: {string.Join(", ", interfaces)}");
        sb.AppendLine($"// Size: {(type.ClassLayout?.ClassSize > 0 ? "0x" + type.ClassLayout.ClassSize.ToString("X") : "Unknown")}");
        sb.AppendLine();
        
        sb.AppendLine("// --- Fields (Memory Layout) ---");
        var fields = new List<(uint Offset, string Declaration)>();
        
        foreach (var field in type.Fields)
        {
            var offsetStr = GetFieldOffset(field);
            uint offset = 0;
            if (offsetStr != null && offsetStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                uint.TryParse(offsetStr[2..], System.Globalization.NumberStyles.HexNumber, null, out offset);
            else if (offsetStr != null)
                uint.TryParse(offsetStr, out offset);
            else if (field.FieldOffset.HasValue)
                offset = field.FieldOffset.Value;
                
            var access = field.IsPublic ? "public" : field.IsPrivate ? "private" : field.IsFamily ? "protected" : field.IsAssembly ? "internal" : "private";
            var staticModifier = field.IsStatic ? " static" : "";
            var decl = $"{access}{staticModifier} {field.FieldType.FullName} {field.Name}; // {(offsetStr != null ? offsetStr : (field.FieldOffset.HasValue ? "0x" + offset.ToString("X") : "Unknown Offset"))}";
            fields.Add((offset, decl));
        }
        
        foreach (var field in fields.OrderBy(f => f.Offset))
        {
            sb.AppendLine(field.Declaration);
        }
        
        if (fields.Count == 0) sb.AppendLine("// No fields found.");
        
        sb.AppendLine();
        sb.AppendLine("// --- Properties ---");
        var props = type.Properties.ToList();
        if (props.Count == 0) sb.AppendLine("// No properties found.");
        foreach(var prop in props) {
           var getter = prop.GetMethod != null ? "get; " : "";
           var setter = prop.SetMethod != null ? "set; " : "";
           sb.AppendLine($"public {prop.PropertySig?.RetType?.FullName ?? "Unknown"} {prop.Name} {{ {getter}{setter}}}");
        }

        sb.AppendLine();
        sb.AppendLine("// --- Methods ---");
        var methods = type.Methods.Where(m => !m.IsGetter && !m.IsSetter && !m.IsAddOn && !m.IsRemoveOn).ToList();
        if (methods.Count == 0) sb.AppendLine("// No methods found.");
        foreach(var method in methods) {
           var rva = GetMethodAddressRva(method);
           var access = method.IsPublic ? "public" : method.IsPrivate ? "private" : method.IsFamily ? "protected" : method.IsAssembly ? "internal" : "private";
           var staticModifier = method.IsStatic ? " static" : "";
           sb.AppendLine($"{access}{staticModifier} {RenderMethodSignature(method)} // {(rva != null ? "RVA: " + rva : "No RVA")}");
        }
        
        return sb.ToString();
    });

    public Task<string> GetMethodRvaAsync(string assemblyPath, string typeFullName, string methodName, string[]? parameterTypeNames) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var type = FindType(module, typeFullName);
        var method = FindMethod(type, methodName, parameterTypeNames);
        
        var rva = GetMethodAddressRva(method);
        if (rva == null)
            return "No RVA found for this method. (Not an Il2Cpp dump or missing AddressAttribute).";
            
        return $"Method: {RenderMethodSignature(method)}\nRVA: {rva}";
    });

    public Task<string> SearchByOffsetAsync(string assemblyPath, string offsetQuery) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        
        // Parse the query into a numeric value for reliable matching
        var targetValue = TryParseHexValue(offsetQuery);
        if (targetValue == null)
        {
            // Try parsing as decimal
            if (ulong.TryParse(offsetQuery, out var decVal))
                targetValue = decVal;
            else
                return "Invalid offset query format. Use decimal (e.g. 5840) or hex (e.g. 0x16D0).";
        }
            
        var results = new List<string>();
        
        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            foreach (var field in type.Fields)
            {
                var offset = GetFieldOffset(field);
                var fieldValue = TryParseHexValue(offset);
                if (fieldValue != null && fieldValue == targetValue)
                {
                    var access = field.IsPublic ? "public" : field.IsPrivate ? "private" : field.IsFamily ? "protected" : field.IsAssembly ? "internal" : "private";
                    results.Add($"{access} {field.FieldType.FullName} {field.Name}; // {offset} | Type={type.FullName}");
                }
            }
            
            foreach (var method in type.Methods)
            {
                var rva = GetMethodAddressRva(method);
                var rvaValue = TryParseHexValue(rva);
                if (rvaValue != null && rvaValue == targetValue)
                {
                    var access = method.IsPublic ? "public" : method.IsPrivate ? "private" : method.IsFamily ? "protected" : method.IsAssembly ? "internal" : "private";
                    var retType = method.ReturnType.FullName;
                    var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type.FullName} {p.Name}"));
                    results.Add($"{access} {retType} {method.Name}({parameters}) // RVA: {rva} | Type={type.FullName}");
                }
            }
        }
        
        return results.Count == 0 ? "No matches found for offset 0x" + targetValue?.ToString("X") : string.Join(Environment.NewLine, results);
    });

    public Task<string> DecompileTypeAsync(string assemblyPath, string typeFullName) => Task.Run(() =>
    {
        var asm = GetOrLoad(assemblyPath);
        var type = FindType(asm.Module, typeFullName);
        var code = asm.Decompiler.Value.DecompileTypeAsString(new FullTypeName(type.ReflectionFullName));

        return $"// dnSpy location: TypeDef token {FormatToken(type.MDToken.Raw)}{Environment.NewLine}{code}";
    });

    public Task<string> DecompileMethodAsync(string assemblyPath, string typeFullName, string methodName, string[]? parameterTypeNames) => Task.Run(() =>
    {
        var asm = GetOrLoad(assemblyPath);
        var type = FindType(asm.Module, typeFullName);
        var method = FindMethod(type, methodName, parameterTypeNames);

        var handle = MetadataTokens.EntityHandle((int)method.MDToken.Raw);
        var code = asm.Decompiler.Value.DecompileAsString(new[] { handle });

        var rva = GetMethodAddressRva(method);
        if (rva != null)
        {
            var isDummy = method.Body == null || 
                          (method.Body.Instructions.Count <= 3 && method.Body.Instructions.Any(i => i.OpCode.Code == dnlib.DotNet.Emit.Code.Newobj && i.Operand?.ToString()?.Contains("NotImplementedException") == true));
            if (isDummy)
            {
                code += $"\n\n// [DnSpyMCP Warning]: This appears to be an Il2Cpp dummy dump. There is no IL code here.\n// If you want to modify this behavior, you must patch GameAssembly.dll at RVA {rva}";
            }
        }

        return $"// dnSpy location: TypeDef {FormatToken(type.MDToken.Raw)}, MethodDef {FormatToken(method.MDToken.Raw)}{Environment.NewLine}{code}";
    });

    public Task<string> GetMethodIlAsync(string assemblyPath, string typeFullName, string methodName, string[]? parameterTypeNames) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var type = FindType(module, typeFullName);
        var method = FindMethod(type, methodName, parameterTypeNames);

        if (!method.HasBody || method.Body is null)
            return "Method has no IL body.";

        var sb = new StringBuilder();
        sb.AppendLine($"dnSpy location: TypeDef {FormatToken(type.MDToken.Raw)}, MethodDef {FormatToken(method.MDToken.Raw)}");
        sb.AppendLine(RenderMethodSignature(method));
        foreach (var instruction in method.Body.Instructions)
        {
            var operand = instruction.Operand is null ? string.Empty : $" {instruction.Operand}";
            sb.AppendLine($"IL_{instruction.Offset:X4}: {instruction.OpCode}{operand}");
        }

        return sb.ToString();
    });

    public Task<string> SearchMembersAsync(string assemblyPath, string query, int maxResults) => Task.Run(() =>
    {
        if (maxResults <= 0)
            maxResults = 500;

        var module = GetOrLoad(assemblyPath).Module;
        var results = new List<string>(capacity: Math.Min(maxResults, 1000));

        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            if (ContainsIgnoreCase(type.FullName, query))
                results.Add($"type: {type.FullName} | TypeDef={FormatToken(type.MDToken.Raw)}");

            foreach (var method in type.Methods)
            {
                if (ContainsIgnoreCase(method.Name, query)) 
                {
                    var methodLine = $"method: {RenderMethodSignature(method)} | Type={type.FullName} | TypeDef={FormatToken(type.MDToken.Raw)} | MethodDef={FormatToken(method.MDToken.Raw)}";
                    var rva = GetMethodAddressRva(method);
                    if (rva != null)
                        methodLine += $" | Il2CppRVA={rva}";
                    results.Add(methodLine);
                }
            }

            foreach (var field in type.Fields)
            {
                if (ContainsIgnoreCase(field.Name, query))
                {
                    var fieldLine = $"field: {field.Name} | Type={type.FullName} | TypeDef={FormatToken(type.MDToken.Raw)} | FieldDef={FormatToken(field.MDToken.Raw)}";
                    var offset = GetFieldOffset(field);
                    if (offset != null)
                        fieldLine += $" | Il2CppFieldOffset={offset}";
                    results.Add(fieldLine);
                }
            }

            foreach (var property in type.Properties)
            {
                if (ContainsIgnoreCase(property.Name, query))
                {
                    results.Add($"property: {property.Name} | Type={type.FullName} | TypeDef={FormatToken(type.MDToken.Raw)} | PropertyDef={FormatToken(property.MDToken.Raw)}");
                }
            }

            if (results.Count >= maxResults)
                break;
        }

        return results.Count == 0 ? "No matches found." : string.Join(Environment.NewLine, results.Take(maxResults));
    });

    public Task<string> FindStringReferencesAsync(string assemblyPath, string text, bool caseSensitive, int maxResults) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Search text cannot be empty.");

        if (maxResults <= 0)
            maxResults = 500;

        var module = GetOrLoad(assemblyPath).Module;
        var results = new List<string>(Math.Min(maxResults, 1000));

        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody || method.Body is null)
                    continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not string literal)
                        continue;

                    if (!Contains(literal, text, caseSensitive))
                        continue;

                    var match = $"{type.FullName}.{RenderMethodSignature(method)} | TypeDef={FormatToken(type.MDToken.Raw)} | MethodDef={FormatToken(method.MDToken.Raw)} | IL_{instruction.Offset:X4} | \"{literal}\"";
                    results.Add(match);

                    if (results.Count >= maxResults)
                        return string.Join(Environment.NewLine, results);
                }
            }
        }

        return results.Count == 0 ? "No string references found." : string.Join(Environment.NewLine, results);
    });

    public Task<string> ListMethodsAsync(string assemblyPath, string typeFullName) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var type = FindType(module, typeFullName);

        var methods = type.Methods
            .Where(m => !m.IsGetter && !m.IsSetter && !m.IsAddOn && !m.IsRemoveOn)
            .Select(m => $"{RenderMethodSignature(m)} | MethodDef={FormatToken(m.MDToken.Raw)}")
            .OrderBy(m => m)
            .ToArray();

        return methods.Length == 0 ? "No methods found." : string.Join(Environment.NewLine, methods);
    });

    public Task<string> PatchReplaceStringLiteralAsync(string assemblyPath, string methodDefToken, string ilOffset, string newText, bool inPlace = false, string? outputPath = null) => Task.Run(() =>
    {
        var sourcePath = NormalizePath(assemblyPath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Assembly not found: {sourcePath}");

        var destinationPath = ResolvePatchDestination(sourcePath, inPlace, outputPath);
        var backupPath = BuildBackupPath(sourcePath);
        File.Copy(sourcePath, backupPath, overwrite: false);

        var bytes = File.ReadAllBytes(sourcePath);
        using var module = ModuleDefMD.Load(bytes);
        var method = ResolveMethodByToken(module, methodDefToken);
        if (!method.HasBody || method.Body is null)
            throw new InvalidOperationException($"Method has no IL body: {methodDefToken}");

        var offset = ParseIlOffset(ilOffset);
        var instruction = method.Body.Instructions.FirstOrDefault(i => i.Offset == offset)
            ?? throw new InvalidOperationException($"IL offset not found in method {methodDefToken}: IL_{offset:X4}");

        if (instruction.Operand is not string oldText)
            throw new InvalidOperationException($"Instruction at IL_{offset:X4} is not a string literal (ldstr).");

        instruction.Operand = newText;
        module.Write(destinationPath);
        
        if (inPlace)
        {
            _cache.TryRemove(sourcePath, out var oldLazy);
            if (oldLazy != null && oldLazy.IsValueCreated)
                oldLazy.Value.Dispose();
        }

        return string.Join(Environment.NewLine,
            "Patch applied: replace string literal",
            $"source: {sourcePath}",
            $"backup: {backupPath}",
            $"output: {destinationPath}",
            $"method: {FormatToken(method.MDToken.Raw)}",
            $"offset: IL_{offset:X4}",
            $"old: \"{oldText}\"",
            $"new: \"{newText}\"");
    });

    public Task<string> PatchNopInstructionsAsync(string assemblyPath, string methodDefToken, string ilOffset, int count, bool inPlace = false, string? outputPath = null) => Task.Run(() =>
    {
        if (count <= 0)
            throw new InvalidOperationException("count must be > 0");

        try
        {
            var sourcePath = NormalizePath(assemblyPath);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Assembly not found: {sourcePath}");

            var destinationPath = ResolvePatchDestination(sourcePath, inPlace, outputPath);
            var backupPath = BuildBackupPath(sourcePath);
            File.Copy(sourcePath, backupPath, overwrite: false);

            var bytes = File.ReadAllBytes(sourcePath);
            using var module = ModuleDefMD.Load(bytes);
            var method = ResolveMethodByToken(module, methodDefToken);
            if (!method.HasBody || method.Body is null)
                throw new InvalidOperationException($"Method has no IL body: {methodDefToken}");

            var offset = ParseIlOffset(ilOffset);
            var instructions = method.Body.Instructions;
            var startIndex = -1;
            for (var i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].Offset == offset)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex < 0)
                throw new InvalidOperationException($"IL offset not found in method {methodDefToken}: IL_{offset:X4}");

            var end = Math.Min(startIndex + count, instructions.Count);
            for (var i = startIndex; i < end; i++)
            {
                instructions[i] = new dnlib.DotNet.Emit.Instruction(dnlib.DotNet.Emit.OpCodes.Nop);
            }

            module.Write(destinationPath);
            
            if (inPlace)
            {
                _cache.TryRemove(sourcePath, out var oldLazy);
                if (oldLazy != null && oldLazy.IsValueCreated)
                    oldLazy.Value.Dispose();
            }

            return string.Join(Environment.NewLine,
                "Patch applied: NOP instructions",
                $"source: {sourcePath}",
                $"backup: {backupPath}",
                $"output: {destinationPath}",
                $"method: {FormatToken(method.MDToken.Raw)}",
                $"startOffset: IL_{offset:X4}",
                $"count: {end - startIndex}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Patch NOP failed: {ex.GetType().Name}: {ex.Message}", ex);
        }
    });

    public Task<string> FindMethodCallersAsync(string assemblyPath, string typeFullName, string methodName) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var targetType = FindType(module, typeFullName);
        var targetMethod = FindMethod(targetType, methodName, null);

        var results = new List<string>();

        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody || method.Body is null) continue;

                foreach (var instr in method.Body.Instructions)
                {
                    if (instr.Operand is dnlib.DotNet.IMethod operandMethod)
                    {
                        if (operandMethod.Name == targetMethod.Name && operandMethod.DeclaringType.FullName == targetMethod.DeclaringType.FullName)
                        {
                            var rva = GetMethodAddressRva(method);
                            results.Add($"{type.FullName}.{method.Name} calls {targetMethod.Name} at IL_{instr.Offset:X4} | TypeDef={FormatToken(type.MDToken.Raw)} MethodDef={FormatToken(method.MDToken.Raw)} {(rva != null ? "RVA=" + rva : "")}");
                            break;
                        }
                    }
                }
            }
        }
        return results.Count == 0 ? "No callers found." : string.Join(Environment.NewLine, results);
    });

    public Task<string> FindFieldReferencesAsync(string assemblyPath, string typeFullName, string fieldName) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var targetType = FindType(module, typeFullName);
        var targetField = targetType.Fields.FirstOrDefault(f => f.Name == fieldName) ?? throw new InvalidOperationException($"Field not found: {fieldName}");

        var results = new List<string>();

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
                            var rva = GetMethodAddressRva(method);
                            var action = (instr.OpCode.Code == dnlib.DotNet.Emit.Code.Stfld || instr.OpCode.Code == dnlib.DotNet.Emit.Code.Stsfld) ? "writes" : "reads";
                            results.Add($"{type.FullName}.{method.Name} {action} {targetField.Name} at IL_{instr.Offset:X4} | TypeDef={FormatToken(type.MDToken.Raw)} MethodDef={FormatToken(method.MDToken.Raw)} {(rva != null ? "RVA=" + rva : "")}");
                        }
                    }
                }
            }
        }
        return results.Count == 0 ? "No references found." : string.Join(Environment.NewLine, results);
    });

    public Task<string> FindDerivedTypesAsync(string assemblyPath, string baseTypeFullName) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var targetType = FindType(module, baseTypeFullName);

        var results = new List<string>();

        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            if (type.BaseType != null && type.BaseType.FullName == targetType.FullName)
            {
                results.Add($"Inherits: {type.FullName} | TypeDef={FormatToken(type.MDToken.Raw)}");
            }
            else if (type.Interfaces.Any(i => i.Interface?.FullName == targetType.FullName))
            {
                results.Add($"Implements: {type.FullName} | TypeDef={FormatToken(type.MDToken.Raw)}");
            }
        }
        return results.Count == 0 ? "No derived types found." : string.Join(Environment.NewLine, results);
    });

    public Task<string> LookupTokenAsync(string assemblyPath, string tokenText) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        uint token;
        try
        {
            token = ParseHexToken(tokenText);
        }
        catch
        {
            return "Invalid token format. Use hex (e.g., 0x06001234).";
        }

        var member = module.ResolveToken(token);
        if (member == null)
            return $"Token {FormatToken(token)} not found in this module.";

        if (member is TypeDef typeDef)
        {
            return $"Token {FormatToken(token)} resolves to Type: {typeDef.FullName}";
        }
        else if (member is MethodDef methodDef)
        {
            var rva = GetMethodAddressRva(methodDef);
            return $"Token {FormatToken(token)} resolves to Method: {methodDef.DeclaringType.FullName}.{RenderMethodSignature(methodDef)} // {(rva != null ? "RVA: " + rva : "No RVA")}";
        }
        else if (member is FieldDef fieldDef)
        {
            var offset = GetFieldOffset(fieldDef);
            return $"Token {FormatToken(token)} resolves to Field: {fieldDef.DeclaringType.FullName}.{fieldDef.Name} // {(offset != null ? "Offset: " + offset : "No Offset")}";
        }
        else if (member is PropertyDef propDef)
        {
            return $"Token {FormatToken(token)} resolves to Property: {propDef.DeclaringType.FullName}.{propDef.Name}";
        }
        else if (member is IMemberDef memberDef)
        {
            return $"Token {FormatToken(token)} resolves to Member: {memberDef.FullName}";
        }

        return $"Token {FormatToken(token)} resolves to {member.GetType().Name}: {member}";
    });

    public Task<string> FindNetworkHandlersAsync(string assemblyPath) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var results = new List<string>();
        
        var networkKeywords = new[] { "Send", "Receive", "Socket", "Tcp", "Udp", "Network", "Packet", "Message", "OnMessage", "Serialize", "Deserialize", "Client", "Server" };
        var networkTypes = new[] { "System.Net.Sockets.Socket", "System.Net.Sockets.TcpClient", "System.Net.Sockets.UdpClient", "System.Net.Sockets.NetworkStream", "UnityEngine.Networking", "Mirror", "Photon", "Lidgren", "NetMQ" };

        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            bool isNetworkType = false;
            if (networkKeywords.Any(k => type.Name.String.Contains(k, StringComparison.OrdinalIgnoreCase)))
                isNetworkType = true;
            
            foreach (var field in type.Fields)
            {
                if (networkTypes.Any(t => field.FieldType.FullName.Contains(t)))
                {
                    isNetworkType = true;
                    var offset = GetFieldOffset(field);
                    results.Add($"[Network Field] {type.FullName}.{field.Name} ({field.FieldType.FullName}) // {(offset != null ? "Offset: " + offset : "")}");
                }
            }

            foreach (var method in type.Methods)
            {
                bool matchesName = networkKeywords.Any(k => method.Name.String.Contains(k, StringComparison.OrdinalIgnoreCase));
                bool matchesParams = method.Parameters.Any(p => networkTypes.Any(nt => p.Type.FullName.Contains(nt)));
                
                if (matchesName && (isNetworkType || matchesParams))
                {
                    var rva = GetMethodAddressRva(method);
                    results.Add($"[Network Method] {type.FullName}.{RenderMethodSignature(method)} // {(rva != null ? "RVA: " + rva : "No RVA")}");
                }
            }
        }
        return results.Count == 0 ? "No network handlers found." : string.Join(Environment.NewLine, results);
    });

    public Task<string> FindCryptoUsageAsync(string assemblyPath) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var results = new List<string>();
        
        var cryptoKeywords = new[] { "Encrypt", "Decrypt", "Crypto", "Cipher", "Aes", "Rsa", "Des", "Md5", "Sha1", "Sha256", "Obfuscate", "Xor" };
        var cryptoNamespaces = new[] { "System.Security.Cryptography" };

        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            foreach (var method in type.Methods)
            {
                bool isCrypto = false;
                if (cryptoKeywords.Any(k => method.Name.String.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    isCrypto = true;
                    
                if (!isCrypto && method.HasBody && method.Body != null)
                {
                    foreach(var instr in method.Body.Instructions)
                    {
                        if (instr.Operand is dnlib.DotNet.IMethod opMethod)
                        {
                            if (cryptoNamespaces.Any(ns => opMethod.DeclaringType.FullName.StartsWith(ns)))
                            {
                                isCrypto = true;
                                break;
                            }
                        }
                    }
                }
                
                if (isCrypto)
                {
                    var rva = GetMethodAddressRva(method);
                    results.Add($"[Crypto Logic] {type.FullName}.{RenderMethodSignature(method)} // {(rva != null ? "RVA: " + rva : "No RVA")}");
                }
            }
        }
        return results.Count == 0 ? "No crypto usage found." : string.Join(Environment.NewLine, results);
    });

    public Task<string> ScanSecretsAsync(string assemblyPath) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var results = new List<string>();
        
        var ipRegex = new Regex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
        var urlRegex = new Regex(@"https?://[\w\-._~:/?#[\]@!$&'()*+,;=]+");
        var wsRegex = new Regex(@"wss?://[\w\-._~:/?#[\]@!$&'()*+,;=]+");
        
        foreach (var type in module.GetTypes().Where(t => !t.IsGlobalModuleType))
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody || method.Body is null) continue;

                foreach (var instr in method.Body.Instructions)
                {
                    if (instr.Operand is string literal && literal.Length > 3)
                    {
                        if (ipRegex.IsMatch(literal))
                            results.Add($"[IP Address] \"{literal}\" in {type.FullName}.{method.Name}");
                        else if (urlRegex.IsMatch(literal) || wsRegex.IsMatch(literal))
                            results.Add($"[URL/Endpoint] \"{literal}\" in {type.FullName}.{method.Name}");
                        else if (literal.Length > 20 && literal.Length < 100 && !literal.Contains(" ") && !literal.Contains("<") && !literal.Contains("{") && !literal.Contains("_") && !literal.Contains("."))
                        {
                            // Basic heuristic for Base64, Tokens, or API Keys: Long string, no typical code/formatting symbols.
                            results.Add($"[Potential Key/Secret] \"{literal}\" in {type.FullName}.{method.Name}");
                        }
                    }
                }
            }
        }
        
        return results.Count == 0 ? "No secrets found." : string.Join(Environment.NewLine, results);
    });

    // ── Multi-Assembly Search ─────────────────────────────────────────────

    public Task<string> SearchWorkspaceAsync(string directoryPath, string query, int maxResultsPerDll, bool recursive) => Task.Run(() =>
    {
        var normalizedDir = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(normalizedDir))
            throw new DirectoryNotFoundException($"Directory not found: {normalizedDir}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var dllFiles = Directory.GetFiles(normalizedDir, "*.dll", searchOption);

        if (dllFiles.Length == 0)
            return $"No .dll files found in {normalizedDir}";

        var allResults = new List<string>();
        var errors = new List<string>();

        foreach (var dllPath in dllFiles.OrderBy(f => f))
        {
            try
            {
                var result = SearchMembersAsync(dllPath, query, maxResultsPerDll).GetAwaiter().GetResult();
                if (result != "No matches found.")
                {
                    var dllName = Path.GetFileName(dllPath);
                    allResults.Add($"--- {dllName} ---");
                    allResults.Add(result);
                    allResults.Add("");
                }
            }
            catch
            {
                errors.Add($"[Skipped] {Path.GetFileName(dllPath)} (not a valid .NET assembly)");
            }
        }

        if (allResults.Count == 0)
        {
            var msg = $"No matches found for \"{query}\" across {dllFiles.Length} DLLs in {normalizedDir}";
            if (errors.Count > 0)
                msg += "\n" + string.Join("\n", errors);
            return msg;
        }

        var summary = $"Search results for \"{query}\" across {dllFiles.Length} DLLs:\n\n";
        return summary + string.Join(Environment.NewLine, allResults);
    });

    // ── Dump.cs Bridge ────────────────────────────────────────────────────

    public Task<string> ResolveDumpLineAsync(string dumpCsPath, int lineNumber, string? assemblyPath) => Task.Run(() =>
    {
        var normalizedPath = Path.GetFullPath(dumpCsPath);
        if (!File.Exists(normalizedPath))
            throw new FileNotFoundException($"dump.cs not found: {normalizedPath}");

        // Read a window of lines around the target line to find context
        var contextSize = 100;
        var startLine = Math.Max(0, lineNumber - contextSize);
        var lines = File.ReadLines(normalizedPath).Skip(startLine).Take(contextSize * 2 + 1).ToArray();

        var relativeLineIndex = Math.Min(lineNumber - startLine - 1, lines.Length - 1);
        if (relativeLineIndex < 0 || relativeLineIndex >= lines.Length)
            return $"Line {lineNumber} is out of range.";

        var targetLine = lines[relativeLineIndex].Trim();

        // Walk backwards to find the enclosing class and namespace
        string? currentNamespace = null;
        string? currentClass = null;
        string? baseClass = null;
        var memberContext = new List<string>();

        // Regex patterns for Il2CppDumper output
        var nsRegex = new Regex(@"^// Namespace:\s*(.*)$");
        var classRegex = new Regex(@"^(?:public\s+|private\s+|protected\s+|internal\s+)?(?:abstract\s+|sealed\s+|static\s+)*(?:class|struct|enum|interface)\s+(\S+)(?:\s*:\s*(.+))?");
        var methodRegex = new Regex(@"^\s*(?:public|private|protected|internal).*?\s+(\S+)\(.*\).*//\s*RVA:\s*(0x[\dA-Fa-f]+)");
        var fieldRegex = new Regex(@"^\s*(?:public|private|protected|internal).*?\s+(\S+)\s+(\S+);\s*//\s*(0x[\dA-Fa-f]+)");

        for (var i = relativeLineIndex; i >= 0; i--)
        {
            var line = lines[i].Trim();

            if (currentClass == null)
            {
                var classMatch = classRegex.Match(line);
                if (classMatch.Success)
                {
                    currentClass = classMatch.Groups[1].Value;
                    baseClass = classMatch.Groups[2].Success ? classMatch.Groups[2].Value.Trim() : null;
                }
            }

            if (currentNamespace == null)
            {
                var nsMatch = nsRegex.Match(line);
                if (nsMatch.Success)
                {
                    currentNamespace = nsMatch.Groups[1].Value.Trim();
                    if (string.IsNullOrEmpty(currentNamespace))
                        currentNamespace = "(global)";
                    break; // Found both namespace and class
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"--- dump.cs line {lineNumber} ---");
        sb.AppendLine($"Content: {targetLine}");
        sb.AppendLine();

        var fullTypeName = !string.IsNullOrEmpty(currentNamespace) && currentNamespace != "(global)"
            ? $"{currentNamespace}.{currentClass}"
            : currentClass;

        sb.AppendLine($"Namespace: {currentNamespace ?? "Unknown"}");
        sb.AppendLine($"Type: {currentClass ?? "Unknown"}");
        sb.AppendLine($"Full Type Name: {fullTypeName ?? "Unknown"}");
        if (baseClass != null)
            sb.AppendLine($"Base/Interfaces: {baseClass}");

        // Parse the target line itself
        var methodMatch = methodRegex.Match(targetLine);
        if (methodMatch.Success)
        {
            sb.AppendLine($"Member Type: Method");
            sb.AppendLine($"Method Name: {methodMatch.Groups[1].Value}");
            sb.AppendLine($"RVA: {methodMatch.Groups[2].Value}");
        }

        var fieldMatch = fieldRegex.Match(targetLine);
        if (fieldMatch.Success)
        {
            sb.AppendLine($"Member Type: Field");
            sb.AppendLine($"Field Type: {fieldMatch.Groups[1].Value}");
            sb.AppendLine($"Field Name: {fieldMatch.Groups[2].Value}");
            sb.AppendLine($"Offset: {fieldMatch.Groups[3].Value}");
        }

        // If assemblyPath is provided, cross-reference with DummyDll
        if (!string.IsNullOrWhiteSpace(assemblyPath) && !string.IsNullOrWhiteSpace(fullTypeName))
        {
            try
            {
                sb.AppendLine();
                sb.AppendLine("--- DummyDll Cross-Reference ---");
                var analysis = AnalyzeTypeAsync(assemblyPath, fullTypeName).GetAwaiter().GetResult();
                sb.AppendLine(analysis);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Cross-reference failed: {ex.Message}");
                sb.AppendLine("Tip: The type name in dump.cs may not match the DummyDll exactly. Try search_members instead.");
            }
        }
        else if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            sb.AppendLine();
            sb.AppendLine("Tip: Provide assemblyPath to cross-reference this type against the DummyDll for full analysis (fields, offsets, methods, RVAs).");
        }

        return sb.ToString();
    });

    // ── C-Struct Layout ───────────────────────────────────────────────────

    public Task<string> GetTypeLayoutAsync(string assemblyPath, string typeFullName) => Task.Run(() =>
    {
        var module = GetOrLoad(assemblyPath).Module;
        var type = FindType(module, typeFullName);

        var sb = new StringBuilder();

        var baseType = type.BaseType?.FullName;
        var interfaces = type.Interfaces.Select(i => i.Interface.FullName).ToList();
        var classSize = type.ClassLayout?.ClassSize > 0 ? "0x" + type.ClassLayout.ClassSize.ToString("X") : "Unknown";

        sb.AppendLine($"// {type.FullName}{(baseType != null ? $" (Base: {baseType})" : "")}");
        if (interfaces.Any())
            sb.AppendLine($"// Implements: {string.Join(", ", interfaces)}");
        sb.AppendLine($"// Size: {classSize}");
        sb.AppendLine($"struct {SanitizeCName(type.Name)} {{");

        // Add Il2Cpp object header fields
        if (baseType == "System.Object" || baseType?.Contains("MonoBehaviour") == true || baseType?.Contains("ScriptableObject") == true)
        {
            sb.AppendLine("    void* klass;              // 0x00 (Il2Cpp internal)");
            sb.AppendLine("    void* monitor;            // 0x08 (Il2Cpp internal)");
        }

        // Build sorted field list
        var fields = new List<(uint Offset, string CType, string Name, string OriginalType, bool IsStatic)>();

        foreach (var field in type.Fields)
        {
            var offsetStr = GetFieldOffset(field);
            uint offset = 0;
            if (offsetStr != null && offsetStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                uint.TryParse(offsetStr[2..], System.Globalization.NumberStyles.HexNumber, null, out offset);
            else if (offsetStr != null)
                uint.TryParse(offsetStr, out offset);
            else if (field.FieldOffset.HasValue)
                offset = field.FieldOffset.Value;

            var cType = MapToCType(field.FieldType.FullName);
            fields.Add((offset, cType, field.Name, field.FieldType.FullName, field.IsStatic));
        }

        // Instance fields sorted by offset
        var instanceFields = fields.Where(f => !f.IsStatic).OrderBy(f => f.Offset).ToList();
        var staticFields = fields.Where(f => f.IsStatic).ToList();

        foreach (var field in instanceFields)
        {
            var padding = new string(' ', Math.Max(1, 22 - field.CType.Length));
            sb.AppendLine($"    {field.CType}{padding}{SanitizeCName(field.Name)};{new string(' ', Math.Max(1, 20 - field.Name.Length))}// 0x{field.Offset:X2} ({field.OriginalType})");
        }

        if (staticFields.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    // --- Static Fields (not in instance memory) ---");
            foreach (var field in staticFields)
            {
                var cType = MapToCType(field.OriginalType);
                sb.AppendLine($"    // static {cType} {SanitizeCName(field.Name)}; ({field.OriginalType})");
            }
        }

        sb.AppendLine("};");

        // Add method RVA list for Frida hooking
        var methods = type.Methods.Where(m => !m.IsGetter && !m.IsSetter && !m.IsAddOn && !m.IsRemoveOn).ToList();
        var methodsWithRva = methods.Where(m => GetMethodAddressRva(m) != null).ToList();

        if (methodsWithRva.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("// --- Method RVAs (for Frida/native hooking) ---");
            foreach (var method in methodsWithRva)
            {
                var rva = GetMethodAddressRva(method);
                sb.AppendLine($"// {RenderMethodSignature(method)} → RVA: {rva}");
            }
        }

        return sb.ToString();
    });

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        foreach (var lazy in _cache.Values)
        {
            if (lazy.IsValueCreated)
            {
                lazy.Value.Dispose();
            }
        }
        _cache.Clear();
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private LoadedAssembly GetOrLoad(string assemblyPath)
    {
        var normalized = NormalizePath(assemblyPath);
        if (!File.Exists(normalized))
            throw new FileNotFoundException($"Assembly not found: {normalized}");

        return _cache.GetOrAdd(normalized, path => new Lazy<LoadedAssembly>(() =>
        {
            var bytes = File.ReadAllBytes(path);
            var module = ModuleDefMD.Load(bytes);
            var settings = new DecompilerSettings(LanguageVersion.Latest)
            {
                ThrowOnAssemblyResolveErrors = false
            };

            var decompiler = new Lazy<CSharpDecompiler>(() => new CSharpDecompiler(path, settings));
            return new LoadedAssembly(path, module, decompiler);
        })).Value;
    }

    private static TypeDef FindType(ModuleDefMD module, string typeFullName)
    {
        var type = module.GetTypes().FirstOrDefault(t =>
            string.Equals(t.FullName, typeFullName, StringComparison.Ordinal) ||
            string.Equals(t.ReflectionFullName, typeFullName, StringComparison.Ordinal));

        return type ?? throw new InvalidOperationException($"Type not found: {typeFullName}");
    }

    private static MethodDef FindMethod(TypeDef type, string methodName, string[]? parameterTypeNames)
    {
        var candidates = type.Methods
            .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException($"Method not found: {type.FullName}.{methodName}");

        if (parameterTypeNames is { Length: > 0 })
        {
            var normalized = parameterTypeNames.Select(NormalizeTypeName).ToArray();
            var matched = candidates.Where(m => ParametersMatch(m, normalized)).ToArray();

            if (matched.Length == 1)
                return matched[0];

            if (matched.Length == 0)
                throw new InvalidOperationException(
                    $"No overload matched parameterTypeNames for {type.FullName}.{methodName}. Available: {string.Join(" | ", candidates.Select(RenderMethodSignature))}");

            throw new InvalidOperationException(
                $"Multiple overloads matched. Provide more specific parameterTypeNames. Matches: {string.Join(" | ", matched.Select(RenderMethodSignature))}");
        }

        if (candidates.Length == 1)
            return candidates[0];

        throw new InvalidOperationException(
            $"Ambiguous method name '{methodName}'. Provide parameterTypeNames. Available: {string.Join(" | ", candidates.Select(RenderMethodSignature))}");
    }

    private static bool ParametersMatch(MethodDef method, IReadOnlyList<string> normalizedParameterTypeNames)
    {
        var sigParams = method.MethodSig.Params;
        if (sigParams.Count != normalizedParameterTypeNames.Count)
            return false;

        for (var i = 0; i < sigParams.Count; i++)
        {
            var actual = NormalizeTypeName(sigParams[i].FullName);
            if (!string.Equals(actual, normalizedParameterTypeNames[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string RenderMethodSignature(MethodDef method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type.FullName} {p.Name}"));
        return $"{method.ReturnType.FullName} {method.Name}({parameters})";
    }

    private static string FormatToken(uint raw) => $"0x{raw:X8}";

    private static MethodDef ResolveMethodByToken(ModuleDefMD module, string methodDefToken)
    {
        var token = ParseHexToken(methodDefToken);
        var provider = module.ResolveToken(token) as MethodDef;
        return provider ?? throw new InvalidOperationException($"MethodDef token not found: {FormatToken(token)}");
    }

    private static uint ParseHexToken(string tokenText)
    {
        var t = tokenText.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            t = t[2..];

        if (!uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"Invalid token format: {tokenText}");

        return value;
    }

    private static int ParseIlOffset(string ilOffset)
    {
        var t = ilOffset.Trim();
        if (t.StartsWith("IL_", StringComparison.OrdinalIgnoreCase))
            t = t[3..];

        if (!int.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidOperationException($"Invalid IL offset: {ilOffset}");

        return value;
    }

    private static string ResolvePatchDestination(string sourcePath, bool inPlace, string? outputPath)
    {
        if (inPlace)
            return sourcePath;

        if (!string.IsNullOrWhiteSpace(outputPath))
            return NormalizePath(outputPath);

        var dir = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        return Path.Combine(dir, $"{name}.patched{ext}");
    }

    private static string BuildBackupPath(string sourcePath)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var candidate = $"{sourcePath}.{timestamp}.bak";
        return candidate;
    }

    private static string NormalizeTypeName(string typeName)
        => typeName.Replace(" ", string.Empty, StringComparison.Ordinal)
                   .Replace("+", "/", StringComparison.Ordinal);

    private static bool ContainsIgnoreCase(string source, string value)
        => source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string source, string value, bool caseSensitive)
        => source.Contains(value, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string input)
    {
        if (Path.IsPathRooted(input))
            return Path.GetFullPath(input);

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, input));
    }

    private static string? GetMethodAddressRva(MethodDef method) =>
        GetIl2CppMetadataAttributeValue(method.CustomAttributes, "AddressAttribute", "RVA");

    private static string? GetFieldOffset(FieldDef field) =>
        GetIl2CppMetadataAttributeValue(field.CustomAttributes, "FieldOffset", "Offset");

    private static string? GetIl2CppMetadataAttributeValue(IEnumerable<CustomAttribute> attributes, string attributeName, string argumentName)
    {
        foreach (var attr in attributes)
        {
            if (!attr.TypeFullName.Contains(attributeName))
                continue;
            
            try
            {
                var arg = attr.NamedArguments.FirstOrDefault(a => a.Name == argumentName);
                if (arg == null)
                    continue;

                var value = arg.Argument.Value;
                return value switch
                {
                    string s => s,
                    uint u => $"0x{u:X}",
                    ulong ul => $"0x{ul:X}",
                    _ => value?.ToString()
                };
            }
            catch
            {
                // Ignore malformed custom attribute blobs
                continue;
            }
        }
        return null;
    }

    /// <summary>
    /// Parse a hex string (with or without 0x prefix) into a numeric value for reliable comparison.
    /// Returns null if the input is null, empty, or unparseable.
    /// </summary>
    private static ulong? TryParseHexValue(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        var clean = hex.Trim();
        if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            clean = clean[2..];

        return ulong.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out var val) ? val : null;
    }

    /// <summary>
    /// Map a .NET type name to an approximate C type for struct layout output.
    /// </summary>
    private static string MapToCType(string dotnetType)
    {
        return dotnetType switch
        {
            "System.Boolean" => "bool",
            "System.Byte" => "uint8_t",
            "System.SByte" => "int8_t",
            "System.Int16" => "int16_t",
            "System.UInt16" => "uint16_t",
            "System.Int32" => "int32_t",
            "System.UInt32" => "uint32_t",
            "System.Int64" => "int64_t",
            "System.UInt64" => "uint64_t",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.Char" => "uint16_t",
            "System.IntPtr" => "intptr_t",
            "System.UIntPtr" => "uintptr_t",
            "System.String" => "String*",
            "System.Object" => "Il2CppObject*",
            "System.Void" => "void",
            _ when dotnetType.EndsWith("[]") => $"Il2CppArray*",
            _ when dotnetType.Contains("List`1") => "Il2CppList*",
            _ when dotnetType.Contains("Dictionary`2") => "Il2CppDictionary*",
            _ when !dotnetType.StartsWith("System.") => $"{SanitizeCName(dotnetType.Split('.').Last())}*",
            _ => "void*"
        };
    }

    /// <summary>
    /// Sanitize a .NET type/field name for use as a C identifier.
    /// </summary>
    private static string SanitizeCName(string name)
    {
        return name.Replace('<', '_').Replace('>', '_').Replace('`', '_').Replace('.', '_').Replace('+', '_').Replace('/', '_');
    }

    private sealed record LoadedAssembly(string Path, ModuleDefMD Module, Lazy<CSharpDecompiler> Decompiler) : IDisposable
    {
        public void Dispose() => Module.Dispose();
    }
}
