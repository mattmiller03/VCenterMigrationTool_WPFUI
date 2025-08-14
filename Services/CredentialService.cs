using Meziantou.Framework.Win32; // Use the new namespace
using VCenterMigrationTool.Models;
using System.Runtime.Versioning;

namespace VCenterMigrationTool.Services;

// This attribute tells the compiler this class only works on Windows
[SupportedOSPlatform("windows")]
public class CredentialService
{
    private const string CredentialTargetPrefix = "VCenterMigrationTool_";

    public void SavePassword(VCenterConnection profile, string password)
    {
        var targetName = GetTargetName(profile);

        if (!profile.ShouldSavePassword || string.IsNullOrEmpty(password))
        {
            // If the user doesn't want to save, or the password is empty,
            // ensure any existing credential for this profile is deleted.
            DeletePassword(profile);
            return;
        }

        // Use the new library to write the credential
        CredentialManager.WriteCredential(
            targetName,
            profile.Username,
            password,
            CredentialPersistence.LocalMachine);
    }

    public string? GetPassword(VCenterConnection profile)
    {
        var targetName = GetTargetName(profile);

        // Use the new library to read the credential
        var credential = CredentialManager.ReadCredential(targetName);

        return credential?.Password;
    }

    public void DeletePassword(VCenterConnection profile)
    {
        var targetName = GetTargetName(profile);

        // Check if credential exists before attempting to delete
        var existingCredential = CredentialManager.ReadCredential(targetName);
        if (existingCredential != null)
        {
            CredentialManager.DeleteCredential(targetName);
        }
    }

    // This helper method remains the same
    private string GetTargetName(VCenterMigrationTool.Models.VCenterConnection profile)
    {
        return $"{CredentialTargetPrefix}{profile.Name}_{profile.ServerAddress}";
    }
}