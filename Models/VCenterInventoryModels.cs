using System;

namespace VCenterMigrationTool.Models;

/// <summary>
/// Extended datacenter information for inventory
/// </summary>
public class DatacenterInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public int ClusterCount { get; set; }
    public int HostCount { get; set; }
    public int VmCount { get; set; }
    public int DatastoreCount { get; set; }
}

/// <summary>
/// Virtual machine information for inventory
/// </summary>
public class VirtualMachineInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string PowerState { get; set; } = string.Empty;
    public string GuestOS { get; set; } = string.Empty;
    public int CpuCount { get; set; }
    public double MemoryGB { get; set; }
    public double DiskGB { get; set; }
    public string HostName { get; set; } = string.Empty;
    public string ClusterName { get; set; } = string.Empty;
    public string DatacenterName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string ResourcePoolName { get; set; } = string.Empty;
    public DateTime? LastBackup { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Datastore information for inventory
/// </summary>
public class DatastoreInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double CapacityGB { get; set; }
    public double UsedGB { get; set; }
    public double FreeGB => CapacityGB - UsedGB;
    public double UtilizationPercent => CapacityGB > 0 ? (UsedGB / CapacityGB) * 100 : 0;
    public int VmCount { get; set; }
    public string[] ConnectedHosts { get; set; } = Array.Empty<string>();
    public bool IsShared => ConnectedHosts.Length > 1;
}

/// <summary>
/// Resource pool information for inventory
/// </summary>
public class ResourcePoolInventoryInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string ClusterName { get; set; } = string.Empty;
    public string DatacenterName { get; set; } = string.Empty;
    public string ParentPath { get; set; } = string.Empty;
    public int CpuLimitMhz { get; set; }
    public int MemoryLimitMB { get; set; }
    public int VmCount { get; set; }
}

/// <summary>
/// Virtual switch information for inventory
/// </summary>
public class VirtualSwitchInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // VDS, VSS
    public int PortGroupCount { get; set; }
    public string[] ConnectedHosts { get; set; } = Array.Empty<string>();
    public string DatacenterName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
}

/// <summary>
/// Folder information for inventory
/// </summary>
public class FolderInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // VM, Host, Datacenter, etc.
    public string Path { get; set; } = string.Empty;
    public string DatacenterName { get; set; } = string.Empty;
    public int ChildCount { get; set; }
}

/// <summary>
/// Tag information for inventory
/// </summary>
public class TagInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int AssignedObjectCount { get; set; }
}

/// <summary>
/// Category information for inventory
/// </summary>
public class CategoryInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TagCount { get; set; }
    public bool IsMultipleCardinality { get; set; }
}

/// <summary>
/// Container for tag and category data returned from PowerShell scripts
/// </summary>
public class TagCategoryData
{
    public CategoryData[] Categories { get; set; } = Array.Empty<CategoryData>();
    public TagData[] Tags { get; set; } = Array.Empty<TagData>();
}

/// <summary>
/// Raw category data from PowerShell script output
/// </summary>
public class CategoryData
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int TagCount { get; set; }
    public bool IsMultipleCardinality { get; set; }
    public string[]? AssociableTypes { get; set; }
}

/// <summary>
/// Raw tag data from PowerShell script output
/// </summary>
public class TagData
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryId { get; set; }
    public int AssignedObjectCount { get; set; }
}

/// <summary>
/// Role information for inventory
/// </summary>
public class RoleInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public string[] Privileges { get; set; } = Array.Empty<string>();
    public int AssignmentCount { get; set; }
}

/// <summary>
/// Permission information for inventory
/// </summary>
public class PermissionInfo
{
    public string Id { get; set; } = string.Empty;
    public string Principal { get; set; } = string.Empty; // User or group
    public string RoleName { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public bool Propagate { get; set; }
}

/// <summary>
/// Custom attribute information for inventory
/// </summary>
public class CustomAttributeInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsGlobal { get; set; }
    public string[] ApplicableTypes { get; set; } = Array.Empty<string>();
    public int AssignmentCount { get; set; }
}