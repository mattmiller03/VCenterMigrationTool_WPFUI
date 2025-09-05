<#
.SYNOPSIS
    Configuration Manager for VM Tags and Permissions automation
.DESCRIPTION
    Provides centralized configuration management for the VM Tags and Permissions system
#>

#region Public Functions

function Get-VMTagsConfig {
    <#
    .SYNOPSIS
        Loads and returns the VM Tags configuration
    .PARAMETER Environment
        Target environment (DEV, PROD, KLEB, OT)
    .PARAMETER ConfigPath
        Path to the configuration file or directory
    .PARAMETER IncludeSecrets
        Include sensitive configuration values
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('DEV', 'PROD', 'KLEB', 'OT')]
        [string]$Environment,
        
        [Parameter(Mandatory = $false)]
        [string]$ConfigPath,
        
        [Parameter(Mandatory = $false)]
        [switch]$IncludeSecrets
    )
    
    try {
        # Determine config file path
        if (-not $ConfigPath) {
            $scriptRoot = Split-Path -Parent $PSCommandPath
            $ConfigPath = Join-Path $scriptRoot "VMTagsConfig.psd1"
        }
        elseif (Test-Path $ConfigPath -PathType Container) {
            # If ConfigPath is a directory, look for the config file in it
            $ConfigPath = Join-Path $ConfigPath "VMTagsConfig.psd1"
        }
        
        if (-not (Test-Path $ConfigPath)) {
            throw "Configuration file not found: $ConfigPath"
        }
        
        Write-Verbose "Loading configuration from: $ConfigPath"
        
        # Load base configuration
        $baseConfig = Import-PowerShellDataFile -Path $ConfigPath
        Write-Verbose "Base config sections: $($baseConfig.Keys -join ', ')"
        
        # Get environment-specific configuration
        if (-not $baseConfig.Environments.$Environment) {
            throw "Environment '$Environment' not found in configuration"
        }
        
        $envConfig = $baseConfig.Environments.$Environment
        Write-Verbose "Environment config sections: $($envConfig.Keys -join ', ')"
        
        # Start with a fresh hashtable
        $config = @{}
        
        # Copy ALL base configuration sections EXCEPT Environments
        foreach ($key in $baseConfig.Keys) {
            if ($key -ne 'Environments') {
                $config[$key] = $baseConfig[$key]
                Write-Verbose "Copied base section: $key"
            }
        }
        
        # Add environment-specific settings (these will override base settings if they exist)
        $config.CurrentEnvironment = $Environment
        $config.vCenterServer = $envConfig.vCenterServer
        $config.SSODomain = $envConfig.SSODomain
        $config.DefaultCredentialUser = $envConfig.DefaultCredentialUser
        $config.TagCategories = $envConfig.TagCategories
        $config.DataPaths = $envConfig.DataPaths
        $config.EnvironmentSettings = $envConfig.Settings
        
        # Add runtime information
        $config.Runtime = @{
            LoadedAt = Get-Date
            LoadedBy = $env:USERNAME
            LoadedFrom = $ConfigPath
            MachineName = $env:COMPUTERNAME
            PowerShellVersion = $PSVersionTable.PSVersion.ToString()
            Environment = $Environment
            ConfigurationVersion = $baseConfig.Application.Version
        }
        
        Write-Verbose "Final config sections: $($config.Keys -join ', ')"
        
        # Verify Security section was copied
        if ($config.ContainsKey('Security')) {
            Write-Verbose "✓ Security section successfully included"
        } else {
            Write-Warning "✗ Security section missing from final config!"
        }
        
        # Resolve paths with environment variables
        $config = Resolve-ConfigurationPaths -Config $config
        
        # Remove sensitive information if not requested
        if (-not $IncludeSecrets) {
            # Don't remove the entire Security section, just sensitive parts if needed
            # $config = Remove-SensitiveConfigData -Config $config
        }
        
        Write-Verbose "Configuration loaded successfully for environment: $Environment"
        return $config
    }
    catch {
        Write-Error "Failed to load configuration: $($_.Exception.Message)"
        throw
    }
}

function Test-VMTagsConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Config
    )
    
    $validationResults = @{
        IsValid = $true
        Issues = @()
        Warnings = @()
    }
    
    try {
        Write-Verbose "=== Configuration Validation Debug ==="
        Write-Verbose "Config keys: $($Config.Keys -join ', ')"
        
        # Test each required section individually with debug output
        $requiredSections = @{
            'Application' = 'Application metadata'
            'DefaultPaths' = 'Default file paths'
            'CSVValidation' = 'CSV validation rules'
            'PowerShell7' = 'PowerShell 7 settings'
            'Logging' = 'Logging configuration'
            'Security' = 'Security settings'
        }
        
        foreach ($section in $requiredSections.GetEnumerator()) {
            Write-Verbose "Checking section: $($section.Key)"
            
            try {
                if (-not $Config.ContainsKey($section.Key)) {
                    $message = "Missing required configuration section: $($section.Key) ($($section.Value))"
                    Write-Verbose "FAILED: $message"
                    $validationResults.Issues += $message
                    $validationResults.IsValid = $false
                }
                elseif ($null -eq $Config[$section.Key]) {
                    $message = "Configuration section is null: $($section.Key)"
                    Write-Verbose "FAILED: $message"
                    $validationResults.Issues += $message
                    $validationResults.IsValid = $false
                }
                else {
                    Write-Verbose "SUCCESS: Found section: $($section.Key)"
                }
            }
            catch {
                $message = "Error checking section $($section.Key): $($_.Exception.Message)"
                Write-Verbose "ERROR: $message"
                $validationResults.Issues += $message
                $validationResults.IsValid = $false
            }
        }
        
        Write-Verbose "=== Validation Results ==="
        Write-Verbose "IsValid: $($validationResults.IsValid)"
        Write-Verbose "Issues: $($validationResults.Issues.Count)"
        foreach ($issue in $validationResults.Issues) {
            Write-Verbose "  - $issue"
        }
        
        return $validationResults
    }
    catch {
        $validationResults.Issues += "Configuration validation failed: $($_.Exception.Message)"
        $validationResults.IsValid = $false
        return $validationResults
    }
}

function New-VMTagsDirectories {
    <#
    .SYNOPSIS
        Creates required directories based on configuration
    .PARAMETER Config
        Configuration object containing directory paths
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Config
    )
    
    try {
        Write-Verbose "Creating required directories..."
        
        $directoriesToCreate = @()
        
        # Add default paths
        if ($Config.DefaultPaths) {
            $Config.DefaultPaths.GetEnumerator() | Where-Object { 
                $_.Key -like "*Directory*" -and $_.Value 
            } | ForEach-Object {
                $directoriesToCreate += $_.Value
            }
        }
        
        # Add environment-specific paths
        if ($Config.DataPaths) {
            $Config.DataPaths.GetEnumerator() | Where-Object { 
                $_.Key -like "*Directory*" -and $_.Value 
            } | ForEach-Object {
                $directoriesToCreate += $_.Value
            }
            
            # Add parent directories of file paths
            $Config.DataPaths.GetEnumerator() | Where-Object { 
                $_.Key -notlike "*Directory*" -and $_.Value 
            } | ForEach-Object {
                $parentDir = Split-Path $_.Value -Parent
                if ($parentDir) {
                    $directoriesToCreate += $parentDir
                }
            }
        }
        
        # Remove duplicates and create directories
        $directoriesToCreate = $directoriesToCreate | Sort-Object -Unique
        
        foreach ($directory in $directoriesToCreate) {
            if (-not (Test-Path $directory)) {
                Write-Verbose "Creating directory: $directory"
                New-Item -Path $directory -ItemType Directory -Force | Out-Null
                
                # Set secure permissions if required
                if ($Config.Security.SecureFilePermissions) {
                    Set-SecureDirectoryPermissions -Path $directory -Config $Config
                }
            }
        }
        
        Write-Verbose "Directory creation completed"
        return $true
    }
    catch {
        Write-Error "Failed to create directories: $($_.Exception.Message)"
        return $false
    }
}

function Get-VMTagsExecutionParameters {
    <#
    .SYNOPSIS
        Generates execution parameters for the main script based on configuration
    .PARAMETER Config
        Configuration object
    .PARAMETER AdditionalParameters
        Additional parameters to include
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Config,
        
        [Parameter(Mandatory = $false)]
        [hashtable]$AdditionalParameters = @{}
    )
    
    try {
        Write-Verbose "Generating execution parameters..."
        
        $parameters = @{
            vCenterServer = $Config.vCenterServer
            Environment = $Config.CurrentEnvironment
        }
        
        # Add data paths
        if ($Config.DataPaths.AppPermissionsCSV) {
            $parameters.AppPermissionsCsvPath = $Config.DataPaths.AppPermissionsCSV
        }
        
        if ($Config.DataPaths.OSMappingCSV) {
            $parameters.OsMappingCsvPath = $Config.DataPaths.OSMappingCSV
        }
        
        # Add environment-specific settings
        if ($Config.EnvironmentSettings) {
            if ($Config.EnvironmentSettings.EnableDebugLogging) {
                $parameters.EnableScriptDebug = $true
            }
        }
        
        # Add PowerShell 7 arguments
        $ps7Arguments = @()
        $ps7Arguments += $Config.PowerShell7.StandardArguments
        
        if ($Config.EnvironmentSettings.EnableDebugLogging) {
            $ps7Arguments += $Config.PowerShell7.DebugArguments
        }
        
        $parameters.PowerShell7Arguments = $ps7Arguments
        
        # Add timeout settings
        if ($Config.PowerShell7.TimeoutMinutes) {
            $parameters.TimeoutMinutes = $Config.PowerShell7.TimeoutMinutes
        }
        
        # Add working directory
        if ($Config.PowerShell7.WorkingDirectory) {
            $parameters.WorkingDirectory = $Config.PowerShell7.WorkingDirectory
        }
        
        # Merge additional parameters
        foreach ($param in $AdditionalParameters.GetEnumerator()) {
            $parameters[$param.Key] = $param.Value
        }
        
        Write-Verbose "Generated $($parameters.Count) execution parameters"
        return $parameters
    }
    catch {
        Write-Error "Failed to generate execution parameters: $($_.Exception.Message)"
        throw
    }
}

function Export-VMTagsConfiguration {
    <#
    .SYNOPSIS
        Exports the current configuration to a file
    .PARAMETER Config
        Configuration object to export
    .PARAMETER FilePath
        Path to export the configuration to
    .PARAMETER Format
        Export format (JSON, XML, PSD1)
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Config,
        
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        
        [Parameter(Mandatory = $false)]
        [ValidateSet('JSON', 'XML', 'PSD1')]
        [string]$Format = 'JSON'
    )
    
    try {
        Write-Verbose "Exporting configuration to: $FilePath (Format: $Format)"
        
        # Ensure directory exists
        $directory = Split-Path $FilePath -Parent
        if (-not (Test-Path $directory)) {
            New-Item -Path $directory -ItemType Directory -Force | Out-Null
        }
        
        # Remove sensitive data before export
        $exportConfig = Remove-SensitiveConfigData -Config $Config
        
        switch ($Format) {
            'JSON' {
                $exportConfig | ConvertTo-Json -Depth 10 | Out-File -FilePath $FilePath -Encoding UTF8
            }
            'XML' {
                $exportConfig | Export-Clixml -Path $FilePath
            }
            'PSD1' {
                # Convert hashtable to PSD1 format (simplified)
                $psd1Content = ConvertTo-PSD1String -Hashtable $exportConfig
                $psd1Content | Out-File -FilePath $FilePath -Encoding UTF8
            }
        }
        
        Write-Verbose "Configuration exported successfully"
        return $true
    }
    catch {
        Write-Error "Failed to export configuration: $($_.Exception.Message)"
        return $false
    }
}

function Debug-ConfigValidation {
    param([hashtable]$Config)
    
    Write-Host "`n=== Configuration Validation Debug ===" -ForegroundColor Cyan
    Write-Host "Config object type: $($Config.GetType().Name)" -ForegroundColor White
    Write-Host "Config keys found: $($Config.Keys -join ', ')" -ForegroundColor White
    
    $requiredSections = @('Application', 'DefaultPaths', 'CSVValidation', 'PowerShell7', 'Logging', 'Security')
    
    foreach ($section in $requiredSections) {
        $exists = $Config.ContainsKey($section)
        $color = if ($exists) { "Green" } else { "Red" }
        $status = if ($exists) { "✓" } else { "✗" }
        Write-Host "$status $section : $exists" -ForegroundColor $color
        
        if ($exists -and $Config.$section) {
            Write-Host "    Type: $($Config.$section.GetType().Name)" -ForegroundColor Gray
            if ($Config.$section -is [hashtable]) {
                Write-Host "    Keys: $($Config.$section.Keys -join ', ')" -ForegroundColor Gray
            }
        }
    }
    Write-Host "=== End Debug ===" -ForegroundColor Cyan
}

# Export the debug function
Export-ModuleMember -Function @('Debug-ConfigValidation')
#endregion

#region Private Functions

function Test-ConfigurationPaths {
    [CmdletBinding()]
    param([hashtable]$Config)
    
    $result = @{
        Issues = @()
        Warnings = @()
    }
    
    $pathsToCheck = @{
        'PowerShell 7' = $Config.DefaultPaths.PowerShell7Path
        'Main Script' = $Config.DefaultPaths.MainScriptPath
        'Config Directory' = $Config.DefaultPaths.ConfigDirectory
    }
    
    foreach ($pathCheck in $pathsToCheck.GetEnumerator()) {
        if ($pathCheck.Value) {
            if (-not (Test-Path $pathCheck.Value)) {
                if ($pathCheck.Key -in @('PowerShell 7', 'Main Script')) {
                    $result.Issues += "$($pathCheck.Key) path not found: $($pathCheck.Value)"
                } else {
                    $result.Warnings += "$($pathCheck.Key) path not found: $($pathCheck.Value)"
                }
            }
        }
    }
    
    return $result
}

function Resolve-ConfigurationPaths {
    [CmdletBinding()]
    param([hashtable]$Config)
    
    # Expand environment variables in paths
    $pathProperties = @('DefaultPaths', 'DataPaths')
    
    foreach ($pathProperty in $pathProperties) {
        if ($Config.$pathProperty) {
            $resolvedPaths = @{}
            foreach ($path in $Config.$pathProperty.GetEnumerator()) {
                $resolvedPaths[$path.Key] = [Environment]::ExpandEnvironmentVariables($path.Value)
            }
            $Config.$pathProperty = $resolvedPaths
        }
    }
    
    return $Config
}

function Remove-SensitiveConfigData {
    [CmdletBinding()]
    param([hashtable]$Config)
    
    $cleanConfig = $Config.Clone()
    
    # Remove sensitive sections
    $sensitiveKeys = @('Security', 'Notifications')
    foreach ($key in $sensitiveKeys) {
        if ($cleanConfig.ContainsKey($key)) {
            $cleanConfig.Remove($key)
        }
    }
    
    return $cleanConfig
}

function Set-SecureDirectoryPermissions {
    [CmdletBinding()]
    param(
        [string]$Path,
        [hashtable]$Config
    )
    
    try {
        if (-not $Config.Security.SecureFilePermissions) {
            return
        }
        
        Write-Verbose "Setting secure permissions on directory: $Path"
        
        $acl = Get-Acl $Path
        $acl.SetAccessRuleProtection($true, $false)  # Remove inherited permissions
        
        # Add permissions for allowed users/groups
        foreach ($allowedUser in $Config.Security.AllowedUsers) {
            $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                $allowedUser,
                "FullControl",
                "ContainerInherit,ObjectInherit",
                "None",
                "Allow"
            )
            $acl.SetAccessRule($accessRule)
        }
        
        Set-Acl -Path $Path -AclObject $acl
        Write-Verbose "Secure permissions applied to: $Path"
    }
    catch {
        Write-Warning "Failed to set secure permissions on $Path : $($_.Exception.Message)"
    }
}

function ConvertTo-PSD1String {
    [CmdletBinding()]
    param([hashtable]$Hashtable, [int]$Depth = 0)
    
    $indent = "    " * $Depth
    $result = "@{`n"
    
    foreach ($item in $Hashtable.GetEnumerator()) {
        $key = $item.Key
        $value = $item.Value
        
        $result += "$indent    $key = "
        
        if ($value -is [hashtable]) {
            $result += (ConvertTo-PSD1String -Hashtable $value -Depth ($Depth + 1))
        }
        elseif ($value -is [array]) {
            $result += "@("
            $arrayItems = @()
            foreach ($arrayItem in $value) {
                if ($arrayItem -is [string]) {
                    $arrayItems += "'$($arrayItem.Replace("'", "''"))'"
                }
                else {
                    $arrayItems += $arrayItem.ToString()
                }
            }
            $result += $arrayItems -join ", "
            $result += ")"
        }
        elseif ($value -is [string]) {
            $result += "'$($value.Replace("'", "''"))'"
        }
        elseif ($value -is [bool]) {
            $result += "`$$value"
        }
        else {
            $result += $value.ToString()
        }
        
        $result += "`n"
    }
    
    $result += "$indent}"
    return $result
}
#region Aria Operations Integration
function Write-AriaLog {
    <#
    .SYNOPSIS
        Writes log entries in Aria Operations compatible format
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        
        [Parameter(Mandatory = $false)]
        [ValidateSet('INFO', 'WARNING', 'ERROR', 'SUCCESS', 'DEBUG')]
        [string]$Level = "INFO",
        
        [Parameter(Mandatory = $false)]
        [string]$Category = "VMware-Automation",
        
        [Parameter(Mandatory = $false)]
        [string]$Environment = $env:VMTAGS_ENVIRONMENT,
        
        [Parameter(Mandatory = $false)]
        [string]$LogFile
    )
    
    $ariaLogEntry = @{
        Timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ss.fffZ"
        Level = $Level
        Source = $Category
        Environment = $Environment
        Message = $Message
        Machine = $env:COMPUTERNAME
        User = $env:USERNAME
        ProcessId = $PID
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    }
    
    # Console output for Aria Operations
    $jsonLog = $ariaLogEntry | ConvertTo-Json -Compress
    Write-Host "ARIA_LOG: $jsonLog"
    
    # File output if specified
    if ($LogFile) {
        try {
            $logLine = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [$Level] [$Category] $Message"
            Add-Content -Path $LogFile -Value $logLine -ErrorAction SilentlyContinue
        }
        catch {
            # Ignore file logging errors
        }
    }
}

function Get-AriaConfig {
    return @{
        LogFormat = "JSON"
        MetricsEnabled = $true
        AlertsEnabled = $true
        OutputPrefix = "ARIA_"
    }
}
#endregion

# Update the Export-ModuleMember line to include the new functions
Export-ModuleMember -Function @(
    'Get-VMTagsConfig',
    'Test-VMTagsConfig', 
    'New-VMTagsDirectories',
    'Get-VMTagsExecutionParameters',
    'Write-AriaLog',
    'Get-AriaConfig'
)