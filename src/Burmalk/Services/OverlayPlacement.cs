using System;

namespace Burmalk.Services
{
    internal static class OverlayPlacement
    {
        public static bool ShouldRender(bool requested,bool targetValid,bool targetForeground) =>
            requested && targetValid && targetForeground;
    }
}
