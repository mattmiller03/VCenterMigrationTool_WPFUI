using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// In Models/VCenterConnection.cs
namespace VCenterMigrationTool.Models;

public class VCenterConnection
{
    public string Name { get; set; } // e.g., "Production", "DMZ"
    public string ServerAddress { get; set; }
    public string Username { get; set; }
}
