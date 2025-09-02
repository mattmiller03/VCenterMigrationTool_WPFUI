using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using VCenterMigrationTool.Views.Pages;
using VCenterMigrationTool.ViewModels.Base;

namespace VCenterMigrationTool.ViewModels;

public partial class EsxiHostsViewModel : ActivityLogViewModelBase
    {
        private readonly PersistentExternalConnectionService _persistentConnectionService;
        private readonly SharedConnectionService _sharedConnectionService;
        private readonly ConfigurationService _configurationService;
        private readonly PowerShellLoggingService _powerShellLoggingService;
        private readonly ILogger<EsxiHostsViewModel> _logger;


        [ObservableProperty]
        private ObservableCollection<ClusterInfo> _sourceClusters = new();

        [ObservableProperty]
        private ObservableCollection<ClusterInfo> _targetClusters = new();

        [ObservableProperty]
        private ClusterInfo? _selectedSourceCluster;

        [ObservableProperty]
        private ClusterInfo? _selectedTargetCluster;

        [ObservableProperty]
        private ObservableCollection<EsxiHost> _selectedSourceHosts = new();

        [ObservableProperty]
        private ObservableCollection<EsxiHost> _availableTargetHosts = new();

        [ObservableProperty]
        private string _migrationStatus = "Ready";

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _loadingMessage = "";

        [ObservableProperty]
        private string _sourceConnectionStatus = "Not connected";

        [ObservableProperty]
        private string _targetConnectionStatus = "Not connected";

        [ObservableProperty]
        private bool _isSourceConnected;

        [ObservableProperty]
        private bool _isTargetConnected;

        // Operation mode flags
        [ObservableProperty]
        private bool _isMigrationMode = true;

        [ObservableProperty]
        private bool _isBackupMode = false;
        
        [ObservableProperty]
        private bool _isMigrating;

        [ObservableProperty]
        private bool _isBackingUp;

        [ObservableProperty]
        private string _migrationProgress = "";

        [ObservableProperty]
        private string _backupProgress = "";


    public EsxiHostsViewModel (
            PersistentExternalConnectionService persistentConnectionService,
            SharedConnectionService sharedConnectionService,
            ConfigurationService configurationService,
            PowerShellLoggingService powerShellLoggingService,
            ILogger<EsxiHostsViewModel> logger)
        {
            _persistentConnectionService = persistentConnectionService;
            _sharedConnectionService = sharedConnectionService;
            _configurationService = configurationService;
            _powerShellLoggingService = powerShellLoggingService;
            _logger = logger;

            // Initialize activity log
            InitializeActivityLog("ESXi Hosts Migration");
        }

    /// <summary>
    /// Initialize the view model and load data
    /// </summary>
    public async Task InitializeAsync ()
        {
        await CheckConnectionsAndLoadData();
        }

    /// <summary>
    /// Check connections and load cluster/host data
    /// </summary>
    private async Task CheckConnectionsAndLoadData ()
        {
        IsLoading = true;
        LoadingMessage = "Checking connections...";

        try
            {
            // Check source connection - try SharedConnectionService first (supports both API and PowerCLI)
            var sourceConnected = await _sharedConnectionService.IsConnectedAsync("source");
            if (sourceConnected && _sharedConnectionService.SourceConnection != null)
                {
                var (isConnected, sessionId, version) = _persistentConnectionService.GetConnectionInfo("source");
                IsSourceConnected = true;
                SourceConnectionStatus = $"✅ {_sharedConnectionService.SourceConnection.ServerAddress}";
                _logger.LogInformation("Source vCenter connected: {Server}", _sharedConnectionService.SourceConnection.ServerAddress);

                // Load source data
                await LoadSourceClusters();
                }
            else
                {
                IsSourceConnected = false;
                SourceConnectionStatus = "❌ Not connected";
                _logger.LogWarning("Source vCenter not connected");
                LogMessage("⚠️ Source vCenter not connected", "WARNING");
                }

            // Check target connection (optional for migration mode)
            var targetConnected = await _sharedConnectionService.IsConnectedAsync("target");
            if (targetConnected && _sharedConnectionService.TargetConnection != null)
                {
                var (isConnected, sessionId, version) = _persistentConnectionService.GetConnectionInfo("target");
                IsTargetConnected = true;
                TargetConnectionStatus = $"✅ {_sharedConnectionService.TargetConnection.ServerAddress}";
                _logger.LogInformation("Target vCenter connected: {Server}", _sharedConnectionService.TargetConnection.ServerAddress);

                // Load target data
                await LoadTargetClusters();
                }
            else
                {
                IsTargetConnected = false;
                TargetConnectionStatus = "❌ Not connected";
                _logger.LogWarning("Target vCenter not connected");
                LogMessage("⚠️ Target vCenter not connected", "WARNING");
                }

            // Update status based on what's connected
            UpdateOperationStatus();
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error checking connections");
            MigrationStatus = $"❌ Error: {ex.Message}";
            LogMessage($"❌ Connection error: {ex.Message}", "ERROR");
            }
        finally
            {
            IsLoading = false;
            LoadingMessage = "";
            }
        }


    private void UpdateOperationStatus ()
    {
        if (!IsSourceConnected)
        {
            MigrationStatus = "⚠️ Please connect to source vCenter from the Dashboard";
        }
        else if (IsMigrationMode && !IsTargetConnected)
        {
            MigrationStatus = "ℹ️ Source connected - Backup operations available. Connect target for migration.";
            // Switch to backup mode if only source is connected
            IsBackupMode = true;
            IsMigrationMode = false;
        }
        else if (IsSourceConnected && IsTargetConnected)
        {
            var sourceHostCount = SourceClusters.Sum(c => c.HostCount);
            var targetHostCount = TargetClusters.Sum(c => c.HostCount);
            MigrationStatus = $"✅ Ready • Source: {SourceClusters.Count} clusters, {sourceHostCount} hosts • Target: {TargetClusters.Count} clusters, {targetHostCount} hosts";
            // Enable both modes
            IsMigrationMode = true;
            IsBackupMode = true;
        }
        else
        {
            var sourceHostCount = SourceClusters.Sum(c => c.HostCount);
            MigrationStatus = $"✅ Backup Ready • Source: {SourceClusters.Count} clusters, {sourceHostCount} hosts";
        }
    }

    /// <summary>
    /// Load clusters and hosts from both vCenters
    /// </summary>
    private async Task LoadClustersAndHosts ()
        {
        LoadingMessage = "Loading clusters and hosts...";

        try
            {
            // Load source clusters and hosts
            _logger.LogInformation("Loading source clusters and hosts...");
            var sourceClustersTask = LoadSourceClusters();

            // Load target clusters and hosts
            _logger.LogInformation("Loading target clusters and hosts...");
            var targetClustersTask = LoadTargetClusters();

            await Task.WhenAll(sourceClustersTask, targetClustersTask);

            // Format the status message with proper spacing
            var sourceHostCount = SourceClusters.Sum(c => c.HostCount);
            var targetHostCount = TargetClusters.Sum(c => c.HostCount);

            MigrationStatus = $"Ready • Source: {SourceClusters.Count} clusters, {sourceHostCount} hosts • Target: {TargetClusters.Count} clusters, {targetHostCount} hosts";
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error loading clusters and hosts");
            MigrationStatus = $"❌ Error loading data: {ex.Message}";
            }
        }

    /// <summary>
    /// Load source clusters and their hosts from inventory cache
    /// </summary>
    private async Task LoadSourceClusters ()
        {
        try
            {
            _logger.LogInformation("Loading source clusters from inventory cache...");
            
            var sourceInventory = _sharedConnectionService.GetSourceInventory();
            SourceClusters.Clear();

            if (sourceInventory != null)
            {
                _logger.LogInformation("Found {Count} clusters in source inventory", sourceInventory.Clusters.Count);
                
                foreach (var cluster in sourceInventory.Clusters)
                {
                    // Get hosts for this cluster from the inventory
                    var hostsInCluster = sourceInventory.GetHostsInCluster(cluster.Name);
                    
                    // Create cluster with hosts
                    var clusterInfo = new ClusterInfo
                    {
                        Name = cluster.Name,
                        Id = cluster.Id,
                        DatacenterName = cluster.DatacenterName,
                        HostCount = hostsInCluster.Count,
                        VmCount = cluster.VmCount,
                        DatastoreCount = cluster.DatastoreCount,
                        TotalCpuGhz = cluster.TotalCpuGhz,
                        TotalMemoryGB = cluster.TotalMemoryGB,
                        HAEnabled = cluster.HAEnabled,
                        DrsEnabled = cluster.DrsEnabled,
                        EVCMode = cluster.EVCMode
                    };

                    // Add hosts from inventory
                    foreach (var host in hostsInCluster)
                    {
                        clusterInfo.Hosts.Add(host);
                    }

                    SourceClusters.Add(clusterInfo);
                }
                
                SubscribeToHostSelectionEvents(SourceClusters);
                _logger.LogInformation("Loaded {Count} source clusters from inventory cache", SourceClusters.Count);
            }
            else
            {
                _logger.LogWarning("No source inventory available - connection may not be established or inventory not loaded");
            }
            
            await Task.CompletedTask; // Keep async signature
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error loading source clusters from inventory");
            }
        }

    /// <summary>
    /// Load target clusters and their hosts from inventory cache
    /// </summary>
    private async Task LoadTargetClusters ()
        {
        try
            {
            _logger.LogInformation("Loading target clusters from inventory cache...");
            
            var targetInventory = _sharedConnectionService.GetTargetInventory();
            TargetClusters.Clear();

            if (targetInventory != null)
            {
                _logger.LogInformation("Found {Count} clusters in target inventory", targetInventory.Clusters.Count);
                
                foreach (var cluster in targetInventory.Clusters)
                {
                    // Get hosts for this cluster from the inventory
                    var hostsInCluster = targetInventory.GetHostsInCluster(cluster.Name);
                    
                    // Create cluster with hosts
                    var clusterInfo = new ClusterInfo
                    {
                        Name = cluster.Name,
                        Id = cluster.Id,
                        DatacenterName = cluster.DatacenterName,
                        HostCount = hostsInCluster.Count,
                        VmCount = cluster.VmCount,
                        DatastoreCount = cluster.DatastoreCount,
                        TotalCpuGhz = cluster.TotalCpuGhz,
                        TotalMemoryGB = cluster.TotalMemoryGB,
                        HAEnabled = cluster.HAEnabled,
                        DrsEnabled = cluster.DrsEnabled,
                        EVCMode = cluster.EVCMode
                    };

                    // Add hosts from inventory
                    foreach (var host in hostsInCluster)
                    {
                        clusterInfo.Hosts.Add(host);
                    }

                    TargetClusters.Add(clusterInfo);
                }
                
                _logger.LogInformation("Loaded {Count} target clusters from inventory cache", TargetClusters.Count);
            }
            else
            {
                _logger.LogWarning("No target inventory available - connection may not be established or inventory not loaded");
            }
            
            await Task.CompletedTask; // Keep async signature
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error loading target clusters from inventory");
            }
        }

    [RelayCommand]
    private async Task RefreshData ()
    {
        // Use the general IsLoading for refresh operations
        await CheckConnectionsAndLoadData();
    }

    [RelayCommand]
    private async Task RefreshInventory()
    {
        try
        {
            IsLoading = true;
            MigrationStatus = "Refreshing vCenter inventory...";
            
            var tasks = new List<Task<bool>>();
            
            if (IsSourceConnected)
            {
                tasks.Add(_sharedConnectionService.RefreshSourceInventoryAsync());
            }
            
            if (IsTargetConnected)
            {
                tasks.Add(_sharedConnectionService.RefreshTargetInventoryAsync());
            }
            
            var results = await Task.WhenAll(tasks);
            var successCount = results.Count(r => r);
            
            if (successCount == tasks.Count)
            {
                MigrationStatus = "Inventory refreshed successfully";
                _logger.LogInformation("Inventory refresh completed successfully");
                
                // Reload the UI data from the refreshed cache
                await LoadClustersAndHosts();
            }
            else
            {
                MigrationStatus = "Inventory refresh failed - some connections could not be refreshed";
                _logger.LogWarning("Inventory refresh partially failed: {Success}/{Total} succeeded", successCount, tasks.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh inventory");
            MigrationStatus = $"Inventory refresh failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectAllSourceHosts ()
    {
        if (SelectedSourceCluster != null)
        {
            foreach (var host in SelectedSourceCluster.Hosts)
            {
                host.IsSelected = true; // This will trigger the selection event
            }
        }
    }
    [RelayCommand]
    private async Task BackupSelectedHosts ()
        {
        if (SelectedSourceHosts.Count == 0)
            {
            MigrationStatus = "⚠️ Please select hosts to backup";
            return;
            }

        IsBackingUp = true;
        BackupProgress = $"Starting backup of {SelectedSourceHosts.Count} hosts...";

        // Start backup job session for dashboard tracking
        var sessionId = _powerShellLoggingService.StartScriptLogging("ESXi Host Backup", "source");
        _powerShellLoggingService.LogParameters(sessionId, "ESXi Host Backup", new Dictionary<string, object>
        {
            ["HostCount"] = SelectedSourceHosts.Count,
            ["BackupType"] = "ESXi Configuration"
        });

        try
            {
            var backupPath = Path.Combine(
                _configurationService.GetConfiguration().ExportPath ?? "Backups",
                $"ESXi_Backup_{DateTime.Now:yyyyMMdd_HHmmss}"
            );

            Directory.CreateDirectory(backupPath);

            // Get the configured log path
            var configuredLogPath = _configurationService.GetConfiguration().LogPath ?? 
                                   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                                               "VCenterMigrationTool", "Logs");

            // Get the absolute path to the script file
            var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "Active", "Backup-ESXiHostConfig.ps1");

            if (!File.Exists(scriptPath))
            {
                _logger.LogError("Backup script not found at {Path}", scriptPath);
                MigrationStatus = "❌ Backup script not found. Please ensure it's in the Scripts folder.";
                return;
            }

            int completed = 0;
            foreach (var host in SelectedSourceHosts)
                {
                completed++;
                BackupProgress = $"Backing up {host.Name} ({completed}/{SelectedSourceHosts.Count})...";
                
                // Log individual host backup activity
                _powerShellLoggingService.LogScriptOutput(sessionId, "ESXi Host Backup", 
                    $"Starting backup for host: {host.Name} ({completed}/{SelectedSourceHosts.Count})");
                LogMessage($"🔄 Backing up host: {host.Name} ({completed}/{SelectedSourceHosts.Count})", "INFO");

                // Build the command to execute the external script with console output suppressed
                // Clear any previous script-level and global variables to ensure clean state for each host
                var backupScript = $@"
                    try {{
                        # Clear any previous logging-related global variables to ensure clean state
                        Remove-Variable -Name 'ScriptLogFile' -Scope Global -ErrorAction SilentlyContinue
                        Remove-Variable -Name 'ScriptSessionId' -Scope Global -ErrorAction SilentlyContinue
                        Remove-Variable -Name 'ScriptStartTime' -Scope Global -ErrorAction SilentlyContinue
                        Remove-Variable -Name 'ConfiguredLogPath' -Scope Global -ErrorAction SilentlyContinue
                        Remove-Variable -Name 'SuppressConsoleOutput' -Scope Global -ErrorAction SilentlyContinue
                        
                        # Capture the output and ensure only JSON is returned
                        $backupOutput = & '{scriptPath}' -HostName '{host.Name}' -BackupPath '{backupPath}' -LogPath '{configuredLogPath}' -BypassModuleCheck $true -SuppressConsoleOutput $true 2>&1
                        
                        # Extract JSON from the output more reliably
                        $allOutput = $backupOutput -join ""`n""
                        
                        # Try multiple approaches to extract valid JSON
                        $jsonFound = $false
                        $jsonOutput = """"
                        
                        # Method 1: Look for lines that start and end with braces
                        $jsonLines = $backupOutput | Where-Object {{ $_ -match '^\s*\{{.*\}}\s*$' }}
                        if ($jsonLines) {{
                            $jsonOutput = $jsonLines | Select-Object -Last 1
                            $jsonFound = $true
                        }}
                        
                        # Method 2: If no complete lines, try regex extraction
                        if (-not $jsonFound -and $allOutput -match '\{{[^{{}}]*\}}') {{
                            $jsonOutput = $matches[0]
                            $jsonFound = $true
                        }}
                        
                        if ($jsonFound) {{
                            $jsonOutput
                        }} else {{
                            # If no JSON found, check if there was an error in the output
                            if ($allOutput -match 'error|exception|fail') {{
                                @{{ Success = $false; Message = ""Script execution error: $allOutput"" }} | ConvertTo-Json -Compress
                            }} else {{
                                @{{ Success = $false; Message = ""No valid JSON output from script. Raw output: $allOutput"" }} | ConvertTo-Json -Compress
                            }}
                        }}
                    }} catch {{
                        # Ensure errors are still captured and sent to the output stream.
                        $errorJson = @{{ Success = $false; Message = ""PowerShell Error: $($_.Exception.Message)"" }} | ConvertTo-Json -Compress
                        Write-Output $errorJson
                    }}
                ";

                var result = await _persistentConnectionService.ExecuteCommandAsync("source", backupScript);

                if (!string.IsNullOrWhiteSpace(result))
                    {
                    try
                        {
                        // The script now outputs JSON, so we can parse it for details
                        var backupResult = JsonSerializer.Deserialize<JsonElement>(result);

                        if (backupResult.TryGetProperty("Success", out var success) && success.GetBoolean())
                        {
                            var filePath = backupResult.GetProperty("FilePath").GetString();
                            _logger.LogInformation("Successfully backed up host {Host} to {File}", host.Name, filePath);
                            
                            // Log successful host backup to PowerShell logging service
                            _powerShellLoggingService.LogScriptOutput(sessionId, "ESXi Host Backup", 
                                $"✅ Successfully backed up {host.Name} to {Path.GetFileName(filePath)}");
                            LogMessage($"✅ Successfully backed up host: {host.Name}", "SUCCESS");
                        }
                        else
                        {
                            var errorMessage = backupResult.TryGetProperty("Message", out var msg) ? msg.GetString() : "Unknown error from script.";
                            var debugOutput = $"JSON Response: {result}\nParsed Success: {(backupResult.TryGetProperty("Success", out var s) ? s.ToString() : "N/A")}";
                            _logger.LogError("Backup script returned failure for host {Host}. Debug: {Debug}", host.Name, debugOutput);
                            throw new InvalidOperationException($"Backup script failed for host {host.Name}: {errorMessage}");
                        }
                    }
                    catch (JsonException ex)
                        {
                        _logger.LogError(ex, "JSON validation failed for host {Host}. Raw output: {Result}", host.Name, result);
                        var debugFileName = Path.Combine(backupPath, $"{host.Name}_debug_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                        await File.WriteAllTextAsync(debugFileName, result);
                        throw new InvalidOperationException($"JSON validation failed for host {host.Name}. See debug file for details.");
                        }
                    }
                else
                    {
                    _logger.LogError("PowerShell script returned empty result for host {Host}", host.Name);
                    throw new InvalidOperationException($"PowerShell script returned no output for host {host.Name}.");
                    }
                }

            MigrationStatus = $"✅ Successfully backed up {SelectedSourceHosts.Count} hosts to {backupPath}";
            BackupProgress = "Backup completed successfully!";
            LogMessage($"✅ Backup operation completed - {SelectedSourceHosts.Count} hosts backed up to {Path.GetFileName(backupPath)}", "SUCCESS");
            
            // End the backup job session successfully
            _powerShellLoggingService.EndScriptLogging(sessionId, "ESXi Host Backup", true, 
                $"Successfully backed up {SelectedSourceHosts.Count} hosts to {Path.GetFileName(backupPath)}");

            // Keep the success message visible for a moment
            await Task.Delay(2000);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error during host backup");
            MigrationStatus = $"❌ Backup failed: {ex.Message}";
            BackupProgress = $"Backup failed: {ex.Message}";
            LogMessage($"❌ Backup operation failed: {ex.Message}", "ERROR");
            
            // End the backup job session with failure
            _powerShellLoggingService.EndScriptLogging(sessionId, "ESXi Host Backup", false, 
                $"Backup failed: {ex.Message}");

            // Keep the error message visible for a moment
            await Task.Delay(3000);
            }
        finally
            {
            IsBackingUp = false;
            BackupProgress = "";
            }
        }

    [RelayCommand]
    private void ClearSourceHostSelection ()
    {
        if (SelectedSourceCluster != null)
        {
            foreach (var host in SelectedSourceCluster.Hosts)
            {
                host.IsSelected = false; // This will trigger the selection event
            }
        }
    }

    [RelayCommand]
    private async Task MigrateSelectedHosts ()
        {
        if (!IsTargetConnected)
            {
            MigrationStatus = "⚠️ Please connect to target vCenter for migration";
            return;
            }

        if (SelectedSourceHosts.Count == 0)
            {
            MigrationStatus = "⚠️ Please select hosts to migrate";
            return;
            }

        if (SelectedTargetCluster == null)
            {
            MigrationStatus = "⚠️ Please select a target cluster";
            return;
            }

        IsMigrating = true;
        MigrationProgress = $"Starting migration of {SelectedSourceHosts.Count} hosts...";

        try
            {
            int completed = 0;
            foreach (var host in SelectedSourceHosts)
                {
                completed++;
                MigrationProgress = $"Migrating {host.Name} ({completed}/{SelectedSourceHosts.Count})...";
                LogMessage($"🔄 Migrating host: {host.Name} ({completed}/{SelectedSourceHosts.Count})", "INFO");

                // Build migration script
                var migrateScript = $@"
                $sourceHost = Get-VMHost -Name '{host.Name}' -ErrorAction Stop
                
                # Put host in maintenance mode
                Write-Output 'Entering maintenance mode...'
                Set-VMHost -VMHost $sourceHost -State Maintenance -Evacuate:$true -Confirm:$false
                
                # DO NOT DISCONNECT - Using persistent connections managed by application
                Write-Output 'Preserving vCenter connection for other operations...'
                # Connection will be maintained for subsequent operations
                
                Write-Output 'Host ready for migration to target vCenter'
                'SUCCESS'
            ";

                var result = await _persistentConnectionService.ExecuteCommandAsync("source", migrateScript);

                if (result.Contains("SUCCESS"))
                    {
                    _logger.LogInformation("Host {Host} prepared for migration", host.Name);
                    LogMessage($"✅ Host {host.Name} prepared for migration", "SUCCESS");

                    // Now add to target cluster
                    var addScript = $@"
                    $targetCluster = Get-Cluster -Name '{SelectedTargetCluster.Name}' -ErrorAction Stop
                    
                    # Add host to target cluster
                    Write-Output 'Adding host to target cluster...'
                    Add-VMHost -Name '{host.Name}' -Location $targetCluster -User 'root' -Password 'YourPassword' -Force -Confirm:$false
                    
                    Write-Output 'Host successfully migrated'
                    'SUCCESS'
                ";

                    // Note: You'll need to handle host credentials properly here
                    result = await _persistentConnectionService.ExecuteCommandAsync("target", addScript);

                    if (result.Contains("SUCCESS"))
                        {
                        _logger.LogInformation("Host {Host} successfully migrated to {Cluster}",
                            host.Name, SelectedTargetCluster.Name);
                        LogMessage($"✅ Host {host.Name} successfully migrated to cluster: {SelectedTargetCluster.Name}", "SUCCESS");
                        }
                    }
                }

            MigrationStatus = $"✅ Successfully migrated {SelectedSourceHosts.Count} hosts";
            MigrationProgress = "Migration completed successfully!";
            LogMessage($"✅ Migration operation completed - {SelectedSourceHosts.Count} hosts migrated successfully", "SUCCESS");

            // Refresh the data
            await RefreshData();

            // Keep the success message visible for a moment
            await Task.Delay(2000);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error during host migration");
            MigrationStatus = $"❌ Migration failed: {ex.Message}";
            MigrationProgress = $"Migration failed: {ex.Message}";
            LogMessage($"❌ Migration operation failed: {ex.Message}", "ERROR");

            // Keep the error message visible for a moment
            await Task.Delay(3000);
            }
        finally
            {
            IsMigrating = false;
            MigrationProgress = "";
            }
        }

    /// <summary>
    /// Handle host selection changes
    /// </summary>
    private void OnHostSelectionChanged (EsxiHost host, bool isSelected)
    {
        if (isSelected && !SelectedSourceHosts.Contains(host))
        {
            SelectedSourceHosts.Add(host);
        }
        else if (!isSelected && SelectedSourceHosts.Contains(host))
        {
            SelectedSourceHosts.Remove(host);
        }
    }
    /// <summary>
    /// Subscribe to host selection events when clusters are loaded
    /// </summary>
    private void SubscribeToHostSelectionEvents (IEnumerable<ClusterInfo> clusters)
    {
        foreach (var cluster in clusters)
        {
            foreach (var host in cluster.Hosts)
            {
                // Unsubscribe first to avoid duplicate subscriptions
                host.SelectionChanged -= OnHostSelectionChanged;
                // Subscribe to selection changes
                host.SelectionChanged += OnHostSelectionChanged;
            }
        }
    }
    partial void OnSelectedSourceClusterChanged (ClusterInfo? value)
    {
        if (value != null)
        {
            _logger.LogInformation("Selected source cluster: {Cluster} with {Count} hosts",
                value.Name, value.Hosts.Count);
        }

        // Clear selection from all hosts in all clusters
        foreach (var cluster in SourceClusters)
        {
            foreach (var host in cluster.Hosts)
            {
                if (host.IsSelected)
                {
                    host.IsSelected = false;
                }
            }
        }

        // Clear the selected hosts collection
        SelectedSourceHosts.Clear();
    }

    partial void OnSelectedTargetClusterChanged (ClusterInfo? value)
        {
        if (value != null)
            {
            _logger.LogInformation("Selected target cluster: {Cluster}", value.Name);

            // Update available hosts for display
            AvailableTargetHosts.Clear();
            foreach (var host in value.Hosts)
                {
                AvailableTargetHosts.Add(host);
                }
            }
        }
    }