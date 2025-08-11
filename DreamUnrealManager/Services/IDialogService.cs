using System.Threading.Tasks;

namespace DreamUnrealManager.Services
{
    public interface IDialogService
    {
        Task ShowMessageAsync(string title, string content);
        Task<bool> ShowConfirmAsync(string title, string content);
    }
}