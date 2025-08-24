using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services
{
    public class ConfigurationService
    {
        private readonly ILogger<ConfigurationService> _logger;
        private readonly string _configFilePath;
        private AppConfig _appConfig;

        public ConfigurationService(ILogger<ConfigurationService> logger)
        {
            _logger = logger;
            
            // Create a user-specific config directory
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDirectory = Path.Combine(appDataPath, "VCenterMigrationTool");
            
            if (!Directory.Exists(appDirectory))
            {
                Directory.CreateDirectory(appDirectory);
            }
            
            _configFilePath = Path.Combine(appDirectory, "userconfig.json");
            _appConfig = LoadConfiguration();
        }

        public AppConfig GetConfiguration()
        {
            return _appConfig;
        }

        public async Task SaveConfigurationAsync(AppConfig config)
        {
            try
            {
                _appConfig = config;
                
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                var jsonString = JsonSerializer.Serialize(_appConfig, options);
                await File.WriteAllTextAsync(_configFilePath, jsonString);
                
                _logger.LogInformation("Configuration saved successfully to {ConfigPath}", _configFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration to {ConfigPath}", _configFilePath);
                throw;
            }
        }

        public void SaveConfiguration(AppConfig config)
        {
            try
            {
                _appConfig = config;
                
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                var jsonString = JsonSerializer.Serialize(_appConfig, options);
                File.WriteAllText(_configFilePath, jsonString);
                
                _logger.LogInformation("Configuration saved successfully to {ConfigPath}", _configFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration to {ConfigPath}", _configFilePath);
                throw;
            }
        }

        private AppConfig LoadConfiguration()
        {
            try
            {
                // First, try to load from appsettings.json
                AppConfig? appSettingsConfig = null;
                var appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (File.Exists(appSettingsPath))
                {
                    try
                    {
                        var appSettingsJson = File.ReadAllText(appSettingsPath);
                        var appSettingsData = JsonSerializer.Deserialize<JsonElement>(appSettingsJson);
                        if (appSettingsData.TryGetProperty("AppConfig", out var appConfigElement))
                        {
                            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            appSettingsConfig = JsonSerializer.Deserialize<AppConfig>(appConfigElement.GetRawText(), options);
                            _logger.LogInformation("Loaded base configuration from appsettings.json with LogPath: {LogPath}", appSettingsConfig?.LogPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load appsettings.json, will use defaults");
                    }
                }
                
                // Then load or merge with userconfig.json
                if (File.Exists(_configFilePath))
                {
                    var jsonString = File.ReadAllText(_configFilePath);
                    var options = new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    };
                    
                    var userConfig = JsonSerializer.Deserialize<AppConfig>(jsonString, options);
                    
                    // If we have appSettings config, merge with user config (user config takes precedence for non-null values)
                    if (appSettingsConfig != null && userConfig != null)
                    {
                        // Use LogPath from appsettings if userconfig doesn't have it
                        if (string.IsNullOrEmpty(userConfig.LogPath) && !string.IsNullOrEmpty(appSettingsConfig.LogPath))
                        {
                            userConfig.LogPath = appSettingsConfig.LogPath;
                            _logger.LogInformation("Using LogPath from appsettings.json: {LogPath}", userConfig.LogPath);
                        }
                        
                        // Use ExportPath from appsettings if userconfig doesn't have it
                        if (string.IsNullOrEmpty(userConfig.ExportPath) && !string.IsNullOrEmpty(appSettingsConfig.ExportPath))
                        {
                            userConfig.ExportPath = appSettingsConfig.ExportPath;
                        }
                    }
                    
                    // Ensure LogPath is set even if not in either config
                    if (userConfig != null && string.IsNullOrEmpty(userConfig.LogPath))
                    {
                        userConfig.LogPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                            "VCenterMigrationTool", 
                            "Logs");
                        _logger.LogInformation("LogPath was empty in both configs, set to default: {LogPath}", userConfig.LogPath);
                        SaveConfiguration(userConfig); // Save the updated config with LogPath
                    }
                    
                    _logger.LogInformation("Configuration loaded with LogPath: {LogPath}", 
                        userConfig?.LogPath ?? "null");
                    return userConfig ?? appSettingsConfig ?? CreateDefaultConfiguration();
                }
                else if (appSettingsConfig != null)
                {
                    // Use appsettings config as base and save to user config
                    _logger.LogInformation("Creating user configuration from appsettings.json");
                    SaveConfiguration(appSettingsConfig);
                    return appSettingsConfig;
                }
                else
                {
                    _logger.LogInformation("Configuration file not found, creating default configuration");
                    var defaultConfig = CreateDefaultConfiguration();
                    SaveConfiguration(defaultConfig);
                    return defaultConfig;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration from {ConfigPath}, using defaults", _configFilePath);
                return CreateDefaultConfiguration();
            }
        }

        private AppConfig CreateDefaultConfiguration()
        {
            return new AppConfig
            {
                ApplicationTitle = "vCenter Migration Tool",
                AppVersion = "1.0.0",
                LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCenterMigrationTool", "Logs"),
                ExportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCenterMigrationTool", "Exports")
            };
        }
    }
}
