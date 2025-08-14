// In Services/ConnectionProfileService.cs
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Manages loading and saving of vCenter connection profiles.
/// </summary>
public class ConnectionProfileService
    {
    private readonly string _profilesPath;

    // --- FIX: Add the missing _entropy field ---
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VCenterMigrationToolSalt");

    public ObservableCollection<VCenterConnection> Profiles { get; private set; }
    public ConnectionProfileService ()
        {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appDirectory = Path.Combine(localAppData, "VCenterMigrationTool");
        Directory.CreateDirectory(appDirectory); // Ensure the directory exists
        _profilesPath = Path.Combine(appDirectory, "profiles.json");

        Profiles = new ObservableCollection<VCenterConnection>();
        LoadProfiles();
        }

    private void LoadProfiles ()
        {
        if (!File.Exists(_profilesPath))
            {
            Profiles = new ObservableCollection<VCenterConnection>();
            return;
            }

        var json = File.ReadAllText(_profilesPath);
        var profiles = JsonSerializer.Deserialize<ObservableCollection<VCenterConnection>>(json);
        Profiles = profiles ?? new ObservableCollection<VCenterConnection>();
        }

    public void UpdateProfile (VCenterConnection profile)
        {
        // The object is already updated in the collection in memory.
        // We just need to save the entire collection to the file.
        SaveProfiles();
        }

    public void SaveProfiles ()
        {
        var json = JsonSerializer.Serialize(Profiles, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_profilesPath, json);
        }

    public void AddProfile (VCenterConnection profile)
        {
        Profiles.Add(profile);
        SaveProfiles();
        }

    public void RemoveProfile (VCenterConnection profile)
        {
        Profiles.Remove(profile);
        SaveProfiles();
        }

    public void ProtectPassword (VCenterConnection profile, string? password)
        {
        if (profile.ShouldSavePassword && !string.IsNullOrEmpty(password))
            {
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var protectedBytes = ProtectedData.Protect(passwordBytes, Entropy, DataProtectionScope.CurrentUser);
            profile.ProtectedPassword = Convert.ToBase64String(protectedBytes);
            }
        else
            {
            profile.ProtectedPassword = null;
            }
        }

    public string? UnprotectPassword (VCenterConnection profile)
        {
        if (string.IsNullOrEmpty(profile.ProtectedPassword))
            return null;

        try
            {
            var protectedBytes = Convert.FromBase64String(profile.ProtectedPassword);
            var passwordBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(passwordBytes);
            }
        catch
            {
            return null;
            }
        }
    }