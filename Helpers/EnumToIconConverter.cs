// In Helpers/EnumToIconConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.Helpers;

// First, let's define an enum for connection states as an example
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed
}

public class EnumToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Enum enumValue)
            return SymbolRegular.ErrorCircle24;

        // Example for ConnectionState
        if (enumValue is ConnectionState state)
        {
            return state switch
            {
                ConnectionState.Disconnected => SymbolRegular.PlugDisconnected24,
                ConnectionState.Connecting => SymbolRegular.ArrowClockwise24, // Represents "in progress"
                ConnectionState.Connected => SymbolRegular.CheckmarkCircle24,
                ConnectionState.Failed => SymbolRegular.ErrorCircle24,
                _ => SymbolRegular.Question24,
            };
        }

        // You can add more 'if (enumValue is AnotherEnumType)' blocks here for other enums

        return SymbolRegular.Question24; // Default icon
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}