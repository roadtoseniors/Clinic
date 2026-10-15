using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Clinic.Data;
using Clinic.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Npgsql;

namespace Clinic.ViewModels;

public partial class EditorField : ObservableObject
{
    public FieldSpec Spec { get; }
    public string Label => (IsDate ? Spec.Label.Replace(" (ГГГГ-ММ-ДД)", "") : Spec.Label) + (Spec.Required ? " *" : "");
    public string Hint => Spec.Key switch
    {
        "phone" => "От 10 до 15 цифр; можно использовать +, пробелы, скобки и дефисы",
        "snils" => "11 цифр, например 123-456-789 00",
        "oms_policy" => "Номер полиса ОМС из 16 цифр",
        "password" or "new_password" => "От 8 символов, латинская буква и цифра",
        "file_name" => "Можно прикрепить PDF, JPG или PNG до 10 МБ",
        "start_time" or "end_time" => "Время в формате ЧЧ:ММ",
        "price" or "amount" => "Сумма в рублях; копейки отделяются запятой или точкой",
        _ => Spec.Required ? "Обязательное поле: " + Spec.Label : Spec.Label
    };
    public string ChoicePlaceholder => Choices.Count == 0 ? "Нет доступных вариантов" : "Выберите…";
    public bool IsChoice => Spec.Lookup is not null;
    public bool IsDate => Spec.Key is "birth_date" or "work_date" or "valid_until" or "booking_date";
    public bool IsText => !IsChoice && !IsDate;
    public bool IsReadOnly => Spec.Key == "file_name";
    public DateTime? Date { get => DateTime.TryParseExact(Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null; set => Text = value?.ToString("yyyy-MM-dd") ?? ""; }

    partial void OnTextChanged(string value) => OnPropertyChanged(nameof(Date));
    public bool IsMultiline => Spec.Multiline;
    public char PasswordChar => Spec.Secret ? '●' : '\0';
    public string Placeholder => Spec.Key switch
    {
        "phone" => "+7 (999) 123-45-67",
        "oms_policy" => "16 цифр",
        "start_time" or "end_time" => "ЧЧ:ММ",
        "password" or "new_password" => "Не менее 8 символов",
        "file_name" => "Прикрепите файл кнопкой ниже",
        _ => ""
    };
    public ObservableCollection<Choice> Choices { get; } = [];

    [ObservableProperty]
    private string _text = "";
    [ObservableProperty]
    private Choice? _selected;
    public string Value => IsChoice ? Selected?.Id ?? "" : Text;

    public EditorField(FieldSpec spec, string value, IEnumerable<Choice>? choices = null)
    {
        Spec = spec;
        Choices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ChoicePlaceholder));
        Text = value;
        if (choices is not null)
            foreach (var choice in choices)
                Choices.Add(choice);
        if (IsChoice)
        {
            if (!spec.Required)
                Choices.Insert(0, new Choice("", "Не выбрано"));
            Selected = Choices.FirstOrDefault(c => c.Id == value);
        }
    }
}
