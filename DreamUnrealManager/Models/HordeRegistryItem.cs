using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Win32;

namespace DreamUnrealManager.Models;

/// <summary>
/// 注册表项展示模型。
///
/// 实现 INotifyPropertyChanged 是因为 CurrentValue / CanEdit 在 XAML 里是 OneWay 绑定：
/// 目前刷新走的是整表重建，原地改值不会更新界面，但绑定模式声明的是 OneWay，
/// 一旦有人改成原地更新就会静默失效（稳定版 XAML 编译器的 WMC1506 正是在提示这点）。
/// </summary>
public sealed class HordeRegistryItem : INotifyPropertyChanged
{
    public string RootDisplay
    {
        get; init;
    } = string.Empty;

    public RegistryHive Hive
    {
        get; init;
    }

    public string SubKeyPath
    {
        get; init;
    } = string.Empty;

    public string ValueName
    {
        get; init;
    } = string.Empty;

    public RegistryValueKind ValueKind
    {
        get; init;
    }

    public bool IsReadOnly
    {
        get; init;
    }

    public bool IsBooleanDword
    {
        get; init;
    }

    private string _currentValue = "(未设置)";

    public string CurrentValue
    {
        get => _currentValue;
        set
        {
            if (_currentValue == value)
            {
                return;
            }

            _currentValue = value;
            OnPropertyChanged();
        }
    }

    public bool CanEdit => !IsReadOnly;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public string FullPath => $"{RootDisplay}\\{SubKeyPath}";

    public string DisplayType => IsBooleanDword ? "REG_DWORD (BOOL)" : ValueKind switch
    {
        RegistryValueKind.String => "REG_SZ",
        RegistryValueKind.DWord => "REG_DWORD",
        _ => ValueKind.ToString()
    };

    public string AccessLabel => IsReadOnly ? "READ ONLY" : "可编辑";
}
