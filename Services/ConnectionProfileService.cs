// In Services/ConnectionProfileService.cs
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services;

public class ConnectionProfileService
{
    private readonly string _appDataPath;
    private readonly string _filePath;
    // A salt for the data protection. Can be anything, but should be consistent.
    private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("VCenterMigrationToolSalt");

    public ObservableCollection<VCenterConnection> Profiles { get; } = new();

    public ConnectionProfileService()
    {
        // Define a path in the user's local AppData folder
        _appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCenterMigrationTool");
        _filePath = Path.Combine(_appDataPath, "profiles.json");

        LoadProfiles();
    }

    private void LoadProfiles()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var loadedProfiles = JsonSerializer.Deserialize<ObservableCollection<VCenterConnection>>(json);

            if (loadedProfiles != null)
            {
                foreach (var profile in loadedProfiles)
                {
                    Profiles.Add(profile);
                }
            }
        }
        catch (Exception)
        {
            // Handle potential file corruption or deserialization errors
        }
    }
    public void UpdateProfile()
    {
        // Because VCenterConnection is an ObservableObject, the UI is already updated.
        // We just need to trigger a save to the JSON file.
        SaveProfiles();
    }
    private void SaveProfiles()
    {
        try
        {
            // Ensure the directory exists
            Directory.CreateDirectory(_appDataPath);
            var json = JsonSerializer.Serialize(Profiles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception)
        {
            // Handle potential file access errors
        }
    }

    public void AddProfile(VCenterConnection profile)
    {
        Profiles.Add(profile);
        SaveProfiles();
    }

    public void RemoveProfile(VCenterConnection profile)
    {
        Profiles.Remove(profile);
        SaveProfiles();
    }

    // New method to encrypt the password before saving a profile
    public void ProtectPassword(VCenterConnection profile, string? password)
    {
        if (profile.ShouldSavePassword && !string.IsNullOrEmpty(password))
        {
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            // Encrypt the data using the current user's context
            var protectedBytes = ProtectedData.Protect(passwordBytes, _entropy, DataProtectionScope.CurrentUser);
            profile.ProtectedPassword = Convert.ToBase64String(protectedBytes);
        }
        else
        {
            profile.ProtectedPassword = null;
        }
    }

    // New method to decrypt the password when it's needed
    public string? UnprotectPassword(VCenterConnection profile)
    {
        if (string.IsNullOrEmpty(profile.ProtectedPassword))
            return null;

        try
        {
            var protectedBytes = Convert.FromBase64String(profile.ProtectedPassword);
            // Decrypt the data
            var passwordBytes = ProtectedData.Unprotect(protectedBytes, _entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(passwordBytes);
        }
        catch
        {
            // Handle cases where decryption fails
            return null;
        }
    }
}