using System.Threading.Tasks;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IDialogService
    {
        Task ShowMessageAsync(string title, string content);
        Task<bool> ShowConfirmAsync(string title, string content);
        Task<bool> ShowWarningConfirmAsync(string title, string content, string primaryText = "继续", string closeText = "取消");
        Task<string?> ShowInputAsync(string title, string message, string? placeholder = null, string primaryText = "确认", string closeText = "取消");
        Task ShowErrorDialog(string title, string message);
    }
}