using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ZmkOverlay.App.Text;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.App.Render;

/// <summary>
/// レイアウト + キーマップ → 表示用のビジュアルツリー。
///
/// 全レイヤーぶんを読み込み時にまとめて作り、寸法を揃えておく。
/// 切り替えのたびに作り直すと、コンボ行の有無などで板の大きさが変わり、
/// 画面上の位置が跳ねて目で追えなくなるため。切り替えは作り置きの差し替えだけで済む。
/// </summary>
internal static class KeymapRenderer
{
    /// <summary>
    /// 全レイヤーのパネル。添字はキーマップ上の並び（ZMK のレイヤー番号とは別物）。
    /// オーバーレイ表示と確認用の PNG 出力の両方がこれを使うので、見た目が食い違うことがない。
    /// </summary>
    /// <param name="interactive">
    /// クリックを受け取る状態か。カーソルの形とマウスを載せたときの色だけが変わる（寸法は変わらない）。
    /// 設定から読まずに引数で受けるのは、設定画面と初期設定のプレビュー・PNG 出力も同じ設定を渡してくるため。
    /// 設定から読むと、押しても何も起きないプレビューのタブに手のカーソルが出る。
    /// </param>
    public static IReadOnlyList<FrameworkElement> BuildPanels(
        AppConfig config, PhysicalLayout layout, Keymap keymap, bool interactive = false)
    {
        var metrics = new Metrics(config.KeyUnitPx);

        var panels = Enumerable.Range(0, keymap.Layers.Count)
            .Select(slot => BuildPanel(config, layout, keymap, slot, metrics, interactive))
            .ToList();

        // 幅はキー配置で決まり全レイヤー共通だが、高さはコンボ行の有無と
        // 折り返しでレイヤーごとに変わる。いちばん大きいものに揃える。
        var width = 0.0;
        var height = 0.0;

        foreach (var panel in panels)
        {
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = Math.Max(width, panel.DesiredSize.Width);
            height = Math.Max(height, panel.DesiredSize.Height);
        }

        foreach (var panel in panels)
        {
            panel.MinWidth = width;
            panel.MinHeight = height;
        }

        return panels;
    }

    /// <summary>キー 1u の大きさから決まる寸法。ここを見れば全体の比率が分かるようにしてある。</summary>
    private readonly record struct Metrics(double Unit)
    {
        public double KeyInset => Math.Max(2, Unit * 0.05);
        public double KeyRadius => Unit * 0.13;
        public double KeyTextPadding => Unit * 0.06;

        /// <summary>1 文字のラベルは大きく、それ以外は控えめに。そこから幅に収まるまで縮める。</summary>
        public double TapSingle => Unit * 0.38;
        public double TapMulti => Unit * 0.27;
        public double Hold => Math.Max(9, Unit * 0.19);

        /// <summary>これより小さくすると読めない。はみ出しは許してでもここで止める。</summary>
        public double MinText => 8;

        public double Tab => Math.Max(11, Unit * 0.23);
        public double Faint => Math.Max(10, Unit * 0.21);
        public double RowGap => Unit * 0.14;
        public double PanelPadding => Unit * 0.28;
    }

    /// <summary>
    /// 上からタブ行・キー・コンボ行。板の幅は常にキー配置の幅に揃える。
    ///
    /// 操作ヒントは、タブと並べて 1 行に収まるならタブ行の右端に、
    /// 収まらなければコンボ行の右端に置く。ヒントのせいで板が横に広がり、
    /// キーの左右に空白ができるのを避けるため。判定はタブの幅だけで決まるので
    /// 全レイヤーで同じ結果になり、寸法を揃える妨げにならない。
    /// </summary>
    private static FrameworkElement BuildPanel(
        AppConfig config, PhysicalLayout layout, Keymap keymap, int slot, Metrics m, bool interactive)
    {
        var (keys, keysWidth) = BuildKeys(layout, keymap.Layers[slot], m);

        var tabs = BuildTabs(config, keymap, slot, m, interactive);
        tabs.MaxWidth = keysWidth;

        var hint = BuildHint(config, keymap, m);

        tabs.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var hintOnTop = tabs.DesiredSize.Width + hint.DesiredSize.Width <= keysWidth;

        var stack = new StackPanel { Orientation = Orientation.Vertical };

        stack.Children.Add(RowWithHint(tabs, hintOnTop ? hint : null, new Thickness(0, 0, 0, m.RowGap)));
        stack.Children.Add(keys);

        var combosWidth = hintOnTop ? keysWidth : Math.Max(0, keysWidth - hint.DesiredSize.Width);
        var combos = BuildCombos(keymap, slot, combosWidth, m);

        if (combos is not null || !hintOnTop)
            stack.Children.Add(RowWithHint(combos, hintOnTop ? null : hint, new Thickness(0, m.RowGap, 0, 0)));

        var border = new Border
        {
            Background = Theme.Panel,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(m.Unit * 0.24),
            Padding = new Thickness(m.PanelPadding),
            Child = stack,
        };

        TextElement.SetFontFamily(border, Theme.Font);

        // 板そのものはドラッグで動かせる。掴めることをカーソルで示す（タブの上だけ手のカーソルが勝つ）。
        if (interactive) border.Cursor = Cursors.SizeAll;

        return border;
    }

    /// <summary>左に本体、右端にヒント（あれば）を置いた 1 行。</summary>
    private static UIElement RowWithHint(UIElement? body, UIElement? hint, Thickness margin)
    {
        var row = new DockPanel { LastChildFill = true, Margin = margin };

        if (hint is not null)
        {
            DockPanel.SetDock(hint, Dock.Right);
            row.Children.Add(hint);
        }

        if (body is not null) row.Children.Add(body);

        return row;
    }

    // ---- タブと操作ヒント ----

    private static WrapPanel BuildTabs(AppConfig config, Keymap keymap, int slot, Metrics m, bool interactive)
    {
        var tabs = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var autoShown = keymap.AutoShownLayerIds();

        for (var i = 0; i < keymap.Layers.Count; i++)
        {
            var layer = keymap.Layers[i];

            // 合図キーを持たず、合図キーの組み合わせ（条件付きレイヤー）でも入らないレイヤーは、
            // キーボードを操作しても自動では出てこない。ベースレイヤーは合図が無くて当然なので対象外。
            var followable = i == 0 || !config.LayerSync.Enabled || autoShown.Contains(layer.Index);

            tabs.Children.Add(BuildTab(layer, active: i == slot, followable, m, interactive));
        }

        return tabs;
    }

    /// <summary>自動で出てこないレイヤーは、手で選ぶしかないことを淡い文字と点線の枠で示す。</summary>
    private static UIElement BuildTab(Layer layer, bool active, bool followable, Metrics m, bool interactive)
    {
        // 塗る板は選択中でなくても置く。マウスを載せたときに差し替えるのがこれ 1 枚で済み、
        // 角丸のまま光らせられる（セル全体を塗ると、選択中の丸いタブの肩が角張って見える）。
        // 子を持たない Border は寸法 0 なので、全レイヤーで寸法を揃える仕組みには影響しない。
        var idle = active ? Theme.TabActiveBg : Brushes.Transparent;

        var fill = new Border
        {
            Background = idle,
            CornerRadius = new CornerRadius(m.Tab),
        };

        var cell = new LayerTab
        {
            LayerId = layer.Index,
            Margin = new Thickness(0, 0, m.Unit * 0.05, m.Unit * 0.04),

            // Background が null の Panel は当たり判定に出てこない。透明でも置いておくと
            // 丸い板の外側（角）も含めてタブ全体が押せる。不透明度 0 なので見た目は変わらず、
            // 背景は寸法の計算にも関わらない。
            Background = Brushes.Transparent,

            Fill = fill,
            IdleBackground = idle,
            HoverBackground = interactive
                ? (active ? Theme.TabActiveHoverBg : Theme.TabHoverBg)
                : null,
        };

        if (interactive) cell.Cursor = Cursors.Hand;

        cell.Children.Add(fill);

        if (!active && !followable)
        {
            cell.Children.Add(new Rectangle
            {
                Stroke = Theme.TabUnavailableOutline,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                RadiusX = m.Tab * 0.9,
                RadiusY = m.Tab * 0.9,
            });
        }

        // 選択中だけ太字にすると幅が変わり、タブ行の長さがレイヤーごとにずれる。
        // 強調は背景と文字色だけで行う。マウスを載せたときも色しか変えない。
        cell.Children.Add(new TextBlock
        {
            Text = $"L{layer.Index} {layer.Name}".TrimEnd(),
            FontSize = m.Tab,
            Foreground = active ? Theme.TabActiveText
                       : followable ? Theme.TabIdleText
                       : Theme.TabUnavailableText,
            Margin = new Thickness(m.Unit * 0.15, m.Unit * 0.03, m.Unit * 0.15, m.Unit * 0.04),
        });

        return cell;
    }

    /// <summary>
    /// 覚えておくべき操作だけを短く出す。合図キーの対応などの詳しい状態は設定画面で見る。
    /// </summary>
    private static TextBlock BuildHint(AppConfig config, Keymap keymap, Metrics m)
    {
        var parts = new List<string>();

        // 文字の入力に使われる組み合わせは登録しないので、案内もしない。
        if (!HotkeyRules.TypesCharacter(config.ToggleHotkey))
            parts.Add(UiText.HintToggle(config.ToggleHotkey.ToString()));

        if (config.EnableManualLayerKeys && LayerKeysHint(config, keymap) is { } keys)
            parts.Add(UiText.HintLayers(keys));

        return new TextBlock
        {
            Text = string.Join("  ·  ", parts),
            FontSize = m.Faint,
            Foreground = Theme.HintText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(m.Unit * 0.5, 0, m.Unit * 0.04, 0),
        };
    }

    /// <summary>
    /// レイヤーのショートカットを 1 つの短い文にまとめる。まとめられなければ null。
    ///
    /// 全レイヤーで同じ文にすること。レイヤーごとに変えると、ヒントの幅でタブ行に収まるかが変わり、
    /// 板の中の行の並びがレイヤーごとに跳ねる（「板の大きさを固定する」の節）。
    /// </summary>
    private static string? LayerKeysHint(AppConfig config, Keymap keymap)
    {
        // 既定の Ctrl+Alt+数字 は範囲でまとめて短く書ける。
        // この PC の配列で文字の入力に使われて登録していない番号があるときは、範囲では書けない。
        var numbered = keymap.Layers.Select(l => l.Index).Where(i => i is >= 0 and <= 9).ToList();

        if (config.LayerHotkeys.Count == 0)
        {
            if (numbered.Count == 0) return null;

            var specs = numbered.Select(config.ManualLayerHotkey).ToList();
            if (specs.Any(s => s is null)) return null;

            var range = numbered.Count == 1
                ? numbered[0].ToString(CultureInfo.InvariantCulture)
                : $"{numbered.Min()}–{numbered.Max()}";

            return With(specs[0]!, range);
        }

        // 割り当てを変えている場合。全レイヤーに割り当てがあり修飾キーが共通なら、キーを並べる。
        // 以前は変えた時点で出さなくしていたが、数字キーの無いキーボードで割り当てを変えた人ほど
        // 手がかりを失っていた（変える理由がいちばんある人たち）。
        var assigned = new List<HotkeySpec>();

        foreach (var layer in keymap.Layers)
        {
            if (config.ManualLayerHotkey(layer.Index) is not { } spec) return null;
            assigned.Add(spec);
        }

        if (assigned.Count == 0) return null;

        var prefix = Modifiers(assigned[0]);
        if (assigned.Any(s => Modifiers(s) != prefix)) return null;

        return With(assigned[0], string.Join(" ", assigned.Select(s => s.Key)));
    }

    /// <summary>修飾キーの並び。表記の揺れは設定画面が正すので、ここでは書かれたまま使う。</summary>
    private static string Modifiers(HotkeySpec spec) => string.Join("+", spec.Modifiers);

    /// <summary>共通の修飾キーに、キーの部分をつなげる。</summary>
    private static string With(HotkeySpec spec, string keys)
    {
        var prefix = Modifiers(spec);
        return prefix.Length == 0 ? keys : prefix + "+" + keys;
    }

    // ---- キー ----

    private static (UIElement Keys, double Width) BuildKeys(PhysicalLayout layout, Layer layer, Metrics m)
    {
        var (extentW, extentH) = layout.Extent();
        var scale = m.Unit / 100.0;

        var canvas = new Canvas
        {
            Width = extentW * scale,
            Height = extentH * scale,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        for (var pos = 0; pos < layout.Keys.Count; pos++)
        {
            var phys = layout.Keys[pos];
            var label = layer.Keys[pos];

            if (label.Kind == KeyKind.None && string.IsNullOrEmpty(label.Tap))
                continue;

            var element = BuildKey(label, phys.W * scale, phys.H * scale, m);

            Canvas.SetLeft(element, phys.X * scale);
            Canvas.SetTop(element, phys.Y * scale);

            // ZMK は 1/100 度で回転を持つ。同梱サンプルの Corne も親指のキーが回転している。
            if (phys.R != 0)
            {
                element.RenderTransformOrigin = new Point(0, 0);
                element.RenderTransform = new RotateTransform(
                    phys.R / 100.0,
                    (phys.Rx - phys.X) * scale,
                    (phys.Ry - phys.Y) * scale);
            }

            canvas.Children.Add(element);
        }

        return (canvas, canvas.Width);
    }

    private static FrameworkElement BuildKey(KeyLabel label, double w, double h, Metrics m)
    {
        var width = Math.Max(0, w - m.KeyInset * 2);
        var height = Math.Max(0, h - m.KeyInset * 2);
        var radius = new CornerRadius(m.KeyRadius);

        // &trans は「下のレイヤーと同じ」。記号を並べると画面がうるさくなるので、枠だけにする。
        if (label.Kind == KeyKind.Transparent)
        {
            return new Border
            {
                Width = width,
                Height = height,
                Margin = new Thickness(m.KeyInset),
                BorderBrush = Theme.TransparentOutline,
                BorderThickness = new Thickness(1),
                CornerRadius = radius,
            };
        }

        var (background, foreground) = Theme.ForKind(label.Kind);
        var textWidth = width - m.KeyTextPadding * 2;
        var hasHold = !string.IsNullOrEmpty(label.Hold);

        var grid = new Grid();

        grid.Children.Add(new TextBlock
        {
            Text = label.Tap,
            Foreground = foreground,
            FontSize = FitFontSize(label.Tap, textWidth, label.Tap.Length <= 1 ? m.TapSingle : m.TapMulti, m.MinText),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, hasHold ? m.Unit * 0.16 : 0, 0, 0),
        });

        if (hasHold)
        {
            grid.Children.Add(new TextBlock
            {
                Text = label.Hold,
                Foreground = Theme.HoldText,
                FontSize = FitFontSize(label.Hold!, textWidth, m.Hold, m.MinText),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(m.KeyTextPadding, m.Unit * 0.03, 0, 0),
            });
        }

        return new Border
        {
            Width = width,
            Height = height,
            Margin = new Thickness(m.KeyInset),
            Background = background,
            CornerRadius = radius,
            Child = grid,
        };
    }

    /// <summary>
    /// 実際に測って、幅に収まる大きさまで縮める。
    /// 文字数で決めると "Muhenkan" と "Win⇧←" のように字幅の違う文字列で揃わない。
    /// </summary>
    private static double FitFontSize(string text, double maxWidth, double preferred, double minimum)
    {
        if (text.Length == 0 || maxWidth <= 0) return preferred;

        var size = preferred;
        while (size > minimum && MeasureWidth(text, size) > maxWidth) size -= 0.5;

        return Math.Max(size, minimum);
    }

    private static double MeasureWidth(string text, double fontSize) =>
        new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Theme.Typeface, fontSize, Brushes.White, pixelsPerDip: 1.0)
        .WidthIncludingTrailingWhitespace;

    // ---- コンボ ----

    /// <summary>
    /// コンボはキー位置ではなくベースレイヤーの文字で示す
    /// （"13+16" より "F+J" の方が押す前に分かる）。与えられた幅で折り返す。
    /// </summary>
    private static UIElement? BuildCombos(Keymap keymap, int slot, double maxWidth, Metrics m)
    {
        var layerId = keymap.Layers[slot].Index;
        var baseLayer = keymap.Layers.FirstOrDefault(l => l.Index == 0) ?? keymap.Layers.FirstOrDefault();

        // combo の layers は ZMK のレイヤー番号。リスト上の位置と比べてはいけない。
        var applicable = keymap.Combos
            .Where(c => c.Layers.Count == 0 || c.Layers.Contains(layerId))
            .ToList();

        if (applicable.Count == 0 || baseLayer is null) return null;

        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = maxWidth,
        };

        wrap.Children.Add(new TextBlock
        {
            Text = UiText.Combos,
            FontSize = m.Faint,
            Foreground = Theme.HintText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(m.Unit * 0.04, 0, m.Unit * 0.12, m.Unit * 0.06),
        });

        foreach (var combo in applicable)
        {
            var keys = combo.KeyPositions
                .Where(p => p >= 0 && p < baseLayer.Keys.Count)
                .Select(p => baseLayer.Keys[p].Tap);

            var text = new TextBlock { FontSize = m.Faint, VerticalAlignment = VerticalAlignment.Center };
            text.Inlines.Add(new Run(string.Join("+", keys) + " → ") { Foreground = Theme.ComboKeysText });
            text.Inlines.Add(new Run(combo.Label) { Foreground = Theme.ComboLabelText });

            wrap.Children.Add(new Border
            {
                Background = Theme.ComboBg,
                CornerRadius = new CornerRadius(m.Unit * 0.08),
                Padding = new Thickness(m.Unit * 0.12, m.Unit * 0.03, m.Unit * 0.12, m.Unit * 0.03),
                Margin = new Thickness(0, 0, m.Unit * 0.08, m.Unit * 0.06),
                Child = text,
            });
        }

        return wrap;
    }
}
