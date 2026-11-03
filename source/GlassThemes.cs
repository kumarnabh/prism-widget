using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace Prism;

public static partial class Theme
{
    public static readonly string[] Ids={"aurora","ocean","dusk","clear"};
    public static readonly string[] Names={"Aurora glass","Ocean glass","Dusk glass","Clear glass · no color"};
    public static string Active {get;private set;}="aurora";
    sealed record Original(Brush Brush,string Theme);
    static readonly ConditionalWeakTable<Brush,Original> Originals=new();
    public static Color Map(Color source)
    {
        if(Active=="aurora")return source;
        double luminance=.2126*source.R+.7152*source.G+.0722*source.B;
        if(Active=="clear"){byte gray=(byte)Math.Clamp(Math.Round(luminance),0,255);return Color.FromArgb(source.A,gray,gray,gray);}
        // Preserve luminance and opacity, using one restrained hue throughout the glass.
        double strength=Math.Min(28,(source.R==source.G&&source.G==source.B?0:1)*Math.Min(luminance,255-luminance)*.32);
        double r=Active=="ocean"?-.65:.2,g=Active=="ocean"?.1:-.45,b=1;
        return Color.FromArgb(source.A,(byte)Math.Clamp(luminance+r*strength,0,255),(byte)Math.Clamp(luminance+g*strength,0,255),(byte)Math.Clamp(luminance+b*strength,0,255));
    }
    public static Brush Adapt(Brush brush)
    {
        if(Originals.TryGetValue(brush,out var original)){if(original.Theme==Active)return brush;brush=original.Brush;}
        var result=brush.CloneCurrentValue();
        if(result is SolidColorBrush solid)solid.Color=Map(solid.Color);
        else if(result is GradientBrush gradient)foreach(var stop in gradient.GradientStops)stop.Color=Map(stop.Color);
        result.Freeze();Originals.Add(result,new(brush,Active));return result;
    }
    public static SolidColorBrush Solid(Color value)=>(SolidColorBrush)Adapt(new SolidColorBrush(value));
    static string ThemedStyles()=>Regex.Replace(Styles,@"#[A-Fa-f0-9]{8}\b|#[A-Fa-f0-9]{6}\b",m=>Map((Color)ColorConverter.ConvertFromString(m.Value)).ToString());
    public static void Select(string id)
    {
        Active=Ids.Contains(id)?id:"aurora";
        if(Application.Current is not Application app)return;
        Install(app);foreach(Window window in app.Windows)Apply(window);
    }
    public static void Apply(DependencyObject root)
    {
        var entries=root.GetLocalValueEnumerator();var updates=new List<(DependencyProperty,Brush)>();
        while(entries.MoveNext()){var property=entries.Current.Property;if(!property.ReadOnly&&typeof(Brush).IsAssignableFrom(property.PropertyType)&&root.GetValue(property) is Brush brush)updates.Add((property,Adapt(brush)));}
        foreach(var (property,brush) in updates)root.SetCurrentValue(property,brush);
        // Template values are not necessarily local values (for example the resize grip).
        if(root is System.Windows.Shapes.Shape shape){if(shape.Stroke is Brush stroke)shape.SetCurrentValue(System.Windows.Shapes.Shape.StrokeProperty,Adapt(stroke));if(shape.Fill is Brush fill)shape.SetCurrentValue(System.Windows.Shapes.Shape.FillProperty,Adapt(fill));}
        if(root is Visual or System.Windows.Media.Media3D.Visual3D)for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)Apply(VisualTreeHelper.GetChild(root,i));
        if(root is UIElement element)element.InvalidateVisual();
    }
}

// A small faceted mark drawn directly into the header: no icon plate or independent glow.
public sealed class PrismMark:FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double w=ActualWidth,h=ActualHeight;
        var outline=Geometry.Parse(FormattableString.Invariant($"M {w*.5},{h*.04} L {w*.94},{h*.52} {w*.5},{h*.96} {w*.06},{h*.52} Z"));
        var fill=Theme.Adapt(new LinearGradientBrush(Color.FromArgb(45,106,245,226),Color.FromArgb(30,194,173,255),0));
        var edge=new Pen(Theme.Solid(Color.FromArgb(180,185,218,236)),.9);
        dc.DrawGeometry(fill,edge,outline);dc.DrawLine(edge,new(w*.5,h*.04),new(w*.5,h*.96));dc.DrawLine(edge,new(w*.06,h*.52),new(w*.5,h*.68));dc.DrawLine(edge,new(w*.5,h*.68),new(w*.94,h*.52));
    }
}
