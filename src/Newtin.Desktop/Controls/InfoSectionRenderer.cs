using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Newtin.Core.Models;

namespace Newtin.Desktop.Controls;

public static class InfoSectionRenderer
{
    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private static Brush ValueBrush(InfoItem item) => item.Level switch
    {
        InfoLevel.Success => Res("SuccessBrush"),
        InfoLevel.Warning => Res("WarningBrush"),
        InfoLevel.Error => Res("ErrorBrush"),
        _ => item.Value == InfoItem.NotAvailable ? Res("WarningBrush") : Res("TextPrimaryBrush")
    };

    public static UIElement Build(InfoSection section)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        foreach (var item in section.Items)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Children.Add(new TextBlock
            {
                Text = item.Label,
                FontSize = 13.5,
                Foreground = Res("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 12, 0)
            });

            var value = new StackPanel();
            Grid.SetColumn(value, 1);

            value.Children.Add(new TextBlock
            {
                Text = item.Value,
                FontSize = 13.5,
                Foreground = ValueBrush(item),
                TextWrapping = TextWrapping.Wrap
            });

            if (item.Percent is double percent)
            {
                var bar = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = percent,
                    Height = 8,
                    Margin = new Thickness(0, 6, 0, 0),
                    BorderThickness = new Thickness(0),
                    Foreground = Res("BrandGradientBrush"),
                    Background = Res("LineBrush")
                };
                AutomationProperties.SetName(bar, $"{item.Label}: {percent}%");
                value.Children.Add(bar);
            }

            if (!string.IsNullOrWhiteSpace(item.Note))
            {
                value.Children.Add(new TextBlock
                {
                    Text = item.Note,
                    FontSize = 12,
                    Foreground = Res("TextSecondaryBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }

            row.Children.Add(value);
            stack.Children.Add(row);
        }

        return new Border
        {
            Background = Res("SurfaceBrush"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 16),
            Child = stack
        };
    }
}