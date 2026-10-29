using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using Newtin.Core.Models;
using Newtin.Core.Validation;

namespace Newtin.Desktop.Controls;

public partial class ShareConnectionForm : UserControl
{
    public ShareConnectionForm()
    {
        InitializeComponent();
        UpdateMode();
        UpdatePreview();
    }

    public void Load(AppSettings settings)
    {
        HostBox.Text = settings.Network.RemoteHost;
        ShareBox.Text = settings.Network.RemoteShare;
        UserBox.Text = settings.Network.RemoteUser;
        UpdatePreview();
    }

    /// <summary>Quien reciba la solicitud debe liberarla con Dispose para borrar la contraseña de la memoria.</summary>
    public bool TryGetRequest([NotNullWhen(true)] out ShareRequest? request, out string error)
    {
        request = null;

        if (!UncPathBuilder.TryBuild(HostBox.Text, ShareBox.Text, out _, out error))
            return false;

        var host = HostBox.Text.Trim();
        var share = ShareBox.Text.Trim();

        if (CredRadio.IsChecked != true)
        {
            request = new ShareRequest(host, share, AuthMode.CurrentSession);
            return true;
        }

        var user = UserBox.Text.Trim();
        if (user.Length == 0)
        {
            error = "Escribe el nombre de usuario.";
            return false;
        }

        var password = PassBox.SecurePassword;
        if (password.Length == 0)
        {
            password.Dispose();
            error = "Escribe la contraseña.";
            return false;
        }

        request = new ShareRequest(host, share, AuthMode.Credentials, user, password);
        return true;
    }

    public void ClearPassword() => PassBox.Clear();

    private void Field_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void Mode_Changed(object sender, RoutedEventArgs e) => UpdateMode();

    private void UpdatePreview()
    {
        if (UncPreview is null) return;

        UncPreview.Text = UncPathBuilder.TryBuild(HostBox.Text, ShareBox.Text, out var unc, out _)
            ? $"Ruta resultante: {unc}"
            : "Ruta resultante: (completa el equipo remoto)";
    }

    private void UpdateMode()
    {
        if (CredPanel is null) return;
        CredPanel.Visibility = CredRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }
}
