// In ViewModels/ConnectionViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Data;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class ConnectionViewModel : ObservableObject, INavigationAware
{
    [ObservableProperty]
    private string _connectionStatus = "Ready.";

    [ObservableProperty]
    private VCenterConnection _selectedProfile;

    public ObservableCollection<VCenterConnection> ConnectionProfiles { get; set; }

    public ConnectionViewModel()
    {
        ConnectionProfiles = new ObservableCollection<VCenterConnection>
        {
            new VCenterConnection { Name = "Production", ServerAddress = "vcenter-prod.domain.local" },
            new VCenterConnection { Name = "IOT", ServerAddress = "vcenter-iot.domain.local" },
            new VCenterConnection { Name = "DMZ", ServerAddress = "vcenter-dmz.domain.local" }
        };
        _selectedProfile = ConnectionProfiles[0];
    }

    // This method is called when you navigate to the page.
    public async Task OnNavigatedToAsync()
    {
        await Task.CompletedTask;
    }

    // This method is called when you navigate away from the page.
    public async Task OnNavigatedFromAsync()
    {
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OnConnect(string password)
    {
        ConnectionStatus = "Connecting...";
        // Here you would call your PowerShellService to test the connection
        await Task.Delay(1000); // Simulate network latency
        ConnectionStatus = $"Successfully connected to {SelectedProfile.ServerAddress}!";
    }
}