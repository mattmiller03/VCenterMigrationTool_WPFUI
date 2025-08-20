using Meziantou.Framework.Win32;
using VCenterMigrationTool.Models;
using System.Runtime.Versioning;

namespace VCenterMigrationTool.Services;

[SupportedOSPlatform("windows")]
public class CredentialService
{
    private const string CredentialTargetPrefix = "VCenterMigrationTool_";

    public void SavePassword (VCenterConnection profile, string password)
    {
        var targetName = GetTargetName(profile);

        if (!profile.ShouldSavePassword || string.IsNullOrEmpty(password))
        {
            DeletePassword(profile);
            return;
        }

        CredentialManager.WriteCredential(
            targetName,
            profile.Username,
            password,
            CredentialPersistence.LocalMachine);
    }

    public string? GetPassword (VCenterConnection profile)
    {
        var targetName = GetTargetName(profile);
        var credential = CredentialManager.ReadCredential(targetName);
        return credential?.Password;
    }

    public void DeletePassword (VCenterConnection profile)
    {
        var targetName = GetTargetName(profile);
        var existingCredential = CredentialManager.ReadCredential(targetName);
        if (existingCredential != null)
        {
            CredentialManager.DeleteCredential(targetName);
        }
    }

    // FIX: Removed the redundant 'VCenterMigrationTool.Models.' qualifier
    private string GetTargetName (VCenterConnection profile)
    {
        return $"{CredentialTargetPrefix}{profile.Name}_{profile.ServerAddress}";
    }
}