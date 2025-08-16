using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VCenterMigrationTool.Helpers;

/// <summary>
/// Attached behavior to fix mouse wheel scrolling issues
/// </summary>
public static class ScrollBehavior
    {
    public static readonly DependencyProperty FixMouseWheelProperty =
        DependencyProperty.RegisterAttached(
            "FixMouseWheel",
            typeof(bool),
            typeof(ScrollBehavior),
            new PropertyMetadata(false, OnFixMouseWheelChanged));

    public static void SetFixMouseWheel (DependencyObject obj, bool value)
        {
        obj.SetValue(FixMouseWheelProperty, value);
        }

    public static bool GetFixMouseWheel (DependencyObject obj)
        {
        return (bool)obj.GetValue(FixMouseWheelProperty);
        }

    private static void OnFixMouseWheelChanged (DependencyObject d, DependencyPropertyChangedEventArgs e)
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
        if (sender is UIElement element)
            {
            // Find the ScrollViewer in the visual tree
            var scrollViewer = FindScrollViewer(element);

            if (scrollViewer != null)
                {
                // Calculate scroll amount (120 is the standard delta for one "notch" of scroll)
                double scrollAmount = e.Delta / 120.0 * 50; // 50 pixels per scroll notch

                // Scroll vertically
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollAmount);

                // Mark the event as handled
                e.Handled = true;
                }
            }
        }

    private static ScrollViewer? FindScrollViewer (DependencyObject element)
        {
        if (element is ScrollViewer scrollViewer)
            return scrollViewer;

        // Search children
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(element, i);
            var result = FindScrollViewer(child);
            if (result != null)
                return result;
            }

        return null;
        }
    }