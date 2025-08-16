using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VCenterMigrationTool.Helpers;

/// <summary>
/// Attached behavior to enable global mouse wheel scrolling at the window level
/// </summary>
public static class WindowScrollBehavior
    {
    public static readonly DependencyProperty EnableGlobalScrollProperty =
        DependencyProperty.RegisterAttached(
            "EnableGlobalScroll",
            typeof(bool),
            typeof(WindowScrollBehavior),
            new PropertyMetadata(false, OnEnableGlobalScrollChanged));

    public static void SetEnableGlobalScroll (DependencyObject obj, bool value)
        {
        obj.SetValue(EnableGlobalScrollProperty, value);
        }

    public static bool GetEnableGlobalScroll (DependencyObject obj)
        {
        return (bool)obj.GetValue(EnableGlobalScrollProperty);
        }

    private static void OnEnableGlobalScrollChanged (DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
        if (d is UIElement element)
            {
            if ((bool)e.NewValue)
                {
                element.PreviewMouseWheel += Element_PreviewMouseWheel;
                }
            else
                {
                element.PreviewMouseWheel -= Element_PreviewMouseWheel;
                }
            }
        }

    private static void Element_PreviewMouseWheel (object sender, MouseWheelEventArgs e)
        {
        // Find the ScrollViewer under the mouse cursor
        var element = e.OriginalSource as DependencyObject;

        // Traverse up the visual tree to find a ScrollViewer
        while (element != null)
            {
            if (element is ScrollViewer scrollViewer && scrollViewer.IsVisible)
                {
                // Check if the ScrollViewer can actually scroll vertically
                if (scrollViewer.ExtentHeight > scrollViewer.ViewportHeight)
                    {
                    double scrollAmount = e.Delta / 120.0 * 50; // 50 pixels per scroll notch
                    double newOffset = scrollViewer.VerticalOffset - scrollAmount;

                    // Clamp the offset to valid range
                    newOffset = Math.Max(0, Math.Min(newOffset, scrollViewer.ScrollableHeight));

                    scrollViewer.ScrollToVerticalOffset(newOffset);
                    e.Handled = true;
                    return;
                    }
                }

            try
                {
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
                }
            catch
                {
                // Sometimes GetParent can throw exceptions, so we'll break out safely
                break;
                }
            }
        }
    }