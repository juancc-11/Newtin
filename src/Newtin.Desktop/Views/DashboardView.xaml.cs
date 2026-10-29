using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Newtin.Desktop.Services;

namespace Newtin.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView(IReadOnlyList<ModuleInfo> modules, Action<string> navigate)
    {
        InitializeComponent();

        foreach (var module in modules)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = module.Number.ToString("00"),
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("BrandGradientBrush")
            });
            content.Children.Add(new TextBlock
            {
                Text = module.Title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                Margin = new Thickness(0, 6, 0, 4),
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = module.Description,
                FontSize = 12.5,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap
            });

            var key = module.Key;
            var button = new Button
            {
                Content = content,
                Style = (Style)FindResource("CardButtonStyle"),
                Width = 250,
                MinHeight = 140,
                Margin = new Thickness(0, 0, 16, 16)
            };
            AutomationProperties.SetName(button, module.Title);
            button.Click += (_, _) => navigate(key);
            CardsPanel.Children.Add(button);
        }
    }
}