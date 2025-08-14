

namespace VCenterMigrationTool.Services;

public interface IDialogService
{
    // --- FIX: Change return type from SecureString to string ---
    (bool? DialogResult, string? Password) ShowPasswordDialog (string title, string message);
}