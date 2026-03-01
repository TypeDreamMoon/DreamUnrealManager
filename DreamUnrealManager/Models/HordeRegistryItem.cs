using Microsoft.Win32;

namespace DreamUnrealManager.Models;

public sealed class HordeRegistryItem
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

    public string CurrentValue
    {
        get; set;
    } = "(未设置)";

    public bool CanEdit => !IsReadOnly;

    public string FullPath => $"{RootDisplay}\\{SubKeyPath}";

    public string DisplayType => IsBooleanDword ? "REG_DWORD (BOOL)" : ValueKind switch
    {
        RegistryValueKind.String => "REG_SZ",
        RegistryValueKind.DWord => "REG_DWORD",
        _ => ValueKind.ToString()
    };

    public string AccessLabel => IsReadOnly ? "READ ONLY" : "可编辑";
}
