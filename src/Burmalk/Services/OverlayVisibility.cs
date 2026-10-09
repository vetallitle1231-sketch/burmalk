namespace Burmalk.Services
{
    // Automatic game detection and an explicit hotkey request are separate states.
    internal sealed class OverlayVisibility
    {
        private bool? _manual;
        public bool IsVisible(bool enabled,bool gameActive,bool profileAllows,bool preview) =>
            preview || (_manual ?? (enabled && gameActive && profileAllows));
        public bool Toggle(bool enabled,bool gameActive,bool profileAllows,bool preview)
        {
            _manual=!IsVisible(enabled,gameActive,profileAllows,preview);
            return _manual.Value;
        }
        public void Reset() => _manual=null;
        public bool SetManual(bool show){_manual=show;return show;}
    }
}
