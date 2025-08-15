<#
.SYNOPSIS
    Copies the VM folder structure (blue folders) from a specific Datacenter in a
    source vCenter to a specific Datacenter in a target vCenter.
.DESCRIPTION
    This script connects to two vCenter Servers, identifies the VM folder hierarchy
    within a specified Datacenter on the source, and replicates that structure in a
    specified Datacenter on the target vCenter.
    Requires PowerCLI module installed.
.PARAMETER SourceVCenter
    The FQDN or IP address of the source vCenter Server.
.PARAMETER TargetVCenter
    The FQDN or IP address of the target vCenter Server.
.PARAMETER SourceDatacenterName
    The name of the Datacenter on the Source vCenter whose folder structure should be copied.
.PARAMETER TargetDatacenterName
    The name of the Datacenter on the Target vCenter where the folder structure should be created.
.PARAMETER SourceUser
    Optional: Username for the source vCenter. If not provided, uses current credentials or prompts.
.PARAMETER SourcePassword
    Optional: Password for the source vCenter. If not provided, uses current credentials or prompts.
.PARAMETER TargetUser
    Optional: Username for the target vCenter. If not provided, uses current credentials or prompts.
.PARAMETER TargetPassword
    Optional: Password for the target vCenter. If not provided, uses current credentials or prompts.
.EXAMPLE
    .\Copy-VMFolderStructure-SingleDC.ps1 -SourceVCenter source-vcenter.domain.local -TargetVCenter target-vcenter.domain.local -SourceDatacenterName "SourceDC_01" -TargetDatacenterName "TargetDC_A"

.EXAMPLE
    .\Copy-VMFolderStructure-SingleDC.ps1 -SourceVCenter 192.168.1.10 -TargetVCenter 192.168.2.20 -SourceDatacenterName "LabDC" -TargetDatacenterName "LabDC" -SourceUser admin@vsphere.local -TargetUser administrator@vsphere.local
    # You will be prompted for passwords securely.

.NOTES
    Author: CodingFleet Code Generator
    Version: 1.1
    Requires: VMware.PowerCLI module v13.0 or higher.
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
    [securestring]$TargetPassword
)

# --- Configuration ---
# Set PowerCLI configuration to ignore certificate warnings if using self-signed certs
# Use with caution in production environments. Consider proper certificate handling.
Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false | Out-Null

# --- Functions ---

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

    # Get child VM folders directly under the source parent folder
    # Use -NoRecursion to only get immediate children
    Write-Verbose "Getting child folders of '$($SourceParentFolder.Name)' on source $($SourceServer.Name)"
    $sourceChildFolders = Get-Folder -Location $SourceParentFolder -Type VM -Server $SourceServer -NoRecursion -ErrorAction SilentlyContinue

    if ($null -eq $sourceChildFolders) {
        Write-Verbose "No child VM folders found under '$($SourceParentFolder.Name)' or error occurred."
        return # No children or error, stop recursion for this branch
    }

    foreach ($sourceFolder in $sourceChildFolders) {
        Write-Host "Processing Source Folder: '$($sourceFolder.Name)' under '$($SourceParentFolder.Name)'" -ForegroundColor Cyan

        # Check if the folder already exists in the target location
        Write-Verbose "Checking for existing folder '$($sourceFolder.Name)' under '$($TargetParentFolder.Name)' on target $($TargetServer.Name)"
        $existingTargetFolder = Get-Folder -Location $TargetParentFolder -Name $sourceFolder.Name -Type VM -Server $TargetServer -NoRecursion -ErrorAction SilentlyContinue

        if ($existingTargetFolder) {
            Write-Host "  Folder '$($sourceFolder.Name)' already exists in Target under '$($TargetParentFolder.Name)'. Using existing." -ForegroundColor Green
            $currentTargetFolder = $existingTargetFolder
        } else {
            Write-Host "  Creating Folder '$($sourceFolder.Name)' in Target under '$($TargetParentFolder.Name)'..." -ForegroundColor Yellow
            try {
                # Create the folder in the target vCenter under the target parent
                $currentTargetFolder = New-Folder -Location $TargetParentFolder -Name $sourceFolder.Name -Server $TargetServer -ErrorAction Stop
                Write-Host "  Successfully created folder '$($currentTargetFolder.Name)'." -ForegroundColor Green
            } catch {
                Write-Error "  Failed to create folder '$($sourceFolder.Name)' in Target under '$($TargetParentFolder.Name)': $($_.Exception.Message)"
                # Decide if you want to stop the script or just skip this folder and its children
                # For now, we skip this folder and continue with the next sibling
                continue
            }
        }

        # Recurse into the child folder (if it exists or was created successfully)
        if ($currentTargetFolder) {
            Copy-FolderStructure -SourceParentFolder $sourceFolder -TargetParentFolder $currentTargetFolder -SourceServer $SourceServer -TargetServer $TargetServer
        }
    }
}

# --- Main Script Logic ---

$sourceVIServer = $null
$targetVIServer = $null

try {
    # Prepare connection parameters
    $sourceConnParams = @{ Server = $SourceVCenter }
    if ($PSBoundParameters.ContainsKey('SourceUser')) { $sourceConnParams.Add('User', $SourceUser) }
    if ($PSBoundParameters.ContainsKey('SourcePassword')) { $sourceConnParams.Add('Password', $SourcePassword) }
    else {
         # Prompt for credentials if User was provided but Password wasn't
         if ($PSBoundParameters.ContainsKey('SourceUser') -and !$PSBoundParameters.ContainsKey('SourcePassword')) {
             $sourceCred = Get-Credential -UserName $SourceUser -Message "Enter password for $SourceUser on $SourceVCenter"
             if ($sourceCred) { $sourceConnParams.Add('Credential', $sourceCred) } else { throw "Credential input cancelled."}
             # Remove user/password keys if credential object is used
             $sourceConnParams.Remove('User')
             $sourceConnParams.Remove('Password')
         }
    }


    $targetConnParams = @{ Server = $TargetVCenter }
    if ($PSBoundParameters.ContainsKey('TargetUser')) { $targetConnParams.Add('User', $TargetUser) }
    if ($PSBoundParameters.ContainsKey('TargetPassword')) { $targetConnParams.Add('Password', $TargetPassword) }
     else {
         # Prompt for credentials if User was provided but Password wasn't
         if ($PSBoundParameters.ContainsKey('TargetUser') -and !$PSBoundParameters.ContainsKey('TargetPassword')) {
             $targetCred = Get-Credential -UserName $TargetUser -Message "Enter password for $TargetUser on $TargetVCenter"
             if ($targetCred) { $targetConnParams.Add('Credential', $targetCred) } else { throw "Credential input cancelled."}
             # Remove user/password keys if credential object is used
             $targetConnParams.Remove('User')
             $targetConnParams.Remove('Password')
         }
    }

    # Connect to Source vCenter
    Write-Host "Connecting to Source vCenter: $SourceVCenter..."
    $sourceVIServer = Connect-VIServer @sourceConnParams -ErrorAction Stop
    Write-Host "Connected to Source: $($sourceVIServer.Name) ($($sourceVIServer.Version))" -ForegroundColor Green

    # Connect to Target vCenter
    Write-Host "Connecting to Target vCenter: $TargetVCenter..."
    $targetVIServer = Connect-VIServer @targetConnParams -ErrorAction Stop
    Write-Host "Connected to Target: $($targetVIServer.Name) ($($targetVIServer.Version))" -ForegroundColor Green

    # Get the specific source datacenter
    Write-Host "Retrieving Source Datacenter '$SourceDatacenterName'..."
    $sourceDc = Get-Datacenter -Name $SourceDatacenterName -Server $sourceVIServer -ErrorAction SilentlyContinue
    if (-not $sourceDc) {
        throw "Source Datacenter '$SourceDatacenterName' not found on vCenter '$($SourceVCenter)'."
    }
    Write-Host "Found Source Datacenter: '$($sourceDc.Name)'" -ForegroundColor Green

    # Get the specific target datacenter
    Write-Host "Retrieving Target Datacenter '$TargetDatacenterName'..."
    $targetDc = Get-Datacenter -Name $TargetDatacenterName -Server $targetVIServer -ErrorAction SilentlyContinue
     if (-not $targetDc) {
        throw "Target Datacenter '$TargetDatacenterName' not found on vCenter '$($TargetVCenter)'."
    }
    Write-Host "Found Target Datacenter: '$($targetDc.Name)'" -ForegroundColor Green

    # Get the root VM folder for the source datacenter (usually named 'vm')
    Write-Verbose "Getting root VM folder for Source Datacenter '$($sourceDc.Name)'"
    $sourceRootVmFolder = Get-Folder -Location $sourceDc -Type VM -Server $sourceVIServer -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'vm' } # Ensure it's the actual root 'vm' folder

    if (-not $sourceRootVmFolder) {
         throw "Root VM folder ('vm') not found in Source Datacenter '$($sourceDc.Name)'. Cannot proceed."
    }

    # Get the root VM folder for the target datacenter
    Write-Verbose "Getting root VM folder for Target Datacenter '$($targetDc.Name)'"
    $targetRootVmFolder = Get-Folder -Location $targetDc -Type VM -Server $targetVIServer -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'vm' }

     if (-not $targetRootVmFolder) {
         throw "Root VM folder ('vm') not found in Target Datacenter '$($targetDc.Name)'. Cannot create folders in this Datacenter."
     }

    Write-Host "Starting folder structure copy from Source DC '$($sourceDc.Name)' to Target DC '$($targetDc.Name)'..." -ForegroundColor Magenta
    # Start the recursive copy process from the root VM folders
    Copy-FolderStructure -SourceParentFolder $sourceRootVmFolder -TargetParentFolder $targetRootVmFolder -SourceServer $sourceVIServer -TargetServer $targetVIServer
    Write-Host "Finished folder structure copy for Datacenter '$($sourceDc.Name)'." -ForegroundColor Magenta


} catch {
    Write-Error "An error occurred: $($_.Exception.Message)"
    Write-Error "Script execution halted."
    # You might want to add more specific error handling here
} finally {
    # Disconnect from vCenters if connections were established
    if ($sourceVIServer) {
        Write-Host "Disconnecting from Source vCenter: $($sourceVIServer.Name)..."
        Disconnect-VIServer -Server $sourceVIServer -Confirm:$false -Force:$true
    }
    if ($targetVIServer) {
        Write-Host "Disconnecting from Target vCenter: $($targetVIServer.Name)..."
        Disconnect-VIServer -Server $targetVIServer -Confirm:$false -Force:$true
    }
    Write-Host "Script finished."
}
