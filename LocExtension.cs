using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace WINHELP
{
    /// <summary>
    /// XAML 本地化标记扩展：<c>Text="{loc:Loc 中文|English}"</c> 或 <c>Content="{loc:Loc 中文|English}"</c>。
    /// 通过绑定到内部 <see cref="LocValue"/>（INotifyPropertyChanged），在 <see cref="UiLanguage.Changed"/>
    /// 触发时自动刷新所有存活的扩展，无需页面手动重渲染静态文本。
    /// 格式为「中文|英文」，未含 | 时回退中文。
    /// 注意：XAML 标记扩展按半角逗号切分位置参数，故英文中的逗号会被拆成多段；本扩展提供
    /// 多参数重载（最多 8 段）并原样以 ", " 拼接还原，英文里写逗号无需转义。英文里请避免
    /// 单/双引号（会破坏标记扩展解析），用空格或替换措辞即可。
    /// 用法：在 XAML 根加 <c>xmlns:loc="clr-namespace:WINHELP"</c>。
    /// </summary>
    /// <summary>XAML 本地化标记扩展：支持 Text="{loc:Loc 中文|英文}" 写法。</summary>
    [MarkupExtensionReturnType(typeof(string))]
    public class LocExtension : MarkupExtension
    {
        private readonly string[] _parts;

        public LocExtension(string a) : this(new[] { a }) { }
        public LocExtension(string a, string b) : this(new[] { a, b }) { }
        public LocExtension(string a, string b, string c) : this(new[] { a, b, c }) { }
        public LocExtension(string a, string b, string c, string d) : this(new[] { a, b, c, d }) { }
        public LocExtension(string a, string b, string c, string d, string e) : this(new[] { a, b, c, d, e }) { }
        public LocExtension(string a, string b, string c, string d, string e, string f) : this(new[] { a, b, c, d, e, f }) { }
        public LocExtension(string a, string b, string c, string d, string e, string f, string g) : this(new[] { a, b, c, d, e, f, g }) { }
        public LocExtension(string a, string b, string c, string d, string e, string f, string g, string h) : this(new[] { a, b, c, d, e, f, g, h }) { }

        private LocExtension(string[] parts) { _parts = parts ?? Array.Empty<string>(); }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var target = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
            if (target?.TargetObject is DependencyObject && target.TargetProperty is DependencyProperty)
            {
                var loc = new LocValue(_parts);
                LocValue.Register(loc);
                var binding = new Binding
                {
                    Source = loc,
                    Path = new PropertyPath(nameof(LocValue.Text)),
                    Mode = BindingMode.OneWay
                };
                return binding.ProvideValue(serviceProvider);
            }
            // 非依赖属性（极少数场景）直接返回当前值
            return Resolve(_parts);
        }

        private static string Resolve(string[] parts)
        {
            var raw = parts == null || parts.Length == 0 ? string.Empty : string.Join(", ", parts);
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            int i = raw.IndexOf('|');
            string zh = i < 0 ? raw : raw.Substring(0, i);
            string en = i < 0 ? raw : raw.Substring(i + 1);
            return UiLanguage.L(zh, en);
        }

        private sealed class LocValue : INotifyPropertyChanged
        {
            private static readonly List<WeakReference<LocValue>> _instances = new();
            private static bool _subscribed;

            private readonly string[] _parts;

            public LocValue(string[] parts) { _parts = parts; }

            public string Text => Resolve(_parts);

            public event PropertyChangedEventHandler? PropertyChanged;

            public static void Register(LocValue value)
            {
                _instances.Add(new WeakReference<LocValue>(value));
                if (!_subscribed)
                {
                    UiLanguage.Changed += OnLanguageChanged;
                    _subscribed = true;
                }
            }

            private static void OnLanguageChanged()
            {
                // 清理已回收的弱引用，并通知存活实例刷新
                _instances.RemoveAll(wr => !wr.TryGetTarget(out _));
                foreach (var wr in _instances)
                {
                    if (wr.TryGetTarget(out var v))
                        v.PropertyChanged?.Invoke(v, new PropertyChangedEventArgs(nameof(Text)));
                }
            }
        }
    }
}
