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

    // SYNTAX extraction — supports multi-line enum blocks like INTEGER { up(1), down(2) }
    private static readonly Regex SyntaxRegex = new(
        @"SYNTAX\s+([\s\S]+?)(?:\r?\n\s*(?:MAX-ACCESS|ACCESS|STATUS|DESCRIPTION|INDEX|DEFVAL|::=))",
        RegexOptions.Compiled);

    // Rich metadata extraction
    private static readonly Regex AccessRegex = new(
        @"(?:MAX-ACCESS|ACCESS)\s+([\w-]+)",
        RegexOptions.Compiled);

    private static readonly Regex StatusRegex = new(
        @"STATUS\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex UnitsRegex = new(
        @"UNITS\s+""([^""]*)""",
        RegexOptions.Compiled);

    private static readonly Regex DefValRegex = new(
        @"DEFVAL\s*\{\s*([^}]*)\s*\}",
        RegexOptions.Compiled);

    private static readonly Regex DisplayHintRegex = new(
        @"DISPLAY-HINT\s+""([^""]*)""",
        RegexOptions.Compiled);

    private static readonly Regex IndexPartsRegex = new(
        @"INDEX\s*\{\s*([^}]+)\s*\}",
        RegexOptions.Compiled);

    // Boundary: detects the start of the NEXT definition (to limit defRegion scope)
    private static readonly Regex NextDefinitionRegex = new(
        @"\n\s*\w[\w-]*\s+(?:OBJECT-TYPE|OBJECT IDENTIFIER|MODULE-IDENTITY|OBJECT-IDENTITY|NOTIFICATION-TYPE|MODULE-COMPLIANCE|OBJECT-GROUP|NOTIFICATION-GROUP)\b",
        RegexOptions.Compiled);

    // Syntax sub-patterns
    private static readonly Regex EnumValuesRegex = new(
        @"\{\s*((?:\w[\w-]*\s*\(\s*-?\d+\s*\)\s*,?\s*)+)\}",
        RegexOptions.Compiled);

    private static readonly Regex SingleEnumRegex = new(
        @"(\w[\w-]*)\s*\(\s*(-?\d+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex IntRangeRegex = new(
        @"\(\s*(-?\d+)\s*\.\.\s*(-?\d+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex SizeConstraintRegex = new(
        @"SIZE\s*\(\s*(\d+)\s*\.\.\s*(\d+)\s*\)",
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

                // Extract all metadata
                var defRegion = ExtractDefinitionRegion(content, name);
                if (defRegion != null)
                    ExtractMetadata(def, defRegion, moduleName);

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

            var defRegion = ExtractDefinitionRegion(content, name);
            if (defRegion != null)
                ExtractMetadata(def, defRegion, result.ModuleName);

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

    /// <summary>
    /// Extract all rich metadata from a definition region in the MIB source.
    /// </summary>
    private static void ExtractMetadata(MibDefinition def, string defRegion, string moduleName)
    {
        def.ModuleName = moduleName;

        // Description
        var descMatch = DescriptionRegex.Match(defRegion);
        if (descMatch.Success)
            def.Description = descMatch.Groups[1].Value.Trim();

        // Syntax (raw)
        var syntaxMatch = SyntaxRegex.Match(defRegion);
        if (syntaxMatch.Success)
            def.Syntax = syntaxMatch.Groups[1].Value.Trim();

        // Access
        var accessMatch = AccessRegex.Match(defRegion);
        if (accessMatch.Success)
            def.Access = accessMatch.Groups[1].Value;

        // Status
        var statusMatch = StatusRegex.Match(defRegion);
        if (statusMatch.Success)
            def.Status = statusMatch.Groups[1].Value;

        // Units
        var unitsMatch = UnitsRegex.Match(defRegion);
        if (unitsMatch.Success)
            def.Units = unitsMatch.Groups[1].Value;

        // Default value
        var defValMatch = DefValRegex.Match(defRegion);
        if (defValMatch.Success)
            def.DefVal = defValMatch.Groups[1].Value.Trim();

        // Display hint
        var hintMatch = DisplayHintRegex.Match(defRegion);
        if (hintMatch.Success)
            def.DisplayHint = hintMatch.Groups[1].Value;

        // Index parts (for table entries)
        var indexMatch = IndexPartsRegex.Match(defRegion);
        if (indexMatch.Success)
            def.IndexParts = indexMatch.Groups[1].Value.Trim();

        // Parse syntax breakdown
        ParseSyntaxDetails(def);
    }

    private static void ParseSyntaxDetails(MibDefinition def)
    {
        var syntax = def.Syntax;
        if (string.IsNullOrEmpty(syntax)) return;

        // Known direct types
        var knownTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Counter32"] = "Counter32",
            ["Counter64"] = "Counter64",
            ["Gauge32"] = "Gauge32",
            ["TimeTicks"] = "TimeTicks",
            ["IpAddress"] = "IpAddress",
            ["Opaque"] = "Opaque",
            ["Unsigned32"] = "Unsigned32",
            ["Integer32"] = "Integer32",
        };

        foreach (var kvp in knownTypes)
        {
            if (syntax.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                def.BaseType = kvp.Value;
                // Check for range on these types too
                var rangeMatch = IntRangeRegex.Match(syntax);
                if (rangeMatch.Success)
                {
                    def.RangeMin = int.Parse(rangeMatch.Groups[1].Value);
                    def.RangeMax = int.Parse(rangeMatch.Groups[2].Value);
                }
                return;
            }
        }

        // INTEGER with enum values: INTEGER { up(1), down(2) }
        if (syntax.StartsWith("INTEGER", StringComparison.OrdinalIgnoreCase))
        {
            def.BaseType = "INTEGER";

            var enumMatch = EnumValuesRegex.Match(syntax);
            if (enumMatch.Success)
            {
                def.EnumValues = new Dictionary<string, int>();
                foreach (Match m in SingleEnumRegex.Matches(enumMatch.Groups[1].Value))
                    def.EnumValues[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
                return;
            }

            var rangeMatch = IntRangeRegex.Match(syntax);
            if (rangeMatch.Success)
            {
                def.RangeMin = int.Parse(rangeMatch.Groups[1].Value);
                def.RangeMax = int.Parse(rangeMatch.Groups[2].Value);
            }
            return;
        }

        // OCTET STRING with SIZE
        if (syntax.IndexOf("OCTET STRING", StringComparison.OrdinalIgnoreCase) >= 0
            || syntax.IndexOf("DisplayString", StringComparison.OrdinalIgnoreCase) >= 0
            || syntax.IndexOf("SnmpAdminString", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            def.BaseType = "OCTET STRING";
            var sizeMatch = SizeConstraintRegex.Match(syntax);
            if (sizeMatch.Success)
            {
                def.SizeMin = int.Parse(sizeMatch.Groups[1].Value);
                def.SizeMax = int.Parse(sizeMatch.Groups[2].Value);
            }
            return;
        }

        if (syntax.IndexOf("OBJECT IDENTIFIER", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            def.BaseType = "OBJECT IDENTIFIER";
            return;
        }

        if (syntax.StartsWith("BITS", StringComparison.OrdinalIgnoreCase))
        {
            def.BaseType = "BITS";
            var enumMatch = EnumValuesRegex.Match(syntax);
            if (enumMatch.Success)
            {
                def.EnumValues = new Dictionary<string, int>();
                foreach (Match m in SingleEnumRegex.Matches(enumMatch.Groups[1].Value))
                    def.EnumValues[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
            }
            return;
        }

        // Textual conventions and other types — try to detect enum or range
        def.BaseType = syntax.Split('(', '{', ' ')[0].Trim();
        var enumFallback = EnumValuesRegex.Match(syntax);
        if (enumFallback.Success)
        {
            def.EnumValues = new Dictionary<string, int>();
            foreach (Match m in SingleEnumRegex.Matches(enumFallback.Groups[1].Value))
                def.EnumValues[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        }
        else
        {
            var rangeFallback = IntRangeRegex.Match(syntax);
            if (rangeFallback.Success)
            {
                def.RangeMin = int.Parse(rangeFallback.Groups[1].Value);
                def.RangeMax = int.Parse(rangeFallback.Groups[2].Value);
            }
            var sizeFallback = SizeConstraintRegex.Match(syntax);
            if (sizeFallback.Success)
            {
                def.SizeMin = int.Parse(sizeFallback.Groups[1].Value);
                def.SizeMax = int.Parse(sizeFallback.Groups[2].Value);
            }
        }
    }

    /// <summary>
    /// Find the definition start for a name — skips occurrences inside SEQUENCE blocks.
    /// Looks for "name OBJECT-TYPE", "name OBJECT IDENTIFIER", "name MODULE-IDENTITY", etc.
    /// </summary>
    /// <summary>
    /// Extract the definition region for a name, bounded by the next definition start.
    /// This prevents metadata leaking from one definition to another.
    /// </summary>
    private static string? ExtractDefinitionRegion(string content, string name)
    {
        var defStart = FindDefinitionStart(content, name);
        if (defStart < 0) return null;

        // Find the end: next definition boundary or max 3000 chars
        var maxLen = Math.Min(3000, content.Length - defStart);
        var region = content.Substring(defStart, maxLen);

        // Look for the next definition keyword (skip the first line which IS this definition)
        var nextDef = NextDefinitionRegex.Match(region, name.Length + 1);
        if (nextDef.Success)
            region = region.Substring(0, nextDef.Index);

        return region;
    }

    private static int FindDefinitionStart(string content, string name)
    {
        var searchFor = $"{name} ";
        int pos = 0;
        while (pos < content.Length)
        {
            var idx = content.IndexOf(searchFor, pos, StringComparison.Ordinal);
            if (idx < 0) return -1;

            // Check: is this inside a SEQUENCE { ... } block?
            // Look backwards for the nearest '{' or '}' to determine context
            bool insideSequence = false;
            var before = content.Substring(Math.Max(0, idx - 500), Math.Min(500, idx));
            var lastSeqStart = before.LastIndexOf("SEQUENCE", StringComparison.Ordinal);
            if (lastSeqStart >= 0)
            {
                var region = before.Substring(lastSeqStart);
                // If there's a '{' after SEQUENCE but no matching '}' before our position, we're inside
                int braceOpen = region.IndexOf('{');
                int braceClose = region.LastIndexOf('}');
                if (braceOpen >= 0 && (braceClose < 0 || braceClose < braceOpen))
                    insideSequence = true;
            }

            if (!insideSequence)
                return idx;

            pos = idx + searchFor.Length;
        }
        return -1;
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
