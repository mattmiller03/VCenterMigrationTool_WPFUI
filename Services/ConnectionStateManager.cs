using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Manages vCenter connection state and metadata
/// </summary>
public class ConnectionStateManager : IDisposable
{
    private readonly ILogger<ConnectionStateManager> _logger;
    private readonly ConcurrentDictionary<string, VCenterConnectionState> _connectionStates = new();
    private bool _disposed = false;

    public ConnectionStateManager(ILogger<ConnectionStateManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Represents the state of a vCenter connection
    /// </summary>
    public class VCenterConnectionState
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
            && FailureCount < 3;
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

    /// <summary>
    /// Creates or updates a connection state
    /// </summary>
    public void CreateOrUpdateConnection(
        string connectionKey,
        VCenterConnection connectionInfo,
        ConnectionStatus status = ConnectionStatus.Connecting)
    {
        try
        {
            var now = DateTime.UtcNow;

            var state = _connectionStates.AddOrUpdate(connectionKey,
                // Add new connection
                new VCenterConnectionState
                {
                    ConnectionKey = connectionKey,
                    ConnectionInfo = connectionInfo,
                    Status = status,
                    ConnectedAt = status == ConnectionStatus.Connected ? now : default,
                    LastActivityAt = now,
                    FailureCount = 0
                },
                // Update existing connection
                (key, existingState) =>
                {
                    existingState.ConnectionInfo = connectionInfo;
                    existingState.Status = status;
                    existingState.LastActivityAt = now;
                    
                    if (status == ConnectionStatus.Connected && existingState.ConnectedAt == default)
                    {
                        existingState.ConnectedAt = now;
                        existingState.FailureCount = 0; // Reset failure count on successful connection
                    }
                    else if (status == ConnectionStatus.Failed)
                    {
                        existingState.FailureCount++;
                    }

                    return existingState;
                });

            _logger.LogDebug("Updated connection state for {ConnectionKey}: Status = {Status}", 
                connectionKey, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating connection state for {ConnectionKey}", connectionKey);
        }
    }

    /// <summary>
    /// Marks a connection as successfully established
    /// </summary>
    public void MarkConnectionEstablished(
        string connectionKey,
        string sessionId,
        string version = "",
        string build = "",
        string productLine = "")
    {
        try
        {
            if (_connectionStates.TryGetValue(connectionKey, out var state))
            {
                var now = DateTime.UtcNow;
                
                state.Status = ConnectionStatus.Connected;
                state.SessionId = sessionId;
                state.VCenterVersion = version;
                state.VCenterBuild = build;
                state.ProductLine = productLine;
                state.ConnectedAt = now;
                state.LastActivityAt = now;
                state.FailureCount = 0;
                state.LastError = null;

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
    public void MarkConnectionFailed(string connectionKey, string errorMessage)
    {
        try
        {
            if (_connectionStates.TryGetValue(connectionKey, out var state))
            {
                state.Status = ConnectionStatus.Failed;
                state.LastError = errorMessage;
                state.FailureCount++;
                state.LastActivityAt = DateTime.UtcNow;

                _logger.LogError("❌ Connection {ConnectionKey} failed (Attempt #{FailureCount}): {Error}", 
                    connectionKey, state.FailureCount, errorMessage);
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
    public void RecordActivity(string connectionKey)
    {
        try
        {
            if (_connectionStates.TryGetValue(connectionKey, out var state))
            {
                state.LastActivityAt = DateTime.UtcNow;
                _logger.LogTrace("Recorded activity for connection {ConnectionKey}", connectionKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording activity for connection {ConnectionKey}", connectionKey);
        }
    }

    /// <summary>
    /// Gets connection state information
    /// </summary>
    public VCenterConnectionState? GetConnectionState(string connectionKey)
    {
        return _connectionStates.TryGetValue(connectionKey, out var state) ? state : null;
    }

    /// <summary>
    /// Gets all connection states
    /// </summary>
    public IReadOnlyDictionary<string, VCenterConnectionState> GetAllConnectionStates()
    {
        return _connectionStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Checks if a connection exists and is connected
    /// </summary>
    public bool IsConnected(string connectionKey)
    {
        var state = GetConnectionState(connectionKey);
        return state?.Status == ConnectionStatus.Connected;
    }

    /// <summary>
    /// Gets connection info tuple for compatibility
    /// </summary>
    public (bool isConnected, string sessionId, string version) GetConnectionInfo(string connectionKey)
    {
        var state = GetConnectionState(connectionKey);
        if (state == null)
        {
            return (false, string.Empty, string.Empty);
        }

        return (
            state.Status == ConnectionStatus.Connected,
            state.SessionId,
            state.VCenterVersion
        );
    }

    /// <summary>
    /// Removes a connection from tracking
    /// </summary>
    public bool RemoveConnection(string connectionKey)
    {
        try
        {
            var removed = _connectionStates.TryRemove(connectionKey, out var state);
            if (removed && state != null)
            {
                _logger.LogInformation("Removed connection {ConnectionKey} from state tracking", connectionKey);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing connection {ConnectionKey} from state tracking", connectionKey);
            return false;
        }
    }

    /// <summary>
    /// Gets summary of all connection states
    /// </summary>
    public ConnectionStateSummary GetStateSummary()
    {
        try
        {
            var states = _connectionStates.Values.ToList();
            
            return new ConnectionStateSummary
            {
                TotalConnections = states.Count,
                ConnectedCount = states.Count(s => s.Status == ConnectionStatus.Connected),
                FailedCount = states.Count(s => s.Status == ConnectionStatus.Failed),
                ConnectingCount = states.Count(s => s.Status == ConnectionStatus.Connecting || s.Status == ConnectionStatus.Reconnecting),
                HealthyCount = states.Count(s => s.IsHealthy),
                UnhealthyCount = states.Count(s => !s.IsHealthy),
                AverageConnectionDuration = states.Where(s => s.Status == ConnectionStatus.Connected)
                    .Select(s => s.ConnectionDuration)
                    .DefaultIfEmpty()
                    .Average(ts => ts.TotalMinutes),
                ConnectionKeys = states.Select(s => s.ConnectionKey).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting connection state summary");
            return new ConnectionStateSummary();
        }
    }

    /// <summary>
    /// Performs health checks on all connections
    /// </summary>
    public List<ConnectionHealthIssue> PerformHealthCheck()
    {
        var issues = new List<ConnectionHealthIssue>();

        try
        {
            foreach (var (connectionKey, state) in _connectionStates)
            {
                // Check for stale connections
                if (state.Status == ConnectionStatus.Connected && 
                    state.TimeSinceLastActivity > TimeSpan.FromMinutes(10))
                {
                    issues.Add(new ConnectionHealthIssue
                    {
                        ConnectionKey = connectionKey,
                        IssueType = HealthIssueType.StaleConnection,
                        Description = $"No activity for {state.TimeSinceLastActivity.TotalMinutes:F1} minutes",
                        Severity = state.TimeSinceLastActivity > TimeSpan.FromMinutes(30) 
                            ? IssueSeverity.High 
                            : IssueSeverity.Medium
                    });
                }

                // Check for repeated failures
                if (state.FailureCount >= 3)
                {
                    issues.Add(new ConnectionHealthIssue
                    {
                        ConnectionKey = connectionKey,
                        IssueType = HealthIssueType.RepeatedFailures,
                        Description = $"{state.FailureCount} consecutive failures. Last error: {state.LastError}",
                        Severity = IssueSeverity.High
                    });
                }

                // Check for long connection duration (might indicate resource leak)
                if (state.Status == ConnectionStatus.Connected && 
                    state.ConnectionDuration > TimeSpan.FromHours(24))
                {
                    issues.Add(new ConnectionHealthIssue
                    {
                        ConnectionKey = connectionKey,
                        IssueType = HealthIssueType.LongRunningConnection,
                        Description = $"Connection has been active for {state.ConnectionDuration.TotalHours:F1} hours",
                        Severity = IssueSeverity.Low
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

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogInformation("Disposing ConnectionStateManager - tracked {ConnectionCount} connections", 
            _connectionStates.Count);

        _connectionStates.Clear();
        _disposed = true;
    }
}

/// <summary>
/// Summary of connection states
/// </summary>
public class ConnectionStateSummary
{
    public int TotalConnections { get; set; }
    public int ConnectedCount { get; set; }
    public int FailedCount { get; set; }
    public int ConnectingCount { get; set; }
    public int HealthyCount { get; set; }
    public int UnhealthyCount { get; set; }
    public double AverageConnectionDuration { get; set; }
    public List<string> ConnectionKeys { get; set; } = new();
}

/// <summary>
/// Connection health issue
/// </summary>
public class ConnectionHealthIssue
{
    public string ConnectionKey { get; set; } = string.Empty;
    public HealthIssueType IssueType { get; set; }
    public string Description { get; set; } = string.Empty;
    public IssueSeverity Severity { get; set; }
}

public enum HealthIssueType
{
    StaleConnection,
    RepeatedFailures,
    LongRunningConnection,
    UnresponsiveConnection
}

public enum IssueSeverity
{
    Low,
    Medium,
    High,
    Critical
}