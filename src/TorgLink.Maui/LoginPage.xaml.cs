using TorgLink.Maui.Localization;
using TorgLink.Maui.Services;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth;
using ShortP2P.Client.ProfileBackup;

namespace TorgLink.Maui;

public partial class LoginPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ILogger<LoginPage> _logger;
    private readonly ProfileBackupService _profileBackup;

    public LoginPage(AuthService auth, ILogger<LoginPage> logger, ProfileBackupService profileBackup)
    {
        InitializeComponent();
        _auth = auth;
        _logger = logger;
        _profileBackup = profileBackup;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyLocalizedUi();
        if (await _auth.TryRestoreSessionAsync().ConfigureAwait(true) && _auth.CurrentUser != null)
            await GoToChatsAsync().ConfigureAwait(true);
    }

    private void ApplyLocalizedUi()
    {
        SubtitleLabel.Text = Loc.T("login.subtitle");
        NicknameEntry.Placeholder = Loc.T("login.nick");
        PasswordEntry.Placeholder = Loc.T("login.password");
        SignInButton.Text = Loc.T("login.sign_in");
        CreateAccountButton.Text = Loc.T("login.create_account");
        ImportProfileButton.Text = Loc.T("login.import_profile");
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        var nick = NicknameEntry.Text?.Trim() ?? "";
        var pass = PasswordEntry.Text ?? "";
        var (ok, err) = await _auth.LoginAsync(nick, pass).ConfigureAwait(true);
        if (!ok)
        {
            _logger.LogWarning("Login failed for {Nickname}: {Reason}", nick, err);
            await DisplayAlert(Loc.T("login.sign_in"), err ?? Loc.T("login.failed"), Loc.T("ok"))
                .ConfigureAwait(true);
            return;
        }

        AppLog.Ui.LogInformation("Login success for {Nickname}", nick);
        await GoToChatsAsync().ConfigureAwait(true);
    }

    private async void OnRegisterNavigateClicked(object? sender, EventArgs e)
    {
        var page = MauiProgram.Services.GetRequiredService<RegisterPage>();
        await Navigation.PushAsync(page).ConfigureAwait(true);
    }

    private async void OnImportProfileClicked(object? sender, EventArgs e)
    {
        var imported = await ProfileFileShare.ImportProfileAsync(this, _auth, _profileBackup, _logger)
            .ConfigureAwait(true);
        if (!imported)
            return;

        AppLog.Ui.LogInformation("Profile import success for {Nickname}", _auth.CurrentUser?.Nickname ?? "");
        await GoToChatsAsync().ConfigureAwait(true);
    }

    private Task GoToChatsAsync()
    {
        Application.Current!.MainPage = MauiProgram.Services.GetRequiredService<AppShell>();
        return Task.CompletedTask;
    }
}
