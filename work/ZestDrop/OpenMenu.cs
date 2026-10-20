using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ZestDrop;

// A small menu at the pointer listing what can be done with some files. It is used when files arrive without a drag:
// from Explorer's right-click entry or from the clipboard.
internal static class OpenMenu
{
    public static void ShowList(string[] paths, List<Operation> options, Action<string[], Operation> submit)
    {
        var menu = new ContextMenu { StaysOpen = false, FontFamily = UiTheme.Font, FontSize = 13 };
        foreach (var option in options)
            menu.Items.Add(Item(paths, option, submit));
        Open(menu);
    }

    private static MenuItem Item(string[] paths, Operation option, Action<string[], Operation> submit)
    {
        var item = new MenuItem { Header = (option.Recent ? "↻ " : "") + option.Label, Foreground = UiTheme.Ink };
        item.Click += (_, _) => submit(paths, option);
        return item;
    }

    public static void Show(string[] paths, Action<string[], Operation> submit)
    {
        var menu = new ContextMenu { StaysOpen = false, FontFamily = UiTheme.Font, FontSize = 13 };
        var formats = Catalog.Options(paths, false);
        var tools = Catalog.Options(paths, true);
        if (formats.Count > 0 && !Catalog.PackingOnly(paths))
        {
            var convert = new MenuItem { Header = L.T("转换为") };
            foreach (var option in formats)
                convert.Items.Add(Item(paths, option, submit));
            menu.Items.Add(convert);
        }
        foreach (var option in tools)
            menu.Items.Add(Item(paths, option, submit));
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = L.T("无可用操作"), IsEnabled = false });
        Open(menu);
    }

    private static void Open(ContextMenu menu)
    {
        UiTheme.Refresh();
        menu.Resources = CreateResources();
        menu.FlowDirection = L.Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        // A transparent full-screen window to detect clicks outside the menu and close it immediately.
        var anchor = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight
        };

        void CloseMenu()
        {
            if (menu.IsOpen)
                menu.IsOpen = false;
            try { anchor.Close(); } catch { }
        }

        anchor.PreviewMouseDown += (_, _) => CloseMenu();
        anchor.Deactivated += (_, _) => CloseMenu();
        anchor.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) CloseMenu(); };

        anchor.Show();
        anchor.Activate();

        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.Closed += (_, _) => CloseMenu();
        menu.IsOpen = true;
    }

    private static ResourceDictionary CreateResources()
    {
        return (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(UiTheme.Fill("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                            xmlns:p="clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework">
          <Style TargetType="ContextMenu">
            <Setter Property="Background" Value="{Surface}"/>
            <Setter Property="Foreground" Value="{Ink}"/>
            <Setter Property="BorderBrush" Value="{Line}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="4"/>
            <Setter Property="FontFamily" Value="Segoe UI, ./assets/fonts/#Source Han Sans CN Medium"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ContextMenu">
                  <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                    <Border.Effect>
                      <DropShadowEffect BlurRadius="12" ShadowDepth="2" Opacity="0.25" Color="#000000"/>
                    </Border.Effect>
                    <ScrollViewer CanContentScroll="True" VerticalScrollBarVisibility="Auto">
                      <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/>
                    </ScrollViewer>
                  </Border>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <Style TargetType="MenuItem">
            <Setter Property="Foreground" Value="{Ink}"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Padding" Value="12,7"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="MenuItem">
                  <Grid>
                    <Border x:Name="border" Background="{TemplateBinding Background}" CornerRadius="6" Margin="0,1" Padding="{TemplateBinding Padding}">
                      <Grid>
                        <Grid.ColumnDefinitions>
                          <ColumnDefinition Width="*"/>
                          <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <ContentPresenter ContentSource="Header" VerticalAlignment="Center" RecognizesAccessKey="True"/>
                        <Path x:Name="arrow" Grid.Column="1" Data="M 0,0 L 4,4 L 0,8" Stroke="{Muted}" StrokeThickness="1.5" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" VerticalAlignment="Center" Margin="16,0,2,0" Visibility="Collapsed"/>
                      </Grid>
                    </Border>
                    <p:Popup x:Name="PART_Popup" AllowsTransparency="True" Placement="Right" PlacementTarget="{Binding ElementName=border}" HorizontalOffset="0" VerticalOffset="-4" Focusable="False" IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}">
                      <Border FlowDirection="{TemplateBinding FlowDirection}" Background="{Surface}" BorderBrush="{Line}" BorderThickness="1" CornerRadius="8" Padding="4" Margin="4">
                        <Border.Effect>
                          <DropShadowEffect BlurRadius="12" ShadowDepth="2" Opacity="0.25" Color="#000000"/>
                        </Border.Effect>
                        <ScrollViewer MaxHeight="380" CanContentScroll="True" VerticalScrollBarVisibility="Auto">
                          <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/>
                        </ScrollViewer>
                      </Border>
                    </p:Popup>
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property="HasItems" Value="True">
                      <Setter TargetName="arrow" Property="Visibility" Value="Visible"/>
                    </Trigger>
                    <Trigger Property="IsHighlighted" Value="True">
                      <Setter TargetName="border" Property="Background" Value="{Hover}"/>
                      <Setter TargetName="arrow" Property="Stroke" Value="{Ink}"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter Property="Opacity" Value="0.45"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """));
    }
}
