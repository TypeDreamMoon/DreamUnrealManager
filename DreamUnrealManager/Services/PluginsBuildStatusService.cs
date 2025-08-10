using System;

namespace DreamUnrealManager.Services
{
    public class PluginsBuildStatusService
    {
        private static readonly Lazy<PluginsBuildStatusService> _instance = new(() => new PluginsBuildStatusService());
        public static PluginsBuildStatusService Instance => _instance.Value;

        private PluginsBuildStatusService()
        {
        }

        public event EventHandler<PluginsBuildStatusEventArgs>? StatusChanged;

        /// <summary>
        /// 更新状态
        /// </summary>
        public void UpdateStatus(bool isActive, int taskCount, string statusText)
        {
            StatusChanged?.Invoke(this, new PluginsBuildStatusEventArgs
            {
                IsActive = isActive,
                TaskCount = taskCount,
                StatusText = statusText
            });
        }

        /// <summary>
        /// 开始构建
        /// </summary>
        public void StartBuilding()
        {
            UpdateStatus(true, 1, "插件构建中...");
        }

        /// <summary>
        /// 开始运行
        /// </summary>
        public void StartRunning()
        {
            UpdateStatus(true, 1, "插件运行中...");
        }

        /// <summary>
        /// 构建并运行
        /// </summary>
        public void StartBuildAndRun()
        {
            UpdateStatus(true, 2, "构建并运行中...");
        }

        /// <summary>
        /// 停止所有任务
        /// </summary>
        public void StopAll()
        {
            UpdateStatus(false, 0, "就绪");
        }

        /// <summary>
        /// 设置错误状态
        /// </summary>
        public void SetError(string errorMessage)
        {
            UpdateStatus(true, 1, $"错误: {errorMessage}");
        }
    }

    public class PluginsBuildStatusEventArgs : EventArgs
    {
        public bool IsActive
        {
            get;
            set;
        }

        public int TaskCount
        {
            get;
            set;
        }

        public string StatusText
        {
            get;
            set;
        } = "";
    }
}