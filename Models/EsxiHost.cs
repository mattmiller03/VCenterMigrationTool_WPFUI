using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// In Models/EsxiHost.cs
namespace VCenterMigrationTool.Models;

public class EsxiHost
{
    public string Name { get; set; }
    public string Cluster { get; set; }
    public string Status { get; set; }
    public bool IsSelected { get; set; }
}
