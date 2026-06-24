#nullable enable
namespace Expandroid.Services;

public enum AppTheme
{
    Light,
    Dark,
    Auto
}

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    bool IsDarkMode { get; }
    event Action? OnThemeChanged;
    void SetTheme(AppTheme theme);
    void ApplyTheme();
}
