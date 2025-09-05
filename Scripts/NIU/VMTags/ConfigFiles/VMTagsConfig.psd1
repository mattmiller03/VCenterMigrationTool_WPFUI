@{
    # Application Metadata
    Application = @{
        Name = "VM Tags and Permissions Management"
        Version = "2.1.0"
        Author = "Infrastructure Team"
        Description = "Automated management of vCenter VM tags and permissions based on CSV configurations"
        LastUpdated = "2024-01-15"
        SupportContact = "infrastructure@company.com"
    }
    
    # Environment Configurations
    Environments = @{
        DEV = @{
            vCenterServer           =   "daisv0tp231.dir.ad.dla.mil"
            SSODomain               =   "DLA-Test-Dev.local"
            DefaultCredentialUser   =   "administrator@DLA-Test-Dev.local"
            
            # Tag Categories for DEV environment
            TagCategories = @{
                App         =       "vCenter-DEV-App-team"
                Function    =       "vCenter-DEV-Function"
                OS          =       "vCenter-DEV-OS"
            }
            
            # File paths for DEV
            DataPaths = @{
                AppPermissionsCSV           =   ".\Data\DEV\AppTagPermissions_DEV.csv"
                OSMappingCSV                =   ".\Data\DEV\OS-Mappings_DEV.csv"
                LogDirectory                =   ".\Logs\DEV"
                BackupDirectory             =   ".\Backup\DEV"
            }
            
            # DEV-specific settings
            Settings = @{
                EnableDebugLogging = $true
                ConnectionTimeout = 180
                MaxRetries = 2
                ValidateCertificates = $false
                ProcessInBatches = $false
                BatchSize = 50
            }
        }
        
        PROD = @{
            vCenterServer           =   "vcsa-prod.corp.local"
            SSODomain               =   "DLA-Prod.local"
            DefaultCredentialUser   =   "svc_vmware_automation@vsphere.local"
            
            TagCategories = @{
                App                 =   "vCenter-PROD-App-team"
                Function            =   "vCenter-PROD-Function"
                OS                  =   "vCenter-PROD-OS"
            }
            
            DataPaths = @{
                AppPermissionsCSV   =       ".\Data\PROD\App-Permissions-PROD.csv"
                OSMappingCSV        =       ".\Data\PROD\OS-Mappings-PROD.csv"
                LogDirectory        =       ".\Logs\PROD"
                BackupDirectory     =       ".\Backup\PROD"
            }
            
            Settings = @{
                EnableDebugLogging = $false
                ConnectionTimeout = 300
                MaxRetries = 3
                ValidateCertificates = $true
                ProcessInBatches = $true
                BatchSize = 100
            }
        }
        
        KLEB = @{
            vCenterServer               =   "klisv0pp251.dir.ad.dla.mil"
            SSODomain                   =   "DLA-Kleber.local"
            DefaultCredentialUser       =   "administrator@DLA-KLEBER.local"
            
            TagCategories = @{
                App                     =   "vCenter-KLEBER-App-team"
                Function                =   "vCenter-KLEBER-Function"
                OS                      =   "vCenter-KLEBER-OS"
            }
            
            DataPaths = @{
                AppPermissionsCSV       =   ".\Data\KLEB\AppTagPermissions_KLE.csv"
                OSMappingCSV            =   ".\Data\KLEB\OS-Mappings_KLE.csv"
                LogDirectory            =   ".\Logs\KLEB"
                BackupDirectory         =   ".\Backup\KLEB"
            }
            
            Settings = @{
                EnableDebugLogging = $false
                ConnectionTimeout = 240
                MaxRetries = 3
                ValidateCertificates = $true
                ProcessInBatches = $true
                BatchSize = 75
            }
        }
        
        OT = @{
            vCenterServer               =   "vcsa-ot.corp.local"
            SSODomain                   =   "DLA-DaytonOT.local"
            DefaultCredentialUser       =   "administrator@vsphere.local"
            
            TagCategories = @{
                App                     =   "vCenter-OT-App-team"
                Function                =   "vCenter-OT-Function"
                OS                      =   "vCenter-OT-OS"
            }
            
            DataPaths = @{
                AppPermissionsCSV       =   ".\Data\OT\App-Permissions-OT.csv"
                OSMappingCSV            =   ".\Data\OT\OS-Mappings-OT.csv"
                LogDirectory            =   ".\Logs\OT"
                BackupDirectory         =   ".\Backup\OT"
            }
            
            Settings = @{
                EnableDebugLogging = $false
                ConnectionTimeout = 240
                MaxRetries = 3
                ValidateCertificates = $true
                ProcessInBatches = $true
                BatchSize = 50
            }
        }
    }
    
    # Global Default Paths - Use relative paths that work with script location
    DefaultPaths = @{
        TempDirectory               =   ".\Temp"
        PowerShell7Path             =   "C:\Program Files\PowerShell\7\pwsh.exe"
        MainScriptPath              =   ".\Scripts\set-VMtagPermissions_v1.2.ps1"
        ConfigDirectory             =   ".\ConfigFiles"
        CredentialStorePath         =   ".\ConfigFiles\Credentials"
        ModulePath                  =   ".\Modules"
    }
    
    # CSV Structure Validation
    CSVValidation = @{
        AppPermissions = @{
            RequiredColumns = @('TagCategory', 'TagName', 'RoleName', 'SecurityGroupDomain', 'SecurityGroupName')
            OptionalColumns = @('Description', 'CreatedBy', 'CreatedDate', 'LastModified')
            MaxRows = 1000
            AllowEmptyValues = $false
        }
        
        OSMapping = @{
            RequiredColumns = @('GuestOSPattern', 'TargetTagName', 'RoleName', 'SecurityGroupDomain', 'SecurityGroupName')
            OptionalColumns = @('Description', 'Priority', 'IsActive', 'LastTested')
            MaxRows = 500
            AllowEmptyValues = $false
        }
    }
    
    # PowerShell 7 Execution Settings
    PowerShell7 = @{
        ExecutionPolicy = "Bypass"
        TimeoutMinutes = 60
        WorkingDirectory = "."
        
        StandardArguments = @(
            "-NoProfile"
            "-NonInteractive"
        )
        
        DebugArguments = @(
            "-Verbose"
        )
        
        MemoryLimitMB = 2048
        MaxConcurrentJobs = 4
    }
    
    # Logging Configuration
    Logging = @{
        DefaultLevel = "INFO"
        DebugLevel = "DEBUG"
        
        # Log retention settings
        MaxLogFiles = 5              # Keep only 5 most recent files per type
        MaxLogSizeMB = 100          # Warn when log directory exceeds this size
        LogRetentionDays = 90       # For reference (used in warnings)
        AutoCleanupOnStart = $true  # Automatically clean logs when launcher starts
        
        # Log format settings
        TimestampFormat = "yyyy-MM-dd HH:mm:ss"
        LogFileFormat = "VMTags_{Environment}_{0:yyyyMMdd_HHmmss}.log"
        
        # What to log
        LogLevels = @{
            Console = @("INFO", "WARNING", "ERROR", "SUCCESS")
            File = @("DEBUG", "INFO", "WARNING", "ERROR", "SUCCESS")
            EventLog = @("ERROR")
        }
        
        # Event log settings (for Windows Event Log)
        EventLog = @{
            LogName = "Application"
            Source = "VMTagsAutomation"
        }
    }
    
    # Enhanced Security Settings
    Security = @{
        # Credential storage settings
        CredentialStorePath = ".\ConfigFiles\Credentials"  # Updated to use relative path
        StoredCredentialMaxAgeDays = 30
        ValidateStoredCredentials = $true
        AutoStoreCredentials = $false
        AutoCleanupExpiredCredentials = $true
        CredentialTimeoutMinutes = 60
        EncryptionAlgorithm = "AES256"
        UseSecureString = $true
        
        # Environment-specific credential policies
        EnvironmentPolicies = @{
            DEV = @{
                AllowStoredCredentials = $true
                AutoStoreCredentials = $true    # More relaxed for dev
                CredentialMaxAgeDays = 60       # Longer expiry for dev
                ValidateCredentials = $false    # Skip validation in dev for speed
            }
            PROD = @{
                AllowStoredCredentials = $true
                AutoStoreCredentials = $false   # Manual approval for prod
                CredentialMaxAgeDays = 14       # Shorter expiry for prod security
                ValidateCredentials = $true     # Always validate in prod
                RequireCredentialRotation = $true
                AlertOnCredentialExpiry = $true
            }
            KLEB = @{
                AllowStoredCredentials = $true
                AutoStoreCredentials = $false
                CredentialMaxAgeDays = 21
                ValidateCredentials = $true
            }
            OT = @{
                AllowStoredCredentials = $false  # Most restrictive for OT environment
                AutoStoreCredentials = $false
                CredentialMaxAgeDays = 7
                ValidateCredentials = $true
                RequireInteractiveAuth = $true   # Always prompt for OT
            }
        }
        
        # File security settings
        FilePermissions = @{
            CredentialFiles = @{
                Owner = "CurrentUser"
                Permissions = "FullControl"
                InheritanceFlags = "None"
                RemoveInheritance = $true
            }
            LogFiles = @{
                Owner = "CurrentUser"
                Permissions = @("FullControl", "ReadAndExecute")
                AllowedGroups = @("Administrators", "SYSTEM")
            }
        }
        
        # Audit and compliance settings
        Auditing = @{
            LogCredentialAccess = $true
            LogCredentialCreation = $true
            LogCredentialDeletion = $true
            LogFailedAuthentication = $true
            AlertOnMultipleFailures = $true
            MaxFailedAttempts = 3
        }
    }
    
    # VMware PowerCLI Settings
    PowerCLI = @{
        RequiredVersion = "12.0.0"
        ModulesToImport = @(
            "VMware.PowerCLI"
            "VMware.VimAutomation.Core"
            "VMware.VimAutomation.Vds"
            "VMware.VimAutomation.Cis.Core"
            "VMware.VimAutomation.Common"
        )
        
        Configuration = @{
            InvalidCertificateAction = "Ignore"
            ParticipateInCEIP = $false
            Scope = "Session"
            ProxyPolicy = "UseSystemProxy"
            DefaultVIServerMode = "Single"
        }
        
        ConnectionSettings = @{
            WebOperationTimeoutSeconds = 300
            VMConsoleWindowBrowser = $null
            MaxConcurrentConnections = 5
            ConnectionPoolSize = 10
        }
        
        # Environment-specific PowerCLI settings
        EnvironmentSettings = @{
            DEV = @{
                InvalidCertificateAction = "Ignore"
                ValidationTimeout = 30
            }
            PROD = @{
                InvalidCertificateAction = "Warn"  # More secure for prod
                ValidationTimeout = 60
                RequireCertificateValidation = $true
            }
            KLEB = @{
                InvalidCertificateAction = "Warn"
                ValidationTimeout = 45
            }
            OT = @{
                InvalidCertificateAction = "Fail"  # Most restrictive for OT
                ValidationTimeout = 90
                RequireCertificateValidation = $true
                RequireSecureConnection = $true
            }
        }
    }
    
    # Feature Flags - New section for controlling functionality
    FeatureFlags = @{
        EnableCredentialStorage = $true
        EnableCredentialValidation = $true
        EnableAutomaticBackup = $true
        EnablePerformanceMonitoring = $true
        EnableDetailedAuditing = $true
        EnableParallelProcessing = $true
        EnableCustomModuleLoading = $true
        EnableNetworkConnectivityTests = $true
    }
    
    # Notification Settings - New section for alerts and notifications
    Notifications = @{
        Email = @{
            Enabled = $false
            SMTPServer = "smtp.dla.mil"
            Port = 587
            UseSSL = $true
            From = "vmtags-automation@dla.mil"
            Recipients = @{
                Errors = @("infrastructure@company.com")
                Warnings = @("vmware-admins@company.com")
                Success = @()  # No notifications for success
            }
        }
        
        EventLog = @{
            Enabled = $true
            LogName = "Application"
            Source = "VMTagsAutomation"
            Categories = @{
                Error = 1
                Warning = 2
                Information = 3
                SuccessAudit = 4
                FailureAudit = 5
            }
        }
        
        SIEM = @{
            Enabled = $false
            SyslogServer = "siem.dla.mil"
            Port = 514
            Protocol = "UDP"
            Facility = "Local0"
        }
    }
}