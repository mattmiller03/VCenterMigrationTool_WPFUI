using System.Threading.Tasks;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services;

/// <summary>
/// A shared service to hold the currently selected source and target vCenter connections.
/// This allows different pages to access the connection details set on the Dashboard.
/// </summary>
public class SharedConnectionService
{
    private readonly CredentialService _credentialService;

    public SharedConnectionService(CredentialService credentialService)
    {
        _credentialService = credentialService;
    }
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
        // For now, simulate based on whether connection details are present and password can be decrypted
        var hasBasicInfo = !string.IsNullOrEmpty(connection.ServerAddress) && 
                          !string.IsNullOrEmpty(connection.Username);
        
        var hasPassword = false;
        if (hasBasicInfo)
        {
            // Check if password is available in Windows Credential Manager
            try
            {
                var password = _credentialService.GetPassword(connection);
                hasPassword = !string.IsNullOrEmpty(password);
            }
            catch
            {
                // Password retrieval failed
                hasPassword = false;
            }
        }
        
        var isConnected = hasBasicInfo && hasPassword;
        
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

        if (connection == null)
        {
            return null;
        }

        // Retrieve password from Windows Credential Manager
        return _credentialService.GetPassword(connection);
    }
    }