using CommunityToolkit.Mvvm.ComponentModel;

namespace VCenterMigrationTool.ViewModels;

public partial class PasswordPromptViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Password Required";

    [ObservableProperty]
    private string _message = "Please enter the password for the connection.";

    // --- FIX: Change from SecureString to string ---
    public string? Password { get; set; }
}