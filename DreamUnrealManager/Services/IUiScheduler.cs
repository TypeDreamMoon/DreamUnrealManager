namespace DreamUnrealManager.Services
{
    public interface IUiScheduler
    {
        Task RunAsync(Action action);
    }
}