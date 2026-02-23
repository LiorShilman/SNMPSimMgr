using System.IO;
using System.Text.RegularExpressions;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services;

public static class MibParserService
{
    // Well-known root OIDs that serve as the resolution base
    private static readonly Dictionary<string, string> RootOids = new()
    {
        ["iso"] = "1",
        ["org"] = "1.3",
        ["dod"] = "1.3.6",
        ["internet"] = "1.3.6.1",
        ["directory"] = "1.3.6.1.1",
        ["mgmt"] = "1.3.6.1.2",
        ["mib-2"] = "1.3.6.1.2.1",
        ["system"] = "1.3.6.1.2.1.1",
        ["interfaces"] = "1.3.6.1.2.1.2",
        ["at"] = "1.3.6.1.2.1.3",
        ["ip"] = "1.3.6.1.2.1.4",
        ["icmp"] = "1.3.6.1.2.1.5",
        ["tcp"] = "1.3.6.1.2.1.6",
        ["udp"] = "1.3.6.1.2.1.7",
        ["transmission"] = "1.3.6.1.2.1.10",
        ["snmp"] = "1.3.6.1.2.1.11",
        ["experimental"] = "1.3.6.1.3",
        ["private"] = "1.3.6.1.4",
        ["enterprises"] = "1.3.6.1.4.1",
        ["snmpV2"] = "1.3.6.1.6",
        ["snmpModules"] = "1.3.6.1.6.3",
    };

    // Regex: captures name and ::= { parent index } assignment
    private static readonly Regex AssignmentRegex = new(
        @"^(\w[\w-]*)\s+(?:OBJECT-TYPE|OBJECT\s+IDENTIFIER|OBJECT-IDENTITY|MODULE-IDENTITY|NOTIFICATION-TYPE|MODULE-COMPLIANCE|OBJECT-GROUP|NOTIFICATION-GROUP|AGENT-CAPABILITIES|TEXTUAL-CONVENTION)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex OidAssignRegex = new(
        @"::=\s*\{\s*(\w[\w-]*)\s+(\d+)\s*\}",
        RegexOptions.Compiled);

    // Simpler form: name OBJECT IDENTIFIER ::= { parent index }
    private static readonly Regex SimpleOidRegex = new(
        @"^(\w[\w-]*)\s+OBJECT\s+IDENTIFIER\s*::=\s*\{\s*(\w[\w-]*)\s+(\d+)\s*\}",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // DESCRIPTION extraction
    private static readonly Regex DescriptionRegex = new(
        @"DESCRIPTION\s*""([^""]*?)""",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // SYNTAX extraction
    private static readonly Regex SyntaxRegex = new(
        @"SYNTAX\s+([\w\s\(\)\.\-]+?)(?:\r?\n\s*(?:MAX-ACCESS|ACCESS|STATUS|DESCRIPTION|INDEX|::=))",
        RegexOptions.Compiled);

    // MODULE-IDENTITY or DEFINITIONS ::= BEGIN
    private static readonly Regex ModuleNameRegex = new(
        @"^(\w[\w-]*)\s+(?:DEFINITIONS\s*::=\s*BEGIN|MODULE-IDENTITY)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static MibFileInfo ParseFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return Parse(content, Path.GetFileName(filePath));
    }

    /// <summary>
    /// Parse multiple MIB files together, resolving cross-file dependencies.
    /// Names defined in one file can be used as parents in another.
    /// </summary>
    public static List<MibFileInfo> ParseMultiple(List<string> filePaths)
    {
        if (filePaths.Count == 0) return new();
        if (filePaths.Count == 1) return new() { ParseFile(filePaths[0]) };

        // Phase 1: Collect raw assignments from all files
        var perFile = new List<(string fileName, string moduleName, string content,
            Dictionary<string, (string parent, int index)> rawAssignments)>();

        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;

            var content = File.ReadAllText(path);
            var fileName = Path.GetFileName(path);

            var moduleMatch = ModuleNameRegex.Match(content);
            var moduleName = moduleMatch.Success ? moduleMatch.Groups[1].Value : fileName;

            var stripped = StripComments(content);
            var rawAssignments = CollectRawAssignments(stripped);

            perFile.Add((fileName, moduleName, stripped, rawAssignments));
        }

        // Phase 2: Merge all raw assignments and resolve together
        var allAssignments = new Dictionary<string, (string parent, int index)>();
        foreach (var (_, _, _, rawAssignments) in perFile)
        {
            foreach (var kvp in rawAssignments)
            {
                if (!allAssignments.ContainsKey(kvp.Key))
                    allAssignments[kvp.Key] = kvp.Value;
            }
        }

        // Resolve all names using the merged map
        var resolved = new Dictionary<string, string>(RootOids);
        int lastCount;
        do
        {
            lastCount = resolved.Count;
            foreach (var kvp in allAssignments)
            {
                var name = kvp.Key;
                var parent = kvp.Value.parent;
                var index = kvp.Value.index;
                if (resolved.ContainsKey(name)) continue;
                if (resolved.TryGetValue(parent, out var parentOid))
                    resolved[name] = $"{parentOid}.{index}";
            }
        } while (resolved.Count > lastCount);

        // Phase 3: Build MibFileInfo per file using the globally resolved names
        var results = new List<MibFileInfo>();

        foreach (var (fileName, moduleName, content, rawAssignments) in perFile)
        {
            var info = new MibFileInfo { FileName = fileName, ModuleName = moduleName };
            var definitions = new Dictionary<string, MibDefinition>();

            foreach (var kvp in rawAssignments)
            {
                var name = kvp.Key;
                var parent = kvp.Value.parent;
                var index = kvp.Value.index;
                if (!resolved.TryGetValue(name, out var oid)) continue;

                var def = new MibDefinition
                {
                    Name = name,
                    Oid = oid,
                    ParentName = parent,
                    Index = index
                };

                // Extract DESCRIPTION and SYNTAX
                var defStart = content.IndexOf($"{name} ", StringComparison.Ordinal);
                if (defStart >= 0)
                {
                    var defRegion = content.Substring(defStart, Math.Min(3000, content.Length - defStart));
                    var descMatch = DescriptionRegex.Match(defRegion);
                    if (descMatch.Success)
                        def.Description = descMatch.Groups[1].Value.Trim();
                    var syntaxMatch = SyntaxRegex.Match(defRegion);
                    if (syntaxMatch.Success)
                        def.Syntax = syntaxMatch.Groups[1].Value.Trim();
                }

                definitions[oid] = def;
            }

            info.Definitions = definitions.Values.ToList();
            info.DefinitionCount = info.Definitions.Count;
            results.Add(info);
        }

        return results;
    }

    public static MibFileInfo Parse(string content, string fileName)
    {
        var result = new MibFileInfo { FileName = fileName };

        // Detect module name
        var moduleMatch = ModuleNameRegex.Match(content);
        result.ModuleName = moduleMatch.Success ? moduleMatch.Groups[1].Value : fileName;

        // Strip single-line comments
        content = StripComments(content);

        // Phase 1: Collect raw assignments
        var rawAssignments = CollectRawAssignments(content);

        // Phase 2: Resolve all names to full numeric OIDs
        var resolved = new Dictionary<string, string>(RootOids);
        var definitions = new Dictionary<string, MibDefinition>();

        int lastCount;
        do
        {
            lastCount = resolved.Count;
            foreach (var kvp in rawAssignments)
            {
                var name = kvp.Key;
                var parent = kvp.Value.parent;
                var index = kvp.Value.index;
                if (resolved.ContainsKey(name)) continue;
                if (resolved.TryGetValue(parent, out var parentOid))
                    resolved[name] = $"{parentOid}.{index}";
            }
        } while (resolved.Count > lastCount);

        // Phase 3: Build MibDefinition objects
        foreach (var kvp2 in rawAssignments)
        {
            var name = kvp2.Key;
            var parent = kvp2.Value.parent;
            var index = kvp2.Value.index;
            if (!resolved.TryGetValue(name, out var oid)) continue;

            var def = new MibDefinition
            {
                Name = name,
                Oid = oid,
                ParentName = parent,
                Index = index
            };

            var defStart = content.IndexOf($"{name} ", StringComparison.Ordinal);
            if (defStart >= 0)
            {
                var defRegion = content.Substring(defStart, Math.Min(3000, content.Length - defStart));
                var descMatch = DescriptionRegex.Match(defRegion);
                if (descMatch.Success)
                    def.Description = descMatch.Groups[1].Value.Trim();
                var syntaxMatch = SyntaxRegex.Match(defRegion);
                if (syntaxMatch.Success)
                    def.Syntax = syntaxMatch.Groups[1].Value.Trim();
            }

            definitions[oid] = def;
        }

        result.Definitions = definitions.Values.ToList();
        result.DefinitionCount = result.Definitions.Count;
        return result;
    }

    private static Dictionary<string, (string parent, int index)> CollectRawAssignments(string content)
    {
        var rawAssignments = new Dictionary<string, (string parent, int index)>();

        // Simple OBJECT IDENTIFIER assignments (single line)
        foreach (Match m in SimpleOidRegex.Matches(content))
        {
            var name = m.Groups[1].Value;
            var parent = m.Groups[2].Value;
            var index = int.Parse(m.Groups[3].Value);
            rawAssignments[name] = (parent, index);
        }

        // Complex multi-line definitions (OBJECT-TYPE, MODULE-IDENTITY, etc.)
        var typeMatches = AssignmentRegex.Matches(content);
        foreach (Match typeMatch in typeMatches)
        {
            var name = typeMatch.Groups[1].Value;
            if (rawAssignments.ContainsKey(name)) continue;

            var searchStart = typeMatch.Index + typeMatch.Length;
            var searchRegion = content.Substring(searchStart, Math.Min(3000, content.Length - searchStart));
            var oidMatch = OidAssignRegex.Match(searchRegion);
            if (oidMatch.Success)
            {
                var parent = oidMatch.Groups[1].Value;
                var index = int.Parse(oidMatch.Groups[2].Value);
                rawAssignments[name] = (parent, index);
            }
        }

        return rawAssignments;
    }

    private static string StripComments(string content)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            bool inString = false;
            for (int j = 0; j < line.Length - 1; j++)
            {
                if (line[j] == '"') inString = !inString;
                if (!inString && line[j] == '-' && line[j + 1] == '-')
                {
                    lines[i] = line[..j];
                    break;
                }
            }
        }
        return string.Join("\n", lines);
    }
}
