using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace Prism;

/// <summary>Application-wide dark glass styling for standard WPF controls used by Prism's windows.</summary>
public static class Theme
{
    static readonly Color Base=Color.FromRgb(14,22,38), BaseDeep=Color.FromRgb(20,18,44);
    public static Brush DialogBackground()
    {
        var brush=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,1)};
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(16,30,50),0));
        brush.GradientStops.Add(new GradientStop(Base,.55));
        brush.GradientStops.Add(new GradientStop(BaseDeep,1));
        brush.Freeze();return brush;
    }
    public static void Install(Application app)
    {
        try{app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Styles));}
        catch(XamlParseException){/* Stock controls remain usable if the theme cannot load. */}
    }
    const string Styles="""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <SolidColorBrush x:Key="Prism.Ink" Color="#F8FBFF"/>
  <SolidColorBrush x:Key="Prism.Muted" Color="#B9CDE1"/>
  <SolidColorBrush x:Key="Prism.Field" Color="#141F35"/>
  <SolidColorBrush x:Key="Prism.FieldHover" Color="#1A2843"/>
  <SolidColorBrush x:Key="Prism.Line" Color="#33476B"/>
  <SolidColorBrush x:Key="Prism.LineHover" Color="#5A74A3"/>
  <SolidColorBrush x:Key="Prism.Popup" Color="#162037"/>
  <SolidColorBrush x:Key="Prism.Highlight" Color="#24365C"/>
  <SolidColorBrush x:Key="Prism.Selected" Color="#2C4272"/>
  <LinearGradientBrush x:Key="Prism.Accent" StartPoint="0,0" EndPoint="1,1">
    <GradientStop Color="#6AF5E2" Offset="0"/><GradientStop Color="#A99BFF" Offset="1"/>
  </LinearGradientBrush>
  <DropShadowEffect x:Key="Prism.PopupShadow" BlurRadius="18" ShadowDepth="5" Direction="270" Opacity=".45" Color="#000000"/>

  <Style x:Key="{x:Static SystemParameters.FocusVisualStyleKey}">
    <Setter Property="Control.Template"><Setter.Value><ControlTemplate>
      <Border Margin="-3" CornerRadius="10" BorderThickness="1.5" BorderBrush="#9A6AF5E2" SnapsToDevicePixels="True"/>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType="ToolTip">
    <Setter Property="Foreground" Value="{StaticResource Prism.Ink}"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="HasDropShadow" Value="False"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ToolTip">
      <Border Margin="0,0,8,8" Background="#F2141E33" BorderBrush="#4C6590" BorderThickness="1" CornerRadius="9" Padding="11,7" MaxWidth="380" Effect="{StaticResource Prism.PopupShadow}">
        <ContentPresenter>
          <ContentPresenter.Resources><Style TargetType="TextBlock"><Setter Property="TextWrapping" Value="Wrap"/><Setter Property="LineHeight" Value="17"/></Style></ContentPresenter.Resources>
        </ContentPresenter>
      </Border>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType="ScrollBar">
    <Setter Property="Width" Value="10"/>
    <Setter Property="MinWidth" Value="10"/>
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
      <Border Background="{TemplateBinding Background}" CornerRadius="5">
        <Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="Vertical">
          <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb">
            <Border x:Name="thumb" Background="#4A5F82" CornerRadius="4" Margin="2"/>
            <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="thumb" Property="Background" Value="#7189B3"/></Trigger></ControlTemplate.Triggers>
          </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
        </Track>
      </Border>
    </ControlTemplate></Setter.Value></Setter>
    <Style.Triggers>
      <Trigger Property="Orientation" Value="Horizontal">
        <Setter Property="Width" Value="Auto"/><Setter Property="MinWidth" Value="0"/><Setter Property="Height" Value="10"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
          <Border Background="{TemplateBinding Background}" CornerRadius="5">
            <Track x:Name="PART_Track" Orientation="Horizontal">
              <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#4A5F82" CornerRadius="4" Margin="2"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
            </Track>
          </Border>
        </ControlTemplate></Setter.Value></Setter>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType="Button">
    <Setter Property="Foreground" Value="{StaticResource Prism.Ink}"/>
    <Setter Property="Background" Value="#253A54"/>
    <Setter Property="BorderBrush" Value="#5A74A3"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
      <Grid>
        <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="10"/>
        <Border x:Name="sheen" Background="#FFFFFF" Opacity="0" CornerRadius="10" IsHitTestVisible="False"/>
        <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="sheen" Property="Opacity" Value=".12"/></Trigger>
        <Trigger Property="IsPressed" Value="True"><Setter TargetName="sheen" Property="Opacity" Value=".03"/></Trigger>
        <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".45"/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType="CheckBox">
    <Setter Property="Foreground" Value="{StaticResource Prism.Ink}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox">
      <Grid Background="Transparent">
        <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
        <Border x:Name="box" Width="18" Height="18" CornerRadius="5" Background="{StaticResource Prism.Field}" BorderBrush="#5A6F95" BorderThickness="1.2" VerticalAlignment="Center">
          <Path x:Name="mark" Data="M3.5,9 L7.2,12.5 L14,5" Stroke="#0E1A2A" StrokeThickness="2.2" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" Opacity="0" Margin="-0.5,-0.5,0,0"/>
        </Border>
        <ContentPresenter Grid.Column="1" Margin="10,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="box" Property="BorderBrush" Value="#8EA6D2"/></Trigger>
        <Trigger Property="IsChecked" Value="True">
          <Setter TargetName="box" Property="Background" Value="{StaticResource Prism.Accent}"/>
          <Setter TargetName="box" Property="BorderBrush" Value="#8CEFE2"/>
          <Setter TargetName="mark" Property="Opacity" Value="1"/>
        </Trigger>
        <Trigger Property="IsChecked" Value="{x:Null}">
          <Setter TargetName="mark" Property="Data" Value="M4.5,9 L13.5,9"/><Setter TargetName="mark" Property="Opacity" Value="1"/><Setter TargetName="mark" Property="Stroke" Value="#8EA6D2"/>
        </Trigger>
        <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".45"/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType="TextBox">
    <Setter Property="Foreground" Value="{StaticResource Prism.Ink}"/>
    <Setter Property="Background" Value="{StaticResource Prism.Field}"/>
    <Setter Property="BorderBrush" Value="{StaticResource Prism.Line}"/>
    <Setter Property="CaretBrush" Value="#6AF5E2"/>
    <Setter Property="SelectionBrush" Value="#6A8CFF"/>
    <Setter Property="Padding" Value="8,6"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox">
      <Border x:Name="field" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="9">
        <ScrollViewer x:Name="PART_ContentHost" Focusable="False" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Auto"/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="field" Property="BorderBrush" Value="{StaticResource Prism.LineHover}"/></Trigger>
        <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="field" Property="BorderBrush" Value="#6AF5E2"/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style x:Key="Prism.ComboToggle" TargetType="ToggleButton">
    <Setter Property="Focusable" Value="False"/>
    <Setter Property="ClickMode" Value="Press"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ToggleButton">
      <Border x:Name="field" Background="{StaticResource Prism.Field}" BorderBrush="{StaticResource Prism.Line}" BorderThickness="1" CornerRadius="9">
        <Path x:Name="chevron" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,12,0" Data="M0,0 L4.5,4.5 L9,0" Stroke="#B9CDE1" StrokeThickness="1.6" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="field" Property="Background" Value="{StaticResource Prism.FieldHover}"/><Setter TargetName="field" Property="BorderBrush" Value="{StaticResource Prism.LineHover}"/></Trigger>
        <Trigger Property="IsChecked" Value="True"><Setter TargetName="field" Property="BorderBrush" Value="#6AF5E2"/><Setter TargetName="chevron" Property="Stroke" Value="#6AF5E2"/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType="ComboBox">
    <Setter Property="Foreground" Value="{StaticResource Prism.Ink}"/>
    <Setter Property="Padding" Value="10,7,30,7"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="ScrollViewer.CanContentScroll" Value="True"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
      <Grid>
        <ToggleButton Style="{StaticResource Prism.ComboToggle}" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"/>
        <ContentPresenter Margin="{TemplateBinding Padding}" IsHitTestVisible="False" VerticalAlignment="Center" HorizontalAlignment="Left"
          Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"/>
        <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
          <Border Margin="0,4,10,12" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="{TemplateBinding MaxDropDownHeight}"
                  Background="{StaticResource Prism.Popup}" BorderBrush="#3E5482" BorderThickness="1" CornerRadius="11" Padding="4" Effect="{StaticResource Prism.PopupShadow}">
            <ScrollViewer SnapsToDevicePixels="True"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
          </Border>
        </Popup>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".45"/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType="ComboBoxItem">
    <Setter Property="Foreground" Value="{StaticResource Prism.Ink}"/>
    <Setter Property="Padding" Value="10,6"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
      <Border x:Name="row" Background="Transparent" CornerRadius="7" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border>
      <ControlTemplate.Triggers>
        <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="row" Property="Background" Value="{StaticResource Prism.Highlight}"/></Trigger>
        <Trigger Property="IsSelected" Value="True"><Setter TargetName="row" Property="Background" Value="{StaticResource Prism.Selected}"/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style x:Key="Prism.SliderFill" TargetType="RepeatButton">
    <Setter Property="Focusable" Value="False"/><Setter Property="IsTabStop" Value="False"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RepeatButton">
      <Border Height="5" CornerRadius="2.5" Background="{TemplateBinding Background}"/>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="Slider">
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider">
      <Track x:Name="PART_Track" MinHeight="22">
        <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource Prism.SliderFill}" Background="{StaticResource Prism.Accent}" Command="Slider.DecreaseLarge"/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource Prism.SliderFill}" Background="#2A3A58" Command="Slider.IncreaseLarge"/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb">
          <Ellipse x:Name="knob" Width="18" Height="18" Fill="#F8FBFF" Stroke="#6AF5E2" StrokeThickness="3"/>
          <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="knob" Property="Stroke" Value="#A99BFF"/></Trigger></ControlTemplate.Triggers>
        </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
      </Track>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType="TabControl">
    <Setter Property="Foreground" Value="{StaticResource Prism.Ink}"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabControl">
      <DockPanel>
        <Border DockPanel.Dock="Top" Background="#B3111A2E" BorderBrush="#2A3A5A" BorderThickness="1" CornerRadius="13" Padding="4" HorizontalAlignment="Left">
          <TabPanel IsItemsHost="True"/>
        </Border>
        <Border Margin="0,12,0,0" Background="#8C141F36" BorderBrush="#2A3A5A" BorderThickness="1" CornerRadius="14">
          <ContentPresenter ContentSource="SelectedContent"/>
        </Border>
      </DockPanel>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="TabItem">
    <Setter Property="Foreground" Value="{StaticResource Prism.Muted}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem">
      <Border x:Name="pill" Background="Transparent" CornerRadius="9" Padding="13,6" Margin="1">
        <ContentPresenter ContentSource="Header" TextElement.FontSize="12.5"/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="pill" Property="Background" Value="#1E2C49"/><Setter Property="Foreground" Value="#F8FBFF"/></Trigger>
        <Trigger Property="IsSelected" Value="True">
          <Setter TargetName="pill" Property="Background" Value="#2C4272"/><Setter Property="Foreground" Value="#F8FBFF"/>
          <Setter TargetName="pill" Property="BorderBrush" Value="#4E6BA3"/><Setter TargetName="pill" Property="BorderThickness" Value="1"/>
        </Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType="ListBox">
    <Setter Property="Background" Value="{StaticResource Prism.Field}"/>
    <Setter Property="Foreground" Value="{StaticResource Prism.Ink}"/>
    <Setter Property="BorderBrush" Value="{StaticResource Prism.Line}"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBox">
      <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="11" Padding="4">
        <ScrollViewer Focusable="False"><ItemsPresenter/></ScrollViewer>
      </Border>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="ListBoxItem">
    <Setter Property="Foreground" Value="{StaticResource Prism.Ink}"/>
    <Setter Property="Padding" Value="6,2"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem">
      <Border x:Name="row" Background="Transparent" CornerRadius="7" Padding="{TemplateBinding Padding}" Margin="0,1"><ContentPresenter/></Border>
      <ControlTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="row" Property="Background" Value="{StaticResource Prism.Highlight}"/></Trigger>
        <Trigger Property="IsSelected" Value="True"><Setter TargetName="row" Property="Background" Value="{StaticResource Prism.Selected}"/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType="ContextMenu">
    <Setter Property="Foreground" Value="{StaticResource Prism.Ink}"/>
    <Setter Property="FontSize" Value="12.5"/>
    <Setter Property="HasDropShadow" Value="False"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ContextMenu">
      <Border Margin="0,0,10,12" Background="#F5141E33" BorderBrush="#3E5482" BorderThickness="1" CornerRadius="12" Padding="5" MinWidth="230" Effect="{StaticResource Prism.PopupShadow}">
        <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
      </Border>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="MenuItem">
    <Setter Property="Foreground" Value="{StaticResource Prism.Ink}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem">
      <Border x:Name="row" Background="Transparent" CornerRadius="7" Padding="11,6">
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <ContentPresenter ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center"/>
          <TextBlock Grid.Column="1" Text="{TemplateBinding InputGestureText}" Foreground="#8EA3C2" Margin="24,0,0,0" FontSize="11" VerticalAlignment="Center"/>
        </Grid>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="row" Property="Background" Value="{StaticResource Prism.Highlight}"/></Trigger>
        <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".45"/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Separator">
      <Border Height="1" Margin="8,5" Background="#2C3D60"/>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>
""";
}
