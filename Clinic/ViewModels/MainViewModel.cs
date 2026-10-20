using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Clinic.Data;
using Clinic.Preferences;
using Clinic.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clinic.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _login = string.Empty;
    [ObservableProperty]
    private string _password = string.Empty;
    [ObservableProperty]
    private string _statusMessage = string.Empty;
    [ObservableProperty]
    private bool _hasStatusMessage;
    [ObservableProperty]
    private bool _isSignedIn;
    [ObservableProperty]
    private bool _isBusy;
    [ObservableProperty]
    private string _apiAddress = Environment.GetEnvironmentVariable("CLINIC_API_URL") ?? "http://127.0.0.1:5278";
    [ObservableProperty]
    private WorkspaceViewModel? _workspace;
    [ObservableProperty]
    private bool _isRegistrationOpen;
    [ObservableProperty]
    private bool _isRegistrationDiscardOpen;
    [ObservableProperty]
    private string _registrationError = "";
    [ObservableProperty]
    private bool _isStatusSuccess;
    [ObservableProperty]
    private bool _revealPassword;
    [ObservableProperty]
    private string _captchaAnswer = "";
    [ObservableProperty]
    private string _captchaImageBase64 = "";
    [ObservableProperty]
    private bool _hasCaptcha;
    private string? _captchaId;
    private void ShowCaptcha(CaptchaChallenge challenge)
    {
        _captchaId = challenge.ChallengeId;
        CaptchaImageBase64 = challenge.ImageBase64;
        CaptchaAnswer = "";
        HasCaptcha = true;
    }

    private void ClearCaptcha()
    {
        _captchaId = null;
        CaptchaImageBase64 = "";
        CaptchaAnswer = "";
        HasCaptcha = false;
    }

    [RelayCommand]
    private async Task RefreshCaptchaAsync()
    {
        if (IsBusy || !HasCaptcha)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _prepareConnection();
            using var api = new HttpClinicGateway(ApiAddress);
            var challenge = await api.RefreshCaptchaAsync(Login);
            ShowCaptcha(challenge);
        }
        catch (Exception exception)
        {
            ShowError(WorkspaceViewModel.Friendly(exception));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [ObservableProperty]
    private bool _isThemeBusy;
    [ObservableProperty]
    private bool _isDarkTheme;
    [ObservableProperty]
    private string _themeToggleLabel = "Тёмная тема";
    [ObservableProperty]
    private string _themeError = string.Empty;
    [ObservableProperty]
    private string _displayName = string.Empty;
    [ObservableProperty]
    private string _role = string.Empty;
    [ObservableProperty]
    private string _initials = string.Empty;
    [ObservableProperty]
    private string _subtitle = string.Empty;
    [ObservableProperty]
    private string _today = DateTime.Today.ToString("dddd, d MMMM yyyy", new CultureInfo("ru-RU"));
    public bool IsSignedOut => !IsSignedIn;
    public bool HasThemeError => !string.IsNullOrEmpty(ThemeError);

    private string _themePreferenceKey = "signin";
    private IClinicGateway? _activeGateway;
    private int? _activeAccountId;
    private Dictionary<string, string> _registrationValues = [];
    private TaskCompletionSource<bool>? _registrationConfirmation;
    private TaskCompletionSource<bool>? _exitConfirmation;
    [ObservableProperty]
    private bool _isExitConfirmOpen;
    [ObservableProperty]
    private string _exitTitle = "";
    [ObservableProperty]
    private string _exitDescription = "";
    [ObservableProperty]
    private string _exitLabel = "";
    public Task<bool> ConfirmExitAsync(bool closeWindow = false)
    {
        if (_exitConfirmation is not null)
        {
            return Task.FromResult(false);
        }

        ExitTitle = closeWindow ? "Закрыть приложение?" : "Выйти из системы?";
        ExitDescription = closeWindow
            ? "Работа с поликлиникой будет завершена."
            : "Для продолжения потребуется снова ввести логин и пароль.";
        ExitLabel = closeWindow ? "Закрыть приложение" : "Выйти";
        _exitConfirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IsExitConfirmOpen = true;
        return _exitConfirmation.Task;
    }

    [RelayCommand]
    private void ConfirmExit()
    {
        FinishExit(true);
    }

    [RelayCommand]
    private void CancelExit()
    {
        FinishExit(false);
    }

    private void FinishExit(bool accepted)
    {
        IsExitConfirmOpen = false;
        var confirmation = _exitConfirmation;
        _exitConfirmation = null;
        confirmation?.TrySetResult(accepted);
    }

    public async Task<bool> ConfirmWindowCloseAsync()
    {
        if (IsBusy || Workspace?.IsBusy == true)
        {
            return false;
        }

        if (HasRegistrationChanges && !await ConfirmRegistrationDiscardAsync())
        {
            return false;
        }

        var workspace = Workspace;
        if (workspace is not null && workspace.HasUnsavedChanges && !await workspace.ConfirmDiscardAsync())
        {
            return false;
        }

        return await ConfirmExitAsync(true);
    }

    partial void OnIsExitConfirmOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
    }

    private readonly Func<IClinicGateway> _gatewayFactory;
    private readonly Func<Task> _prepareConnection;
    public MainViewModel(Func<IClinicGateway>? gatewayFactory = null, Func<Task>? prepareConnection = null)
    {
        _gatewayFactory = gatewayFactory ?? (() => new HttpClinicGateway(ApiAddress));
        _prepareConnection = prepareConnection ?? (() => Task.CompletedTask);
        ApplyTheme(ThemePreferenceStore.Get(_themePreferenceKey));
    }

    public ObservableCollection<EditorField> RegistrationFields { get; } = [];
    public bool CanInteract => !IsBusy && !IsRegistrationDiscardOpen && !IsExitConfirmOpen;
    public bool HasRegistrationChanges
    {
        get
        {
            if (!IsRegistrationOpen)
            {
                return false;
            }

            return RegistrationFields.Any(input =>
                _registrationValues.GetValueOrDefault(input.Spec.Key) != input.Value);
        }
    }

    public string SignInLabel => IsBusy ? "Входим…" : "Войти";
    public string RegistrationHint => "Заполните личные данные и полис ОМС, затем создайте логин и пароль.";

    private IClinicGateway CreateGateway()
    {
        var gateway = _gatewayFactory();
        if (gateway is HttpClinicGateway http)
        {
            http.SessionExpired += message => EndSession(message);
        }

        return gateway;
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsBusy || IsRegistrationOpen || IsSignedIn || IsExitConfirmOpen)
        {
            return;
        }

        if (HasCaptcha && string.IsNullOrWhiteSpace(CaptchaAnswer))
        {
            ShowError("Введите символы с картинки.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrWhiteSpace(Password))
        {
            ShowError("Введите логин и пароль, чтобы продолжить.");
            return;
        }

        IsBusy = true;
        IClinicGateway? gateway = null;
        try
        {
            await _prepareConnection();
            gateway = CreateGateway();
            if (gateway is HttpClinicGateway api)
            {
                api.SetCaptchaResponse(_captchaId, CaptchaAnswer);
            }

            var result = await gateway.SignInAsync(Login, Password);
            if (result is null)
            {
                ShowError("Неверный логин или пароль.");
                return;
            }

            var session = result.Value.Session;
            DisplayName = session.Name;
            Role = session.Role;
            Initials = InitialsFromName(session.Name);
            Subtitle = Catalog.RoleSubtitle(session.Role);
            var userTheme = await gateway.GetUserThemeAsync(session.AccountId);
            if (userTheme is null)
            {
                // Переносим ранее выбранную тему устройства в учётную запись.
                var previousTheme = ThemePreferenceStore.Get($"account:{session.AccountId}");
                if (previousTheme)
                {
                    await gateway.SaveUserThemeAsync(session.AccountId, true);
                }

                ApplyTheme(previousTheme);
            }
            else
            {
                ApplyTheme(userTheme.Value);
            }

            _activeGateway = gateway;
            _activeAccountId = session.AccountId;
            Workspace = new WorkspaceViewModel(this, gateway, session);
            await Workspace.InitializeAsync();
            if (Workspace is null || _activeGateway != gateway)
            {
                throw new SessionExpiredException("Сессия завершена. Войдите снова.");
            }

            IsSignedIn = true;
            ClearCaptcha();
            ClearStatus();
        }
        catch (CaptchaRequiredException exception)
        {
            ShowCaptcha(exception.Challenge);
            ShowError(exception.Message);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            ShowError(WorkspaceViewModel.Friendly(exception));
        }
        finally
        {
            if (!IsSignedIn)
            {
                if (gateway is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                Workspace = null;
                _activeGateway = null;
                _activeAccountId = null;
            }

            Password = string.Empty;
            RevealPassword = false;
            IsBusy = false;
        }
    }

    private static string InitialsFromName(string name)
    {
        var nameParts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(nameParts.Take(2).Select(part => part[0]));
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (IsBusy || Workspace?.IsBusy == true)
        {
            return;
        }

        var workspace = Workspace;
        if (workspace is not null && workspace.HasUnsavedChanges && !await workspace.ConfirmDiscardAsync())
        {
            return;
        }

        if (!await ConfirmExitAsync())
        {
            return;
        }

        if (_activeGateway is HttpClinicGateway api)
        {
            IsBusy = true;
            try
            {
                await api.SignOutAsync();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception);
            }
            finally
            {
                IsBusy = false;
            }
        }

        EndSession();
    }

    private void EndSession(string? message = null)
    {
        IsSignedIn = false;
        if (_activeGateway is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _activeGateway = null;
        _activeAccountId = null;
        Workspace = null;
        _themePreferenceKey = "signin";
        ApplyTheme(ThemePreferenceStore.Get(_themePreferenceKey));
        ThemeError = string.Empty;
        Login = string.Empty;
        Password = string.Empty;
        RevealPassword = false;
        DisplayName = string.Empty;
        Role = string.Empty;
        Initials = string.Empty;
        Subtitle = string.Empty;
        ClearStatus();
        ClearCaptcha();
        if (message is not null)
        {
            ShowError(message);
        }
    }

    [RelayCommand]
    private async Task OpenRegistrationAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            RegistrationFields.Clear();
            RegistrationError = "";
            await _prepareConnection();
            var gateway = CreateGateway();
            using var disposable = gateway as IDisposable;
            var api = gateway as HttpClinicGateway;
            var choices = new Dictionary<string, List<Choice>>();
            if (api is not null)
            {
                choices = await api.RegistrationChoicesAsync();
            }

            var fields = api?.FieldsFor("registration") ?? Catalog.Fields("registration");
            foreach (var field in fields)
            {
                List<Choice>? fieldChoices = null;
                if (field.Lookup is not null)
                {
                    fieldChoices = choices.GetValueOrDefault(field.Lookup);
                }

                var value = field.Default ?? "";
                RegistrationFields.Add(new EditorField(field, value, fieldChoices));
            }

            _registrationValues = RegistrationFields.ToDictionary(field => field.Spec.Key, field => field.Value);
            IsRegistrationOpen = true;
        }
        catch (Exception exception)
        {
            ShowError(WorkspaceViewModel.Friendly(exception));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CloseRegistrationAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (HasRegistrationChanges && !await ConfirmRegistrationDiscardAsync())
        {
            return;
        }

        IsRegistrationOpen = false;
        RegistrationFields.Clear();
        RegistrationError = "";
    }

    public Task<bool> ConfirmRegistrationDiscardAsync()
    {
        if (_registrationConfirmation is not null)
        {
            return Task.FromResult(false);
        }

        _registrationConfirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IsRegistrationDiscardOpen = true;
        return _registrationConfirmation.Task;
    }

    [RelayCommand]
    private void CancelRegistrationDiscard()
    {
        FinishRegistrationDiscard(false);
    }

    [RelayCommand]
    private void ConfirmRegistrationDiscard()
    {
        FinishRegistrationDiscard(true);
    }

    private void FinishRegistrationDiscard(bool confirmed)
    {
        IsRegistrationDiscardOpen = false;
        var pending = _registrationConfirmation;
        _registrationConfirmation = null;
        pending?.TrySetResult(confirmed);
    }

    partial void OnIsRegistrationDiscardOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _prepareConnection();
            IClinicGateway gateway = CreateGateway();
            using var disposable = gateway as IDisposable;
            FormValidation.Validate(RegistrationFields);
            var values = RegistrationFields.ToDictionary(field => field.Spec.Key, field => field.Value);
            await gateway.RegisterAsync(values);
            Login = values["login"];
            IsRegistrationOpen = false;
            RegistrationFields.Clear();
            ShowStatus("Учётная запись создана. Введите пароль для входа.", success: true);
        }
        catch (Exception exception)
        {
            RegistrationError = WorkspaceViewModel.Friendly(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        if (IsThemeBusy)
        {
            return;
        }

        ThemeError = string.Empty;
        var requestedTheme = !IsDarkTheme;
        if (_activeGateway is not null && _activeAccountId.HasValue)
        {
            var accountId = _activeAccountId.Value;
            IsThemeBusy = true;
            try
            {
                await _activeGateway.SaveUserThemeAsync(accountId, requestedTheme);
                ApplyTheme(requestedTheme);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception);
                ThemeError = "Не удалось сохранить тему. Повторите попытку.";
            }
            finally
            {
                IsThemeBusy = false;
            }

            return;
        }

        if (ThemePreferenceStore.Save(_themePreferenceKey, requestedTheme))
        {
            ApplyTheme(requestedTheme);
        }
        else
        {
            ThemeError = "Не удалось сохранить тему на этом устройстве.";
        }
    }

    private void ApplyTheme(bool dark)
    {
        IsDarkTheme = dark;
        ThemeToggleLabel = dark ? "Светлая тема" : "Тёмная тема";
        var app = Application.Current;
        if (app is not null)
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    partial void OnThemeErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasThemeError));
    }

    partial void OnIsSignedInChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSignedOut));
    }

    partial void OnLoginChanged(string value)
    {
        ClearStatus();
        ClearCaptcha();
    }

    partial void OnPasswordChanged(string value)
    {
        if (!IsBusy)
        {
            ClearStatus();
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(SignInLabel));
    }

    private void ShowError(string message)
    {
        ShowStatus(message);
    }

    private void ShowStatus(string message, bool success = false)
    {
        IsStatusSuccess = success;
        StatusMessage = message;
        HasStatusMessage = true;
    }

    private void ClearStatus()
    {
        HasStatusMessage = false;
        StatusMessage = string.Empty;
    }
}
