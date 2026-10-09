using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth;
using ShortP2P.Client.ProfileBackup;
using TorgLink.Localization;

namespace TorgLink.WinForms;

public sealed partial class LoginForm : AppForm
{
    private readonly AuthService _auth = null!;
    private readonly IServiceProvider _services = null!;
    private readonly ILogger<LoginForm> _logger = null!;

    public LoginForm()
    {
        InitializeComponent();
    }

    public LoginForm(AuthService auth, IServiceProvider services, ILogger<LoginForm> logger)
        : this()
    {
        _auth = auth;
        _services = services;
        _logger = logger;
        _login.Click += async (_, _) => await OnLoginAsync().ConfigureAwait(true);
        _register.Click += OnRegister;
        _import.Click += async (_, _) => await OnImportAsync().ConfigureAwait(true);
        ApplyLocalizedUi();
        Load += async (_, _) =>
        {
            if (await _auth.TryRestoreSessionAsync().ConfigureAwait(true) && _auth.CurrentUser != null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
    }

    protected override void ApplyLocalizedUi()
    {
        Text = "TorgLink — " + LocalizationUtils.GetStringByKey("login.sign_in");
        _lblNick.Text = LocalizationUtils.GetStringByKey("login.nick");
        _lblPass.Text = LocalizationUtils.GetStringByKey("login.password");
        _login.Text = LocalizationUtils.GetStringByKey("login.sign_in");
        _register.Text = LocalizationUtils.GetStringByKey("register.header");
        _import.Text = LocalizationUtils.GetStringByKey("login.import_profile");
        _exit.Text = LocalizationUtils.GetStringByKey("close");
    }

    private async Task OnLoginAsync()
    {
        var (ok, err) = await _auth.LoginAsync(_nick.Text.Trim(), _pass.Text ?? "").ConfigureAwait(true);
        if (!ok)
        {
            _logger.LogWarning("Login failed: {Reason}", err);
            MessageBox.Show(this, err ?? LocalizationUtils.GetStringByKey("login.failed"),
                LocalizationUtils.GetStringByKey("login.sign_in"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnRegister(object? sender, EventArgs e)
    {
        using var form = _services.GetRequiredService<RegisterForm>();
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private async Task OnImportAsync()
    {
        var backup = _services.GetRequiredService<ProfileBackupService>();
        if (!await ProfileFileShare.ImportProfileAsync(this, _auth, backup, _logger).ConfigureAwait(true))
            return;

        // DialogResult.OK lets Program.cs run the usual post-login bootstrap
        // (LAN scanner + MessengerServersBootstrap) for the restored user.
        DialogResult = DialogResult.OK;
        Close();
    }
}
