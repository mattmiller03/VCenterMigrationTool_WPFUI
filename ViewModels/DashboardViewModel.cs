// In ViewModels/DashboardViewModel.cs

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class DashboardViewModel : ObservableObject, INavigationAware
{
    private readonly PowerShellService _powerShellService;

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private string _scriptOutput = "Script output will be displayed here...";

    public DashboardViewModel(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    [RelayCommand]
    private async Task OnExportConfiguration()
    {
        IsBusy = true;
        ScriptOutput = "Starting configuration export...\n";

        // IMPORTANT: For now, connection details are hardcoded.
        // We will implement a shared service to get these details later.
        var scriptParams = new Dictionary<string, object>
        {
            { "VCenterServer", "vcenter-prod.domain.local" },
            { "User", "your-user" },
            { "Password", "your-password" }, // This needs to be handled securely!
            { "ExportPath", "C:\\vCenter-Export" }
        };

        // Make sure you have this script in a 'Scripts' folder in your project
        string scriptPath = ".\\Scripts\\Export-vCenterConfig.ps1";
        ScriptOutput += await _powerShellService.RunScriptAsync(scriptPath, scriptParams);

        IsBusy = false;
    }

    [RelayCommand]
    private void OnImportConfiguration()
    {
        IsBusy = true;
        ScriptOutput = "Import feature is not yet implemented.";
        // You would follow a similar pattern to the export command here.
        IsBusy = false;
    }

    public async Task OnNavigatedToAsync()
    {
        await Task.CompletedTask;
    }

    public async Task OnNavigatedFromAsync()
    {
        await Task.CompletedTask;
    }
}