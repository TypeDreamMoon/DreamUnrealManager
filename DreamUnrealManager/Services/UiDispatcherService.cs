using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DreamUnrealManager.Services
{
    public static class UiDispatcherService
    {
        public static DispatcherQueue Queue
        {
            get;
            private set;
        }

        public static void Initialize(Window window)
        {
            Queue = window?.DispatcherQueue;
        }

        public static void Enqueue(Action action)
        {
            if (Queue == null || Queue.HasThreadAccess)
            {
                action();
            }
            else
            {
                Queue.TryEnqueue(() => action());
            }
        }
    }
}