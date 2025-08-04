using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// In Models/VirtualMachine.cs
namespace VCenterMigrationTool.Models;

public class VirtualMachine
{
    public string Name { get; set; }
    public string PowerState { get; set; }
    public string EsxiHost { get; set; }
    public bool IsSelected { get; set; }
}
