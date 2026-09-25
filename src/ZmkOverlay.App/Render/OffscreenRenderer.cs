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

        Save(panel, panel.ActualWidth, panel.ActualHeight, path);
    }

    /// <summary>
    /// 表示中のウィンドウの中身を、スクロールせずに全体が入る高さで写す。
    ///
    /// 高さをウィンドウに任せられない。ウィンドウをいくら高くしても Windows が作業領域の高さで抑えるので
    /// （WM_GETMINMAXINFO の <c>ptMaxTrackSize</c>）、縦に長いページは下が切れる。
    /// そこで中身だけを測り直し、必要な高さに並べ直してから写す。
    ///
    /// **幅は今のまま**にすること。幅も測り直すと、幅いっぱいに広がるスライダーなどが
    /// 最小幅に縮んでしまい、実際の見た目と違う絵になる。
    /// </summary>
    public static void RenderWholePage(Window window, string path)
    {
        if (window.Content is not FrameworkElement root)
        {
            RenderAsShown(window, path);
            return;
        }

        var width = window.ActualWidth;

        // UpdateLayout はここで呼ばない。呼ぶとウィンドウから並べ直され、
        // せっかく広げた高さが作業領域の分に戻ってしまう。
        root.InvalidateMeasure();
        root.Measure(new Size(width, double.PositiveInfinity));
        var height = Math.Max(window.ActualHeight, root.DesiredSize.Height);
        root.Arrange(new Rect(0, 0, width, height));

        Compose(root, width, height, path);

        // 次のページのために、ウィンドウの本当の大きさへ戻す。
        root.InvalidateMeasure();
        window.UpdateLayout();
    }

    /// <summary>表示中のウィンドウを、いまの大きさのまま写す。</summary>
    public static void RenderAsShown(Window window, string path) =>
        Compose(window, window.ActualWidth, window.ActualHeight, path);

    /// <summary>
    /// Windows 11 風テーマのウィンドウは、背景を Windows（Mica）に描かせる。ウィンドウ自身の絵では
    /// 背景が透明になり、ダークテーマの明るい文字が透明の上に乗って読めない。テーマに合った地を敷いてから重ねる。
    /// </summary>
    private static void Compose(Visual visual, double actualWidth, double actualHeight, string path)
    {
        var width = (int)Math.Ceiling(actualWidth);
        var height = (int)Math.Ceiling(actualHeight);
        var bounds = new Rect(0, 0, width, height);

        var shot = new RenderTargetBitmap(width, height, Dpi, Dpi, PixelFormats.Pbgra32);
        shot.Render(visual);

        var composed = new DrawingVisual();
        using (var context = composed.RenderOpen())
        {
            context.DrawRectangle(IsDarkTheme() ? DarkGround : LightGround, null, bounds);
            context.DrawImage(shot, bounds);
        }

        Save(composed, width, height, path);
    }

    private static readonly Brush DarkGround = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
    private static readonly Brush LightGround = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

    /// <summary>アプリ向けのダーク / ライト設定。ThemeMode="System" もこれに従う。</summary>
    private static bool IsDarkTheme() =>
        Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int light && light == 0;

    private static void Save(Visual visual, double actualWidth, double actualHeight, string path)
    {
        var width = (int)Math.Ceiling(actualWidth);
        var height = (int)Math.Ceiling(actualHeight);

        var bitmap = new RenderTargetBitmap(width, height, Dpi, Dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
