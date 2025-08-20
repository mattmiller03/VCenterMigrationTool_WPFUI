using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Messages;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.ViewModels.Settings;
using Wpf.Ui.Abstractions.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace VCenterMigrationTool.ViewModels
    {
    public partial class SettingsViewModel : ObservableObject, INavigationAware
        {
        [ObservableProperty]
        private ObservableCollection<SettingsCategory> _settingsCategories = new();

        [ObservableProperty]
        private SettingsCategory? _selectedCategory;

        [ObservableProperty]
        private object? _selectedContent;

        public SettingsViewModel (IServiceProvider serviceProvider, IMessenger messenger)
            {
            // Create instances of all our settings ViewModels using the service provider
            var appearanceViewModel = serviceProvider.GetRequiredService<AppearanceSettingsViewModel>();
            var viewProfilesViewModel = serviceProvider.GetRequiredService<ViewProfilesViewModel>();
            var profileEditorViewModel = serviceProvider.GetRequiredService<ProfileEditorViewModel>();
            var powerShellViewModel = serviceProvider.GetRequiredService<PowerShellSettingsViewModel>();
            var filePathsViewModel = serviceProvider.GetRequiredService<FilePathsSettingsViewModel>();

            // Define the categories and assign their content (the ViewModels)
            var appearanceCategory = new SettingsCategory { Name = "Appearance", Content = appearanceViewModel };
            var powerShellCategory = new SettingsCategory { Name = "PowerShell", Content = powerShellViewModel };
            var filePathsCategory = new SettingsCategory { Name = "File Paths", Content = filePathsViewModel };
            var viewProfilesCategory = new SettingsCategory { Name = "View Profiles", Content = viewProfilesViewModel };
            var addEditProfileCategory = new SettingsCategory { Name = "Add/Edit Profile", Content = profileEditorViewModel };

            // Build the final TreeView structure
            var applicationParentCategory = new SettingsCategory
                {
                Name = "Application",
                SubCategories = { appearanceCategory }
                };
            var generalParentCategory = new SettingsCategory
                {
                Name = "General",
                SubCategories = { powerShellCategory, filePathsCategory }
                };
            var profilesParentCategory = new SettingsCategory
                {
                Name = "Connection Profiles",
                SubCategories = { viewProfilesCategory, addEditProfileCategory }
                };

            SettingsCategories.Add(applicationParentCategory);
            SettingsCategories.Add(generalParentCategory);
            SettingsCategories.Add(profilesParentCategory);

            // Set the default selection when the page loads
            SelectedCategory = appearanceCategory;

            // Register to receive the message to switch to the profile editor
            messenger.Register<EditProfileMessage>(this, (recipient, message) =>
            {
                profileEditorViewModel.LoadProfileForEditing(message.ProfileToEdit);

                // Find the "Add/Edit Profile" category and select it
                SelectedCategory = SettingsCategories
                    .SelectMany(c => c.SubCategories)
                    .FirstOrDefault(sc => sc.Name == "Add/Edit Profile");
            });
            }

        partial void OnSelectedCategoryChanged (SettingsCategory? value)
            {
            if (value is not null)
                {
                SelectedContent = value.Content ?? $"Placeholder for {value.Name} settings.";
                }
            }

        public async Task OnNavigatedToAsync ()
            {
            // This ensures the prerequisite check runs automatically when the settings page is loaded.
            var powerShellViewModel = SettingsCategories
                .SelectMany(c => c.SubCategories)
                .FirstOrDefault(sc => sc.Content is PowerShellSettingsViewModel)?
                .Content as PowerShellSettingsViewModel;

            if (powerShellViewModel is not null)
                {
                await powerShellViewModel.InitializeAsync();
                }
            }

        public async Task OnNavigatedFromAsync () => await Task.CompletedTask;
        }
    }