using CommunityToolkit.Mvvm.ComponentModel;

namespace Clinic.Workspace;
public partial class NavigationItem(Section section) : ObservableObject
{
    public Section Section { get; } = section;
    public string Key => Section.Key;
    public string Title => Section.Title;
    public string IconPath => Section.IconPath;

    [ObservableProperty]
    private bool _isActive;
}
