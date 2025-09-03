using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.Models;

/// <summary>
/// Represents a node in the vCenter inventory tree view
/// </summary>
public partial class InventoryTreeNode : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty] 
    private string _details = string.Empty;

    [ObservableProperty]
    private SymbolRegular _iconSymbol = SymbolRegular.Folder24;

    [ObservableProperty]
    private InventoryNodeType _nodeType = InventoryNodeType.Unknown;

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private ObservableCollection<InventoryTreeNode> _children = new();

    public InventoryTreeNode()
    {
    }

    public InventoryTreeNode(string name, string details, SymbolRegular icon, InventoryNodeType nodeType, string id = "")
    {
        Name = name;
        Details = details;
        IconSymbol = icon;
        NodeType = nodeType;
        Id = id;
    }

    /// <summary>
    /// Helper method to create a datacenter node
    /// </summary>
    public static InventoryTreeNode CreateDatacenterNode(DatacenterInfo datacenter)
    {
        return new InventoryTreeNode(
            datacenter.Name,
            $"{datacenter.ClusterCount} clusters, {datacenter.HostCount} hosts, {datacenter.VmCount} VMs",
            SymbolRegular.Building24,
            InventoryNodeType.Datacenter,
            datacenter.Id
        );
    }

    /// <summary>
    /// Helper method to create a cluster node
    /// </summary>
    public static InventoryTreeNode CreateClusterNode(ClusterInfo cluster)
    {
        return new InventoryTreeNode(
            cluster.Name,
            $"{cluster.HostCount} hosts, {cluster.VmCount} VMs",
            SymbolRegular.Server24,
            InventoryNodeType.Cluster,
            cluster.Id
        );
    }

    /// <summary>
    /// Helper method to create a host node
    /// </summary>
    public static InventoryTreeNode CreateHostNode(string name, string details, string id = "")
    {
        return new InventoryTreeNode(
            name,
            details,
            SymbolRegular.Desktop24,
            InventoryNodeType.Host,
            id
        );
    }

    /// <summary>
    /// Helper method to create a VM node
    /// </summary>
    public static InventoryTreeNode CreateVmNode(VirtualMachineInfo vm)
    {
        var powerIcon = vm.PowerState?.ToLower() switch
        {
            "poweredon" => SymbolRegular.Play24,
            "poweredoff" => SymbolRegular.Stop24,
            "suspended" => SymbolRegular.Pause24,
            _ => SymbolRegular.QuestionCircle24
        };

        return new InventoryTreeNode(
            vm.Name,
            $"{vm.PowerState} • {vm.GuestOS} • {vm.CpuCount} vCPU, {vm.MemoryGB:F1}GB RAM",
            powerIcon,
            InventoryNodeType.VirtualMachine,
            vm.Id
        );
    }

    /// <summary>
    /// Helper method to create a datastore node
    /// </summary>
    public static InventoryTreeNode CreateDatastoreNode(DatastoreInfo datastore)
    {
        return new InventoryTreeNode(
            datastore.Name,
            $"{datastore.Type} • {datastore.FreeGB:F1}GB free of {datastore.CapacityGB:F1}GB ({datastore.UtilizationPercent:F0}% used)",
            SymbolRegular.Storage24,
            InventoryNodeType.Datastore,
            datastore.Id
        );
    }

    /// <summary>
    /// Helper method to create a folder node
    /// </summary>
    public static InventoryTreeNode CreateFolderNode(FolderInfo folder)
    {
        return new InventoryTreeNode(
            folder.Name,
            folder.Type,
            SymbolRegular.Folder24,
            InventoryNodeType.Folder,
            folder.Id
        );
    }

    /// <summary>
    /// Helper method to create a resource pool node
    /// </summary>
    public static InventoryTreeNode CreateResourcePoolNode(ResourcePoolInventoryInfo resourcePool)
    {
        return new InventoryTreeNode(
            resourcePool.Name,
            $"CPU: {resourcePool.CpuLimitMhz}MHz, Memory: {resourcePool.MemoryLimitMB}MB",
            SymbolRegular.DatabaseStack16,
            InventoryNodeType.ResourcePool,
            resourcePool.Id
        );
    }

    /// <summary>
    /// Helper method to create a network node
    /// </summary>
    public static InventoryTreeNode CreateNetworkNode(string name, string details, string id = "")
    {
        return new InventoryTreeNode(
            name,
            details,
            SymbolRegular.NetworkCheck24,
            InventoryNodeType.Network,
            id
        );
    }
}

/// <summary>
/// Types of inventory nodes for filtering and behavior
/// </summary>
public enum InventoryNodeType
{
    Unknown,
    vCenter,
    Datacenter,
    Cluster,
    Host,
    VirtualMachine,
    Datastore,
    Folder,
    ResourcePool,
    Network
}