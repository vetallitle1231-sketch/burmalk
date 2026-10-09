using System.Windows;

namespace Burmalk.Views
{
    /// <summary>Limited lookup helper for the Dark theme dictionary (code-first views have no XAML).</summary>
    internal static class Theme
    {
        public static Style Style(string key)
        {
            try { return Application.Current.TryFindResource(key) as Style; }
            catch { return null; }
        }

        public static System.Windows.Media.Brush Brush(string key)
        {
            try { return (System.Windows.Media.Brush)Application.Current.TryFindResource(key); }
            catch { return null; }
        }
    }
}

