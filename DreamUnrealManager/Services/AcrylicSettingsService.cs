using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DreamUnrealManager.Services
{
    public sealed class AcrylicSettingsService : INotifyPropertyChanged
    {
        public static AcrylicSettingsService Instance { get; } = new();

        private AcrylicSettingsService()
        {
            // 从你的 Settings 里读取（你项目里已有 Settings.Get/Save）
            _tintOpacity = Clamp(Settings.Get("App.Theme.Acrylic.TintOpacity", 0.6));
            _tintLuminosityOpacity = Clamp(Settings.Get("App.Theme.Acrylic.TintLuminosityOpacity", 0.0));
        }

        private double _tintOpacity;
        public double TintOpacity
        {
            get => _tintOpacity;
            set
            {
                var v = Clamp(value);
                if (Math.Abs(_tintOpacity - v) > double.Epsilon)
                {
                    _tintOpacity = v;
                    Settings.Set("App.Theme.Acrylic.TintOpacity", v);
                    OnPropertyChanged();
                }
            }
        }

        private double _tintLuminosityOpacity;
        public double TintLuminosityOpacity
        {
            get => _tintLuminosityOpacity;
            set
            {
                var v = Clamp(value);
                if (Math.Abs(_tintLuminosityOpacity - v) > double.Epsilon)
                {
                    _tintLuminosityOpacity = v;
                    Settings.Set("App.Theme.Acrylic.TintLuminosityOpacity", v);
                    OnPropertyChanged();
                }
            }
        }

        private static double Clamp(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}