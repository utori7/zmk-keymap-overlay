using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZmkOverlay.App.Render;

/// <summary>
/// オーバーレイを画面に出さずに PNG へ書き出す。
///
/// 常駐アプリの見た目はホットキーを押さないと確認できず、レビューにも残せない。
/// 表示用と同じ <see cref="KeymapRenderer.BuildPanels"/> の結果を受け取るので、
/// ここで見た絵と実際のオーバーレイは一致する。
/// </summary>
internal static class OffscreenRenderer
{
    private const double Dpi = 96.0;

    public static void RenderToPng(FrameworkElement panel, string path)
    {
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));
        panel.UpdateLayout();

        var width = (int)Math.Ceiling(panel.ActualWidth);
        var height = (int)Math.Ceiling(panel.ActualHeight);

        var bitmap = new RenderTargetBitmap(width, height, Dpi, Dpi, PixelFormats.Pbgra32);
        bitmap.Render(panel);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
