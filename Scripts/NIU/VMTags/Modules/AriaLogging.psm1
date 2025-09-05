<#
.SYNOPSIS
    Aria Operations Logging Module
.DESCRIPTION
    Provides standardized logging functions for Aria Operations integration
#>

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

function Write-AriaMetric {
    <#
    .SYNOPSIS
        Writes performance metrics for Aria Operations
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$MetricName,
        
        [Parameter(Mandatory = $true)]
        [double]$Value,
        
        [Parameter(Mandatory = $false)]
        [string]$Unit = "count",
        
        [Parameter(Mandatory = $false)]
        [string]$Description
    )
    
    $metric = @{
        Timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ss.fffZ"
        MetricName = $MetricName
        Value = $Value
        Unit = $Unit
        Description = $Description
        Source = "VMTags-Automation"
        Machine = $env:COMPUTERNAME
    }
    
    Write-Host "ARIA_METRIC: $($metric | ConvertTo-Json -Compress)"
}

function Write-AriaAlert {
    <#
    .SYNOPSIS
        Writes alerts for Aria Operations monitoring
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$AlertName,
        
        [Parameter(Mandatory = $true)]
        [ValidateSet('INFO', 'WARNING', 'CRITICAL')]
        [string]$Severity,
        
        [Parameter(Mandatory = $true)]
        [string]$Message,
        
        [Parameter(Mandatory = $false)]
        [hashtable]$Properties = @{}
    )
    
    $alert = @{
        Timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ss.fffZ"
        AlertName = $AlertName
        Severity = $Severity
        Message = $Message
        Source = "VMTags-Automation"
        Machine = $env:COMPUTERNAME
        Properties = $Properties
    }
    
    Write-Host "ARIA_ALERT: $($alert | ConvertTo-Json -Compress)"
}

Export-ModuleMember -Function @('Write-AriaLog', 'Write-AriaMetric', 'Write-AriaAlert')