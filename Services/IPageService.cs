using System.Windows.Controls;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Defines a service that provides pages to the navigation system.
/// </summary>
public interface IPageService
{
    /// <summary>
    /// Gets a page of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of the page to get.</typeparam>
    /// <returns>An instance of the specified page type.</returns>
    public Page GetPage<T> () where T : class;
}