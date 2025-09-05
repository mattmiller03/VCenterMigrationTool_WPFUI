@(
    "C:\DLA-Failsafe\vRA\MattM\Workflow\VMTags\Data\DEV",
    "C:\DLA-Failsafe\vRA\MattM\Workflow\VMTags\Data\PROD", 
    "C:\DLA-Failsafe\vRA\MattM\Workflow\VMTags\Data\KLEB",
    "C:\DLA-Failsafe\vRA\MattM\Workflow\VMTags\Data\OT",
    "C:\DLA-Failsafe\vRA\MattM\Workflow\VMTags\Logs\DEV",
    "C:\DLA-Failsafe\vRA\MattM\Workflow\VMTags\Logs\PROD",
    "C:\DLA-Failsafe\vRA\MattM\Workflow\VMTags\Logs\KLEB", 
    "C:\DLA-Failsafe\vRA\MattM\Workflow\VMTags\Logs\OT",
    "C:\DLA-Failsafe\vRA\MattM\Workflow\VMTags\Backup\DEV",
    "C:\DLA-Failsafe\vRA\MattM\Workflow\VMTags\Backup\PROD",
    "C:\DLA-Failsafe\vRA\MattM\Workflow\VMTags\Backup\KLEB",
    "C:\DLA-Failsafe\vRA\MattM\Workflow\VMTags\Backup\OT",
    "C:\Temp\VMTags"
) | ForEach-Object { 
    if (-not (Test-Path $_)) { 
        New-Item -Path $_ -ItemType Directory -Force | Out-Null
        Write-Host "Created: $_" -ForegroundColor Green
    }
}