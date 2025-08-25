using System;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using Microsoft.Extensions.Logging;

namespace VCenterMigrationTool.Services;

/// <summary>
/// A shared service to hold the currently selected source and target vCenter connections.
/// This allows different pages to access the connection details set on the Dashboard.
/// </summary>
public class SharedConnectionService
{
    private readonly CredentialService _credentialService;
    private readonly VCenterInventoryService _inventoryService;
    private readonly VSphereApiService _vSphereApiService;
    private readonly ILogger<SharedConnectionService> _logger;

    public SharedConnectionService(
        CredentialService credentialService, 
        VCenterInventoryService inventoryService,
        VSphereApiService vSphereApiService,
        ILogger<SharedConnectionService> logger)
    {
        _credentialService = credentialService;
        _inventoryService = inventoryService;
        _vSphereApiService = vSphereApiService;
        _logger = logger;
    }
    /// <summary>
    /// Gets or sets the currently selected source vCenter connection.
    /// </summary>
    public VCenterConnection? SourceConnection { get; set; }

    /// <summary>
    /// Gets or sets the currently selected target vCenter connection.
    /// </summary>
    public VCenterConnection? TargetConnection { get; set; }

    /// <summary>
    /// Tracks whether source connection is using PowerCLI (due to SSL issues)
    /// </summary>
    public bool SourceUsingPowerCLI { get; set; }

    /// <summary>
    /// Tracks whether target connection is using PowerCLI (due to SSL issues)
    /// </summary>
    public bool TargetUsingPowerCLI { get; set; }

    /// <summary>
    /// Stores PowerCLI session info when using PowerCLI fallback
    /// </summary>
    public string? SourcePowerCLISessionId { get; set; }
    public string? TargetPowerCLISessionId { get; set; }

    public async Task<(bool IsConnected, string ServerName, string Version)> GetConnectionStatusAsync(string connectionType)
    {
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

        // Check if using PowerCLI fallback
        bool usingPowerCLI = connectionType.ToLower() switch
        {
            "source" => SourceUsingPowerCLI,
            "target" => TargetUsingPowerCLI,
            _ => false
        };

        if (usingPowerCLI)
        {
            // For PowerCLI connections, check if session is still valid
            var sessionId = connectionType.ToLower() switch
            {
                "source" => SourcePowerCLISessionId,
                "target" => TargetPowerCLISessionId,
                _ => null
            };

            if (!string.IsNullOrEmpty(sessionId))
            {
                _logger.LogInformation("PowerCLI connection active for {ConnectionType}: {Server} (Session: {SessionId})", 
                    connectionType, connection.ServerAddress, sessionId);
                return (true, connection.ServerAddress ?? "Unknown", "PowerCLI Session");
            }
            
            return (false, connection.ServerAddress ?? "Unknown", "PowerCLI session lost");
        }

        // Convert VCenterConnection to VCenterConnectionInfo for API service
        var connectionInfo = new VCenterConnectionInfo
        {
            ServerAddress = connection.ServerAddress ?? "",
            Username = connection.Username ?? ""
        };

        // Check if password is available
        string password;
        try
        {
            password = _credentialService.GetPassword(connection);
            if (string.IsNullOrEmpty(password))
            {
                return (false, connection.ServerAddress ?? "Unknown", "No password");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve password for {ConnectionType} connection", connectionType);
            return (false, connection.ServerAddress ?? "Unknown", "Password error");
        }

        // Use vSphere API to test actual connection
        try
        {
            var (isConnected, version, build) = await _vSphereApiService.GetConnectionStatusAsync(connectionInfo, password);
            var versionString = !string.IsNullOrEmpty(version) ? $"{version} (Build: {build})" : "Connected";
            
            _logger.LogInformation("{ConnectionType} connection status: {IsConnected} to {Server}", 
                connectionType, isConnected, connection.ServerAddress);
                
            return (isConnected, connection.ServerAddress ?? "Unknown", versionString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing {ConnectionType} connection to {Server}", 
                connectionType, connection.ServerAddress);
            return (false, connection.ServerAddress ?? "Unknown", "Connection error");
        }
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

    /// <summary>
    /// Set source connection and load its inventory
    /// </summary>
    public async Task<bool> SetSourceConnectionAsync(VCenterConnection connection)
    {
        try
        {
            SourceConnection = connection;
            
            // Load inventory in background
            var password = _credentialService.GetPassword(connection);
            if (!string.IsNullOrEmpty(password))
            {
                _logger.LogInformation("Loading inventory for source vCenter: {ServerName}", connection.ServerAddress);
                await _inventoryService.LoadInventoryAsync(connection.ServerAddress!, connection.Username!, password, "source");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set source connection and load inventory for {ServerName}", connection.ServerAddress);
        }
        
        return false;
    }

    /// <summary>
    /// Set target connection and load its inventory
    /// </summary>
    public async Task<bool> SetTargetConnectionAsync(VCenterConnection connection)
    {
        try
        {
            TargetConnection = connection;
            
            // Load inventory in background
            var password = _credentialService.GetPassword(connection);
            if (!string.IsNullOrEmpty(password))
            {
                _logger.LogInformation("Loading inventory for target vCenter: {ServerName}", connection.ServerAddress);
                await _inventoryService.LoadInventoryAsync(connection.ServerAddress!, connection.Username!, password, "target");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set target connection and load inventory for {ServerName}", connection.ServerAddress);
        }
        
        return false;
    }

    /// <summary>
    /// Clear source connection and its inventory
    /// </summary>
    public void ClearSourceConnection()
    {
        if (SourceConnection != null)
        {
            _inventoryService.ClearInventory(SourceConnection.ServerAddress!);
            SourceConnection = null;
            SourceUsingPowerCLI = false;
            SourcePowerCLISessionId = null;
            _logger.LogInformation("Cleared source connection and inventory");
        }
    }

    /// <summary>
    /// Clear target connection and its inventory
    /// </summary>
    public void ClearTargetConnection()
    {
        if (TargetConnection != null)
        {
            _inventoryService.ClearInventory(TargetConnection.ServerAddress!);
            TargetConnection = null;
            TargetUsingPowerCLI = false;
            TargetPowerCLISessionId = null;
            _logger.LogInformation("Cleared target connection and inventory");
        }
    }

    /// <summary>
    /// Get inventory for source vCenter
    /// </summary>
    public VCenterInventory? GetSourceInventory()
    {
        return SourceConnection != null 
            ? _inventoryService.GetCachedInventory(SourceConnection.ServerAddress!) 
            : null;
    }

    /// <summary>
    /// Load infrastructure inventory for source vCenter (datacenters, clusters, hosts, datastores)
    /// </summary>
    public async Task<bool> LoadSourceInfrastructureAsync()
    {
        if (SourceConnection == null) return false;
        
        try
        {
            var password = _credentialService.GetPassword(SourceConnection);
            if (!string.IsNullOrEmpty(password))
            {
                await _inventoryService.LoadInfrastructureInventoryAsync(
                    SourceConnection.ServerAddress!, 
                    SourceConnection.Username!, 
                    password, 
                    "source");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load source infrastructure inventory");
        }
        return false;
    }

    /// <summary>
    /// Load VMs inventory for source vCenter
    /// </summary>
    public async Task<bool> LoadSourceVirtualMachinesAsync()
    {
        if (SourceConnection == null) return false;
        
        try
        {
            var password = _credentialService.GetPassword(SourceConnection);
            if (!string.IsNullOrEmpty(password))
            {
                await _inventoryService.LoadVirtualMachinesInventoryAsync(
                    SourceConnection.ServerAddress!, 
                    SourceConnection.Username!, 
                    password, 
                    "source");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load source VM inventory");
        }
        return false;
    }

    /// <summary>
    /// Load admin config inventory for source vCenter
    /// </summary>
    public async Task<bool> LoadSourceAdminConfigAsync()
    {
        if (SourceConnection == null) return false;
        
        try
        {
            var password = _credentialService.GetPassword(SourceConnection);
            if (!string.IsNullOrEmpty(password))
            {
                await _inventoryService.LoadAdminConfigInventoryAsync(
                    SourceConnection.ServerAddress!, 
                    SourceConnection.Username!, 
                    password, 
                    "source");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load source admin config inventory");
        }
        return false;
    }

    /// <summary>
    /// Load infrastructure inventory for target vCenter (datacenters, clusters, hosts, datastores)
    /// </summary>
    public async Task<bool> LoadTargetInfrastructureAsync()
    {
        if (TargetConnection == null) return false;
        
        try
        {
            var password = _credentialService.GetPassword(TargetConnection);
            if (!string.IsNullOrEmpty(password))
            {
                await _inventoryService.LoadInfrastructureInventoryAsync(
                    TargetConnection.ServerAddress!, 
                    TargetConnection.Username!, 
                    password, 
                    "target");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load target infrastructure inventory");
        }
        return false;
    }

    /// <summary>
    /// Load VMs inventory for target vCenter
    /// </summary>
    public async Task<bool> LoadTargetVirtualMachinesAsync()
    {
        if (TargetConnection == null) return false;
        
        try
        {
            var password = _credentialService.GetPassword(TargetConnection);
            if (!string.IsNullOrEmpty(password))
            {
                await _inventoryService.LoadVirtualMachinesInventoryAsync(
                    TargetConnection.ServerAddress!, 
                    TargetConnection.Username!, 
                    password, 
                    "target");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load target VM inventory");
        }
        return false;
    }

    /// <summary>
    /// Load admin config inventory for target vCenter
    /// </summary>
    public async Task<bool> LoadTargetAdminConfigAsync()
    {
        if (TargetConnection == null) return false;
        
        try
        {
            var password = _credentialService.GetPassword(TargetConnection);
            if (!string.IsNullOrEmpty(password))
            {
                await _inventoryService.LoadAdminConfigInventoryAsync(
                    TargetConnection.ServerAddress!, 
                    TargetConnection.Username!, 
                    password, 
                    "target");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load target admin config inventory");
        }
        return false;
    }

    /// <summary>
    /// Get inventory for target vCenter
    /// </summary>
    public VCenterInventory? GetTargetInventory()
    {
        return TargetConnection != null 
            ? _inventoryService.GetCachedInventory(TargetConnection.ServerAddress!) 
            : null;
    }

    /// <summary>
    /// Refresh inventory for source vCenter
    /// </summary>
    public async Task<bool> RefreshSourceInventoryAsync()
    {
        if (SourceConnection == null) return false;

        try
        {
            var password = _credentialService.GetPassword(SourceConnection);
            if (!string.IsNullOrEmpty(password))
            {
                await _inventoryService.RefreshInventoryAsync(SourceConnection.ServerAddress!, SourceConnection.Username!, password, "source");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh source inventory for {ServerName}", SourceConnection.ServerAddress);
        }
        
        return false;
    }

    /// <summary>
    /// Refresh inventory for target vCenter
    /// </summary>
    public async Task<bool> RefreshTargetInventoryAsync()
    {
        if (TargetConnection == null) return false;

        try
        {
            var password = _credentialService.GetPassword(TargetConnection);
            if (!string.IsNullOrEmpty(password))
            {
                await _inventoryService.RefreshInventoryAsync(TargetConnection.ServerAddress!, TargetConnection.Username!, password, "target");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh target inventory for {ServerName}", TargetConnection.ServerAddress);
        }
        
        return false;
    }

    /// <summary>
    /// Get basic inventory counts using vSphere API for dashboard display
    /// </summary>
    public async Task<InventoryCounts?> GetInventoryCountsAsync(string connectionType)
    {
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

        // Convert VCenterConnection to VCenterConnectionInfo for API service
        var connectionInfo = new VCenterConnectionInfo
        {
            ServerAddress = connection.ServerAddress ?? "",
            Username = connection.Username ?? ""
        };

        // Check if password is available
        string password;
        try
        {
            password = _credentialService.GetPassword(connection);
            if (string.IsNullOrEmpty(password))
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve password for inventory counts on {ConnectionType} connection", connectionType);
            return null;
        }

        // Use vSphere API to get inventory counts
        try
        {
            var counts = await _vSphereApiService.GetInventoryCountsAsync(connectionInfo, password);
            _logger.LogInformation("Retrieved inventory counts for {ConnectionType}: VMs={VmCount}, Hosts={HostCount}", 
                connectionType, counts.VmCount, counts.HostCount);
            return counts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inventory counts for {ConnectionType} connection to {Server}", 
                connectionType, connection.ServerAddress);
            return null;
        }
    }
    }