using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace S7WpfApp.Converters;

/// <summary>
/// Bool 转颜色（连接状态等）：连接=运行绿，断开=未激活灰
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // 运行绿
        return new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)); // 未激活灰
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 反转 Bool
/// </summary>
public class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>
/// 字符串非空转 Bool
/// </summary>
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Int > 0 转 Bool（用于列表非空判断）
/// </summary>
public class IntToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i > 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Int == 0 转 Visibility（空列表显示提示）
/// </summary>
public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Bool 转 Visibility
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>
/// 反转 Bool 转 Visibility
/// </summary>
public class InvertBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Bool 转状态颜色（伺服使能等）：启用=运行绿，未启用=未激活灰
/// </summary>
public class BoolToStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // 运行绿
        return new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)); // 未激活灰
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Bool 转 FontWeight（数组父节点加粗）
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? FontWeights.Bold : FontWeights.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 绑定类型 → 卡片顶部色块颜色
/// 点动按钮=绿、保持按钮=蓝、数值输入=橙、状态指示=灰、轴控=紫
/// </summary>
public class BindingTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.BindingType type)
        {
            return type switch
            {
                Models.BindingType.MomentaryButton => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), // 绿
                Models.BindingType.ToggleButton => new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)),    // 蓝
                Models.BindingType.NumericInput => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),     // 橙
                _ => new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E))                                    // 灰
            };
        }
        // 特殊：AxisControl 类型（通过 parameter 传入 DataType 字符串判断）
        if (value is string dt && dt == "AxisControl")
            return new SolidColorBrush(Color.FromRgb(0x7B, 0x1F, 0xA2)); // 紫
        return new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 绑定类型 → 类型显示图标
/// </summary>
public class BindingTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.BindingType type)
        {
            return type switch
            {
                Models.BindingType.MomentaryButton => "👆",
                Models.BindingType.ToggleButton => "🔘",
                Models.BindingType.NumericInput => "✏️",
                Models.BindingType.StatusIndicator => "📊",
                _ => "⚙️"
            };
        }
        return "⚙️";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
