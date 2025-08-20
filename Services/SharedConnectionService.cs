using System.Threading.Tasks;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services;

/// <summary>
/// A shared service to hold the currently selected source and target vCenter connections.
/// This allows different pages to access the connection details set on the Dashboard.
/// </summary>
public class SharedConnectionService
{
    /// <summary>
    /// Gets or sets the currently selected source vCenter connection.
    /// </summary>
    public VCenterConnection? SourceConnection { get; set; }

    /// <summary>
    /// Gets or sets the currently selected target vCenter connection.
    /// </summary>
    public VCenterConnection? TargetConnection { get; set; }

    public async Task<(bool IsConnected, string ServerName, string Version)> GetConnectionStatusAsync(string connectionType)
    {
        // Simulate async operation
        await Task.Delay(100);

        var connection = connectionType.ToLower() switch
        {
            "source" => SourceConnection,
            "target" => TargetConnection,
            _ => null
        };

        if (connection == null)
        {
            return (false, "Not configured", "");
        }

        // In a real implementation, this would test the actual connection
        // For now, simulate based on whether connection details are present
        var isConnected = !string.IsNullOrEmpty(connection.ServerAddress) && 
                         !string.IsNullOrEmpty(connection.Username);
        
        return (isConnected, connection.ServerAddress ?? "Unknown", "7.0");
    }

    public async Task<VCenterConnection?> GetConnectionAsync(string connectionType)
    {
        await Task.Delay(10); // Simulate async operation
        
        return connectionType.ToLower() switch
        {
            "source" => SourceConnection,
            "target" => TargetConnection,
            _ => null
        };
    }

    public async Task<string?> GetPasswordAsync(string connectionType)
    {
        await Task.Delay(10); // Simulate async operation
        
        var connection = connectionType.ToLower() switch
        {
            "source" => SourceConnection,
            "target" => TargetConnection,
            _ => null
        };

        // Note: In a real implementation, passwords should be retrieved from 
        // secure storage and decrypted. For now, return ProtectedPassword
        // assuming it contains the password (this would need proper decryption)
        return connection?.ProtectedPassword;
    }
    }