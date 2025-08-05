// In ViewModels/ViewModel.cs

using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.ViewModels
{
    /// <summary>
    /// A base class for ViewModels that need to be aware of navigation events.
    /// </summary>
    public abstract partial class ViewModel : ObservableObject, INavigationAware
    {
        /// <summary>
        /// This method is called when the page is navigated to.
        /// </summary>
        public virtual async Task OnNavigatedToAsync()
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// This method is called when the page is navigated away from.
        /// </summary>
        public virtual async Task OnNavigatedFromAsync()
        {
            await Task.CompletedTask;
        }
    }
}