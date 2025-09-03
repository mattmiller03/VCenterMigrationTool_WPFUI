# Get-VCenterObjects.ps1 - Retrieves vCenter objects for migration
param(
    [string]$ClusterName,
    [bool]$IncludeRoles = $false,
    [bool]$IncludeFolders = $false,
    [bool]$IncludeTags = $false,
    [bool]$IncludePermissions = $false,
    [bool]$IncludeResourcePools = $false,
    [bool]$IncludeCustomAttributes = $false,
    [string]$LogPath = "",
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions (check if file exists before sourcing)
if (Test-Path "$PSScriptRoot\Write-ScriptLog.ps1") {
    . "$PSScriptRoot\Write-ScriptLog.ps1"
} else {
    # Embedded minimal logging functions for standalone execution
    function Write-LogInfo { param([string]$Message, [string]$Category = '') Write-Host "[INFO] [$Category] $Message" -ForegroundColor White }
    function Write-LogSuccess { param([string]$Message, [string]$Category = '') Write-Host "[SUCCESS] [$Category] $Message" -ForegroundColor Green }
    function Write-LogWarning { param([string]$Message, [string]$Category = '') Write-Host "[WARNING] [$Category] $Message" -ForegroundColor Yellow }
    function Write-LogError { param([string]$Message, [string]$Category = '') Write-Host "[ERROR] [$Category] $Message" -ForegroundColor Red }
    function Write-LogCritical { param([string]$Message, [string]$Category = '') Write-Host "[CRITICAL] [$Category] $Message" -ForegroundColor Red }
    function Start-ScriptLogging { param($ScriptName, $LogPath, $SuppressConsoleOutput) }
    function Stop-ScriptLogging { param($Success, $Summary, $Statistics) }
}

# Start logging
Start-ScriptLogging -ScriptName "Get-VCenterObjects" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$objects = @()
$totalCount = 0

try {
    Write-LogInfo "Starting vCenter objects discovery" -Category "Initialization"
    
    # SSO Admin module managed by service layer
    $ssoModuleAvailable = $false  # SSO Admin not used in current implementation
    Write-LogInfo "SSO Admin module functionality managed by service layer" -Category "Module"
    
    # Check for existing connection or discover active connections
    $connectionEstablished = $false
    
    # First, check the default connection
    if ($global:DefaultVIServer -and $global:DefaultVIServer.IsConnected) {
        Write-LogInfo "Using existing default vCenter connection: $($global:DefaultVIServer.Name)" -Category "Connection"
        $connectionEstablished = $true
    }
    else {
        # Check if we have any VI connections at all
        Write-LogInfo "Checking for active vCenter connections..." -Category "Connection"
        $allConnections = Get-VIServer -ErrorAction SilentlyContinue
        if ($allConnections -and ($allConnections | Where-Object { $_.IsConnected })) {
            $activeConnection = $allConnections | Where-Object { $_.IsConnected } | Select-Object -First 1
            Write-LogInfo "Found active vCenter connection: $($activeConnection.Name)" -Category "Connection"
            $global:DefaultVIServer = $activeConnection
            $connectionEstablished = $true
        }
        else {
            Write-LogError "No active vCenter connection found" -Category "Connection"
            throw "No vCenter connection available. Please connect to vCenter first."
        }
    }
    
    if (-not $connectionEstablished) {
        throw "Unable to establish or find vCenter connection"
    }
    
    Write-LogInfo "Using vCenter connection: $($global:DefaultVIServer.Name)" -Category "Connection"
    
    # SSO connection handled through service layer if needed
    $ssoConnected = $false  # Not used in current implementation
    
    # Get cluster if specified
    $cluster = $null
    if ($ClusterName) {
        Write-LogInfo "Getting cluster: $ClusterName" -Category "Discovery"
        $cluster = Get-Cluster -Name $ClusterName -ErrorAction SilentlyContinue
        if (-not $cluster) {
            Write-LogWarning "Cluster '$ClusterName' not found. Will retrieve global objects." -Category "Discovery"
        }
        else {
            Write-LogSuccess "Found cluster: $($cluster.Name)" -Category "Discovery"
        }
    }
    
    # Get Roles (enhanced with SSO awareness)
    if ($IncludeRoles) {
        Write-LogInfo "Retrieving roles..." -Category "Roles"
        try {
            $roles = Get-VIRole -ErrorAction Stop | Where-Object { $_.IsSystem -eq $false }
            foreach ($role in $roles) {
                # Count role assignments for additional context
                $assignmentCount = 0
                try {
                    $assignments = Get-VIPermission | Where-Object { $_.Role -eq $role.Name }
                    $assignmentCount = @($assignments).Count
                } catch {
                    Write-LogWarning "Could not count assignments for role '$($role.Name)'" -Category "Roles"
                }
                
                $rolePrivileges = $role.PrivilegeList -join ", "
                $objects += @{
                    Id = $role.Id
                    Name = $role.Name
                    Type = "Role"
                    Path = "/Roles/$($role.Name)"
                    ItemCount = $role.PrivilegeList.Count
                    IsSelected = $true
                    Status = "Ready"
                    Details = @{
                        Description = $role.Description
                        Privileges = $rolePrivileges
                        IsSystem = $role.IsSystem
                        AssignmentCount = $assignmentCount
                        StandardDiscovery = $true
                    }
                }
            }
            
            Write-LogSuccess "Found $($roles.Count) custom roles" -Category "Roles"
        }
        catch {
            Write-LogError "Failed to retrieve roles: $($_.Exception.Message)" -Category "Roles"
        }
    }
    
    # Get Folders
    if ($IncludeFolders) {
        Write-LogInfo "Retrieving folders..." -Category "Folders"
        try {
            $folders = Get-Folder -ErrorAction Stop | Where-Object { 
                $_.Type -eq "VM" -and 
                $_.Name -ne "vm" -and 
                $_.Name -ne "Datacenters" -and
                $_.ParentId -ne $null
            }
            
            foreach ($folder in $folders) {
                $folderPath = $folder.Name
                $parent = $folder
                while ($parent.Parent -and $parent.Parent.Name -ne "Datacenters") {
                    $parent = $parent.Parent
                    $folderPath = "$($parent.Name)/$folderPath"
                }
                
                $childCount = ($folder | Get-ChildItem -ErrorAction SilentlyContinue).Count
                $objects += @{
                    Id = $folder.Id
                    Name = $folder.Name
                    Type = "Folder"
                    Path = "/vm/$folderPath"
                    ItemCount = $childCount
                    IsSelected = $true
                    Status = "Ready"
                    Details = @{
                        Type = $folder.Type
                        ParentId = $folder.ParentId
                        ChildCount = $childCount
                    }
                }
            }
            Write-LogSuccess "Found $($folders.Count) VM folders" -Category "Folders"
        }
        catch {
            Write-LogError "Failed to retrieve folders: $($_.Exception.Message)" -Category "Folders"
        }
    }
    
    # Get Tags and Categories
    if ($IncludeTags) {
        Write-LogInfo "Retrieving tags and categories..." -Category "Tags"
        try {
            $tagCategories = Get-TagCategory -ErrorAction Stop
            foreach ($category in $tagCategories) {
                $categoryTags = Get-Tag -Category $category -ErrorAction SilentlyContinue
                $objects += @{
                    Id = $category.Id
                    Name = $category.Name
                    Type = "TagCategory"
                    Path = "/Tags/$($category.Name)"
                    ItemCount = $categoryTags.Count
                    IsSelected = $true
                    Status = "Ready"
                    Details = @{
                        Description = $category.Description
                        Cardinality = $category.Cardinality
                        EntityType = $category.EntityType -join ", "
                        TagCount = $categoryTags.Count
                    }
                }
                
                foreach ($tag in $categoryTags) {
                    $objects += @{
                        Id = $tag.Id
                        Name = "$($category.Name):$($tag.Name)"
                        Type = "Tag"
                        Path = "/Tags/$($category.Name)/$($tag.Name)"
                        ItemCount = 0
                        IsSelected = $true
                        Status = "Ready"
                        Details = @{
                            Description = $tag.Description
                            Category = $category.Name
                            CategoryId = $category.Id
                        }
                    }
                }
            }
            Write-LogSuccess "Found $($tagCategories.Count) tag categories with $($categoryTags.Count) total tags" -Category "Tags"
        }
        catch {
            Write-LogError "Failed to retrieve tags: $($_.Exception.Message)" -Category "Tags"
        }
    }
    
    # Get Permissions (enhanced with global permissions and SSO awareness)
    if ($IncludePermissions) {
        Write-LogInfo "Retrieving permissions..." -Category "Permissions"
        try {
            $permissions = Get-VIPermission -ErrorAction Stop
            
            # Get global permissions using root folder
            $globalPermissions = @()
            try {
                Write-LogInfo "Retrieving global permissions..." -Category "Permissions"
                $rootFolder = Get-Folder -NoRecursion -ErrorAction SilentlyContinue
                $globalPermissions = Get-VIPermission -Entity $rootFolder -ErrorAction SilentlyContinue
                Write-LogInfo "Found $($globalPermissions.Count) global permissions" -Category "Permissions"
            } catch {
                Write-LogWarning "Could not retrieve global permissions: $($_.Exception.Message)" -Category "Permissions"
            }
            
            # Combine standard and global permissions
            $allPermissions = @($permissions) + @($globalPermissions)
            $allPermissions = $allPermissions | Sort-Object Principal, Entity, Role -Unique
            
            $groupedPermissions = $allPermissions | Group-Object Entity | ForEach-Object {
                $entityName = if ($_.Name) { $_.Name } else { "Root" }
                @{
                    Id = "perm-$($_.Name -replace '[^a-zA-Z0-9]', '-')"
                    Name = "Permissions for $entityName"
                    Type = "Permission"
                    Path = "/permissions/$entityName"
                    ItemCount = $_.Count
                    IsSelected = $true
                    Status = "Ready"
                    Details = @{
                        Entity = $entityName
                        PermissionCount = $_.Count
                        Principals = ($_.Group.Principal | Sort-Object -Unique) -join ", "
                        IncludesGlobal = ($globalPermissions.Count -gt 0)
                        StandardDiscovery = $true
                    }
                }
            }
            $objects += $groupedPermissions
            
            Write-LogSuccess "Found $($allPermissions.Count) permissions on $($groupedPermissions.Count) entities" -Category "Permissions"
        }
        catch {
            Write-LogError "Failed to retrieve permissions: $($_.Exception.Message)" -Category "Permissions"
        }
    }
    
    # Get Resource Pools
    if ($IncludeResourcePools) {
        Write-LogInfo "Retrieving resource pools..." -Category "ResourcePools"
        try {
            $resourcePools = if ($cluster) { 
                Get-ResourcePool -Location $cluster -ErrorAction Stop
            } else { 
                Get-ResourcePool -ErrorAction Stop
            }
            
            foreach ($rp in $resourcePools) {
                # Skip the default "Resources" pool
                if ($rp.Name -ne "Resources") {
                    $objects += @{
                        Id = $rp.Id
                        Name = $rp.Name
                        Type = "ResourcePool"
                        Path = "/resourcepools/$($rp.Name)"
                        ItemCount = ($rp | Get-VM -ErrorAction SilentlyContinue).Count
                        IsSelected = $true
                        Status = "Ready"
                        Details = @{
                            CpuReservationMhz = $rp.CpuReservationMhz
                            CpuLimitMhz = $rp.CpuLimitMhz
                            MemReservationGB = [math]::Round($rp.MemReservationMB / 1024, 2)
                            MemLimitGB = if ($rp.MemLimitMB -eq -1) { "Unlimited" } else { [math]::Round($rp.MemLimitMB / 1024, 2) }
                            NumCpuShares = $rp.NumCpuShares
                            NumMemShares = $rp.NumMemShares
                        }
                    }
                }
            }
            Write-LogSuccess "Found $($resourcePools.Count) resource pools" -Category "ResourcePools"
        }
        catch {
            Write-LogError "Failed to retrieve resource pools: $($_.Exception.Message)" -Category "ResourcePools"
        }
    }
    
    # Get Custom Attributes
    if ($IncludeCustomAttributes) {
        Write-LogInfo "Retrieving custom attributes..." -Category "CustomAttributes"
        try {
            $customAttributes = Get-CustomAttribute -ErrorAction Stop
            foreach ($attr in $customAttributes) {
                $objects += @{
                    Id = $attr.Key
                    Name = $attr.Name
                    Type = "CustomAttribute"
                    Path = "/custom-attributes/$($attr.Name)"
                    ItemCount = 0
                    IsSelected = $true
                    Status = "Ready"
                    Details = @{
                        Key = $attr.Key
                        TargetType = $attr.TargetType -join ", "
                    }
                }
            }
            Write-LogSuccess "Found $($customAttributes.Count) custom attributes" -Category "CustomAttributes"
        }
        catch {
            Write-LogError "Failed to retrieve custom attributes: $($_.Exception.Message)" -Category "CustomAttributes"
        }
    }
    
    $totalCount = $objects.Count
    $scriptSuccess = $true
    $finalSummary = "Successfully retrieved $totalCount vCenter objects"
    Write-LogSuccess $finalSummary -Category "Summary"
    
    # Output result as JSON
    $result = @{
        Success = $true
        Objects = $objects
        TotalCount = $totalCount
        ClusterName = $ClusterName
        Timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    }
    
    Write-Output ($result | ConvertTo-Json -Depth 5 -Compress)
}
catch {
    $scriptSuccess = $false
    $finalSummary = "Failed to retrieve vCenter objects: $($_.Exception.Message)"
    Write-LogCritical $finalSummary -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    # Return error in JSON format
    $errorResult = @{
        Success = $false
        Error = $_.Exception.Message
        Objects = @()
        TotalCount = 0
    }
    Write-Output ($errorResult | ConvertTo-Json -Compress)
    
    exit 1
}
finally {
    # Final statistics
    $stats = @{
        "TotalObjects" = $totalCount
        "ClusterName" = $ClusterName
        "IncludeRoles" = $IncludeRoles
        "IncludeFolders" = $IncludeFolders
        "IncludeTags" = $IncludeTags
        "IncludePermissions" = $IncludePermissions
        "IncludeResourcePools" = $IncludeResourcePools
        "IncludeCustomAttributes" = $IncludeCustomAttributes
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $stats
}