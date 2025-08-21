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
    private readonly ILogger<SharedConnectionService> _logger;

    public SharedConnectionService(
        CredentialService credentialService, 
        VCenterInventoryService inventoryService,
        ILogger<SharedConnectionService> logger)
    {
        _credentialService = credentialService;
        _inventoryService = inventoryService;
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
    }