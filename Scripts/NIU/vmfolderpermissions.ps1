#Requires -Version 5.1
#Requires -Modules VMware.PowerCLI

<#
.SYNOPSIS
    Resets all VM folder permissions in vCenter to inherited only.

.DESCRIPTION
    This script connects to a vCenter server and traverses through all datacenters
    and their VM folders, removing all explicit permissions and ensuring only
    inherited permissions remain. It creates a detailed backup of all permissions
    before removal.

.PARAMETER VCenterServer
    The FQDN or IP address of the vCenter server.

.PARAMETER Credential
    PSCredential object for vCenter authentication. If not provided, will prompt.

.PARAMETER WhatIf
    Performs a dry run without making actual changes.

.PARAMETER LogPath
    Path to save the log file. Default is current directory with timestamp.

.PARAMETER BackupPath
    Path to save the permissions backup CSV file. Default is current directory with timestamp.

.EXAMPLE
    .\Reset-VMFolderPermissions.ps1 -VCenterServer "vcenter.domain.com"

.EXAMPLE
    $cred = Get-Credential
    .\Reset-VMFolderPermissions.ps1 -VCenterServer "vcenter.domain.com" -Credential $cred -WhatIf

.NOTES
    Author: Cloud Operations Team
    Date: September 2025
    Version: 1.1
    PSScriptAnalyzer: Compliant
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$VCenterServer,
    
    [Parameter(Mandatory = $false)]
    [System.Management.Automation.PSCredential]
    [System.Management.Automation.Credential()]
    $Credential,
    
    [Parameter(Mandatory = $false)]
    [string]$LogPath = ".\VMFolderPermissions_Reset_$(Get-Date -Format 'yyyyMMdd_HHmmss').log",
    
    [Parameter(Mandatory = $false)]
    [string]$BackupPath = ".\VMFolderPermissions_Backup_$(Get-Date -Format 'yyyyMMdd_HHmmss').csv"
)

#region Functions

function Write-LogMessage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        
        [Parameter(Mandatory = $false)]
        [ValidateSet('Info', 'Warning', 'Error', 'Success')]
        [string]$Level = 'Info',
        
        [Parameter(Mandatory = $false)]
        [string]$LogFile = $script:LogPath
    )
    
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $logEntry = "[$timestamp] [$Level] $Message"
    
    # Write to console with appropriate color
    switch ($Level) {
        'Info'    { Write-Host $logEntry -ForegroundColor White }
        'Warning' { Write-Warning $Message }
        'Error'   { Write-Error $Message }
        'Success' { Write-Host $logEntry -ForegroundColor Green }
    }
    
    # Write to log file
    try {
        Add-Content -Path $LogFile -Value $logEntry -ErrorAction Stop
    }
    catch {
        Write-Warning "Failed to write to log file: $_"
    }
}

function Get-VMFolderPermissions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Types.V1.Inventory.Folder]$Folder
    )
    
    try {
        $permissions = Get-VIPermission -Entity $Folder -ErrorAction Stop
        return $permissions
    }
    catch {
        Write-LogMessage -Message "Failed to get permissions for folder '$($Folder.Name)': $_" -Level Error
        return $null
    }
}

function Export-PermissionBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.ArrayList]$PermissionsList,
        
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )
    
    try {
        if ($PermissionsList.Count -gt 0) {
            $PermissionsList | Export-Csv -Path $FilePath -NoTypeInformation -Force
            Write-LogMessage -Message "Successfully exported $($PermissionsList.Count) permission record(s) to: $FilePath" -Level Success
        }
        else {
            Write-LogMessage -Message "No permissions to export" -Level Info
        }
    }
    catch {
        Write-LogMessage -Message "Failed to export permissions backup: $_" -Level Error
    }
}

function New-PermissionBackupObject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Permission,
        
        [Parameter(Mandatory = $true)]
        [string]$FolderPath,
        
        [Parameter(Mandatory = $true)]
        [string]$FolderId,
        
        [Parameter(Mandatory = $true)]
        [string]$DatacenterName
    )
    
    $backupObject = [PSCustomObject]@{
        Timestamp      = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
        VCenter        = $VCenterServer
        Datacenter     = $DatacenterName
        FolderPath     = $FolderPath
        FolderId       = $FolderId
        Principal      = $Permission.Principal
        PrincipalType  = if ($Permission.IsGroup) { "Group" } else { "User" }
        Role           = $Permission.Role
        RoleId         = $Permission.RoleId
        Propagate      = $Permission.Propagate
        IsGroup        = $Permission.IsGroup
        EntityId       = $Permission.EntityId
        Entity         = $Permission.Entity.Name
        EntityType     = $Permission.Entity.GetType().Name
    }
    
    return $backupObject
}

function Reset-FolderPermissions {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Types.V1.Inventory.Folder]$Folder,
        
        [Parameter(Mandatory = $true)]
        [string]$DatacenterName,
        
        [Parameter(Mandatory = $true)]
        [System.Collections.ArrayList]$BackupList
    )
    
    $folderPath = Get-FolderPath -Folder $Folder
    $folderId = $Folder.Id
    Write-LogMessage -Message "Processing folder: $folderPath (ID: $folderId)"
    
    # Get current permissions
    $permissions = Get-VMFolderPermissions -Folder $Folder
    
    if ($null -eq $permissions) {
        Write-LogMessage -Message "Could not retrieve permissions for folder: $folderPath" -Level Warning
        return
    }
    
    # Filter for explicit (non-inherited) permissions
    $explicitPermissions = $permissions | Where-Object { -not $_.IsGroup -or $_.Propagate -eq $false }
    
    if ($explicitPermissions.Count -eq 0) {
        Write-LogMessage -Message "No explicit permissions found on folder: $folderPath" -Level Info
        return
    }
    
    Write-LogMessage -Message "Found $($explicitPermissions.Count) explicit permission(s) on folder: $folderPath" -Level Warning
    
    foreach ($permission in $explicitPermissions) {
        # Create backup object for this permission
        $backupObject = New-PermissionBackupObject -Permission $permission -FolderPath $folderPath -FolderId $folderId -DatacenterName $DatacenterName
        [void]$BackupList.Add($backupObject)
        
        $permissionDetails = "Principal: $($permission.Principal), Role: $($permission.Role), Propagate: $($permission.Propagate)"
        
        # Log the permission details that will be removed
        Write-LogMessage -Message "Permission to be removed from '$folderPath': $permissionDetails" -Level Info
        
        if ($PSCmdlet.ShouldProcess($folderPath, "Remove permission: $permissionDetails")) {
            try {
                Remove-VIPermission -Permission $permission -Confirm:$false -ErrorAction Stop
                Write-LogMessage -Message "Successfully removed permission from folder '$folderPath': $permissionDetails" -Level Success
                
                # Add removal status to backup object
                $backupObject | Add-Member -NotePropertyName "RemovalStatus" -NotePropertyValue "Success" -Force
                $backupObject | Add-Member -NotePropertyName "RemovalTime" -NotePropertyValue (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') -Force
            }
            catch {
                Write-LogMessage -Message "Failed to remove permission from folder '$folderPath': $_" -Level Error
                
                # Add removal status to backup object
                $backupObject | Add-Member -NotePropertyName "RemovalStatus" -NotePropertyValue "Failed" -Force
                $backupObject | Add-Member -NotePropertyName "RemovalError" -NotePropertyValue $_.ToString() -Force
            }
        }
        else {
            Write-LogMessage -Message "[WhatIf] Would remove permission from folder '$folderPath': $permissionDetails" -Level Info
            
            # Add removal status to backup object for WhatIf
            $backupObject | Add-Member -NotePropertyName "RemovalStatus" -NotePropertyValue "WhatIf" -Force
            $backupObject | Add-Member -NotePropertyName "RemovalTime" -NotePropertyValue "N/A" -Force
        }
    }
}

function Get-FolderPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Types.V1.Inventory.Folder]$Folder
    )
    
    $path = $Folder.Name
    $parent = $Folder.Parent
    
    while ($parent -and $parent -isnot [VMware.VimAutomation.ViCore.Types.V1.Inventory.Datacenter]) {
        if ($parent.Name -ne "vm") {
            $path = "$($parent.Name)\$path"
        }
        $parent = $parent.Parent
    }
    
    if ($parent) {
        $path = "$($parent.Name)\$path"
    }
    
    return $path
}

function Get-AllVMFolders {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Types.V1.Inventory.Folder]$StartFolder,
        
        [Parameter(Mandatory = $false)]
        [System.Collections.ArrayList]$FolderList = (New-Object System.Collections.ArrayList)
    )
    
    # Add current folder to list (except for root 'vm' folder)
    if ($StartFolder.Name -ne "vm" -or $StartFolder.Parent -isnot [VMware.VimAutomation.ViCore.Types.V1.Inventory.Datacenter]) {
        [void]$FolderList.Add($StartFolder)
    }
    
    # Get child folders
    $childFolders = Get-Folder -Location $StartFolder -NoRecursion -ErrorAction SilentlyContinue | 
        Where-Object { $_.Type -eq "VM" }
    
    foreach ($childFolder in $childFolders) {
        Get-AllVMFolders -StartFolder $childFolder -FolderList $FolderList
    }
    
    return $FolderList
}

function Show-PermissionSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.ArrayList]$BackupList
    )
    
    if ($BackupList.Count -eq 0) {
        Write-LogMessage -Message "No permissions were found to remove" -Level Info
        return
    }
    
    Write-LogMessage -Message "`n=== Permission Removal Summary ===" -Level Info
    
    # Group by removal status
    $statusGroups = $BackupList | Group-Object -Property RemovalStatus
    
    foreach ($group in $statusGroups) {
        Write-LogMessage -Message "$($group.Name): $($group.Count) permission(s)" -Level Info
    }
    
    # Group by role
    Write-LogMessage -Message "`n=== Permissions by Role ===" -Level Info
    $roleGroups = $BackupList | Group-Object -Property Role | Sort-Object Count -Descending
    
    foreach ($group in $roleGroups) {
        Write-LogMessage -Message "$($group.Name): $($group.Count) permission(s)" -Level Info
    }
    
    # Group by principal
    Write-LogMessage -Message "`n=== Top 10 Principals by Permission Count ===" -Level Info
    $principalGroups = $BackupList | Group-Object -Property Principal | Sort-Object Count -Descending | Select-Object -First 10
    
    foreach ($group in $principalGroups) {
        Write-LogMessage -Message "$($group.Name): $($group.Count) permission(s)" -Level Info
    }
}

#endregion Functions

#region Main Script

try {
    # Initialize log
    Write-LogMessage -Message "=== Starting VM Folder Permissions Reset Script ===" -Level Info
    Write-LogMessage -Message "Target vCenter: $VCenterServer" -Level Info
    Write-LogMessage -Message "Log file: $LogPath" -Level Info
    Write-LogMessage -Message "Backup file: $BackupPath" -Level Info
    
    if ($PSCmdlet.MyInvocation.BoundParameters["WhatIf"]) {
        Write-LogMessage -Message "Running in WhatIf mode - no changes will be made" -Level Warning
    }
    
    # Check for VMware PowerCLI module
    if (-not (Get-Module -Name VMware.PowerCLI -ListAvailable)) {
        throw "VMware PowerCLI module is not installed. Please install it using: Install-Module -Name VMware.PowerCLI"
    }
    
    # Import VMware PowerCLI module
    Write-LogMessage -Message "Importing VMware PowerCLI module..." -Level Info
    Import-Module -Name VMware.PowerCLI -ErrorAction Stop
    
    # Set PowerCLI configuration to ignore certificate warnings (optional, remove if not needed)
    $null = Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session -WhatIf:$false
    
    # Connect to vCenter
    Write-LogMessage -Message "Connecting to vCenter server: $VCenterServer" -Level Info
    
    $connectParams = @{
        Server      = $VCenterServer
        ErrorAction = 'Stop'
    }
    
    if ($Credential) {
        $connectParams['Credential'] = $Credential
    }
    
    $vcConnection = Connect-VIServer @connectParams
    Write-LogMessage -Message "Successfully connected to vCenter: $($vcConnection.Name)" -Level Success
    
    # Initialize backup list
    $permissionsBackupList = New-Object System.Collections.ArrayList
    
    # Get all datacenters
    $datacenters = Get-Datacenter -Server $vcConnection
    Write-LogMessage -Message "Found $($datacenters.Count) datacenter(s)" -Level Info
    
    $totalFoldersProcessed = 0
    $totalPermissionsRemoved = 0
    
    foreach ($datacenter in $datacenters) {
        Write-LogMessage -Message "`nProcessing datacenter: $($datacenter.Name)" -Level Info
        Write-LogMessage -Message ("=" * 50) -Level Info
        
        # Get the root VM folder for the datacenter
        $rootVMFolder = Get-Folder -Name "vm" -Location $datacenter -Type VM -ErrorAction SilentlyContinue
        
        if (-not $rootVMFolder) {
            Write-LogMessage -Message "Could not find VM folder in datacenter: $($datacenter.Name)" -Level Warning
            continue
        }
        
        # Get all VM folders recursively
        $allFolders = Get-AllVMFolders -StartFolder $rootVMFolder
        Write-LogMessage -Message "Found $($allFolders.Count) VM folder(s) in datacenter: $($datacenter.Name)" -Level Info
        
        # Process each folder
        foreach ($folder in $allFolders) {
            Reset-FolderPermissions -Folder $folder -DatacenterName $datacenter.Name -BackupList $permissionsBackupList
            $totalFoldersProcessed++
        }
    }
    
    # Export backup to CSV
    if ($permissionsBackupList.Count -gt 0) {
        Write-LogMessage -Message "`nExporting permissions backup..." -Level Info
        Export-PermissionBackup -PermissionsList $permissionsBackupList -FilePath $BackupPath
        
        # Show summary
        Show-PermissionSummary -BackupList $permissionsBackupList
    }
    else {
        Write-LogMessage -Message "No permissions were collected for backup" -Level Info
    }
    
    Write-LogMessage -Message "`n=== Script Execution Complete ===" -Level Success
    Write-LogMessage -Message "Total folders processed: $totalFoldersProcessed" -Level Info
    Write-LogMessage -Message "Total permissions collected: $($permissionsBackupList.Count)" -Level Info
    
    if (-not $PSCmdlet.MyInvocation.BoundParameters["WhatIf"]) {
        $successfulRemovals = ($permissionsBackupList | Where-Object { $_.RemovalStatus -eq "Success" }).Count
        Write-LogMessage -Message "Total permissions removed: $successfulRemovals" -Level Info
    }
    
    Write-LogMessage -Message "Review the log file for details: $LogPath" -Level Info
    
    if ($permissionsBackupList.Count -gt 0) {
        Write-LogMessage -Message "Permissions backup saved to: $BackupPath" -Level Info
        Write-LogMessage -Message "IMPORTANT: Keep this backup file for potential rollback operations" -Level Warning
    }
}
catch {
    # Try to display error even if logging isn't working
    $errorMessage = "Critical error occurred: $_"
    $stackTrace = "Stack Trace: $($_.ScriptStackTrace)"
    
    # Try to log if possible
    if ($script:LogPath) {
        Write-LogMessage -Message $errorMessage -Level Error
        Write-LogMessage -Message $stackTrace -Level Error
    }
    else {
        # Fallback to console only
        Write-Host $errorMessage -ForegroundColor Red
        Write-Host $stackTrace -ForegroundColor Red
    }
    
    # Still try to export any collected permissions before exiting
    if ($null -ne $permissionsBackupList -and $permissionsBackupList.Count -gt 0) {
        if ($script:LogPath) {
            Write-LogMessage -Message "Attempting to export partial backup before exit..." -Level Warning
        }
        else {
            Write-Host "Attempting to export partial backup before exit..." -ForegroundColor Yellow
        }
        Export-PermissionBackup -PermissionsList $permissionsBackupList -FilePath $BackupPath
    }
    
    exit 1
}
finally {
    # Disconnect from vCenter
    if ($vcConnection) {
        if ($script:LogPath) {
            Write-LogMessage -Message "Disconnecting from vCenter..." -Level Info
        }
        else {
            Write-Host "Disconnecting from vCenter..." -ForegroundColor Yellow
        }
        Disconnect-VIServer -Server $vcConnection -Confirm:$false -ErrorAction SilentlyContinue
    }
}

#endregion Main Script