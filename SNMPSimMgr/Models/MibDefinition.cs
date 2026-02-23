namespace SNMPSimMgr.Models;

public class MibDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Oid { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Syntax { get; set; }
    public string? ParentName { get; set; }
    public int Index { get; set; }
}

public class MibFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public int DefinitionCount { get; set; }
    public List<MibDefinition> Definitions { get; set; } = new();
}
