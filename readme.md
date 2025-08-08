vCenter Migration Tool v1.0

1. Introduction

This document outlines the structure and a development guide for the vCenter Migration Tool, a WPF application designed to assist with migrating a VMware vCenter environment. The application provides a modern, intuitive graphical user interface (GUI) for executing complex PowerShell and PowerCLI operations. The project is currently at version 1.0, with a solid architectural foundation and several core features implemented.

2. Core Features (Current Status)

    Modern Fluent UI: Built using the WPF-UI library, providing a modern look and feel with built-in theme support (Light/Dark).

    MVVM Architecture: Strictly follows the Model-View-ViewModel pattern for a clean separation of concerns.

    Dependency Injection: Utilizes the .NET Generic Host to manage services and viewmodels, ensuring a decoupled and testable codebase.

    Persistent Connection Profiles:

        Users can add, edit, and delete vCenter connection profiles (name, server address, username).

        Passwords are encrypted using the current user's Windows credentials (ProtectedData) and stored in a profiles.json file in the user's %LocalAppData%\VCenterMigrationTool folder.

        A central ConnectionProfileService manages all profile data.

    Dashboard:

        Displays connection status for a selected "Source" and "Target" vCenter.

        Allows users to select from saved profiles to establish connections.

        Features a "Current Job Status" area with a progress bar to monitor long-running tasks.

    Multi-Page Navigation: A fluent navigation menu provides access to different functional areas of the application.

    Modular Migration Pages:

        vCenter Object Migration: A dedicated page with checkboxes for selecting high-level objects to migrate (Roles, Folders, Tags). Includes granular, cluster-specific selection for Resource Pools and vDS via a ComboBox and ListView.

        ESXi Host Migration: A page featuring a TreeView to display the source vCenter's cluster/host topology, allowing for multi-host selection. Users can select a target cluster from a ComboBox to initiate migration.

        VM Migration: A page with a DataGrid to display a detailed inventory of VMs from the source. Users can select multiple VMs, a target host, and a target datastore for migration.

    PowerShell Integration:

        A robust PowerShellService can execute any .ps1 script.

        Supports passing complex parameters to scripts.

        Can return simple string output for logs or structured JSON objects that are automatically deserialized into C# models.

    Logging: Integrated Serilog for logging to both the Visual Studio Debug window and rolling daily log files, which is essential for diagnosing PowerShell script failures.

3. Technology Stack

    Framework: .NET 8

    UI: Windows Presentation Foundation (WPF)

    UI Library: WPF-UI (Fluent) v4.0.3

    Architecture: Model-View-ViewModel (MVVM)

    MVVM Toolkit: CommunityToolkit.Mvvm for source-generated observable properties and commands.

    Dependency Injection: Microsoft.Extensions.Hosting

    Logging: Serilog

    Backend Logic: PowerShell 7+ with the Microsoft.PowerShell.SDK

4. Project Structure

The project follows a standard MVVM structure:

    /Models: Contains the C# classes that represent data (e.g., VCenterConnection.cs, MigrationTask.cs).

    /Views:

        /Pages: Contains the XAML files for individual pages (DashboardPage.xaml, SettingsPage.xaml, etc.).

        /Windows: Contains the main application shell (MainWindow.xaml).

    /ViewModels: Contains the C# classes that hold the logic and data for each View (e.g., DashboardViewModel.cs, SettingsViewModel.cs).

    /Services: Contains backend services that perform specific tasks.

        PowerShellService.cs: Executes PowerShell scripts.

        ConnectionProfileService.cs: Manages loading and saving connection profiles.

        ApplicationHostService.cs: Manages the application startup and initial navigation.

    /Helpers: Contains value converters and other helper classes (BoolToVisibilityConverter.cs, etc.).

    /Scripts: Contains all .ps1 files used by the application. Crucially, all scripts must have their "Copy to Output Directory" property set to "Copy if newer".

5. Key Development Patterns

To ensure consistency, please follow these established patterns when adding new features:

Creating a New Page

    Create the Model(s): In the /Models folder, create the C# classes needed to represent the data for the page.

    Create the PowerShell Script(s): In the /Scripts folder, create the .ps1 files. Scripts that return data to be displayed should output a single JSON string using ConvertTo-Json.

    Create the ViewModel: In the /ViewModels folder, create a new public partial class that inherits from ObservableObject. Inject any required services (like PowerShellService) through the constructor.

    Create the View: In the /Views/Pages folder, create a new <Page>.

    Create the Code-Behind: Create the .xaml.cs file for the new page. The constructor must accept its ViewModel via dependency injection and set the DataContext.

    Register in App.xaml.cs: Register the new Page and ViewModel as singletons in the ConfigureServices method.

    Add to Navigation in MainWindowViewModel.cs: Add a new NavigationViewItem to the _menuItems collection to make the page accessible.

Data Binding in Pages

The established pattern for pages is:

    The code-behind's constructor receives the ViewModel via DI.

    The page's DataContext is set to the page itself (DataContext = this;).

    The ViewModel is exposed via a public property: public MyViewModel ViewModel { get; }.
    
    All XAML bindings must then use the ViewModel. prefix (e.g., Text="{Binding ViewModel.MyProperty}").

This pattern has proven stable across the application.

6. Current State & Next Steps

The application is stable and compiles without errors or warnings. The core UI structure is complete, and the main migration pages have been scaffolded with placeholder logic.

Immediate next steps for the next developer would be:

    Implement Real PowerShell Logic: Replace the Task.Delay simulations in the ViewModel commands (OnStartMigration, OnMigrateHosts, etc.) with actual calls to the PowerShellService that execute the migration scripts (Move-EsxiHost.ps1, etc.).

    Shared Connection Service: The ViewModels currently use placeholder connection profiles. A shared service should be created to hold the currently selected "Source" and "Target" profiles from the Dashboard, so other pages can access them.

    Implement Password Prompts: For connection tests or migrations where a password is not saved, implement a dialog box to securely prompt the user for the password at runtime.

    Complete the VM Migration Page: Build out the DataGrid on the VmMigrationPage with full functionality, including "select all" and potentially filtering/sorting.

    Build the Network Migration Page: The page has been designed; the next step is to implement the real PowerShell logic for migrating network components.