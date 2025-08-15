<#
.SYNOPSIS
    Encrypt or rekey multiple VMs with a Native Key Provider using PowerCLI in parallel,
    logging output to .\logs\<ScriptName>_<timestamp>.log

.DESCRIPTION
    - Creates a "logs" folder next to this script if it doesn't exist.
    - Names the logfile: <ScriptName>_yyyy-MM-dd_HH-mm-ss.log
    - All Write-Log calls write to host and append to the logfile.
    - Processes VMs in parallel with a configurable throttle limit.

.PARAMETER vCenterServer
    FQDN or IP of vCenter.

.PARAMETER vmNames
    Array of VM names to process.

.PARAMETER CurrentNKP
    The "old" key provider (Name or KeyProviderId.Id).

.PARAMETER NewNKP
    The "new" key provider (Name or KeyProviderId.Id).

.PARAMETER Credential
    PSCredential for vCenter.

.PARAMETER ThrottleLimit
    Maximum parallel operations (default: 5).

.PARAMETER SkipModuleCheck
    Skip re-importing PowerCLI modules for faster reruns (assumes they're already loaded).

.EXAMPLE
    .\RekeyMultipleVMs.ps1 `
      -vCenterServer vcsa.lab.local `
      -vmNames @("VM1","VM2") `
      -CurrentNKP "OldNKP" `
      -NewNKP "NewNKP" `
      -Credential (Get-Credential)

.EXAMPLE
    # After first run (modules already loaded), skip module import:
    .\RekeyMultipleVMs.ps1 `
      -vCenterServer vcsa.lab.local `
      -vmNames @("VM1","VM2") `
      -CurrentNKP "OldNKP" `
      -NewNKP "NewNKP" `
      -Credential (Get-Credential) `
      -SkipModuleCheck
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]       $vCenterServer,
    [Parameter(Mandatory)][string[]]     $vmNames,
    [Parameter(Mandatory)][string]       $CurrentNKP,
    [Parameter(Mandatory)][string]       $NewNKP,
    [Parameter(Mandatory)][PSCredential] $Credential,
    [Parameter()][int]                   $ThrottleLimit = 5,
    [Parameter()][switch]                $SkipModuleCheck
)

# --- Prepare logfile ---
# Resolve this script's folder & base name
$scriptPath = if ($PSCommandPath) { $PSCommandPath } else { $MyInvocation.MyCommand.Path }
$scriptDir  = Split-Path $scriptPath -Parent
$scriptBase = [IO.Path]::GetFileNameWithoutExtension($scriptPath)

# Create logs folder
$logFolder = Join-Path $scriptDir 'logs'
if (-not (Test-Path $logFolder)) {
    New-Item -Path $logFolder -ItemType Directory -Force | Out-Null
}

# Build logfile name
$timestamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$LogFile   = Join-Path $logFolder "$($scriptBase)_$($timestamp).log"

# Central logging function (writes host + file)
function Write-Log {
    param(
        [Parameter(Mandatory)][string]              $Message,
        [ValidateSet('INFO','WARN','ERROR')][string]$Level  = 'INFO',
        [string]                                    $VMName = ''
    )

    $ts     = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $prefix = if ($VMName) { "[$($VMName)] " } else { "" }
    $line   = "[$($ts)] [$($Level)] $($prefix)$($Message)"

    # Write to console
    switch ($Level) {
        'INFO'  { Write-Host $line -ForegroundColor White }
        'WARN'  { Write-Host $line -ForegroundColor Yellow }
        'ERROR' { Write-Host $line -ForegroundColor Red }
    }

    # Append to logfile
    Add-Content -Path $LogFile -Value $line
}

try {
    # 1. Import PowerCLI modules
    if (-not $SkipModuleCheck) {
        Write-Log "Loading PowerCLI modules..." 'INFO'
        try {
            Import-Module VMware.VimAutomation.Core   -ErrorAction Stop
            Import-Module VMware.VimAutomation.Storage -ErrorAction Stop
            Write-Log "Modules loaded." 'INFO'
        }
        catch {
            Write-Log "Failed to load modules: $($_)" 'ERROR'
            throw
        }
    }

    # Verify Get-KeyProvider is available
    if (-not (Get-Command Get-KeyProvider -ErrorAction SilentlyContinue)) {
        throw "Get-KeyProvider not found. Ensure VMware.VimAutomation.Storage is loaded."
    }

    # 2. Connect to vCenter
    Write-Log "Connecting to vCenter '$($vCenterServer)'…" 'INFO'
    $vc = Connect-VIServer -Server $vCenterServer -Credential $Credential -ErrorAction Stop
    Write-Log "Connected to $($vc.Name)." 'INFO'

    # 3. Retrieve all key providers
    Write-Log "Retrieving key providers…" 'INFO'
    $allProviders = Get-KeyProvider -Server $vc -ErrorAction Stop
    if (-not $allProviders) { throw "No key providers found." }
    Write-Log "Found: $($allProviders.Name -join ', ')." 'INFO'

    # Helper: resolve by name or ID
    function Resolve-Provider {
        param([string]$NameOrId)
        $p = $allProviders |
             Where-Object { $_.Name -eq $NameOrId -or $_.KeyProviderId.Id -eq $NameOrId }
        if (-not $p) {
            throw "Provider '$($NameOrId)' not found."
        }
        return $p
    }

    Write-Log "Validating providers…" 'INFO'
    $oldProv = Resolve-Provider $CurrentNKP
    $newProv = Resolve-Provider $NewNKP
    Write-Log "Old   provider: $($oldProv.Name) [$($oldProv.KeyProviderId.Id)]" 'INFO'
    Write-Log "New   provider: $($newProv.Name) [$($newProv.KeyProviderId.Id)]" 'INFO'

    # 4. Get the target VMs
    Write-Log "Retrieving VMs: $($vmNames -join ', ')" 'INFO'
    $vms = Get-VM -Server $vc -Name $vmNames -ErrorAction SilentlyContinue
    $missing = $vmNames | Where-Object { $_ -notin $vms.Name }
    if ($missing) {
        Write-Log "VMs not found: $($missing -join ', ')" 'WARN'
    }
    if (-not $vms) { throw "No valid VMs to process." }

    # Prepare values for parallel block
    #  - We cannot pass complex objects; pass simple strings/IDs
    $vcConn           = $vc
    $oldProviderId    = $oldProv.KeyProviderId.Id
    $newProviderName  = $newProv.Name
    $logFileForWorker = $LogFile

    # 5. Process in parallel
    $vms |
      ForEach-Object -ThrottleLimit $ThrottleLimit -Parallel {

        # local Write-Log inside runspace
        function Write-Log {
            param(
                [string]$Message,
                [ValidateSet('INFO','WARN','ERROR')][string]$Level = 'INFO',
                [string]$VMName = ''
            )
            $ts     = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
            $prefix= if ($VMName) { "[$($VMName)] " } else { "" }
            $line   = "[$($ts)] [$($Level)] $($prefix)$($Message)"
            switch ($Level) {
                'INFO'  { Write-Host $line -ForegroundColor White }
                'WARN'  { Write-Host $line -ForegroundColor Yellow }
                'ERROR' { Write-Host $line -ForegroundColor Red }
            }
            Add-Content -Path $using:logFileForWorker -Value $line
        }

        try {
            $vmName = $_.Name
            Write-Log "Starting" 'INFO' $($vmName)

            # Ensure storage module (for Get-KeyProvider) is loaded in this runspace
            try {
                Import-Module VMware.VimAutomation.Core   -ErrorAction Stop
                Import-Module VMware.VimAutomation.Storage -ErrorAction Stop
                Write-Log "Modules loaded." 'INFO'
            }
            catch {
                Write-Log "Failed to load modules: $($_)" 'ERROR'
                throw
            }
            # Re-fetch new provider object here
            $newProvObj = Get-KeyProvider -Name $using:newProviderName -Server $using:vcConn
            if (-not $newProvObj) { throw "Cannot retrieve provider $($using:newProviderName)" }

            # Check disk encryption
            $crypto = $_.ExtensionData.CryptoState
            $vtpm   = $_.ExtensionData.Config.Hardware.Device |
                      Where-Object { $_.GetType().Name -eq 'VirtualTPM' }

            if ($crypto -and $crypto.KeyId) {
                $curId = $crypto.KeyId.KeyProviderId.Id
                Write-Log "Disk encrypted by $($curId)" 'INFO' $($vmName)
                if ($curId -ne $using:oldProviderId) {
                    Write-Log "Skipping – unexpected provider ($($curId))" 'WARN' $($vmName)
                    return
                }
                if ($_.PowerState -eq 'PoweredOn') {
                    Write-Log "Must power off for rekey" 'WARN' $($vmName)
                    return
                }
                Set-VM -VM $_ -KeyProvider $newProvObj -Confirm:$false
                Write-Log "Disk rekey initiated" 'INFO' $($vmName)
            }
            elseif ($vtpm -and $vtpm.Backing -and $vtpm.Backing.KeyId) {
                $curId = $vtpm.Backing.KeyId.KeyProviderId.Id
                Write-Log "vTPM encrypted by $($curId)" 'INFO' $($vmName)
                if ($curId -ne $using:oldProviderId) {
                    Write-Log "Skipping – unexpected provider ($($curId))" 'WARN' $($vmName)
                    return
                }
                Set-VM -VM $_ -KeyProvider $newProvObj -Confirm:$false
                Write-Log "vTPM rekey initiated" 'INFO' $($vmName)
            }
            else {
                Write-Log "Not encrypted – applying new encryption" 'INFO' $($vmName)
                Set-VM -VM $_ -KeyProvider $newProvObj -Confirm:$false
                Write-Log "Encryption applied" 'INFO' $($vmName)
            }
        }
        catch {
            Write-Log "ERROR: $($_.Exception.Message)" 'ERROR' $($vmName)
        }
      }

    Write-Log "All tasks queued." 'INFO'
}
catch {
    Write-Log "SCRIPT FAILURE: $($_)" 'ERROR'
    exit 1
}
finally {
    if ($vc -and $vc.IsConnected) {
        Disconnect-VIServer -Server $vc -Confirm:$false -ErrorAction SilentlyContinue
        Write-Log "Disconnected from vCenter" 'INFO'
    }
}
