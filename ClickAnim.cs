using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WINHELP;

/// <summary>
/// 通用点击 / 悬停动画行为。
/// 通过附加属性 <see cref="IsEnabled"/> 开启：开启后元素在鼠标按下时轻微缩小（按压感），
/// 松开时以弹性回弹放大再归位（"Pop" 手感）；对可悬停元素（如首页卡片）还可设置 <see cref="HoverScale"/> 实现悬停微放大。
/// 仅修改元素的 RenderTransform（缩放），不与任何控件模板 / 背景冲突，可安全作用于全应用所有 Button 与可点击卡片。
/// </summary>
public static class ClickAnim
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(ClickAnim),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty HoverScaleProperty =
        DependencyProperty.RegisterAttached(
            "HoverScale", typeof(double), typeof(ClickAnim),
            new PropertyMetadata(1.0));

    public static readonly DependencyProperty PressScaleProperty =
        DependencyProperty.RegisterAttached(
            "PressScale", typeof(double), typeof(ClickAnim),
            new PropertyMetadata(0.94));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static double GetHoverScale(DependencyObject obj) => (double)obj.GetValue(HoverScaleProperty);
    public static void SetHoverScale(DependencyObject obj, double value) => obj.SetValue(HoverScaleProperty, value);

    public static double GetPressScale(DependencyObject obj) => (double)obj.GetValue(PressScaleProperty);
    public static void SetPressScale(DependencyObject obj, double value) => obj.SetValue(PressScaleProperty, value);

    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };
    private static readonly BackEase BackPop = new() { EasingMode = EasingMode.EaseOut, Amplitude = 2.4 };

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        if ((bool)e.NewValue)
        {
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.PreviewMouseLeftButtonDown += OnPreviewDown;
            el.PreviewMouseLeftButtonUp += OnPreviewUp;
            el.MouseEnter += OnEnter;
            el.MouseLeave += OnLeave;
        }
        else
        {
            el.PreviewMouseLeftButtonDown -= OnPreviewDown;
            el.PreviewMouseLeftButtonUp -= OnPreviewUp;
            el.MouseEnter -= OnEnter;
            el.MouseLeave -= OnLeave;
        }
    }

    private static void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement el)
            AnimateTo(el, GetPressScale(el), 0.09, EaseOut);
    }

    private static void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement el)
            AnimateTo(el, el.IsMouseOver ? GetHoverScale(el) : 1.0, 0.28, BackPop);
    }

    private static void OnEnter(object sender, MouseEventArgs e)
    {
        if (sender is UIElement el)
        {
            double hover = GetHoverScale(el);
            if (hover != 1.0) AnimateTo(el, hover, 0.18, EaseOut);
        }
    }

    private static void OnLeave(object sender, MouseEventArgs e)
    {
        if (sender is UIElement el)
            AnimateTo(el, 1.0, 0.22, EaseOut);
    }

    private static ScaleTransform GetScale(UIElement el)
    {
        if (el.RenderTransform is ScaleTransform st) return st;
        var ns = new ScaleTransform(1, 1);
        el.RenderTransform = ns;
        return ns;
    }

    private static void AnimateTo(UIElement el, double to, double seconds, IEasingFunction ease)
    {
        var s = GetScale(el);
        var dur = new Duration(TimeSpan.FromSeconds(seconds));
        s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, dur) { EasingFunction = ease });
        s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, dur) { EasingFunction = ease });
    }
}
