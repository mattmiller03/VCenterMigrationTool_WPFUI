using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// In Models/ClusterItem.cs
namespace VCenterMigrationTool.Models;

public class ClusterItem
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "ResourcePool" or "VDS"
    public bool IsSelected { get; set; } = true; // Selected by default
}