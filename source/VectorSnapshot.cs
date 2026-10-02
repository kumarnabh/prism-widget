using System.Windows;
using System.Windows.Media;

namespace Prism;

/// <summary>Export glyph outlines so screen-DPI glyph bitmaps are never enlarged.</summary>
public static class VectorSnapshot
{
    public static DrawingVisual Capture(Visual root)
    {
        var result=new DrawingVisual();
        using(var context=result.RenderOpen())Paint(root,context,true);
        return result;
    }
    static void Paint(Visual visual,DrawingContext context,bool root=false)
    {
        var matrix=VisualTreeHelper.GetTransform(visual)?.Value??Matrix.Identity;
        var offset=VisualTreeHelper.GetOffset(visual);
        if(!root)matrix.Translate(offset.X,offset.Y);
        context.PushTransform(new MatrixTransform(matrix));
        context.PushOpacity(VisualTreeHelper.GetOpacity(visual));
        var clip=VisualTreeHelper.GetClip(visual);if(clip!=null)context.PushClip(clip);
        var drawing=VisualTreeHelper.GetDrawing(visual);if(drawing!=null)context.DrawDrawing(Outlines(drawing));
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(visual);i++)
            if(VisualTreeHelper.GetChild(visual,i) is Visual child)Paint(child,context);
        if(clip!=null)context.Pop();
        context.Pop();context.Pop();
    }
    static Drawing Outlines(Drawing drawing)
    {
        if(drawing is GlyphRunDrawing glyph)return new GeometryDrawing(glyph.ForegroundBrush,null,glyph.GlyphRun.BuildGeometry());
        if(drawing is not DrawingGroup group)return drawing;
        var result=new DrawingGroup{Transform=group.Transform,Opacity=group.Opacity,ClipGeometry=group.ClipGeometry,OpacityMask=group.OpacityMask,GuidelineSet=group.GuidelineSet};
        foreach(var child in group.Children)result.Children.Add(Outlines(child));
        return result;
    }
}
