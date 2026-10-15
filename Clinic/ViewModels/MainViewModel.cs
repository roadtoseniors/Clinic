using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    [ObservableProperty] private string _login = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasStatusMessage;
    [ObservableProperty] private bool _isSignedIn;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _apiAddress = Environment.GetEnvironmentVariable("CLINIC_API_URL") ?? "http://127.0.0.1:5278";
    [ObservableProperty] private WorkspaceViewModel? _workspace;
    [ObservableProperty] private bool _isRegistrationOpen;
    [ObservableProperty] private bool _isRegistrationDiscardOpen;
    [ObservableProperty] private string _registrationError = "";
    [ObservableProperty] private bool _isStatusSuccess;
    [ObservableProperty] private bool _revealPassword;
    [ObservableProperty] private bool _isThemeBusy;
    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private string _themeToggleLabel = "Тёмная тема";
    [ObservableProperty] private string _themeError = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _role = string.Empty;
    [ObservableProperty] private string _initials = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _today = DateTime.Today.ToString("dddd, d MMMM yyyy", new System.Globalization.CultureInfo("ru-RU"));

    public bool IsSignedOut => !IsSignedIn;
    public bool HasThemeError => !string.IsNullOrEmpty(ThemeError);
    private string _themePreferenceKey = "signin";
    private IClinicGateway? _activeGateway;
    private int? _activeAccountId;
    private Dictionary<string, string> _registrationValues = [];
    private TaskCompletionSource<bool>? _registrationConfirmation;

    private readonly Func<IClinicGateway> _gatewayFactory;
    private readonly Func<Task> _prepareConnection;
    public MainViewModel(Func<IClinicGateway>? gatewayFactory = null, Func<Task>? prepareConnection = null)
    {
        _gatewayFactory = gatewayFactory ?? (() => new HttpClinicGateway(ApiAddress));
        _prepareConnection = prepareConnection ?? (() => Task.CompletedTask);
        ApplyTheme(ThemePreferenceStore.Get(_themePreferenceKey));
    }
    public ObservableCollection<EditorField> RegistrationFields { get; } = [];
    public bool CanInteract => !IsBusy && !IsRegistrationDiscardOpen;
    public bool HasRegistrationChanges => IsRegistrationOpen && RegistrationFields.Any(f => _registrationValues.GetValueOrDefault(f.Spec.Key) != f.Value);
    public string SignInLabel => IsBusy ? "Входим…" : "Войти";
    public string RegistrationHint => "Заполните личные данные и полис ОМС, затем создайте логин и пароль.";
    private IClinicGateway CreateGateway() => _gatewayFactory();

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrWhiteSpace(Password))
        {
            ShowError("Введите логин и пароль, чтобы продолжить.");
            return;
        }

        IsBusy = true;
        IClinicGateway? database = null;
        try
        {
            await _prepareConnection();
            database = CreateGateway();
            var result = await database.SignInAsync(Login, Password);
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
            var databaseTheme = await database.GetUserThemeAsync(session.AccountId);
            if (databaseTheme is null)
            {
                // Keep a previously selected local theme when moving to user_theme.
                var previousTheme = ThemePreferenceStore.Get($"account:{session.AccountId}");
                if (previousTheme)
                    await database.SaveUserThemeAsync(session.AccountId, true);
                ApplyTheme(previousTheme);
            }
            else
                ApplyTheme(databaseTheme.Value);
            _activeGateway = database;
            _activeAccountId = session.AccountId;
            Workspace = new WorkspaceViewModel(this, database, session);
            await Workspace.InitializeAsync();
            IsSignedIn = true;
            ClearStatus();
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
                if (database is IDisposable disposable) disposable.Dispose();
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
        => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0]));

    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (IsBusy || Workspace?.IsBusy == true) return;
        if (Workspace is { HasUnsavedChanges: true } workspace &&
            !await workspace.ConfirmDiscardAsync()) return;
        IsSignedIn = false;
        if (_activeGateway is IDisposable disposable) disposable.Dispose();
        _activeGateway = null;
        _activeAccountId = null;
        Workspace = null;
        _themePreferenceKey = "signin";
        ApplyTheme(ThemePreferenceStore.Get(_themePreferenceKey));
        ThemeError = string.Empty;
        Login = string.Empty;
        Password = string.Empty;
        RevealPassword = false;
        DisplayName = Role = Initials = Subtitle = string.Empty;
        ClearStatus();
    }

    [RelayCommand] private async Task OpenRegistrationAsync()
    {
        if(IsBusy)return;IsBusy=true;
        try
        {
            RegistrationFields.Clear();RegistrationError = "";
            await _prepareConnection();
            var gateway = CreateGateway();
            using var disposable = gateway as IDisposable;
            var api = gateway as HttpClinicGateway;
            var choices = api is null ? new Dictionary<string, List<Choice>>() : await api.RegistrationChoicesAsync();
            foreach(var spec in api?.FieldsFor("registration")??Catalog.Fields("registration"))RegistrationFields.Add(new EditorField(spec,spec.Default??"",spec.Lookup is string lookup?choices.GetValueOrDefault(lookup):null));
            _registrationValues=RegistrationFields.ToDictionary(f=>f.Spec.Key,f=>f.Value);
            IsRegistrationOpen=true;
        }
        catch(Exception ex){ShowError(WorkspaceViewModel.Friendly(ex));}finally{IsBusy=false;}
    }
    [RelayCommand] private async Task CloseRegistrationAsync()
    {
        if(IsBusy) return;
        if(HasRegistrationChanges && !await ConfirmRegistrationDiscardAsync()) return;
        IsRegistrationOpen=false;RegistrationFields.Clear();RegistrationError="";
    }
    public Task<bool> ConfirmRegistrationDiscardAsync()
    {
        if(_registrationConfirmation is not null) return Task.FromResult(false);
        _registrationConfirmation=new(TaskCreationOptions.RunContinuationsAsynchronously);
        IsRegistrationDiscardOpen=true;
        return _registrationConfirmation.Task;
    }
    [RelayCommand] private void CancelRegistrationDiscard()=>FinishRegistrationDiscard(false);
    [RelayCommand] private void ConfirmRegistrationDiscard()=>FinishRegistrationDiscard(true);
    private void FinishRegistrationDiscard(bool confirmed)
    {
        IsRegistrationDiscardOpen=false;var pending=_registrationConfirmation;_registrationConfirmation=null;pending?.TrySetResult(confirmed);
    }
    partial void OnIsRegistrationDiscardOpenChanged(bool value)=>OnPropertyChanged(nameof(CanInteract));
    [RelayCommand] private async Task RegisterAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _prepareConnection();
            IClinicGateway gateway = CreateGateway();
            using var disposable = gateway as IDisposable;
            FormValidation.Validate(RegistrationFields);
            await gateway.RegisterAsync(RegistrationFields.ToDictionary(f=>f.Spec.Key,f=>f.Value));
            Login = RegistrationFields.First(f=>f.Spec.Key=="login").Value;
            IsRegistrationOpen = false;
            RegistrationFields.Clear();
            ShowStatus("Учётная запись создана. Введите пароль для входа.", success: true);
        }
        catch (Exception ex) { RegistrationError = WorkspaceViewModel.Friendly(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        if (IsThemeBusy) return;
        ThemeError = string.Empty;
        var requestedTheme = !IsDarkTheme;
        if (_activeGateway is not null && _activeAccountId is int accountId)
        {
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
            finally { IsThemeBusy = false; }
            return;
        }

        if (ThemePreferenceStore.Save(_themePreferenceKey, requestedTheme))
            ApplyTheme(requestedTheme);
        else
            ThemeError = "Не удалось сохранить тему на этом устройстве.";
    }

    private void ApplyTheme(bool dark)
    {
        IsDarkTheme = dark;
        ThemeToggleLabel = dark ? "Светлая тема" : "Тёмная тема";
        if (Application.Current is { } app)
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    partial void OnThemeErrorChanged(string value) => OnPropertyChanged(nameof(HasThemeError));

    partial void OnIsSignedInChanged(bool value) => OnPropertyChanged(nameof(IsSignedOut));
    partial void OnLoginChanged(string value) => ClearStatus();
    partial void OnPasswordChanged(string value) { if(!IsBusy) ClearStatus(); }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(SignInLabel));
    }

    private void ShowError(string message) => ShowStatus(message);
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
