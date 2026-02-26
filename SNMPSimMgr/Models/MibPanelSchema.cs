namespace SNMPSimMgr.Models;

public class MibPanelSchema
{
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceIp { get; set; } = string.Empty;
    public int DevicePort { get; set; }
    public string Community { get; set; } = string.Empty;
    public string SnmpVersion { get; set; } = "V2c";
    public DateTime ExportedAt { get; set; }
    public int TotalFields { get; set; }
    public List<MibModuleSchema> Modules { get; set; } = new();
}

public class MibModuleSchema
{
    public string ModuleName { get; set; } = string.Empty;
    public int ScalarCount { get; set; }
    public int TableCount { get; set; }

    /// <summary>Scalar OIDs — single-value fields (e.g., sysDescr.0, sysName.0)</summary>
    public List<MibFieldSchema> Scalars { get; set; } = new();

    /// <summary>Table OIDs — multi-instance data with columns and rows</summary>
    public List<MibTableSchema> Tables { get; set; } = new();
}

/// <summary>
/// A single scalar field definition with its current value.
/// </summary>
public class MibFieldSchema
{
    // Identity — 1:1 OID mapping
    public string Oid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Access control — determines if field is read-only or editable
    public string Access { get; set; } = "read-only";
    public bool IsWritable { get; set; }

    // Type system — determines input control type
    public string InputType { get; set; } = "text";
    public string BaseType { get; set; } = "OCTET STRING";
    public string? Units { get; set; }
    public string? DisplayHint { get; set; }

    // Constraints — for input validation
    public int? MinValue { get; set; }
    public int? MaxValue { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public string? DefaultValue { get; set; }

    // Enum options — for dropdown/select
    public List<EnumOption>? Options { get; set; }

    // Current value (from walk data)
    public string? CurrentValue { get; set; }
    public string? CurrentValueType { get; set; }

    // Metadata
    public string? Status { get; set; }
    public string? TableIndex { get; set; }
}

/// <summary>
/// An SNMP table with column definitions and instance rows.
/// Angular app renders this as a data grid or repeated card sections.
/// </summary>
public class MibTableSchema
{
    public string Name { get; set; } = string.Empty;
    public string Oid { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LabelColumn { get; set; }       // column name used for row labels (e.g., "ifDescr")
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<MibFieldSchema> Columns { get; set; } = new();
    public List<MibTableRow> Rows { get; set; } = new();
}

/// <summary>
/// A single row (instance) in an SNMP table.
/// </summary>
public class MibTableRow
{
    public string Index { get; set; } = string.Empty;      // instance index (e.g., "1", "2")
    public string? Label { get; set; }                      // descriptive label (e.g., "FastEthernet0/1")
    public Dictionary<string, MibCellValue> Values { get; set; } = new();  // columnOid → value
}

/// <summary>
/// A single cell value in a table row.
/// </summary>
public class MibCellValue
{
    public string Value { get; set; } = string.Empty;
    public string? Type { get; set; }                       // SNMP value type
    public string? EnumLabel { get; set; }                  // resolved enum label (e.g., "up" for value 1)
}

public class EnumOption
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}
