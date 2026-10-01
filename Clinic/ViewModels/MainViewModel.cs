using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Clinic.Data;
using Clinic.Demo;
using Clinic.Preferences;
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
    [ObservableProperty] private string _sourceLabel = "ТЕСТОВАЯ БД";
    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private string _themeToggleLabel = "Тёмная тема";
    [ObservableProperty] private string _themeError = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _role = string.Empty;
    [ObservableProperty] private string _initials = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _sectionTitle = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _today = DateTime.Today.ToString("dddd, d MMMM yyyy", new System.Globalization.CultureInfo("ru-RU"));

    public bool IsSignedOut => !IsSignedIn;
    public bool HasThemeError => !string.IsNullOrEmpty(ThemeError);
    private string _themePreferenceKey = "signin";
    private ClinicDatabase? _activeDatabase;
    private int? _activeAccountId;

    public MainViewModel() => ApplyTheme(ThemePreferenceStore.Get(_themePreferenceKey));
    public ObservableCollection<string> Navigation { get; } = [];
    public ObservableCollection<DashboardMetric> Metrics { get; } = [];
    public ObservableCollection<DashboardItem> Items { get; } = [];
    public string DemoCredentials => "Тестовый пароль: demo123";
    public string TestAccountsHint => OperatingSystem.IsBrowser()
        ? "Демо: patient, registrar, doctor, laborant, admin"
        : "Тестовые логины: kirillov, petrova, sokolov, lebedeva, admin";

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
        try
        {
            if (OperatingSystem.IsBrowser())
            {
                var account = DemoData.Accounts.FirstOrDefault(a =>
                    string.Equals(a.Login, Login.Trim(), StringComparison.OrdinalIgnoreCase) && a.Password == Password);
                if (account is null)
                {
                    ShowError("Неверный логин или пароль демо-учётной записи.");
                    return;
                }
                ApplyDashboard(account.Name, account.Role, account.Initials, null);
                SourceLabel = "ДЕМО-РЕЖИМ";
                SelectUserTheme($"demo:{account.Login}");
            }
            else
            {
                var database = ClinicDatabase.FromLocalConfiguration();
                var result = await database.SignInAsync(Login, Password);
                if (result is null)
                {
                    ShowError("Неверный логин или пароль тестовой учётной записи.");
                    return;
                }
                var session = result.Value.Session;
                ApplyDashboard(session.Name, session.Role, InitialsFromName(session.Name), result.Value.Dashboard);
                SourceLabel = "ТЕСТОВАЯ БД";
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
                _activeDatabase = database;
                _activeAccountId = session.AccountId;
            }
            IsSignedIn = true;
            ClearStatus();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            ShowError("Не удалось подключиться к тестовой базе. Проверьте PostgreSQL и db.local.json.");
        }
        finally
        {
            Password = string.Empty;
            IsBusy = false;
        }
    }

    private void ApplyDashboard(string name, string role, string initials, LiveDashboard? live)
    {
        var template = DemoData.ForRole(role);
        DisplayName = name;
        Role = role;
        Initials = initials;
        Subtitle = template.Subtitle;
        SectionTitle = live?.SectionTitle ?? template.Section;
        Note = live?.Note ?? template.Note;
        Replace(Navigation, template.Navigation);
        Replace(Metrics, live?.Metrics ?? template.Metrics);
        Replace(Items, live?.Items ?? template.Items);
    }

    private static string InitialsFromName(string name)
        => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0]));

    [RelayCommand]
    private void SignOut()
    {
        IsSignedIn = false;
        _activeDatabase = null;
        _activeAccountId = null;
        _themePreferenceKey = "signin";
        ApplyTheme(ThemePreferenceStore.Get(_themePreferenceKey));
        ThemeError = string.Empty;
        Login = string.Empty;
        Password = string.Empty;
        Navigation.Clear();
        Metrics.Clear();
        Items.Clear();
        ClearStatus();
    }

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        ThemeError = string.Empty;
        var requestedTheme = !IsDarkTheme;
        if (_activeDatabase is not null && _activeAccountId is int accountId)
        {
            try
            {
                await _activeDatabase.SaveUserThemeAsync(accountId, requestedTheme);
                ApplyTheme(requestedTheme);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception);
                ThemeError = "Не удалось сохранить тему в базе данных.";
            }
            return;
        }

        if (ThemePreferenceStore.Save(_themePreferenceKey, requestedTheme))
            ApplyTheme(requestedTheme);
        else
            ThemeError = "Не удалось сохранить тему на этом устройстве.";
    }

    private void SelectUserTheme(string key)
    {
        _themePreferenceKey = key;
        ApplyTheme(ThemePreferenceStore.Get(key, IsDarkTheme));
        ThemePreferenceStore.Save(key, IsDarkTheme);
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
    partial void OnPasswordChanged(string value) => ClearStatus();

    private void ShowError(string message)
    {
        StatusMessage = message;
        HasStatusMessage = true;
    }

    private void ClearStatus()
    {
        HasStatusMessage = false;
        StatusMessage = string.Empty;
    }

    private static void Replace<T>(ObservableCollection<T> collection, T[] values)
    {
        collection.Clear();
        foreach (var value in values) collection.Add(value);
    }
}
