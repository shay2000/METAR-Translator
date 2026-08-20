using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace MetarViewer.Helpers;

/// <summary>
/// Converts a string to a boolean value. Returns true if the string is not null or empty.
/// </summary>
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return !string.IsNullOrEmpty(value as string);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a null value to <see cref="Visibility.Collapsed"/> and a non-null value to <see cref="Visibility.Visible"/>.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a boolean value to <see cref="Visibility"/>. 
/// True maps to <see cref="Visibility.Visible"/>, False maps to <see cref="Visibility.Collapsed"/>.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a <see cref="DateTime"/> object to a formatted string.
/// </summary>
public class DateTimeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime dateTime)
        {
            // Format: 31 Mar 2024 12:00 UTC
            return dateTime.ToString("dd MMM yyyy HH:mm") + " UTC";
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
