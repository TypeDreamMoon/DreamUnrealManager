namespace DreamUnrealManager.Contracts.Services
{
    public interface IUiSchedulerService
    {
        Task RunAsync(Action action);
    }
}