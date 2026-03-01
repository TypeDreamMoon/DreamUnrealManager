using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.ServiceProcess;
using DreamUnrealManager.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace DreamUnrealManager.Views;

public sealed partial class UnrealHordePage : Page
{
    private const string HordeAgentServiceName = "HordeAgent";
    private const string HordeServerServiceName = "HordeServer";
    private static readonly TimeSpan ServiceWaitTimeout = TimeSpan.FromSeconds(20);

    private readonly ObservableCollection<HordeRegistryItem> _registryItems = new();

    private readonly List<HordeRegistryItem> _registryDefinitions =
    [
        new HordeRegistryItem
        {
            RootDisplay = @"HKEY_LOCAL_MACHINE",
            Hive = RegistryHive.LocalMachine,
            SubKeyPath = @"SOFTWARE\Epic Games\Horde",
            ValueName = "Url",
            ValueKind = RegistryValueKind.String
        },
        new HordeRegistryItem
        {
            RootDisplay = @"HKEY_LOCAL_MACHINE",
            Hive = RegistryHive.LocalMachine,
            SubKeyPath = @"SOFTWARE\Epic Games\Horde\Agent",
            ValueName = "Installed",
            ValueKind = RegistryValueKind.DWord,
            IsReadOnly = true,
            IsBooleanDword = true
        },
        new HordeRegistryItem
        {
            RootDisplay = @"HKEY_LOCAL_MACHINE",
            Hive = RegistryHive.LocalMachine,
            SubKeyPath = @"SOFTWARE\Epic Games\Horde\Agent",
            ValueName = "WorkingDir",
            ValueKind = RegistryValueKind.String
        },
        new HordeRegistryItem
        {
            RootDisplay = @"HKEY_LOCAL_MACHINE",
            Hive = RegistryHive.LocalMachine,
            SubKeyPath = @"SOFTWARE\Epic Games\Horde\Server",
            ValueName = "DataDir",
            ValueKind = RegistryValueKind.String
        },
        new HordeRegistryItem
        {
            RootDisplay = @"HKEY_LOCAL_MACHINE",
            Hive = RegistryHive.LocalMachine,
            SubKeyPath = @"SOFTWARE\Epic Games\Horde\Server",
            ValueName = "Http2Port",
            ValueKind = RegistryValueKind.DWord
        },
        new HordeRegistryItem
        {
            RootDisplay = @"HKEY_LOCAL_MACHINE",
            Hive = RegistryHive.LocalMachine,
            SubKeyPath = @"SOFTWARE\Epic Games\Horde\Server",
            ValueName = "HttpPort",
            ValueKind = RegistryValueKind.DWord
        },
        new HordeRegistryItem
        {
            RootDisplay = @"HKEY_LOCAL_MACHINE",
            Hive = RegistryHive.LocalMachine,
            SubKeyPath = @"SOFTWARE\Epic Games\Horde\Server",
            ValueName = "InstalledServerExecutable",
            ValueKind = RegistryValueKind.String,
            IsReadOnly = true
        },
        new HordeRegistryItem
        {
            RootDisplay = @"HKEY_CURRENT_USER",
            Hive = RegistryHive.CurrentUser,
            SubKeyPath = @"Software\Epic Games\Horde",
            ValueName = "agent",
            ValueKind = RegistryValueKind.DWord,
            IsReadOnly = true,
            IsBooleanDword = true
        },
        new HordeRegistryItem
        {
            RootDisplay = @"HKEY_CURRENT_USER",
            Hive = RegistryHive.CurrentUser,
            SubKeyPath = @"Software\Epic Games\Horde",
            ValueName = "Url",
            ValueKind = RegistryValueKind.String
        },
        new HordeRegistryItem
        {
            RootDisplay = @"HKEY_CURRENT_USER",
            Hive = RegistryHive.CurrentUser,
            SubKeyPath = @"Software\Epic Games\Horde\Server",
            ValueName = "Installed",
            ValueKind = RegistryValueKind.DWord,
            IsReadOnly = true,
            IsBooleanDword = true
        }
    ];

    public UnrealHordePage()
    {
        InitializeComponent();
        RegistryListView.ItemsSource = _registryItems;
        Loaded += UnrealHordePage_Loaded;
    }

    private async void UnrealHordePage_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        RefreshLocalIp();
        await RefreshServicesAsync();
        await RefreshRegistryValuesAsync();
    }

    private void RefreshLocalIp()
    {
        try
        {
            var hostName = Dns.GetHostName();
            var ips = GetLocalIPv4Addresses();

            HostNameTextBlock.Text = $"主机名: {hostName}";
            LocalIpTextBlock.Text = ips.Count == 0
                ? "本机IP: 未检测到可用 IPv4 地址"
                : $"本机IP: {string.Join(", ", ips)}";
        }
        catch (Exception ex)
        {
            LocalIpTextBlock.Text = $"本机IP读取失败: {ex.Message}";
        }
    }

    private static List<string> GetLocalIPv4Addresses()
    {
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(unicast.Address))
                {
                    continue;
                }

                addresses.Add(unicast.Address.ToString());
            }
        }

        if (addresses.Count > 0)
        {
            return addresses.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var fallback = Dns.GetHostAddresses(Dns.GetHostName())
            .Where(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))
            .Select(x => x.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return fallback;
    }

    private async Task RefreshServicesAsync()
    {
        try
        {
            var agent = await Task.Run(() => QueryService(HordeAgentServiceName));
            var server = await Task.Run(() => QueryService(HordeServerServiceName));

            ApplyServiceSnapshot(agent,
                AgentServiceNameText,
                AgentServiceStatusText,
                StartAgentButton,
                PauseOrContinueAgentButton);

            ApplyServiceSnapshot(server,
                ServerServiceNameText,
                ServerServiceStatusText,
                StartServerButton,
                PauseOrContinueServerButton);

            ServiceActionTextBlock.Text = $"服务操作日志: 状态已刷新 ({DateTime.Now:HH:mm:ss})";
        }
        catch (Exception ex)
        {
            ServiceActionTextBlock.Text = $"服务状态刷新失败: {ex.Message}";
        }
    }

    private sealed class ServiceSnapshot
    {
        public string RequestedName
        {
            get; init;
        } = string.Empty;

        public bool IsInstalled
        {
            get; init;
        }

        public string DisplayName
        {
            get; init;
        } = string.Empty;

        public string ServiceName
        {
            get; init;
        } = string.Empty;

        public ServiceControllerStatus? Status
        {
            get; init;
        }

        public bool CanPauseAndContinue
        {
            get; init;
        }
    }

    private static ServiceSnapshot QueryService(string requestedName)
    {
        var allServices = ServiceController.GetServices();
        using var service = allServices.FirstOrDefault(x =>
            string.Equals(x.ServiceName, requestedName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.DisplayName, requestedName, StringComparison.OrdinalIgnoreCase));

        if (service == null)
        {
            return new ServiceSnapshot
            {
                RequestedName = requestedName,
                IsInstalled = false,
                DisplayName = requestedName,
                ServiceName = requestedName
            };
        }

        service.Refresh();
        return new ServiceSnapshot
        {
            RequestedName = requestedName,
            IsInstalled = true,
            DisplayName = service.DisplayName,
            ServiceName = service.ServiceName,
            Status = service.Status,
            CanPauseAndContinue = service.CanPauseAndContinue
        };
    }

    private static string ToServiceStatusText(ServiceControllerStatus? status)
    {
        return status switch
        {
            ServiceControllerStatus.Running => "运行中",
            ServiceControllerStatus.Stopped => "已停止",
            ServiceControllerStatus.Paused => "已暂停",
            ServiceControllerStatus.StartPending => "启动中",
            ServiceControllerStatus.StopPending => "停止中",
            ServiceControllerStatus.PausePending => "暂停中",
            ServiceControllerStatus.ContinuePending => "恢复中",
            _ => "未知"
        };
    }

    private void ApplyServiceSnapshot(
        ServiceSnapshot snapshot,
        TextBlock serviceNameBlock,
        TextBlock serviceStatusBlock,
        Button startButton,
        Button pauseOrContinueButton)
    {
        serviceNameBlock.Text = $"{snapshot.DisplayName} ({snapshot.ServiceName})";

        if (!snapshot.IsInstalled)
        {
            serviceStatusBlock.Text = "状态: 未安装或未找到";
            startButton.IsEnabled = false;
            pauseOrContinueButton.IsEnabled = false;
            pauseOrContinueButton.Content = "暂停";
            return;
        }

        var status = snapshot.Status ?? ServiceControllerStatus.Stopped;
        serviceStatusBlock.Text = $"状态: {ToServiceStatusText(status)}";

        startButton.IsEnabled = status == ServiceControllerStatus.Stopped ||
                                (status == ServiceControllerStatus.Paused && snapshot.CanPauseAndContinue);

        if (status == ServiceControllerStatus.Paused)
        {
            pauseOrContinueButton.Content = "继续";
            pauseOrContinueButton.IsEnabled = snapshot.CanPauseAndContinue;
        }
        else if (status == ServiceControllerStatus.Running)
        {
            pauseOrContinueButton.Content = "暂停";
            pauseOrContinueButton.IsEnabled = snapshot.CanPauseAndContinue;
        }
        else
        {
            pauseOrContinueButton.Content = "暂停";
            pauseOrContinueButton.IsEnabled = false;
        }
    }

    private enum ServiceOperation
    {
        Start,
        PauseOrContinue
    }

    private async Task RunServiceActionAsync(string targetName, ServiceOperation operation)
    {
        if (!IsRunningAsAdministrator())
        {
            var actionText = operation switch
            {
                ServiceOperation.Start => "启动",
                ServiceOperation.PauseOrContinue => "暂停/继续",
                _ => "操作"
            };
            var permissionMessage = $"执行服务{actionText}需要管理员权限。请以管理员身份重新启动程序后重试。";
            ServiceActionTextBlock.Text = $"服务操作失败: {permissionMessage}";
            await ShowInfoDialogAsync("需要管理员权限", permissionMessage);
            return;
        }

        try
        {
            var message = await Task.Run(() => ExecuteServiceAction(targetName, operation));
            ServiceActionTextBlock.Text = $"服务操作日志: {message}";
        }
        catch (Exception ex)
        {
            var friendlyMessage = BuildServiceActionErrorMessage(ex, targetName, operation);
            ServiceActionTextBlock.Text = $"服务操作失败: {friendlyMessage}";
            await ShowInfoDialogAsync("服务操作失败", friendlyMessage);
        }
        finally
        {
            await RefreshServicesAsync();
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string ExecuteServiceAction(string targetName, ServiceOperation operation)
    {
        var allServices = ServiceController.GetServices();
        using var service = allServices.FirstOrDefault(x =>
            string.Equals(x.ServiceName, targetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.DisplayName, targetName, StringComparison.OrdinalIgnoreCase));

        if (service == null)
        {
            throw new InvalidOperationException($"未找到服务: {targetName}");
        }

        service.Refresh();
        var currentStatus = service.Status;

        switch (operation)
        {
            case ServiceOperation.Start:
                if (currentStatus == ServiceControllerStatus.Running)
                {
                    return $"{service.DisplayName} 已在运行中";
                }

                if (currentStatus == ServiceControllerStatus.Paused && service.CanPauseAndContinue)
                {
                    service.Continue();
                    service.WaitForStatus(ServiceControllerStatus.Running, ServiceWaitTimeout);
                    return $"{service.DisplayName} 已恢复运行";
                }

                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, ServiceWaitTimeout);
                return $"{service.DisplayName} 启动成功";

            case ServiceOperation.PauseOrContinue:
                if (currentStatus == ServiceControllerStatus.Paused)
                {
                    if (!service.CanPauseAndContinue)
                    {
                        throw new InvalidOperationException($"{service.DisplayName} 不支持继续操作");
                    }

                    service.Continue();
                    service.WaitForStatus(ServiceControllerStatus.Running, ServiceWaitTimeout);
                    return $"{service.DisplayName} 已继续运行";
                }

                if (currentStatus == ServiceControllerStatus.Running)
                {
                    if (service.CanPauseAndContinue)
                    {
                        service.Pause();
                        service.WaitForStatus(ServiceControllerStatus.Paused, ServiceWaitTimeout);
                        return $"{service.DisplayName} 已暂停";
                    }

                    throw new InvalidOperationException($"{service.DisplayName} 不支持暂停");
                }

                throw new InvalidOperationException($"{service.DisplayName} 当前状态为 {ToServiceStatusText(currentStatus)}，无法执行暂停/继续");
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static string BuildServiceActionErrorMessage(Exception ex, string targetName, ServiceOperation operation)
    {
        var actionText = operation switch
        {
            ServiceOperation.Start => "启动",
            ServiceOperation.PauseOrContinue => "暂停/继续",
            _ => "操作"
        };

        if (TryGetNativeErrorCode(ex, out var nativeCode))
        {
            return nativeCode switch
            {
                5 => $"无法{actionText}服务 `{targetName}`：权限不足。请以管理员身份运行程序后重试。",
                1060 => $"无法{actionText}服务 `{targetName}`：系统未找到该服务，请先确认 Horde 服务已正确安装。",
                _ => $"无法{actionText}服务 `{targetName}`：{ex.GetBaseException().Message} (Win32: {nativeCode})"
            };
        }

        if (ContainsAccessDenied(ex))
        {
            return $"无法{actionText}服务 `{targetName}`：权限不足。请以管理员身份运行程序后重试。";
        }

        return $"无法{actionText}服务 `{targetName}`：{ex.GetBaseException().Message}";
    }

    private static bool TryGetNativeErrorCode(Exception ex, out int code)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is Win32Exception win32Ex)
            {
                code = win32Ex.NativeErrorCode;
                return true;
            }
        }

        code = 0;
        return false;
    }

    private static bool ContainsAccessDenied(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task RefreshRegistryValuesAsync()
    {
        _registryItems.Clear();
        foreach (var definition in _registryDefinitions)
        {
            var item = new HordeRegistryItem
            {
                RootDisplay = definition.RootDisplay,
                Hive = definition.Hive,
                SubKeyPath = definition.SubKeyPath,
                ValueName = definition.ValueName,
                ValueKind = definition.ValueKind,
                IsReadOnly = definition.IsReadOnly,
                IsBooleanDword = definition.IsBooleanDword
            };

            object? rawValue;
            try
            {
                rawValue = await Task.Run(() => ReadRegistryValue(item));
            }
            catch (Exception ex)
            {
                rawValue = $"读取失败: {ex.Message}";
            }

            item.CurrentValue = ConvertRegistryValueToDisplay(item, rawValue);
            _registryItems.Add(item);
        }
    }

    private static object? ReadRegistryValue(HordeRegistryItem item)
    {
        foreach (var view in GetRegistryViews())
        {
            using var baseKey = RegistryKey.OpenBaseKey(item.Hive, view);
            using var key = baseKey.OpenSubKey(item.SubKeyPath, writable: false);
            if (key == null)
            {
                continue;
            }

            return key.GetValue(item.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        return null;
    }

    private static IEnumerable<RegistryView> GetRegistryViews()
    {
        if (Environment.Is64BitOperatingSystem)
        {
            return [RegistryView.Registry64, RegistryView.Registry32];
        }

        return [RegistryView.Registry32];
    }

    private static string ConvertRegistryValueToDisplay(HordeRegistryItem item, object? rawValue)
    {
        if (rawValue == null)
        {
            return "(未设置)";
        }

        if (rawValue is string errorText && errorText.StartsWith("读取失败:", StringComparison.Ordinal))
        {
            return errorText;
        }

        try
        {
            if (item.ValueKind == RegistryValueKind.DWord)
            {
                var number = Convert.ToInt32(rawValue);
                if (item.IsBooleanDword)
                {
                    return number != 0 ? "True (1)" : "False (0)";
                }

                return number.ToString();
            }

            return rawValue.ToString() ?? "(空值)";
        }
        catch
        {
            return rawValue.ToString() ?? "(空值)";
        }
    }

    private async void RefreshAll_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private void RefreshIp_Click(object sender, RoutedEventArgs e)
    {
        RefreshLocalIp();
    }

    private async void RefreshServices_Click(object sender, RoutedEventArgs e)
    {
        await RefreshServicesAsync();
    }

    private async void StartAgent_Click(object sender, RoutedEventArgs e)
    {
        await RunServiceActionAsync(HordeAgentServiceName, ServiceOperation.Start);
    }

    private async void PauseOrContinueAgent_Click(object sender, RoutedEventArgs e)
    {
        await RunServiceActionAsync(HordeAgentServiceName, ServiceOperation.PauseOrContinue);
    }

    private async void StartServer_Click(object sender, RoutedEventArgs e)
    {
        await RunServiceActionAsync(HordeServerServiceName, ServiceOperation.Start);
    }

    private async void PauseOrContinueServer_Click(object sender, RoutedEventArgs e)
    {
        await RunServiceActionAsync(HordeServerServiceName, ServiceOperation.PauseOrContinue);
    }

    private async void RefreshRegistry_Click(object sender, RoutedEventArgs e)
    {
        await RefreshRegistryValuesAsync();
    }

    private async void EditRegistryValue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HordeRegistryItem item })
        {
            return;
        }

        if (item.IsReadOnly)
        {
            await ShowInfoDialogAsync("只读项", "该项为 READ ONLY，不允许修改。");
            return;
        }

        var editedText = await ShowRegistryEditDialogAsync(item);
        if (editedText == null)
        {
            return;
        }

        object parsedValue;
        try
        {
            parsedValue = ParseRegistryValue(item, editedText);
        }
        catch (Exception ex)
        {
            await ShowInfoDialogAsync("输入无效", ex.Message);
            return;
        }

        var confirmed = await ShowRegistryConfirmDialogAsync(item, item.CurrentValue, editedText);
        if (!confirmed)
        {
            return;
        }

        try
        {
            await Task.Run(() => WriteRegistryValue(item, parsedValue));
            await RefreshRegistryValuesAsync();
            await ShowInfoDialogAsync("修改成功", $"{item.ValueName} 已写入注册表。");
        }
        catch (UnauthorizedAccessException)
        {
            await ShowInfoDialogAsync("权限不足", "写入注册表失败：请以管理员身份运行程序后重试。");
        }
        catch (Exception ex)
        {
            await ShowInfoDialogAsync("修改失败", ex.Message);
        }
    }

    private async Task<string?> ShowRegistryEditDialogAsync(HordeRegistryItem item)
    {
        var inputBox = new TextBox
        {
            Text = item.CurrentValue == "(未设置)" ? string.Empty : item.CurrentValue,
            PlaceholderText = item.ValueKind == RegistryValueKind.DWord ? "请输入整数值" : "请输入字符串值"
        };

        var stack = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = item.FullPath, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"键名: {item.ValueName} ({item.DisplayType})" },
                inputBox
            }
        };

        var dialog = new ContentDialog
        {
            Title = "修改注册表值",
            Content = stack,
            PrimaryButtonText = "下一步",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return inputBox.Text.Trim();
    }

    private async Task<bool> ShowRegistryConfirmDialogAsync(HordeRegistryItem item, string oldValue, string newValue)
    {
        var stack = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "请再次确认写入注册表。该操作会立即生效。",
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock { Text = $"路径: {item.FullPath}", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"键名: {item.ValueName}" },
                new TextBlock { Text = $"旧值: {oldValue}", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"新值: {newValue}", TextWrapping = TextWrapping.Wrap }
            }
        };

        var dialog = new ContentDialog
        {
            Title = "二次确认",
            Content = stack,
            PrimaryButtonText = "确认写入",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static object ParseRegistryValue(HordeRegistryItem item, string input)
    {
        if (item.ValueKind == RegistryValueKind.DWord)
        {
            if (!int.TryParse(input, out var value) || value < 0)
            {
                throw new InvalidOperationException("REG_DWORD 必须是大于等于 0 的整数。");
            }

            return value;
        }

        return input;
    }

    private static void WriteRegistryValue(HordeRegistryItem item, object value)
    {
        var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
        using var baseKey = RegistryKey.OpenBaseKey(item.Hive, view);
        using var key = baseKey.CreateSubKey(item.SubKeyPath, writable: true);
        if (key == null)
        {
            throw new InvalidOperationException($"无法打开注册表路径: {item.FullPath}");
        }

        key.SetValue(item.ValueName, value, item.ValueKind);
    }

    private async Task ShowInfoDialogAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
