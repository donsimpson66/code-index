using System.Text.RegularExpressions;

namespace CodeIndex.Core;

internal static partial class MultiLanguageSymbolParser
{
    private static readonly Regex EcmaTypeRegex = new(@"\b(?<abstract>abstract\s+)?(?<kind>class|interface|enum|namespace|module)\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EcmaFunctionRegex = new(@"^\s*(?:export\s+(?:default\s+)?)?(?:async\s+)?function\s*\*?\s*(?<name>[A-Za-z_$][A-Za-z0-9_$]*)?\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EcmaTopLevelArrowRegex = new(@"^\s*(?:export\s+(?:default\s+)?)?(?:const|let|var)\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*=\s*(?:async\s*)?(?:\([^;]*\)|[A-Za-z_$][A-Za-z0-9_$]*)\s*=>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EcmaMemberRegex = new(@"^\s*(?:(?:public|private|protected|static|readonly|abstract|override|async|declare)\s+)*(?<accessor>get|set)?\s*\*?\s*(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EcmaArrowMemberRegex = new(@"^\s*(?:(?:public|private|protected|static|readonly|abstract|override|declare)\s+)*(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*=\s*(?:async\s*)?(?:\([^;]*\)|[A-Za-z_$][A-Za-z0-9_$]*)\s*=>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EcmaFunctionExpressionMemberRegex = new(@"^\s*(?:(?:public|private|protected|static|readonly|abstract|override|declare)\s+)*(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*=\s*(?:async\s+)?function\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EcmaTopLevelBindingRegex = new(@"^\s*(?:export\s+)?(?:default\s+)?(?:const|let|var)\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EcmaFieldRegex = new(@"^\s*(?:(?:public|private|protected|static|readonly|abstract|override|declare)\s+)*(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*(?::[^=;]+)?\s*(?:=[^;]+)?;", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EcmaEnumMemberRegex = new(@"^\s*(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*(?:=[^,}]*)?[,}]?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static IReadOnlyList<SymbolRecord> ParseEcmaScript(FileRecord file, string source)
    {
        var moduleName = BuildLogicalModuleQualifier(file.Path);
        var maskedLines = EcmaScriptScanner.Mask(source);
        var symbols = new List<SymbolRecord>();
        var declared = new HashSet<(string Kind, string QualifiedName)>();
        var scopes = new Stack<BraceScope>();
        var pendingScope = default(SymbolRecord);
        var braceDepth = 0;
        var callableBodyDepth = 0;
        var pendingCallableBody = false;
        var moduleSymbol = CreateContainerSymbol(file, Path.GetFileNameWithoutExtension(file.Path), moduleName, SymbolKinds.Module, 1);
        symbols.Add(moduleSymbol);
        declared.Add((moduleSymbol.Kind, moduleSymbol.QualifiedName));

        void Add(string name, string qualifiedName, string kind, string? parentId, int lineNumber, string line, bool isAbstract = false)
        {
            if (!declared.Add((kind, qualifiedName)))
            {
                return;
            }

            symbols.Add(CreateSymbol(file, name, qualifiedName, kind, parentId, lineNumber, line, ExtractAccessibility(line), IsStatic(line), isAbstract, false, IsOverride(line)));
        }

        for (var index = 0; index < maskedLines.Length; index++)
        {
            var line = maskedLines[index];
            var lineNumber = index + 1;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            while (scopes.Count > 0 && braceDepth < scopes.Peek().BodyDepth)
            {
                scopes.Pop();
            }

            if (callableBodyDepth > 0 && braceDepth < callableBodyDepth)
            {
                callableBodyDepth = 0;
            }

            var parent = scopes.Count > 0 ? scopes.Peek().Symbol : null;
            var typeMatch = EcmaTypeRegex.Match(line);
            SymbolRecord? declaredContainer = null;
            var declaredCallable = false;
            if (typeMatch.Success)
            {
                var name = typeMatch.Groups["name"].Value;
                var kind = typeMatch.Groups["kind"].Value switch
                {
                    "class" => SymbolKinds.Class,
                    "interface" => SymbolKinds.Interface,
                    "enum" => SymbolKinds.Enum,
                    _ => SymbolKinds.Namespace
                };
                var qualifiedName = BuildQualifiedName(parent?.QualifiedName ?? moduleName, name);
                Add(name, qualifiedName, kind, parent?.Id ?? moduleSymbol.Id, lineNumber, line, typeMatch.Groups["abstract"].Success);
                declaredContainer = symbols.LastOrDefault(symbol => symbol.Kind == kind && string.Equals(symbol.QualifiedName, qualifiedName, StringComparison.Ordinal));
            }
            else if (parent?.Kind == SymbolKinds.Enum)
            {
                var enumMember = EcmaEnumMemberRegex.Match(line);
                if (enumMember.Success && !line.TrimStart().StartsWith("}", StringComparison.Ordinal))
                {
                    var name = enumMember.Groups["name"].Value;
                    Add(name, BuildQualifiedName(parent.QualifiedName, name), SymbolKinds.Field, parent.Id, lineNumber, line);
                }
            }
            else
            {
                EcmaScriptScanner.TryJoinDeclaration(maskedLines, index, out var declaration, out _);
                var functionMatch = EcmaFunctionRegex.Match(declaration);
                var topLevelArrowMatch = parent is null ? EcmaTopLevelArrowRegex.Match(declaration) : Match.Empty;
                var memberMatch = parent is not null && IsTypeSymbol(parent) && callableBodyDepth == 0 ? EcmaMemberRegex.Match(declaration) : Match.Empty;
                var arrowMatch = parent is not null && IsTypeSymbol(parent) && callableBodyDepth == 0 ? EcmaArrowMemberRegex.Match(declaration) : Match.Empty;
                var functionExpressionMatch = parent is not null && IsTypeSymbol(parent) && callableBodyDepth == 0 ? EcmaFunctionExpressionMemberRegex.Match(declaration) : Match.Empty;
                var fieldMatch = parent is not null && IsTypeSymbol(parent) && callableBodyDepth == 0 ? EcmaFieldRegex.Match(line) : Match.Empty;
                var bindingMatch = parent is null ? EcmaTopLevelBindingRegex.Match(line) : Match.Empty;

                if (functionMatch.Success)
                {
                    var name = functionMatch.Groups["name"].Success ? functionMatch.Groups["name"].Value : "default";
                    Add(name, BuildQualifiedName(parent?.QualifiedName ?? moduleName, name), SymbolKinds.Method, parent?.Id ?? moduleSymbol.Id, lineNumber, line);
                    declaredCallable = true;
                }
                else if (topLevelArrowMatch.Success)
                {
                    var name = topLevelArrowMatch.Groups["name"].Value;
                    Add(name, BuildQualifiedName(moduleName, name), SymbolKinds.Method, moduleSymbol.Id, lineNumber, line);
                    declaredCallable = true;
                }
                else if (memberMatch.Success)
                {
                    var name = memberMatch.Groups["name"].Value;
                    var kind = memberMatch.Groups["accessor"].Success ? SymbolKinds.Property :
                        (string.Equals(name, "constructor", StringComparison.Ordinal) || string.Equals(name, parent!.Name, StringComparison.Ordinal)) ? SymbolKinds.Constructor : SymbolKinds.Method;
                    Add(name, BuildQualifiedName(parent!.QualifiedName, name), kind, parent.Id, lineNumber, line);
                    declaredCallable = true;
                }
                else if (arrowMatch.Success || functionExpressionMatch.Success)
                {
                    var match = arrowMatch.Success ? arrowMatch : functionExpressionMatch;
                    var name = match.Groups["name"].Value;
                    Add(name, BuildQualifiedName(parent!.QualifiedName, name), SymbolKinds.Method, parent.Id, lineNumber, line);
                    declaredCallable = true;
                }
                else if (fieldMatch.Success)
                {
                    var name = fieldMatch.Groups["name"].Value;
                    Add(name, BuildQualifiedName(parent!.QualifiedName, name), SymbolKinds.Field, parent.Id, lineNumber, line);
                }
                else if (bindingMatch.Success && !line.Contains("=>", StringComparison.Ordinal) && !line.Contains("function", StringComparison.Ordinal))
                {
                    var name = bindingMatch.Groups["name"].Value;
                    Add(name, BuildQualifiedName(moduleName, name), SymbolKinds.Field, moduleSymbol.Id, lineNumber, line);
                }
            }

            var opens = CountOccurrences(line, '{');
            var closes = CountOccurrences(line, '}');
            braceDepth += opens - closes;
            if (declaredContainer is not null && opens > closes)
            {
                scopes.Push(new BraceScope(braceDepth, declaredContainer));
            }
            else if (declaredContainer is not null)
            {
                pendingScope = declaredContainer;
            }
            else if (pendingScope is not null && opens > closes)
            {
                scopes.Push(new BraceScope(braceDepth, pendingScope));
                pendingScope = null;
            }

            if (declaredCallable && opens > closes)
            {
                callableBodyDepth = braceDepth;
                pendingCallableBody = false;
            }
            else if (declaredCallable)
            {
                pendingCallableBody = true;
            }
            else if (pendingCallableBody && opens > closes)
            {
                callableBodyDepth = braceDepth;
                pendingCallableBody = false;
            }
        }

        return symbols;
    }
}
