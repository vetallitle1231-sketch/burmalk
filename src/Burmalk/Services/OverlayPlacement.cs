using System;

namespace Burmalk.Services
{
    internal static class OverlayPlacement
    {
        public static bool ShouldRender(bool requested,bool targetValid,bool targetForeground) =>
            requested && targetValid && targetForeground;
        public static (int X,int Y) Corner(int left,int top,int right,int bottom,int width,int height,string position)
        {
            int insetX=Math.Min(12,Math.Max(0,(right-left-width)/2));
            int insetY=Math.Min(12,Math.Max(0,(bottom-top-height)/2));
            return ((position??"").Contains("Right")?Math.Max(left,right-width-insetX):left+insetX,
                    (position??"").Contains("Bottom")?Math.Max(top,bottom-height-insetY):top+insetY);
        }
    }
}
