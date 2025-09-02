<#
.SYNOPSIS
    Copies the VM folder structure (blue folders) from a specific Datacenter in a
    source vCenter to a specific Datacenter in a target vCenter.
.DESCRIPTION
    This script connects to two vCenter Servers, identifies the VM folder hierarchy
    within a specified Datacenter on the source, and replicates that structure in a
    specified Datacenter on the target vCenter.
    Requires PowerCLI module installed and Write-ScriptLog.ps1 in the same directory.
.NOTES
    Author: CodingFleet Code Generator
    Version: 2.0 (Integrated with standard logging)
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$SourceVCenter,

    [Parameter(Mandatory=$true)]
    [string]$TargetVCenter,

    [Parameter(Mandatory=$true)]
    [string]$SourceDatacenterName,

    [Parameter(Mandatory=$true)]
    [string]$TargetDatacenterName,

    [Parameter(Mandatory=$false)]
    [string]$SourceUser,

    [Parameter(Mandatory=$false)]
    [securestring]$SourcePassword,

    [Parameter(Mandatory=$false)]
    [string]$TargetUser,

    [Parameter(Mandatory=$false)]
    [securestring]$TargetPassword,
    
    [Parameter(Mandatory=$false)]
    [string]$LogPath,

    [Parameter(Mandatory=$false)]
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Functions ---
$global:foldersCreated = 0
$global:foldersSkipped = 0
$global:foldersFailed = 0

# Recursive function to copy folder structure
function Copy-FolderStructure {
    param(
        [Parameter(Mandatory=$true)]
        $SourceParentFolder,

        [Parameter(Mandatory=$true)]
        $TargetParentFolder,

        [Parameter(Mandatory=$true)]
        $SourceServer,

        [Parameter(Mandatory=$true)]
        $TargetServer
    )

    Write-LogDebug "Getting child folders of '$($SourceParentFolder.Name)' on source $($SourceServer.Name)" -Category "Discovery"
    $sourceChildFolders = Get-Folder -Location $SourceParentFolder -Type VM -Server $SourceServer -NoRecursion -ErrorAction SilentlyContinue

    if ($null -eq $sourceChildFolders) {
        Write-LogDebug "No child VM folders found under '$($SourceParentFolder.Name)'." -Category "Discovery"
        return
    }

    foreach ($sourceFolder in $sourceChildFolders) {
        Write-LogInfo "Processing Source Folder: '$($sourceFolder.Name)' under '$($SourceParentFolder.Name)'" -Category "Processing"

        Write-LogDebug "Checking for existing folder '$($sourceFolder.Name)' under '$($TargetParentFolder.Name)' on target $($TargetServer.Name)" -Category "Verification"
        $existingTargetFolder = Get-Folder -Location $TargetParentFolder -Name $sourceFolder.Name -Type VM -Server $TargetServer -NoRecursion -ErrorAction SilentlyContinue

        if ($existingTargetFolder) {
            Write-LogInfo "  Folder '$($sourceFolder.Name)' already exists in Target under '$($TargetParentFolder.Name)'. Using existing." -Category "Skipped"
            $global:foldersSkipped++
            $currentTargetFolder = $existingTargetFolder
        } else {
            Write-LogInfo "  Creating Folder '$($sourceFolder.Name)' in Target under '$($TargetParentFolder.Name)'..." -Category "Creation"
            try {
                $currentTargetFolder = New-Folder -Location $TargetParentFolder -Name $sourceFolder.Name -Server $TargetServer -ErrorAction Stop
                Write-LogSuccess "  Successfully created folder '$($currentTargetFolder.Name)'." -Category "Creation"
                $global:foldersCreated++
            } catch {
                Write-LogError "  Failed to create folder '$($sourceFolder.Name)' in Target under '$($TargetParentFolder.Name)': $($_.Exception.Message)" -Category "Creation"
                $global:foldersFailed++
                continue
            }
        }

        if ($currentTargetFolder) {
            Copy-FolderStructure -SourceParentFolder $sourceFolder -TargetParentFolder $currentTargetFolder -SourceServer $SourceServer -TargetServer $TargetServer
        }
    }
}

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Copy-VMFolderStructure" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$sourceVIServer = $null
$targetVIServer = $null

try {
    # Set PowerCLI configuration
    Write-LogInfo "Setting PowerCLI invalid certificate action to 'Ignore'." -Category "Configuration"
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null

    # Prepare connection parameters
    $sourceConnParams = @{ Server = $SourceVCenter }
    if ($PSBoundParameters.ContainsKey('SourceUser')) { $sourceConnParams.Add('User', $SourceUser) }
    if ($PSBoundParameters.ContainsKey('SourcePassword')) { $sourceConnParams.Add('Password', $SourcePassword) }
    
    $targetConnParams = @{ Server = $TargetVCenter }
    if ($PSBoundParameters.ContainsKey('TargetUser')) { $targetConnParams.Add('User', $TargetUser) }
    if ($PSBoundParameters.ContainsKey('TargetPassword')) { $targetConnParams.Add('Password', $TargetPassword) }
    
    # Connect to Source vCenter
    Write-LogInfo "Connecting to Source vCenter: $SourceVCenter..." -Category "Connection"
    $sourceVIServer = Connect-VIServer @sourceConnParams -ErrorAction Stop
    Write-LogSuccess "Connected to Source: $($sourceVIServer.Name) ($($sourceVIServer.Version))" -Category "Connection"

    # Connect to Target vCenter
    Write-LogInfo "Connecting to Target vCenter: $TargetVCenter..." -Category "Connection"
    $targetVIServer = Connect-VIServer @targetConnParams -ErrorAction Stop
    Write-LogSuccess "Connected to Target: $($targetVIServer.Name) ($($targetVIServer.Version))" -Category "Connection"

    # Get the specific source datacenter
    Write-LogInfo "Retrieving Source Datacenter '$SourceDatacenterName'..." -Category "Discovery"
    $sourceDc = Get-Datacenter -Name $SourceDatacenterName -Server $sourceVIServer -ErrorAction Stop
    Write-LogSuccess "Found Source Datacenter: '$($sourceDc.Name)'" -Category "Discovery"

    # Get the specific target datacenter
    Write-LogInfo "Retrieving Target Datacenter '$TargetDatacenterName'..." -Category "Discovery"
    $targetDc = Get-Datacenter -Name $TargetDatacenterName -Server $targetVIServer -ErrorAction Stop
    Write-LogSuccess "Found Target Datacenter: '$($targetDc.Name)'" -Category "Discovery"

    # Get the root VM folder for the source datacenter
    Write-LogDebug "Getting root VM folder for Source Datacenter '$($sourceDc.Name)'" -Category "Discovery"
    $sourceRootVmFolder = Get-Folder -Location $sourceDc -Type VM -Server $sourceVIServer | Where-Object { !$_.ParentId.Contains("Folder") }
    if (-not $sourceRootVmFolder) { throw "Root VM folder not found in Source Datacenter '$($sourceDc.Name)'." }

    # Get the root VM folder for the target datacenter
    Write-LogDebug "Getting root VM folder for Target Datacenter '$($targetDc.Name)'" -Category "Discovery"
    $targetRootVmFolder = Get-Folder -Location $targetDc -Type VM -Server $targetVIServer | Where-Object { !$_.ParentId.Contains("Folder") }
    if (-not $targetRootVmFolder) { throw "Root VM folder not found in Target Datacenter '$($targetDc.Name)'." }

    Write-LogInfo "Starting folder structure copy from '$($sourceDc.Name)' to '$($targetDc.Name)'..." -Category "MainProcess"
    Copy-FolderStructure -SourceParentFolder $sourceRootVmFolder -TargetParentFolder $targetRootVmFolder -SourceServer $sourceVIServer -TargetServer $targetVIServer
    Write-LogSuccess "Finished folder structure copy for Datacenter '$($sourceDc.Name)'." -Category "MainProcess"
    
    $scriptSuccess = $true
    $finalSummary = "Successfully copied folder structure. Created: $global:foldersCreated, Skipped: $global:foldersSkipped, Failed: $global:foldersFailed."

} catch {
    $scriptSuccess = $false
    $finalSummary = "Script failed with error: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    throw $_
} finally {
    if ($sourceVIServer) {
        Write-LogInfo "Disconnecting from Source vCenter: $($sourceVIServer.Name)..." -Category "Cleanup"
        # DISCONNECT REMOVED - Using persistent connections managed by application
    }
    if ($targetVIServer) {
        Write-LogInfo "Disconnecting from Target vCenter: $($targetVIServer.Name)..." -Category "Cleanup"
        # DISCONNECT REMOVED - Using persistent connections managed by application
    }
    
    $finalStats = @{
        "SourceVCenter" = $SourceVCenter
        "TargetVCenter" = $TargetVCenter
        "SourceDatacenter" = $SourceDatacenterName
        "TargetDatacenter" = $TargetDatacenterName
        "FoldersCreated" = $global:foldersCreated
        "FoldersSkippedExisting" = $global:foldersSkipped
        "FoldersFailedToCreate" = $global:foldersFailed
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
}