using System;
using System.Drawing;
namespace GhostReader
{
    // Kept name/ordinal values for compatibility; now includes four single-axis edge handles.
    internal enum ResizeCorner { None, TopLeft, TopRight, BottomLeft, BottomRight, Left, Right, Top, Bottom }
    internal static class CornerResize
    {
        private static bool MovesLeft(ResizeCorner handle)=>handle==ResizeCorner.Left||handle==ResizeCorner.TopLeft||handle==ResizeCorner.BottomLeft;
        private static bool MovesRight(ResizeCorner handle)=>handle==ResizeCorner.Right||handle==ResizeCorner.TopRight||handle==ResizeCorner.BottomRight;
        private static bool MovesTop(ResizeCorner handle)=>handle==ResizeCorner.Top||handle==ResizeCorner.TopLeft||handle==ResizeCorner.TopRight;
        private static bool MovesBottom(ResizeCorner handle)=>handle==ResizeCorner.Bottom||handle==ResizeCorner.BottomLeft||handle==ResizeCorner.BottomRight;
        public static ResizeCorner Hit(Rectangle bounds,Point pointer,int grip)
        {
            if(!bounds.Contains(pointer))return ResizeCorner.None;
            // Leave a middle edge zone even for a very short strip.
            grip=Math.Max(1,Math.Min(grip,Math.Max(1,Math.Min(bounds.Width,bounds.Height)/3)));
            bool left=pointer.X<bounds.Left+grip,right=pointer.X>=bounds.Right-grip;
            bool top=pointer.Y<bounds.Top+grip,bottom=pointer.Y>=bounds.Bottom-grip;
            if(left&&top)return ResizeCorner.TopLeft;
            if(right&&top)return ResizeCorner.TopRight;
            if(left&&bottom)return ResizeCorner.BottomLeft;
            if(right&&bottom)return ResizeCorner.BottomRight;
            int edge=Math.Max(1,grip/2);
            if(pointer.X<bounds.Left+edge)return ResizeCorner.Left;
            if(pointer.X>=bounds.Right-edge)return ResizeCorner.Right;
            if(pointer.Y<bounds.Top+edge)return ResizeCorner.Top;
            if(pointer.Y>=bounds.Bottom-edge)return ResizeCorner.Bottom;
            return ResizeCorner.None;
        }
        public static Rectangle LimitToScreen(Rectangle original,Rectangle resized,ResizeCorner handle,Rectangle screen)
        {
            int l=MovesLeft(handle)?Math.Max(screen.Left,resized.Left):original.Left;
            int r=MovesRight(handle)?Math.Min(screen.Right,resized.Right):original.Right;
            int t=MovesTop(handle)?Math.Max(screen.Top,resized.Top):original.Top;
            int b=MovesBottom(handle)?Math.Min(screen.Bottom,resized.Bottom):original.Bottom;
            return Rectangle.FromLTRB(l,t,r,b);
        }
        public static Rectangle Calculate(Rectangle original,ResizeCorner handle,Point delta,Size minimum,Size maximum)
        {
            bool left=MovesLeft(handle),right=MovesRight(handle),top=MovesTop(handle),bottom=MovesBottom(handle);
            int width=left||right?(int)Math.Clamp((long)original.Width+(left?-(long)delta.X:delta.X),minimum.Width,Math.Max(minimum.Width,maximum.Width)):original.Width;
            int height=top||bottom?(int)Math.Clamp((long)original.Height+(top?-(long)delta.Y:delta.Y),minimum.Height,Math.Max(minimum.Height,maximum.Height)):original.Height;
            return new Rectangle(left?original.Right-width:original.Left,top?original.Bottom-height:original.Top,width,height);
        }
    }
}
