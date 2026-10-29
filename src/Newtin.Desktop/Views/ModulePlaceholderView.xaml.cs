using System.Windows;
using System.Windows.Controls;
using Newtin.Desktop.Services;

namespace Newtin.Desktop.Views;

public partial class ModulePlaceholderView : UserControl
{
    private readonly Action _goHome;

    public ModulePlaceholderView(ModuleInfo module, Action goHome)
    {
        InitializeComponent();
        _goHome = goHome;
        PhaseNote.Text = $"Esta herramienta se construirá en la {module.PhaseLabel}. " +
                         "La pantalla ya está lista para recibirla, pero todavía no realiza ninguna comprobación.";
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _goHome();
}