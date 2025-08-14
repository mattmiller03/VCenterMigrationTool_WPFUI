using System;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace VCenterMigrationTool.Services;

/// <summary>
/// A service that provides page instances.
/// </summary>
public class PageService : IPageService
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to use for resolving pages.</param>
    public PageService (IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets a page of the specified type from the service provider.
    /// </summary>
    /// <typeparam name="T">The type of the page to get.</typeparam>
    /// <returns>An instance of the specified page type.</returns>
    public Page GetPage<T> () where T : class
        {
        var page = _serviceProvider.GetRequiredService<T>() as Page;
        if (page is null)
            throw new InvalidOperationException($"The requested service of type '{typeof(T).FullName}' is not a Page.");
        return page;
        }
}