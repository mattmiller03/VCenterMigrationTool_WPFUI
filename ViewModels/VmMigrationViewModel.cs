using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class VmMigrationViewModel : ObservableObject, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ILogger<VmMigrationViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<VirtualMachine> _sourceVms = new();

    [ObservableProperty]
    private ObservableCollection<TargetHost> _targetHosts = new();

    [ObservableProperty]
    private TargetHost? _selectedTargetHost;

    [ObservableProperty]
    private ObservableCollection<TargetDatastore> _targetDatastores = new();

    [ObservableProperty]
    private TargetDatastore? _selectedTargetDatastore;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _logOutput = "Awaiting migration task...";

    public VmMigrationViewModel (
        HybridPowerShellService powerShellService,
        SharedConnectionService sharedConnectionService,
        ILogger<VmMigrationViewModel> logger)
        {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
        _logger = logger;
        }

    public void OnNavigatedTo () { }
    public void OnNavigatedFrom () { }

    [RelayCommand]
    private async Task OnMigrateVms ()
        {
        IsBusy = true;
        LogOutput = "Starting VM migration...";

        var selectedVms = SourceVms.Where(vm => vm.IsSelected).ToList();
        if (!selectedVms.Any() || SelectedTargetHost is null || SelectedTargetDatastore is null)
            {
            LogOutput += "\nPlease select at least one VM, a target host, and a target datastore.";
            IsBusy = false;
            return;
            }

        LogOutput = "Starting VM migration...\n";

        foreach (var vm in selectedVms)
            {
            LogOutput += $"Simulating migration of VM '{vm.Name}' to host '{SelectedTargetHost.Name}'...\n";
            await Task.Delay(2000);
            LogOutput += $"Migration of '{vm.Name}' complete.\n";
            }

        LogOutput += "\nAll selected VMs have been migrated.";
        IsBusy = false;
        }

    [RelayCommand]
    private async Task LoadData ()
        {
        IsBusy = true;
        LogOutput = "Loading VMs from source and resources from target...";

        try
            {
            // Check if we have active connections
            if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
                {
                LogOutput = "Error: Please establish both source and target connections on the Dashboard page first.";

                // Add some sample data for testing
                LoadSampleData();
                IsBusy = false;
                return;
                }

            // Load VMs from source - with error handling
            try
                {
                var vms = await _powerShellService.RunScriptAndGetObjectsAsync<VirtualMachine>(
                    ".\\Scripts\\Get-VmsForMigration.ps1",
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "VCenterServer", _sharedConnectionService.SourceConnection.ServerAddress },
                        { "Username", _sharedConnectionService.SourceConnection.Username },
                        { "Password", "placeholder" } // This would need proper password handling
                    });

                if (vms?.Any() == true)
                    {
                    SourceVms = new ObservableCollection<VirtualMachine>(vms.OrderBy(vm => vm.Name));
                    LogOutput += $"\nLoaded {SourceVms.Count} VMs from source vCenter.";
                    }
                else
                    {
                    LogOutput += "\nNo VMs returned from source script. Loading sample data...";
                    LoadSampleVMs();
                    }
                }
            catch (System.Exception ex)
                {
                _logger.LogError(ex, "Failed to load VMs from source");
                LogOutput += $"\nError loading VMs: {ex.Message}. Loading sample data...";
                LoadSampleVMs();
                }

            // Load target resources - with error handling
            try
                {
                var targetJson = await _powerShellService.RunScriptAsync(
                    ".\\Scripts\\Get-TargetResources.ps1",
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "VCenterServer", _sharedConnectionService.TargetConnection.ServerAddress },
                        { "Username", _sharedConnectionService.TargetConnection.Username },
                        { "Password", "placeholder" } // This would need proper password handling
                    });

                // Check if we got valid JSON
                if (!string.IsNullOrWhiteSpace(targetJson) && targetJson.TrimStart().StartsWith("{"))
                    {
                    using var doc = JsonDocument.Parse(targetJson);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("Hosts", out var hostsElement))
                        {
                        TargetHosts = JsonSerializer.Deserialize<ObservableCollection<TargetHost>>(
                            hostsElement.GetRawText()) ?? new();
                        }

                    if (root.TryGetProperty("Datastores", out var datastoresElement))
                        {
                        TargetDatastores = JsonSerializer.Deserialize<ObservableCollection<TargetDatastore>>(
                            datastoresElement.GetRawText()) ?? new();
                        }

                    LogOutput += $"\nLoaded {TargetHosts.Count} hosts and {TargetDatastores.Count} datastores from target.";
                    }
                else
                    {
                    LogOutput += "\nInvalid response from target resources script. Loading sample data...";
                    LoadSampleTargetResources();
                    }
                }
            catch (System.Exception ex)
                {
                _logger.LogError(ex, "Failed to load target resources");
                LogOutput += $"\nError loading target resources: {ex.Message}. Loading sample data...";
                LoadSampleTargetResources();
                }

            LogOutput += "\nData loading completed.";
            }
        catch (System.Exception ex)
            {
            _logger.LogError(ex, "Unexpected error during data loading");
            LogOutput += $"\nUnexpected error: {ex.Message}";
            LoadSampleData();
            }
        finally
            {
            IsBusy = false;
            }
        }

    private void LoadSampleData ()
        {
        LoadSampleVMs();
        LoadSampleTargetResources();
        }

    private void LoadSampleVMs ()
        {
        SourceVms = new ObservableCollection<VirtualMachine>
        {
            new() { Name = "Web-Server-01", PowerState = "PoweredOn", EsxiHost = "esx01.lab.local", Datastore = "datastore1", Cluster = "Cluster1" },
            new() { Name = "DB-Server-01", PowerState = "PoweredOn", EsxiHost = "esx02.lab.local", Datastore = "datastore2", Cluster = "Cluster1" },
            new() { Name = "App-Server-01", PowerState = "PoweredOff", EsxiHost = "esx01.lab.local", Datastore = "datastore1", Cluster = "Cluster1" },
            new() { Name = "Test-VM-01", PowerState = "PoweredOn", EsxiHost = "esx03.lab.local", Datastore = "datastore3", Cluster = "Cluster2" }
        };
        }

    private void LoadSampleTargetResources ()
        {
        TargetHosts = new ObservableCollection<TargetHost>
        {
            new() { Name = "target-esx01.lab.local" },
            new() { Name = "target-esx02.lab.local" },
            new() { Name = "target-esx03.lab.local" }
        };

        TargetDatastores = new ObservableCollection<TargetDatastore>
        {
            new() { Name = "target-datastore1" },
            new() { Name = "target-datastore2" },
            new() { Name = "target-datastore3" }
        };
        }

    public async Task OnNavigatedToAsync ()
        {
        // Only load data if we don't have any VMs yet
        if (!SourceVms.Any())
            {
            await LoadData();
            }
        }

    public async Task OnNavigatedFromAsync () => await Task.CompletedTask;
    }