// Шаблоны кнопок и тонкой полосы прокрутки панели «Давинчи» и векторные иконки 24×24.
// XAML разбирается при старте (XamlReader) — сборка через csc без компиляции разметки.
// Цвета шаблонов — DynamicResource на кисти Theme (Theme.Publish).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace KompasMcp.Ui
{
  static class Styles
  {
    public static ResourceDictionary Load()
    {
      return (ResourceDictionary)XamlReader.Parse(Xaml);
    }

    // Иконка: контур (stroke) или заливка по пути в системе 24×24, масштабированная до size.
    public static FrameworkElement Icon(string data, Brush brush, double size, bool filled = false)
    {
      Shapes.Path path = new Shapes.Path();
      path.Data = Geometry.Parse(data);
      if (filled) path.Fill = brush;
      else
      {
        path.Stroke = brush;
        path.StrokeThickness = 1.8;
        path.StrokeStartLineCap = PenLineCap.Round;
        path.StrokeEndLineCap = PenLineCap.Round;
        path.StrokeLineJoin = PenLineJoin.Round;
      }
      Canvas canvas = new Canvas { Width = 24, Height = 24 };
      canvas.Children.Add(path);
      return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='IconButton' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Padding' Value='7'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Background='Transparent' CornerRadius='6' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource Pressed}'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='SendButton' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Width='32' Height='32' CornerRadius='16' Background='{DynamicResource Accent}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.88'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.75'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter TargetName='bd' Property='Opacity' Value='0.35'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='PromptButton' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='HorizontalContentAlignment' Value='Left'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Background='Transparent' BorderBrush='{DynamicResource Border}' BorderThickness='1'
                  CornerRadius='8' Padding='12,8'>
            <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource Pressed}'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Choice' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Background='Transparent' BorderBrush='{DynamicResource Border}' BorderThickness='1'
                  CornerRadius='6' Padding='10,5'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
            <Trigger Property='Tag' Value='on'>
              <Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              <Setter TargetName='bd' Property='Background' Value='{DynamicResource Hover}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='AccentButton' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Background='{DynamicResource Accent}' CornerRadius='6' Padding='16,6'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' TextElement.Foreground='{DynamicResource AccentText}'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.88'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.75'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Field' TargetType='TextBox'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='CaretBrush' Value='{DynamicResource Text}'/>
    <Setter Property='SelectionBrush' Value='{DynamicResource Selection}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Padding' Value='8,6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='bd' Background='{DynamicResource Surface}' BorderBrush='{DynamicResource Border}' BorderThickness='1' CornerRadius='6'>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'
                          VerticalScrollBarVisibility='Hidden' HorizontalScrollBarVisibility='Hidden'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource FocusBorder}'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='SecretField' TargetType='PasswordBox'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='CaretBrush' Value='{DynamicResource Text}'/>
    <Setter Property='SelectionBrush' Value='{DynamicResource Selection}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Padding' Value='8,6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='PasswordBox'>
          <Border x:Name='bd' Background='{DynamicResource Surface}' BorderBrush='{DynamicResource Border}' BorderThickness='1' CornerRadius='6'>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'
                          VerticalScrollBarVisibility='Hidden' HorizontalScrollBarVisibility='Hidden'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource FocusBorder}'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Check' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Border x:Name='box' Width='16' Height='16' CornerRadius='4' BorderThickness='1' VerticalAlignment='Center'
                    BorderBrush='{DynamicResource FocusBorder}' Background='{DynamicResource Surface}'>
              <Path x:Name='mark' Data='M3.5 8 L6.5 11 L12 5' Stroke='{DynamicResource AccentText}' StrokeThickness='2'
                    StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Visibility='Collapsed'/>
            </Border>
            <ContentPresenter Margin='8,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='box' Property='Background' Value='{DynamicResource Accent}'/>
              <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              <Setter TargetName='mark' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='PageButton' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='SlimThumb' TargetType='Thumb'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border CornerRadius='3' Margin='2' Background='{DynamicResource ScrollThumb}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='10'/>
    <Setter Property='MinWidth' Value='10'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource PageButton}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource PageButton}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>
              <Track.Thumb><Thumb Style='{StaticResource SlimThumb}'/></Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='10'/>
        <Setter Property='MinHeight' Value='10'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
              <Grid Background='Transparent'>
                <Track x:Name='PART_Track'>
                  <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource PageButton}' Command='ScrollBar.PageLeftCommand'/></Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource PageButton}' Command='ScrollBar.PageRightCommand'/></Track.IncreaseRepeatButton>
                  <Track.Thumb><Thumb Style='{StaticResource SlimThumb}'/></Track.Thumb>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>";
  }

  // Контурные иконки в системе 24×24 (Stop — заливкой).
  static class Icons
  {
    public const string Send = "M12 19 V5 M5.5 11.5 L12 5 L18.5 11.5";
    public const string Stop = "M8 7 H16 A1 1 0 0 1 17 8 V16 A1 1 0 0 1 16 17 H8 A1 1 0 0 1 7 16 V8 A1 1 0 0 1 8 7 Z";
    public const string Attach = "M20 11.5 L12 19.5 A5 5 0 0 1 5 12.5 L13.5 4 A3.4 3.4 0 0 1 18.3 8.8 L10 17 A1.8 1.8 0 0 1 7.5 14.5 L15 7";
    public const string NewChat = "M12 4.5 H6.5 A2 2 0 0 0 4.5 6.5 V17.5 A2 2 0 0 0 6.5 19.5 H17.5 A2 2 0 0 0 19.5 17.5 V12 M17.8 3.7 A1.9 1.9 0 0 1 20.5 6.4 L12.5 14.4 L9 15.2 L9.8 11.7 Z";
    public const string ChevronRight = "M9.5 6.5 L15 12 L9.5 17.5";
    public const string ChevronDown = "M6.5 9.5 L12 15 L17.5 9.5";
    public const string Close = "M7 7 L17 17 M17 7 L7 17";
    public const string Settings = "M4 7 H12.5 M17.5 7 H20 M15 4.8 A2.2 2.2 0 1 1 14.99 4.8 Z M4 17 H6.5 M11.5 17 H20 M9 14.8 A2.2 2.2 0 1 1 8.99 14.8 Z";
    public const string Back = "M14.5 6 L8.5 12 L14.5 18";
  }
}
