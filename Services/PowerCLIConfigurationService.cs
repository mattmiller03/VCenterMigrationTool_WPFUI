using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Manages PowerCLI module loading, configuration, and validation
/// </summary>
public class PowerCLIConfigurationService
{
    private readonly ILogger<PowerCLIConfigurationService> _logger;
    private readonly PowerShellProcessManager _processManager;

    public PowerCLIConfigurationService(
        ILogger<PowerCLIConfigurationService> logger,
        PowerShellProcessManager processManager)
    {
        _logger = logger;
        _processManager = processManager;
    }

    /// <summary>
    /// PowerCLI module information
    /// </summary>
    public class PowerCLIModuleInfo
    {
        public string ModuleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string ModuleType { get; set; } = string.Empty;
        public bool IsLoaded { get; set; }
        public List<string> AvailableCommands { get; set; } = new();
        public Dictionary<string, object> Configuration { get; set; } = new();
    }

    /// <summary>
    /// Configuration result information
    /// </summary>
    public class ConfigurationResult
    {
        public bool Success { get; set; }
        public string ModuleType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public PowerCLIModuleInfo? ModuleInfo { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Loads and configures PowerCLI modules in the specified process
    /// </summary>
    public async Task<ConfigurationResult> ConfigurePowerCLIAsync(
        PowerShellProcessManager.ManagedPowerShellProcess process,
        bool bypassModuleCheck = false)
    {
        if (process == null)
            throw new ArgumentNullException(nameof(process));

        var result = new ConfigurationResult();

        try
        {
            _logger.LogInformation("Configuring PowerCLI modules in process {ProcessId}...", process.ProcessId);

            if (bypassModuleCheck)
            {
                _logger.LogInformation("Bypassing PowerCLI module configuration due to bypassModuleCheck=true");
                result.Success = true;
                result.ModuleType = "Bypass Mode";
                result.Message = "PowerCLI configuration bypassed - limited functionality available";
                result.Warnings.Add("PowerCLI modules not loaded - only basic PowerShell commands available");
                return result;
            }

            // Step 1: Import PowerCLI modules
            var importResult = await ImportPowerCLIModulesAsync(process);
            if (!importResult.Success)
            {
                result.Success = false;
                result.Message = importResult.Message;
                result.Errors.AddRange(importResult.Errors);
                return result;
            }

            result.ModuleType = importResult.ModuleType;
            result.Warnings.AddRange(importResult.Warnings);

            // Step 2: Configure PowerCLI settings
            var configResult = await ApplyPowerCLIConfigurationAsync(process, importResult.ModuleType);
            if (!configResult.Success)
            {
                result.Success = false;
                result.Message = $"Module import succeeded but configuration failed: {configResult.Message}";
                result.Errors.AddRange(configResult.Errors);
                return result;
            }

            result.Warnings.AddRange(configResult.Warnings);

            // Step 3: Validate configuration
            var validationResult = await ValidatePowerCLIConfigurationAsync(process);
            result.ModuleInfo = validationResult.ModuleInfo;
            result.Warnings.AddRange(validationResult.Warnings);

            // Success
            result.Success = true;
            result.Message = $"PowerCLI successfully configured using {result.ModuleType}";
            
            _logger.LogInformation("✅ PowerCLI configuration completed successfully using {ModuleType}", result.ModuleType);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring PowerCLI in process {ProcessId}", process.ProcessId);
            
            result.Success = false;
            result.Message = $"PowerCLI configuration failed: {ex.Message}";
            result.Errors.Add($"Exception: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Imports PowerCLI modules using multiple fallback strategies
    /// </summary>
    private async Task<ConfigurationResult> ImportPowerCLIModulesAsync(
        PowerShellProcessManager.ManagedPowerShellProcess process)
    {
        try
        {
            _logger.LogDebug("Importing PowerCLI modules in process {ProcessId}...", process.ProcessId);

            var importScript = PowerShellScriptBuilder.BuildPowerCLIImportScript();
            var output = await _processManager.ExecuteCommandAsync(process, importScript, TimeSpan.FromSeconds(90));

            var result = new ConfigurationResult();

            if (output.Contains("MODULES_LOADED:"))
            {
                // Extract module type from output
                var moduleTypeMatch = System.Text.RegularExpressions.Regex.Match(output, @"MODULES_LOADED:(.+?)(?:\r?\n|$)");
                var moduleType = moduleTypeMatch.Success ? moduleTypeMatch.Groups[1].Value.Trim() : "Unknown";

                result.Success = true;
                result.ModuleType = moduleType;
                result.Message = $"Successfully loaded PowerCLI modules: {moduleType}";

                _logger.LogInformation("✅ PowerCLI modules imported successfully: {ModuleType}", moduleType);
            }
            else
            {
                result.Success = false;
                result.Message = "Failed to import PowerCLI modules";
                result.Errors.Add("No MODULES_LOADED confirmation found in output");
                
                // Extract diagnostic information
                var diagnosticLines = output.Split('\n')
                    .Where(line => line.Contains("DIAGNOSTIC:"))
                    .Select(line => line.Replace("DIAGNOSTIC:", "").Trim())
                    .ToList();
                
                result.Errors.AddRange(diagnosticLines);
                
                _logger.LogError("❌ Failed to import PowerCLI modules. Output: {Output}", output);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing PowerCLI modules in process {ProcessId}", process.ProcessId);
            
            return new ConfigurationResult
            {
                Success = false,
                Message = $"PowerCLI module import failed: {ex.Message}",
                Errors = { $"Exception: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Applies PowerCLI configuration settings
    /// </summary>
    private async Task<ConfigurationResult> ApplyPowerCLIConfigurationAsync(
        PowerShellProcessManager.ManagedPowerShellProcess process,
        string moduleType)
    {
        try
        {
            _logger.LogDebug("Applying PowerCLI configuration in process {ProcessId}...", process.ProcessId);

            var configScript = PowerShellScriptBuilder.BuildPowerCLIConfigurationScript(moduleType);
            var output = await _processManager.ExecuteCommandAsync(process, configScript, TimeSpan.FromSeconds(30));

            var result = new ConfigurationResult();

            if (output.Contains("CONFIG_SUCCESS"))
            {
                result.Success = true;
                result.Message = "PowerCLI configuration applied successfully";

                // Extract configuration verification details
                var verificationLines = output.Split('\n')
                    .Where(line => line.Contains("CONFIG_VERIFICATION:"))
                    .Select(line => line.Replace("CONFIG_VERIFICATION:", "").Trim())
                    .ToList();

                _logger.LogInformation("✅ PowerCLI configuration applied successfully");
                foreach (var verification in verificationLines)
                {
                    _logger.LogDebug("Config verification: {Verification}", verification);
                }
            }
            else
            {
                result.Success = false;
                result.Message = "PowerCLI configuration may have failed";
                result.Warnings.Add("No CONFIG_SUCCESS confirmation found, but continuing");
                
                _logger.LogWarning("PowerCLI configuration completed but without explicit success confirmation");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying PowerCLI configuration in process {ProcessId}", process.ProcessId);
            
            return new ConfigurationResult
            {
                Success = false,
                Message = $"PowerCLI configuration failed: {ex.Message}",
                Errors = { $"Exception: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Validates PowerCLI configuration and gathers module information
    /// </summary>
    private async Task<ConfigurationResult> ValidatePowerCLIConfigurationAsync(
        PowerShellProcessManager.ManagedPowerShellProcess process)
    {
        try
        {
            _logger.LogDebug("Validating PowerCLI configuration in process {ProcessId}...", process.ProcessId);

            var validationScript = @"
                # Validate PowerCLI module and command availability
                $loadedModules = Get-Module -Name VMware* | Select-Object Name, Version
                $availableCommands = Get-Command -Module VMware* | Select-Object Name, Source -First 20
                $config = Get-PowerCLIConfiguration -ErrorAction SilentlyContinue
                
                Write-Output 'VALIDATION_START'
                Write-Output 'LOADED_MODULES:'
                $loadedModules | ForEach-Object { Write-Output ""MODULE:$($_.Name):$($_.Version)"" }
                
                Write-Output 'AVAILABLE_COMMANDS:'
                $availableCommands | ForEach-Object { Write-Output ""COMMAND:$($_.Name):$($_.Source)"" }
                
                Write-Output 'CONFIGURATION:'
                if ($config) {
                    Write-Output ""CONFIG:InvalidCertificateAction:$($config.InvalidCertificateAction)""
                    Write-Output ""CONFIG:DefaultVIServerMode:$($config.DefaultVIServerMode)""
                    Write-Output ""CONFIG:WebOperationTimeoutSeconds:$($config.WebOperationTimeoutSeconds)""
                    Write-Output ""CONFIG:ProxyPolicy:$($config.ProxyPolicy)""
                    Write-Output ""CONFIG:ParticipateInCeip:$($config.ParticipateInCeip)""
                } else {
                    Write-Output 'CONFIG:Not Available'
                }
                
                Write-Output 'VALIDATION_END'
            ";

            var output = await _processManager.ExecuteCommandAsync(process, validationScript, TimeSpan.FromSeconds(15));

            var result = new ConfigurationResult();
            var moduleInfo = new PowerCLIModuleInfo();

            if (output.Contains("VALIDATION_START") && output.Contains("VALIDATION_END"))
            {
                // Parse loaded modules
                var moduleLines = output.Split('\n')
                    .Where(line => line.StartsWith("MODULE:"))
                    .ToList();

                foreach (var moduleLine in moduleLines)
                {
                    var parts = moduleLine.Replace("MODULE:", "").Split(':');
                    if (parts.Length >= 2)
                    {
                        if (string.IsNullOrEmpty(moduleInfo.ModuleName) || parts[0].Contains("PowerCLI"))
                        {
                            moduleInfo.ModuleName = parts[0];
                            moduleInfo.Version = parts[1];
                            moduleInfo.IsLoaded = true;
                        }
                    }
                }

                // Parse available commands
                var commandLines = output.Split('\n')
                    .Where(line => line.StartsWith("COMMAND:"))
                    .Take(20) // Limit to avoid excessive logging
                    .ToList();

                moduleInfo.AvailableCommands = commandLines
                    .Select(line => line.Replace("COMMAND:", "").Split(':')[0])
                    .ToList();

                // Parse configuration
                var configLines = output.Split('\n')
                    .Where(line => line.StartsWith("CONFIG:"))
                    .ToList();

                foreach (var configLine in configLines)
                {
                    var configPart = configLine.Replace("CONFIG:", "");
                    var keyValue = configPart.Split(':');
                    if (keyValue.Length >= 2)
                    {
                        moduleInfo.Configuration[keyValue[0]] = keyValue[1];
                    }
                }

                result.Success = true;
                result.ModuleInfo = moduleInfo;
                result.Message = "PowerCLI validation completed successfully";

                _logger.LogInformation("✅ PowerCLI validation completed - {ModuleName} v{Version}, {CommandCount} commands available",
                    moduleInfo.ModuleName, moduleInfo.Version, moduleInfo.AvailableCommands.Count);
            }
            else
            {
                result.Success = false;
                result.Message = "PowerCLI validation failed - incomplete output";
                result.Warnings.Add("Validation output was incomplete or malformed");
                
                _logger.LogWarning("PowerCLI validation returned incomplete output");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating PowerCLI configuration in process {ProcessId}", process.ProcessId);
            
            return new ConfigurationResult
            {
                Success = false,
                Message = $"PowerCLI validation failed: {ex.Message}",
                Errors = { $"Exception: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Checks if PowerCLI is properly configured in the process
    /// </summary>
    public async Task<bool> IsPowerCLIConfiguredAsync(PowerShellProcessManager.ManagedPowerShellProcess process)
    {
        try
        {
            var checkScript = @"
                $connectCmd = Get-Command Connect-VIServer -ErrorAction SilentlyContinue
                $config = Get-PowerCLIConfiguration -ErrorAction SilentlyContinue
                
                if ($connectCmd -and $config) {
                    Write-Output 'POWERCLI_READY'
                } else {
                    Write-Output 'POWERCLI_NOT_READY'
                }
            ";

            var output = await _processManager.ExecuteCommandAsync(process, checkScript, TimeSpan.FromSeconds(5));
            return output.Contains("POWERCLI_READY");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking PowerCLI configuration status");
            return false;
        }
    }

    /// <summary>
    /// Gets current PowerCLI configuration from the process
    /// </summary>
    public async Task<Dictionary<string, object>?> GetCurrentConfigurationAsync(
        PowerShellProcessManager.ManagedPowerShellProcess process)
    {
        try
        {
            var configScript = @"
                $config = Get-PowerCLIConfiguration -ErrorAction SilentlyContinue
                if ($config) {
                    Write-Output ""InvalidCertificateAction:$($config.InvalidCertificateAction)""
                    Write-Output ""DefaultVIServerMode:$($config.DefaultVIServerMode)""
                    Write-Output ""WebOperationTimeoutSeconds:$($config.WebOperationTimeoutSeconds)""
                    Write-Output ""ProxyPolicy:$($config.ProxyPolicy)""
                    Write-Output ""ParticipateInCeip:$($config.ParticipateInCeip)""
                } else {
                    Write-Output 'CONFIG_NOT_AVAILABLE'
                }
            ";

            var output = await _processManager.ExecuteCommandAsync(process, configScript, TimeSpan.FromSeconds(10));

            if (output.Contains("CONFIG_NOT_AVAILABLE"))
            {
                return null;
            }

            var config = new Dictionary<string, object>();
            var lines = output.Split('\n');

            foreach (var line in lines)
            {
                var parts = line.Trim().Split(':');
                if (parts.Length >= 2)
                {
                    var key = parts[0];
                    var value = string.Join(":", parts.Skip(1));
                    config[key] = value;
                }
            }

            return config.Any() ? config : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting PowerCLI configuration from process {ProcessId}", process.ProcessId);
            return null;
        }
    }
}