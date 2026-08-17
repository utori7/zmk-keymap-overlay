using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.App.Render;

/// <summary>
/// レイアウト + キーマップ + 現在レイヤー → 表示用のビジュアルツリー。
/// 状態を持たないので、レイヤーが変わるたびに作り直す。40 キー程度では十分速い。
/// </summary>
internal static class KeymapRenderer
{
    /// <summary>キー同士が接触して見えないようにする内側の余白（ピクセル）。</summary>
    private const double KeyInset = 3.0;

    /// <summary>
    /// 枠つきのパネル一式。オーバーレイ表示と、確認用の PNG 出力の
    /// 両方がこれを使うので、見た目が食い違うことがない。
    /// </summary>
    public static FrameworkElement BuildPanel(
        AppConfig config, PhysicalLayout layout, Keymap keymap, int layerIndex)
    {
        var unitPx = config.KeyUnitPx;

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(BuildTabs(keymap, layerIndex, unitPx));
        stack.Children.Add(BuildKeys(layout, keymap.Layers[layerIndex], unitPx));

        var footer = BuildFooter(keymap, layerIndex, unitPx);
        if (footer is not null) stack.Children.Add(footer);

        stack.Children.Add(new TextBlock
        {
            Text = BuildHint(config, keymap.Layers[layerIndex]),
            Foreground = Theme.FaintText,
            FontSize = unitPx * 0.175,
            Margin = new Thickness(unitPx * 0.05, unitPx * 0.24, 0, 0),
        });

        return new Border
        {
            Background = Theme.Panel,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(unitPx * 0.25),
            Padding = new Thickness(unitPx * 0.32),
            Child = stack,
        };
    }

    private static string BuildHint(AppConfig config, Layer layer)
    {
        string sync;
        if (!config.LayerSync.Enabled)
            sync = "レイヤー連動: 無効";
        else if (layer.SignalKey is null)
            sync = "このレイヤーは自動追従できません";
        else
            sync = $"合図キー {layer.SignalKey}（{(config.LayerSync.IsHoldMode ? "押している間" : "トグル")}）";

        var manual = config.EnableManualLayerKeys
            ? "　Ctrl+Alt+レイヤー番号 で切替"
            : "";

        return $"{config.ToggleHotkey} 有効/無効{manual}　│　{sync}";
    }

    private static UIElement BuildTabs(Keymap keymap, int layerIndex, double unitPx)
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, unitPx * 0.18),
        };

        foreach (var layer in keymap.Layers)
        {
            var active = layer.Index == layerIndex;

            // 合図キーを持たないレイヤーは Phase 2 でも自動追従できない。
            // 手動で選ぶしかないことが見て分かるよう、タブ側で区別しておく。
            var syncable = layer.SignalKey is not null;

            var text = new TextBlock
            {
                Text = $"L{layer.Index}  {layer.Name}",
                FontSize = unitPx * 0.21,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = active
                    ? Theme.TabActiveText
                    : syncable ? Theme.TabIdleText : Theme.TabUnavailableText,
            };

            panel.Children.Add(new Border
            {
                Background = active ? Theme.TabActiveBg : Theme.TabIdleBg,
                CornerRadius = new CornerRadius(unitPx * 0.09),
                Padding = new Thickness(unitPx * 0.16, unitPx * 0.06, unitPx * 0.16, unitPx * 0.06),
                Margin = new Thickness(0, 0, unitPx * 0.11, unitPx * 0.08),
                Child = text,
            });
        }

        return panel;
    }

    private static UIElement BuildKeys(PhysicalLayout layout, Layer layer, double unitPx)
    {
        var (extentW, extentH) = layout.Extent();
        var scale = unitPx / 100.0;

        var canvas = new Canvas
        {
            Width = extentW * scale,
            Height = extentH * scale,
        };

        for (var pos = 0; pos < layout.Keys.Count; pos++)
        {
            var phys = layout.Keys[pos];
            var label = layer.Keys[pos];

            if (label.Kind == KeyKind.None && string.IsNullOrEmpty(label.Tap))
                continue;

            var element = BuildKey(label, phys.W * scale, phys.H * scale, unitPx);

            Canvas.SetLeft(element, phys.X * scale);
            Canvas.SetTop(element, phys.Y * scale);

            // ZMK は 1/100 度で回転を持つ。Pyuron は未使用だが、
            // 回転列を持つ分割キーボードのために対応しておく。
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

        return canvas;
    }

    private static FrameworkElement BuildKey(KeyLabel label, double w, double h, double unitPx)
    {
        var (background, foreground) = Theme.ForKind(label.Kind);

        var grid = new Grid();

        grid.Children.Add(new TextBlock
        {
            Text = label.Tap,
            Foreground = foreground,
            FontSize = TapFontSize(label.Tap, unitPx),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(unitPx * 0.03, label.Hold is null ? 0 : unitPx * 0.12, unitPx * 0.03, 0),
        });

        if (label.Hold is not null)
        {
            grid.Children.Add(new TextBlock
            {
                Text = label.Hold,
                Foreground = Theme.HoldText,
                FontSize = unitPx * 0.155,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(unitPx * 0.07, unitPx * 0.04, 0, 0),
            });
        }

        return new Border
        {
            Width = Math.Max(0, w - KeyInset * 2),
            Height = Math.Max(0, h - KeyInset * 2),
            Margin = new Thickness(KeyInset),
            Background = background,
            BorderBrush = Theme.KeyBorder,
            BorderThickness = new Thickness(label.Kind == KeyKind.None ? 0 : 1),
            CornerRadius = new CornerRadius(unitPx * 0.11),
            Child = grid,
        };
    }

    /// <summary>
    /// "Q" と "Win⇧←" を同じ大きさで描くと後者がはみ出すので、
    /// 文字数からざっくり縮める。厳密な計測より安定して見栄えが揃う。
    /// </summary>
    private static double TapFontSize(string text, double unitPx) => text.Length switch
    {
        <= 1 => unitPx * 0.36,
        2 => unitPx * 0.30,
        3 => unitPx * 0.24,
        4 => unitPx * 0.20,
        5 => unitPx * 0.175,
        _ => unitPx * 0.15,
    };

    /// <summary>
    /// コンボと操作ヒント。コンボはキー位置ではなく BASE レイヤーの文字で示す
    /// （"13+16" より "F+J" の方が押す前に分かる）。
    /// </summary>
    private static UIElement? BuildFooter(Keymap keymap, int layerIndex, double unitPx)
    {
        var baseLayer = keymap.Layers.FirstOrDefault(l => l.Index == 0);

        var applicable = keymap.Combos
            .Where(c => c.Layers.Count == 0 || c.Layers.Contains(layerIndex))
            .ToList();

        if (applicable.Count == 0 || baseLayer is null) return null;

        var parts = new List<string>();
        foreach (var combo in applicable)
        {
            var keys = combo.KeyPositions
                .Where(p => p >= 0 && p < baseLayer.Keys.Count)
                .Select(p => baseLayer.Keys[p].Tap);

            parts.Add($"{string.Join("+", keys)} → {combo.Label}");
        }

        return new TextBlock
        {
            Text = "コンボ   " + string.Join("     ", parts),
            Foreground = Theme.MutedText,
            FontSize = unitPx * 0.185,
            Margin = new Thickness(unitPx * 0.05, unitPx * 0.2, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
    }
}
