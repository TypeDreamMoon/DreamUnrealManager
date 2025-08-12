using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DreamUnrealManager.Services
{
    public sealed class BackgroundSettingsService : INotifyPropertyChanged
    {
        BackgroundSettingsService()
        {
            _backgroundOpacity = Clamp(Settings.Get("App.Theme.Background.Opacity", 0.5));
        }
        public static BackgroundSettingsService Instance
        {
            get;
        } = new();

        private double _backgroundOpacity;

        public double BackgroundOpacity
        {
            get => _backgroundOpacity;
            set
            {
                var v = Clamp(value);
                if (Math.Abs(_backgroundOpacity - v) > double.Epsilon)
                {
                    _backgroundOpacity = v;
                    Settings.Set("App.Theme.Background.Opacity", v);
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