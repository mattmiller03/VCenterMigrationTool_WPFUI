using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Unified connection service that consolidates all vCenter connection management
/// Combines connection state management, persistent connections, and inventory tracking
/// </summary>
public class UnifiedConnectionService : IDisposable
{
    private readonly ILogger<UnifiedConnectionService> _logger;
    private readonly UnifiedPowerShellService _powerShellService;
    private readonly CredentialService _credentialService;
    private readonly VCenterInventoryService _inventoryService;
    private readonly ConcurrentDictionary<string, VCenterConnectionContext> _connections = new();
    private bool _disposed = false;

    public UnifiedConnectionService(
        ILogger<UnifiedConnectionService> logger,
        UnifiedPowerShellService powerShellService,
        CredentialService credentialService,
        VCenterInventoryService inventoryService)
    {
        _logger = logger;
        _powerShellService = powerShellService;
        _credentialService = credentialService;
        _inventoryService = inventoryService;

        _logger.LogInformation("UnifiedConnectionService initialized successfully");
    }

    #region Connection Context Management

    /// <summary>
    /// Comprehensive connection context that tracks all aspects of a vCenter connection
    /// </summary>
    public class VCenterConnectionContext
    {
        public string ConnectionKey { get; set; } = string.Empty;
        public VCenterConnection ConnectionInfo { get; set; } = new();
        public ConnectionStatus Status { get; set; } = ConnectionStatus.Disconnected;
        public string SessionId { get; set; } = string.Empty;
        public string VCenterVersion { get; set; } = string.Empty;
        public string VCenterBuild { get; set; } = string.Empty;
        public string ProductLine { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public int FailureCount { get; set; }
        public string? LastError { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        
        // PowerShell process information
        public UnifiedPowerShellService.ManagedPowerShellProcess? ManagedProcess { get; set; }
        public bool IsPowerCLIConfigured { get; set; }
        public string ModuleType { get; set; } = string.Empty;
        
        // Connection method tracking
        public bool IsUsingPowerCLI { get; set; }
        public bool IsAPIConnectionActive { get; set; }
        
        // Inventory tracking
        public VCenterInventory? CachedInventory { get; set; }
        public DateTime? LastInventoryUpdate { get; set; }

        /// <summary>
        /// Gets the connection duration
        /// </summary>
        public TimeSpan ConnectionDuration => Status == ConnectionStatus.Connected 
            ? DateTime.UtcNow - ConnectedAt 
            : TimeSpan.Zero;

        /// <summary>
        /// Gets the time since last activity
        /// </summary>
        public TimeSpan TimeSinceLastActivity => DateTime.UtcNow - LastActivityAt;

        /// <summary>
        /// Checks if connection is healthy based on activity and status
        /// </summary>
        public bool IsHealthy => Status == ConnectionStatus.Connected 
            && TimeSinceLastActivity < TimeSpan.FromMinutes(5) 
            && FailureCount < 3
            && (ManagedProcess == null || !ManagedProcess.HasExited);

        /// <summary>
        /// Checks if inventory data is fresh (updated within last 5 minutes)
        /// </summary>
        public bool HasFreshInventory => CachedInventory != null 
            && LastInventoryUpdate.HasValue 
            && (DateTime.UtcNow - LastInventoryUpdate.Value).TotalMinutes < 5;
    }

    /// <summary>
    /// Connection status enumeration
    /// </summary>
    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Failed,
        Timeout
    }

    #endregion

    #region Connection Management

    /// <summary>
    /// Establishes a persistent vCenter connection
    /// </summary>
    public async Task<(bool success, string message, string sessionId)> ConnectAsync(
        VCenterConnection connectionInfo,
        string password,
        bool isSource = true,
        bool bypassModuleCheck = false)
    {
        var connectionKey = isSource ? "source" : "target";

        try
        {
            _logger.LogInformation("🔗 Establishing connection to {Server} ({ConnectionKey})",
                connectionInfo.ServerAddress, connectionKey);

            // Initialize or update connection context
            var context = CreateOrUpdateConnectionContext(connectionKey, connectionInfo, ConnectionStatus.Connecting);

            // Clean up any existing connection
            await DisconnectAsync(connectionKey);

            // Step 1: Create PowerShell process
            _logger.LogDebug("Creating PowerShell process for {ConnectionKey}...", connectionKey);
            var process = await _powerShellService.CreatePersistentProcessAsync($"vcenter-{connectionKey}");

            if (process == null)
            {
                var errorMsg = "Failed to create PowerShell process";
                MarkConnectionFailed(connectionKey, errorMsg);
                return (false, errorMsg, string.Empty);
            }

            // Update context with process
            context.ManagedProcess = process;
            _logger.LogInformation("✅ PowerShell process created for {ConnectionKey} (PID: {ProcessId})", 
                connectionKey, process.ProcessId);

            // Step 2: Configure PowerCLI (unless bypassed)
            var sessionId = string.Empty;
            
            if (bypassModuleCheck)
            {
                _logger.LogInformation("⚠️ Bypassing PowerCLI configuration for {ConnectionKey}", connectionKey);
                sessionId = $"bypass-{Guid.NewGuid():N}";
                
                MarkConnectionEstablished(connectionKey, sessionId, 
                    "Unknown (bypass mode)", "", "Bypass Mode");
                
                return (true, "Connected in bypass mode - PowerCLI functionality limited", sessionId);
            }

            _logger.LogDebug("Configuring PowerCLI for {ConnectionKey}...", connectionKey);
            var configResult = await _powerShellService.ConfigurePowerCLIAsync(process, bypassModuleCheck);

            if (!configResult.Success)
            {
                var errorMsg = $"PowerCLI configuration failed: {configResult.Message}";
                _logger.LogError("❌ {ErrorMessage}", errorMsg);
                MarkConnectionFailed(connectionKey, errorMsg);
                
                await DisconnectAsync(connectionKey);
                return (false, errorMsg, string.Empty);
            }

            // Update context with PowerCLI info
            context.IsPowerCLIConfigured = true;
            context.ModuleType = configResult.ModuleType;
            context.IsUsingPowerCLI = true;

            _logger.LogInformation("✅ PowerCLI configured successfully using {ModuleType}", configResult.ModuleType);

            // Step 3: Connect to vCenter
            _logger.LogInformation("🔌 Connecting to vCenter {Server}...", connectionInfo.ServerAddress);
            var connectScript = PowerShellScriptBuilder.BuildVCenterConnectionScript(connectionInfo, password, connectionKey);
            var connectResult = await _powerShellService.ExecuteCommandAsync(process, connectScript, TimeSpan.FromSeconds(120));

            // Step 4: Process connection result
            if (connectResult.Contains("CONNECTION_SUCCESS"))
            {
                // Parse connection details from PowerShell output
                var connectionDetails = ParseConnectionDetails(connectResult);
                sessionId = connectionDetails.SessionId;

                MarkConnectionEstablished(connectionKey, sessionId, 
                    connectionDetails.Version, connectionDetails.Build, connectionDetails.ProductLine);

                _logger.LogInformation("✅ vCenter connection established for {ConnectionKey} (Session: {SessionId}, Version: {Version})",
                    connectionKey, sessionId, connectionDetails.Version);

                return (true, "Connected successfully", sessionId);
            }
            else
            {
                // Handle connection failure with detailed logging
                var errorMessage = await HandleConnectionFailureAsync(connectionKey, connectionInfo, connectResult);
                
                MarkConnectionFailed(connectionKey, errorMessage);
                await DisconnectAsync(connectionKey);
                
                return (false, errorMessage, string.Empty);
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Unexpected error during connection: {ex.Message}";
            _logger.LogError(ex, "💥 Exception while connecting to {Server}", connectionInfo.ServerAddress);
            
            MarkConnectionFailed(connectionKey, errorMessage);
            await DisconnectAsync(connectionKey);
            
            return (false, errorMessage, string.Empty);
        }
    }

    /// <summary>
    /// Executes a command in the context of a persistent connection
    /// </summary>
    public async Task<string> ExecuteCommandAsync(string connectionKey, string command)
    {
        try
        {
            if (_connections.TryGetValue(connectionKey, out var context) && context.ManagedProcess != null)
            {
                RecordActivity(connectionKey);
                return await _powerShellService.ExecuteCommandAsync(context.ManagedProcess, command, TimeSpan.FromMinutes(5));
            }
            else
            {
                return $"ERROR: No active connection for {connectionKey}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command for connection {ConnectionKey}", connectionKey);
            return $"ERROR: Command execution failed - {ex.Message}";
        }
    }

    /// <summary>
    /// Checks if a connection is still active
    /// </summary>
    public async Task<bool> IsConnectedAsync(string connectionKey)
    {
        try
        {
            // Check connection context first
            if (!_connections.TryGetValue(connectionKey, out var context) || 
                context.Status != ConnectionStatus.Connected)
            {
                return false;
            }

            // Verify process is still running
            if (context.ManagedProcess?.HasExited == true)
            {
                _logger.LogWarning("PowerShell process has exited for {ConnectionKey}", connectionKey);
                MarkConnectionFailed(connectionKey, "PowerShell process has exited");
                return false;
            }

            // Test connection with a simple validation command
            if (context.ManagedProcess != null)
            {
                var testScript = PowerShellScriptBuilder.BuildConnectionValidationScript(connectionKey);
                var result = await _powerShellService.ExecuteCommandAsync(context.ManagedProcess, testScript, TimeSpan.FromSeconds(10));
                
                var isActive = result.Contains("CONNECTION_ACTIVE");
                if (!isActive)
                {
                    MarkConnectionFailed(connectionKey, "Connection validation failed");
                }
                else
                {
                    RecordActivity(connectionKey);
                }

                return isActive;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking connection status for {ConnectionKey}", connectionKey);
            return false;
        }
    }

    /// <summary>
    /// Gets connection info for compatibility
    /// </summary>
    public (bool isConnected, string sessionId, string version) GetConnectionInfo(string connectionKey)
    {
        if (_connections.TryGetValue(connectionKey, out var context))
        {
            return (
                context.Status == ConnectionStatus.Connected,
                context.SessionId,
                context.VCenterVersion
            );
        }

        return (false, string.Empty, string.Empty);
    }

    /// <summary>
    /// Disconnects and cleans up a connection
    /// </summary>
    public async Task DisconnectAsync(string connectionKey)
    {
        if (!_connections.TryGetValue(connectionKey, out var context))
        {
            _logger.LogDebug("No connection found for key {ConnectionKey}", connectionKey);
            return;
        }

        try
        {
            var serverAddress = context.ConnectionInfo?.ServerAddress ?? "unknown";
            
            _logger.LogInformation("🔌 Disconnecting from {Server} ({ConnectionKey})", serverAddress, connectionKey);

            if (context.ManagedProcess != null && !context.ManagedProcess.HasExited)
            {
                // Try to disconnect gracefully from vCenter first
                try
                {
                    var checkScript = @"
                        try {
                            if (Get-Command 'Get-VIServer' -ErrorAction SilentlyContinue) { 
                                $connections = @(Get-VIServer -ErrorAction SilentlyContinue)
                                if ($connections.Count -gt 0) { 
                                    'POWERCLI_CONNECTED'
                                } else { 
                                    'POWERCLI_NO_CONNECTION' 
                                }
                            } else { 
                                'NO_POWERCLI' 
                            }
                        } catch {
                            'ERROR_CHECKING'
                        }";
                    
                    var checkResult = await _powerShellService.ExecuteCommandAsync(context.ManagedProcess, checkScript, TimeSpan.FromSeconds(5));
                    
                    if (checkResult.Contains("POWERCLI_CONNECTED"))
                    {
                        var disconnectScript = serverAddress != "unknown" 
                            ? $"Disconnect-VIServer -Server '{serverAddress}' -Force -Confirm:$false"
                            : "Disconnect-VIServer * -Force -Confirm:$false";
                        
                        await _powerShellService.ExecuteCommandAsync(context.ManagedProcess, disconnectScript, TimeSpan.FromSeconds(10));
                        _logger.LogInformation("✅ Disconnected from vCenter {Server}", serverAddress);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error during graceful vCenter disconnect for {ConnectionKey}", connectionKey);
                }

                // Terminate the PowerShell process
                await _powerShellService.DisposePersistentProcessAsync($"vcenter-{connectionKey}");
            }

            // Remove connection from tracking
            _connections.TryRemove(connectionKey, out _);

            _logger.LogInformation("✅ Successfully cleaned up connection {ConnectionKey}", connectionKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during disconnect cleanup for {ConnectionKey}", connectionKey);
        }
    }

    /// <summary>
    /// Disconnects all active connections
    /// </summary>
    public async Task DisconnectAllAsync()
    {
        var connectionKeys = _connections.Keys.ToList();
        _logger.LogInformation("🔌 Disconnecting {Count} active connections", connectionKeys.Count);

        var tasks = connectionKeys.Select(key => DisconnectAsync(key));
        await Task.WhenAll(tasks);

        _logger.LogInformation("✅ All connections disconnected successfully");
    }

    #endregion

    #region Inventory Management

    /// <summary>
    /// Gets or refreshes inventory for a connection
    /// </summary>
    public async Task<VCenterInventory?> GetInventoryAsync(string connectionKey, bool forceRefresh = false)
    {
        if (!_connections.TryGetValue(connectionKey, out var context))
        {
            return null;
        }

        try
        {
            // Return cached inventory if fresh and not forcing refresh
            if (!forceRefresh && context.HasFreshInventory)
            {
                _logger.LogDebug("Returning cached inventory for {ConnectionKey}", connectionKey);
                return context.CachedInventory;
            }

            // Check if connection is active
            if (context.Status != ConnectionStatus.Connected || context.ManagedProcess == null)
            {
                _logger.LogWarning("Cannot load inventory - connection {ConnectionKey} is not active", connectionKey);
                return null;
            }

            _logger.LogInformation("🔄 Refreshing inventory for {ConnectionKey}...", connectionKey);

            // Load inventory using the inventory service
            // Get password for the connection
            var password = _credentialService.GetPassword(context.ConnectionInfo);
            if (string.IsNullOrEmpty(password))
            {
                _logger.LogWarning("No stored password found for {ServerAddress}", context.ConnectionInfo.ServerAddress);
                return null;
            }
            
            var inventory = await _inventoryService.LoadInventoryAsync(
                context.ConnectionInfo.ServerAddress, 
                context.ConnectionInfo.Username, 
                password, 
                connectionKey);
            
            if (inventory != null)
            {
                // Cache the inventory
                context.CachedInventory = inventory;
                context.LastInventoryUpdate = DateTime.UtcNow;
                
                _logger.LogInformation("✅ Inventory refreshed for {ConnectionKey} - {DatacenterCount} datacenters, {VmCount} VMs", 
                    connectionKey, inventory.Datacenters?.Count ?? 0, inventory.VirtualMachines?.Count ?? 0);
            }
            else
            {
                _logger.LogWarning("❌ Failed to load inventory for {ConnectionKey}", connectionKey);
            }

            return inventory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading inventory for {ConnectionKey}", connectionKey);
            return null;
        }
    }

    /// <summary>
    /// Clears cached inventory for a connection
    /// </summary>
    public void ClearInventoryCache(string connectionKey)
    {
        if (_connections.TryGetValue(connectionKey, out var context))
        {
            context.CachedInventory = null;
            context.LastInventoryUpdate = null;
            _logger.LogDebug("Cleared inventory cache for {ConnectionKey}", connectionKey);
        }
    }

    #endregion

    #region Connection State Management

    /// <summary>
    /// Creates or updates a connection context
    /// </summary>
    private VCenterConnectionContext CreateOrUpdateConnectionContext(
        string connectionKey,
        VCenterConnection connectionInfo,
        ConnectionStatus status = ConnectionStatus.Connecting)
    {
        try
        {
            var now = DateTime.UtcNow;

            var context = _connections.AddOrUpdate(connectionKey,
                // Add new connection
                new VCenterConnectionContext
                {
                    ConnectionKey = connectionKey,
                    ConnectionInfo = connectionInfo,
                    Status = status,
                    ConnectedAt = status == ConnectionStatus.Connected ? now : default,
                    LastActivityAt = now,
                    FailureCount = 0
                },
                // Update existing connection
                (key, existingContext) =>
                {
                    existingContext.ConnectionInfo = connectionInfo;
                    existingContext.Status = status;
                    existingContext.LastActivityAt = now;
                    
                    if (status == ConnectionStatus.Connected && existingContext.ConnectedAt == default)
                    {
                        existingContext.ConnectedAt = now;
                        existingContext.FailureCount = 0; // Reset failure count on successful connection
                    }
                    else if (status == ConnectionStatus.Failed)
                    {
                        existingContext.FailureCount++;
                    }

                    return existingContext;
                });

            _logger.LogDebug("Updated connection context for {ConnectionKey}: Status = {Status}", 
                connectionKey, status);

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating connection context for {ConnectionKey}", connectionKey);
            
            // Return a default context to prevent null references
            return new VCenterConnectionContext
            {
                ConnectionKey = connectionKey,
                ConnectionInfo = connectionInfo,
                Status = ConnectionStatus.Failed,
                LastError = $"Error creating connection context: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Marks a connection as successfully established
    /// </summary>
    private void MarkConnectionEstablished(
        string connectionKey,
        string sessionId,
        string version = "",
        string build = "",
        string productLine = "")
    {
        try
        {
            if (_connections.TryGetValue(connectionKey, out var context))
            {
                var now = DateTime.UtcNow;
                
                context.Status = ConnectionStatus.Connected;
                context.SessionId = sessionId;
                context.VCenterVersion = version;
                context.VCenterBuild = build;
                context.ProductLine = productLine;
                context.ConnectedAt = now;
                context.LastActivityAt = now;
                context.FailureCount = 0;
                context.LastError = null;

                _logger.LogInformation("✅ Connection {ConnectionKey} established successfully (Session: {SessionId}, Version: {Version})", 
                    connectionKey, sessionId, version);
            }
            else
            {
                _logger.LogWarning("Attempted to mark unknown connection {ConnectionKey} as established", connectionKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking connection {ConnectionKey} as established", connectionKey);
        }
    }

    /// <summary>
    /// Marks a connection as failed with error details
    /// </summary>
    private void MarkConnectionFailed(string connectionKey, string errorMessage)
    {
        try
        {
            if (_connections.TryGetValue(connectionKey, out var context))
            {
                context.Status = ConnectionStatus.Failed;
                context.LastError = errorMessage;
                context.FailureCount++;
                context.LastActivityAt = DateTime.UtcNow;

                _logger.LogError("❌ Connection {ConnectionKey} failed (Attempt #{FailureCount}): {Error}", 
                    connectionKey, context.FailureCount, errorMessage);
            }
            else
            {
                _logger.LogWarning("Attempted to mark unknown connection {ConnectionKey} as failed", connectionKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking connection {ConnectionKey} as failed", connectionKey);
        }
    }

    /// <summary>
    /// Records activity for a connection (keeps it alive)
    /// </summary>
    private void RecordActivity(string connectionKey)
    {
        try
        {
            if (_connections.TryGetValue(connectionKey, out var context))
            {
                context.LastActivityAt = DateTime.UtcNow;
                _logger.LogTrace("Recorded activity for connection {ConnectionKey}", connectionKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording activity for connection {ConnectionKey}", connectionKey);
        }
    }

    /// <summary>
    /// Gets connection context information
    /// </summary>
    public VCenterConnectionContext? GetConnectionContext(string connectionKey)
    {
        return _connections.TryGetValue(connectionKey, out var context) ? context : null;
    }

    /// <summary>
    /// Gets all connection contexts
    /// </summary>
    public IReadOnlyDictionary<string, VCenterConnectionContext> GetAllConnectionContexts()
    {
        return _connections.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Connection details parsed from PowerShell output
    /// </summary>
    private class ConnectionDetails
    {
        public string SessionId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Build { get; set; } = string.Empty;
        public string ProductLine { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parses connection details from PowerShell output
    /// </summary>
    private ConnectionDetails ParseConnectionDetails(string output)
    {
        var details = new ConnectionDetails();
        
        try
        {
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("SESSION_ID:"))
                    details.SessionId = trimmedLine.Substring(11).Trim();
                else if (trimmedLine.StartsWith("VERSION:"))
                    details.Version = trimmedLine.Substring(8).Trim();
                else if (trimmedLine.StartsWith("BUILD:"))
                    details.Build = trimmedLine.Substring(6).Trim();
                else if (trimmedLine.StartsWith("PRODUCT_LINE:"))
                    details.ProductLine = trimmedLine.Substring(13).Trim();
                else if (trimmedLine.StartsWith("USER:"))
                    details.User = trimmedLine.Substring(5).Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing connection details from output");
        }

        return details;
    }

    /// <summary>
    /// Handles connection failure with detailed logging and error analysis
    /// </summary>
    private async Task<string> HandleConnectionFailureAsync(string connectionKey, VCenterConnection connectionInfo, string connectResult)
    {
        try
        {
            _logger.LogWarning("🔍 Analyzing connection failure for {Server}...", connectionInfo.ServerAddress);
            
            // Log complete output for debugging
            _logger.LogDebug("Complete PowerShell output for {Server} ({Length} chars): {Output}", 
                connectionInfo.ServerAddress, connectResult?.Length ?? 0, connectResult);

            // Parse the output for specific error information
            var lines = connectResult.Split('\n');
            var errorLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;
                
                if (trimmedLine.StartsWith("DEBUG_ERROR_VAR:") || 
                    trimmedLine.StartsWith("CONNECTION_FAILED:") ||
                    trimmedLine.Contains("EXCEPTION") || 
                    trimmedLine.Contains("ERROR"))
                {
                    errorLines.Add(trimmedLine);
                    _logger.LogError("🚨 Connection Error: {Error}", trimmedLine);
                }
            }

            // Determine primary error message
            string primaryError = errorLines.FirstOrDefault() ?? "Connection failed - no specific error information available";

            // Provide categorized guidance
            var lowerError = primaryError.ToLower();
            if (lowerError.Contains("certificate") || lowerError.Contains("ssl") || lowerError.Contains("tls"))
            {
                _logger.LogInformation("💡 Suggestion: SSL/Certificate issue detected - verify PowerCLI InvalidCertificateAction setting");
            }
            else if (lowerError.Contains("authentication") || lowerError.Contains("login") || lowerError.Contains("credential"))
            {
                _logger.LogInformation("💡 Suggestion: Authentication issue detected - verify username/password and account status");
            }
            else if (lowerError.Contains("timeout") || lowerError.Contains("network") || lowerError.Contains("connection"))
            {
                _logger.LogInformation("💡 Suggestion: Network/Timeout issue detected - check connectivity and firewall settings");
            }

            return primaryError;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing connection failure for {ConnectionKey}", connectionKey);
            return $"Connection failed with analysis error: {ex.Message}";
        }
    }

    #endregion

    #region Health Monitoring

    /// <summary>
    /// Performs health checks on all connections
    /// </summary>
    public List<ConnectionHealthIssue> PerformHealthCheck()
    {
        var issues = new List<ConnectionHealthIssue>();

        try
        {
            foreach (var (connectionKey, context) in _connections)
            {
                // Check for stale connections
                if (context.Status == ConnectionStatus.Connected && 
                    context.TimeSinceLastActivity > TimeSpan.FromMinutes(10))
                {
                    issues.Add(new ConnectionHealthIssue
                    {
                        ConnectionKey = connectionKey,
                        IssueType = HealthIssueType.StaleConnection,
                        Description = $"No activity for {context.TimeSinceLastActivity.TotalMinutes:F1} minutes",
                        Severity = context.TimeSinceLastActivity > TimeSpan.FromMinutes(30) 
                            ? IssueSeverity.High 
                            : IssueSeverity.Medium
                    });
                }

                // Check for repeated failures
                if (context.FailureCount >= 3)
                {
                    issues.Add(new ConnectionHealthIssue
                    {
                        ConnectionKey = connectionKey,
                        IssueType = HealthIssueType.RepeatedFailures,
                        Description = $"{context.FailureCount} consecutive failures. Last error: {context.LastError}",
                        Severity = IssueSeverity.High
                    });
                }

                // Check for long connection duration (might indicate resource leak)
                if (context.Status == ConnectionStatus.Connected && 
                    context.ConnectionDuration > TimeSpan.FromHours(24))
                {
                    issues.Add(new ConnectionHealthIssue
                    {
                        ConnectionKey = connectionKey,
                        IssueType = HealthIssueType.LongRunningConnection,
                        Description = $"Connection has been active for {context.ConnectionDuration.TotalHours:F1} hours",
                        Severity = IssueSeverity.Low
                    });
                }

                // Check for PowerShell process health
                if (context.ManagedProcess?.HasExited == true && context.Status == ConnectionStatus.Connected)
                {
                    issues.Add(new ConnectionHealthIssue
                    {
                        ConnectionKey = connectionKey,
                        IssueType = HealthIssueType.UnresponsiveConnection,
                        Description = "PowerShell process has exited but connection is marked as active",
                        Severity = IssueSeverity.High
                    });
                }
            }

            if (issues.Any())
            {
                _logger.LogWarning("Health check found {IssueCount} connection issues", issues.Count);
            }
            else
            {
                _logger.LogDebug("Health check passed - all connections healthy");
            }

            return issues;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing connection health check");
            return issues;
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogInformation("Disposing UnifiedConnectionService - cleaning up {ConnectionCount} connections", 
            _connections.Count);

        try
        {
            // Disconnect all connections synchronously
            DisconnectAllAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during connection cleanup");
        }
        finally
        {
            _disposed = true;
            _logger.LogInformation("✅ UnifiedConnectionService disposed successfully");
        }
    }

    #endregion
}

// Note: ConnectionHealthIssue, HealthIssueType, and IssueSeverity are defined in ConnectionStateManager.cs