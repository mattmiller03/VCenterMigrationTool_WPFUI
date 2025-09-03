# Migrate-VCenterObject.ps1 - Migrates vCenter objects between vCenter servers
param(
    [Parameter(Mandatory=$true)]
    [string]$SourceVCenter,
    
    [Parameter(Mandatory=$true)]
    [string]$TargetVCenter,
    
    [Parameter(Mandatory=$true)]
    [string]$ObjectType,
    
    [Parameter(Mandatory=$true)]
    [string]$ObjectName,
    
    [string]$ObjectId = "",
    [string]$ObjectPath = "",
    [string]$LogPath = "",
    [bool]$SuppressConsoleOutput = $false,
    [bool]$ValidateOnly = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Start logging
Start-ScriptLogging -ScriptName "Migrate-VCenterObject" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$sourceConnection = $null
$targetConnection = $null

try {
    Write-LogInfo "Starting vCenter object migration" -Category "Initialization"
    Write-LogInfo "Object Type: $ObjectType, Name: $ObjectName" -Category "Initialization"
    
    # PowerCLI module management handled by service layer
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    
    # Use existing vCenter connections established by PersistentVcenterConnectionService
    Write-LogInfo "Using existing source vCenter connection: $SourceVCenter" -Category "Connection"
    $sourceConnection = $global:DefaultVIServers | Where-Object { $_.Name -eq $SourceVCenter }
    if (-not $sourceConnection -or -not $sourceConnection.IsConnected) {
        throw "Source vCenter connection to '$SourceVCenter' not found or not active. Please establish connection through main UI first."
    }
    Write-LogSuccess "Using source vCenter connection: $($sourceConnection.Name)" -Category "Connection"
    
    # Use existing target vCenter connection established by PersistentVcenterConnectionService
    Write-LogInfo "Using existing target vCenter connection: $TargetVCenter" -Category "Connection"
    $targetConnection = $global:DefaultVIServers | Where-Object { $_.Name -eq $TargetVCenter }
    if (-not $targetConnection -or -not $targetConnection.IsConnected) {
        throw "Target vCenter connection to '$TargetVCenter' not found or not active. Please establish connection through main UI first."
    }
    Write-LogSuccess "Using target vCenter connection: $($targetConnection.Name)" -Category "Connection"
    
    # Switch to source connection for object retrieval
    $global:DefaultVIServer = $sourceConnection
    
    # Migrate based on object type
    switch ($ObjectType) {
        "Role" {
            Write-LogInfo "Migrating role: $ObjectName" -Category "Migration"
            
            # Get source role
            $sourceRole = Get-VIRole -Name $ObjectName -Server $sourceConnection -ErrorAction Stop
            if (-not $sourceRole) {
                throw "Role '$ObjectName' not found in source vCenter"
            }
            
            Write-LogInfo "Found source role with $($sourceRole.PrivilegeList.Count) privileges" -Category "Migration"
            
            # Switch to target connection
            $global:DefaultVIServer = $targetConnection
            
            # Check if role already exists
            $existingRole = Get-VIRole -Name $ObjectName -Server $targetConnection -ErrorAction SilentlyContinue
            if ($existingRole) {
                Write-LogWarning "Role '$ObjectName' already exists in target vCenter" -Category "Migration"
                $finalSummary = "Role already exists in target - skipped"
            }
            else {
                # Create role in target
                $targetRole = New-VIRole -Name $sourceRole.Name -Privilege $sourceRole.PrivilegeList -Server $targetConnection -ErrorAction Stop
                Write-LogSuccess "Created role '$($targetRole.Name)' in target vCenter" -Category "Migration"
                $finalSummary = "Successfully migrated role '$ObjectName'"
            }
        }
        
        "Folder" {
            Write-LogInfo "Migrating folder: $ObjectName" -Category "Migration"
            
            # Get source folder
            $sourceFolder = Get-Folder -Name $ObjectName -Type VM -Server $sourceConnection -ErrorAction Stop
            if (-not $sourceFolder) {
                throw "Folder '$ObjectName' not found in source vCenter"
            }
            
            Write-LogInfo "Found source folder at path: $($sourceFolder.Parent.Name)/$($sourceFolder.Name)" -Category "Migration"
            
            # Switch to target connection
            $global:DefaultVIServer = $targetConnection
            
            # Check if folder already exists
            $existingFolder = Get-Folder -Name $ObjectName -Type VM -Server $targetConnection -ErrorAction SilentlyContinue
            if ($existingFolder) {
                Write-LogWarning "Folder '$ObjectName' already exists in target vCenter" -Category "Migration"
                $finalSummary = "Folder already exists in target - skipped"
            }
            else {
                # Create folder in target (under vm root for now)
                $vmFolder = Get-Folder -Name "vm" -Type VM -Server $targetConnection
                $targetFolder = New-Folder -Name $sourceFolder.Name -Location $vmFolder -Server $targetConnection -ErrorAction Stop
                Write-LogSuccess "Created folder '$($targetFolder.Name)' in target vCenter" -Category "Migration"
                $finalSummary = "Successfully migrated folder '$ObjectName'"
            }
        }
        
        "ResourcePool" {
            Write-LogInfo "Migrating resource pool: $ObjectName" -Category "Migration"
            
            # Get source resource pool
            $sourceRP = Get-ResourcePool -Name $ObjectName -Server $sourceConnection -ErrorAction Stop
            if (-not $sourceRP) {
                throw "Resource pool '$ObjectName' not found in source vCenter"
            }
            
            Write-LogInfo "Found source resource pool with CPU: $($sourceRP.CpuReservationMhz)MHz, Memory: $($sourceRP.MemReservationMB)MB" -Category "Migration"
            
            # Switch to target connection
            $global:DefaultVIServer = $targetConnection
            
            # Check if resource pool already exists
            $existingRP = Get-ResourcePool -Name $ObjectName -Server $targetConnection -ErrorAction SilentlyContinue
            if ($existingRP) {
                Write-LogWarning "Resource pool '$ObjectName' already exists in target vCenter" -Category "Migration"
                $finalSummary = "Resource pool already exists in target - skipped"
            }
            else {
                Write-LogWarning "Resource pool migration requires target cluster specification - feature not implemented" -Category "Migration"
                $finalSummary = "Resource pool migration not fully implemented"
            }
        }
        
        "CustomAttribute" {
            Write-LogInfo "Migrating custom attribute: $ObjectName" -Category "Migration"
            
            # Get source custom attribute
            $sourceAttr = Get-CustomAttribute -Name $ObjectName -Server $sourceConnection -ErrorAction Stop
            if (-not $sourceAttr) {
                throw "Custom attribute '$ObjectName' not found in source vCenter"
            }
            
            Write-LogInfo "Found source custom attribute for: $($sourceAttr.TargetType -join ', ')" -Category "Migration"
            
            # Switch to target connection
            $global:DefaultVIServer = $targetConnection
            
            # Check if custom attribute already exists
            $existingAttr = Get-CustomAttribute -Name $ObjectName -Server $targetConnection -ErrorAction SilentlyContinue
            if ($existingAttr) {
                Write-LogWarning "Custom attribute '$ObjectName' already exists in target vCenter" -Category "Migration"
                $finalSummary = "Custom attribute already exists in target - skipped"
            }
            else {
                # Create custom attribute in target
                $targetAttr = New-CustomAttribute -Name $sourceAttr.Name -TargetType $sourceAttr.TargetType -Server $targetConnection -ErrorAction Stop
                Write-LogSuccess "Created custom attribute '$($targetAttr.Name)' in target vCenter" -Category "Migration"
                $finalSummary = "Successfully migrated custom attribute '$ObjectName'"
            }
        }
        
        "Datacenter" {
            Write-LogInfo "Migrating datacenter: $ObjectName" -Category "Migration"
            
            # Working with target connection
            Write-LogDebug "Working with target connection: $($targetConnection.Name)" -Category "Connection"
            
            # Check if datacenter already exists in TARGET vCenter
            $existingDC = Get-Datacenter -Name $ObjectName -Server $targetConnection -ErrorAction SilentlyContinue
            if ($existingDC) {
                Write-LogWarning "Datacenter '$ObjectName' already exists in target vCenter" -Category "Migration"
                $finalSummary = "Datacenter already exists in target - skipped"
            }
            else {
                if ($ValidateOnly) {
                    Write-LogInfo "VALIDATION ONLY: Would create datacenter '$ObjectName' in target vCenter" -Category "Validation"
                    $finalSummary = "Validation: Would create datacenter '$ObjectName'"
                }
                else {
                    # Get root folder from TARGET vCenter
                    $rootFolder = Get-Folder -Name "Datacenters" -Type Datacenter -Server $targetConnection -ErrorAction SilentlyContinue
                    if (-not $rootFolder) {
                        # Fallback: get the invisible root folder from TARGET vCenter
                        $rootFolder = Get-Folder -Type Datacenter -Server $targetConnection | Where-Object { $_.Name -eq "Datacenters" -or $_.Parent.Name -eq "" } | Select-Object -First 1
                    }
                    
                    if ($rootFolder) {
                        # Create datacenter in target
                        $targetDC = New-Datacenter -Name $ObjectName -Location $rootFolder -Server $targetConnection -ErrorAction Stop
                        Write-LogSuccess "Created datacenter '$($targetDC.Name)' in target vCenter" -Category "Migration"
                        $finalSummary = "Successfully migrated datacenter '$ObjectName'"
                    }
                    else {
                        Write-LogError "Could not find root datacenter folder in target vCenter" -Category "Migration"
                        $finalSummary = "Failed to locate root datacenter folder"
                    }
                }
            }
        }
        
        default {
            Write-LogWarning "Migration not implemented for object type: $ObjectType" -Category "Migration"
            $finalSummary = "Migration not implemented for type '$ObjectType'"
        }
    }
    
    $scriptSuccess = $true
    if ([string]::IsNullOrEmpty($finalSummary)) {
        $finalSummary = "Migration process completed"
    }
    
    Write-LogSuccess $finalSummary -Category "Summary"
    
    # Output result as JSON
    $result = @{
        Success = $true
        ObjectType = $ObjectType
        ObjectName = $ObjectName
        Message = $finalSummary
        Timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    }
    
    Write-Output ($result | ConvertTo-Json -Compress)
}
catch {
    $scriptSuccess = $false
    $finalSummary = "Failed to migrate vCenter object: $($_.Exception.Message)"
    Write-LogCritical $finalSummary -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    # Return error in JSON format
    $errorResult = @{
        Success = $false
        ObjectType = $ObjectType
        ObjectName = $ObjectName
        Error = $_.Exception.Message
        Message = $finalSummary
    }
    Write-Output ($errorResult | ConvertTo-Json -Compress)
    
    exit 1
}
finally {
    # Disconnect from vCenters
    if ($sourceConnection) {
        Write-LogInfo "Disconnecting from source vCenter..." -Category "Cleanup"
        # DISCONNECT REMOVED - Using persistent connections managed by application
    }
    
    if ($targetConnection) {
        Write-LogInfo "Disconnecting from target vCenter..." -Category "Cleanup"
        # DISCONNECT REMOVED - Using persistent connections managed by application
    }
    
    $finalStats = @{
        "ObjectType" = $ObjectType
        "ObjectName" = $ObjectName
        "SourceVCenter" = $SourceVCenter
        "TargetVCenter" = $TargetVCenter
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
}