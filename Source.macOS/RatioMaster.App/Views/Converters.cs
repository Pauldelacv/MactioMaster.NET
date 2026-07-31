namespace RatioMaster.App.Views;

using Avalonia.Data.Converters;
using Avalonia.Media;

public static class Converters
{
    /// <summary>Green dot while a tab is announcing, grey while it is idle.</summary>
    public static readonly IValueConverter RunningBrush = new FuncValueConverter<bool, IBrush>(
        running => running ? Brushes.MediumSeaGreen : new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)));
}
