using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;

namespace VCenterMigrationTool.ViewModels;

public partial class NetworkMigrationViewModel : ObservableObject
{
    private readonly PowerShellService _powerShellService;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _logOutput = "Migration log will be displayed here...";
    [ObservableProperty] private ObservableCollection<NetworkHostNode> _networkTopology = new();

    public NetworkMigrationViewModel(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    [RelayCommand]
    private async Task LoadNetworkData()
    {
        IsBusy = true;
        LogOutput = "Loading network topology from source vCenter...";
        NetworkTopology = await _powerShellService.RunScriptAndGetObjectsAsync<NetworkHostNode>(".\\Scripts\\Get-NetworkTopology.ps1", new());
        LogOutput = $"Loaded network topology for {NetworkTopology.Count} hosts.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task OnMigrateNetwork()
    {
        IsBusy = true;
        LogOutput = "Starting network migration...\n";
        // Logic to iterate through _networkTopology and find all IsSelected items
        await Task.Delay(2000); // Simulate migration work
        LogOutput += "Network migration simulation complete.";
        IsBusy = false;
    }
}