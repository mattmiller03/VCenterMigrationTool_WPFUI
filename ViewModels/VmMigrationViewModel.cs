using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class VmMigrationViewModel : ObservableObject, INavigationAware
{
    private readonly PowerShellService _powerShellService;
    private readonly ConnectionProfileService _profileService;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _logOutput = "Migration log will be displayed here...";

    [ObservableProperty] private ObservableCollection<VirtualMachine> _sourceVms = new();
    [ObservableProperty] private ObservableCollection<TargetHost> _targetHosts = new();
    [ObservableProperty] private ObservableCollection<TargetDatastore> _targetDatastores = new();

    [ObservableProperty] private TargetHost? _selectedTargetHost;
    [ObservableProperty] private TargetDatastore? _selectedTargetDatastore;

    public VmMigrationViewModel(PowerShellService powerShellService, ConnectionProfileService profileService)
    {
        _powerShellService = powerShellService;
        _profileService = profileService;
    }

    [RelayCommand]
    private async Task LoadData()
    {
        IsBusy = true;
        LogOutput = "Loading VMs from source and resources from target...";

        // Load VMs from source
        var vms = await _powerShellService.RunScriptAndGetObjectsAsync<VirtualMachine>(".\\Scripts\\Get-VmsForMigration.ps1", new());
        SourceVms = new ObservableCollection<VirtualMachine>(vms.OrderBy(vm => vm.Name));

        // Load Hosts and Datastores from target
        var targetJson = await _powerShellService.RunScriptAsync(".\\Scripts\\Get-TargetResources.ps1", new());
        using (var doc = JsonDocument.Parse(targetJson))
        {
            var root = doc.RootElement;
            TargetHosts = JsonSerializer.Deserialize<ObservableCollection<TargetHost>>(root.GetProperty("Hosts").GetRawText()) ?? new();
            TargetDatastores = JsonSerializer.Deserialize<ObservableCollection<TargetDatastore>>(root.GetProperty("Datastores").GetRawText()) ?? new();
        }

        LogOutput = $"Loaded {SourceVms.Count} VMs and {TargetHosts.Count} target hosts.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task OnMigrateVms()
    {
        var selectedVms = SourceVms.Where(vm => vm.IsSelected).ToList();
        if (!selectedVms.Any() || SelectedTargetHost is null || SelectedTargetDatastore is null)
        {
            LogOutput += "\nPlease select at least one VM, a target host, and a target datastore.";
            return;
        }

        IsBusy = true;
        LogOutput = "Starting VM migration...\n";

        foreach (var vm in selectedVms)
        {
            LogOutput += $"Simulating migration of VM '{vm.Name}' to host '{SelectedTargetHost.Name}'...\n";
            await Task.Delay(2000);
            LogOutput += $"Migration of '{vm.Name}' complete.\n";
        }

        IsBusy = false;
        LogOutput += "\nAll selected VMs have been migrated.";
    }

    public async Task OnNavigatedToAsync()
    {
        if (!SourceVms.Any())
        {
            await LoadData();
        }
    }
    public async Task OnNavigatedFromAsync() => await Task.CompletedTask;
}