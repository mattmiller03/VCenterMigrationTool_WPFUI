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
                if (File.Exists(_configFilePath))
                {
                    var jsonString = File.ReadAllText(_configFilePath);
                    var options = new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    };
                    
                    var config = JsonSerializer.Deserialize<AppConfig>(jsonString, options);
                    _logger.LogInformation("Configuration loaded successfully from {ConfigPath}", _configFilePath);
                    return config ?? CreateDefaultConfiguration();
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
