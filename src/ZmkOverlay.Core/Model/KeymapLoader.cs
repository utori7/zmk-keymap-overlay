using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Text;
using ZmkOverlay.Core.Zmk;

namespace ZmkOverlay.Core.Model;

public sealed class LoadedKeymap
{
    public PhysicalLayout Layout { get; init; } = new();
    public Keymap Keymap { get; init; } = new();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>ZMK のソースから読んだか、手書き JSON から読んだか。</summary>
    public bool FromZmkSource { get; init; }

    /// <summary>物理レイアウトをどこから得たか。</summary>
    public LayoutSource LayoutSource { get; init; }

    /// <summary>物理レイアウトを読んだファイル。推定したときは null。</summary>
    public string? LayoutPath { get; init; }
}

/// <summary>
/// 設定に従ってレイアウトとキーマップを用意する。
///
/// 取り込み口は 2 つある。ZMK のソースを直接読む経路と、手書き JSON の経路。
/// JSON 側を残してあるのは、ZMK のソースが手元に無い環境でも
/// 動かせるようにするため（会社 PC に zmk-config を置きたくない場合など）。
/// </summary>
public static class KeymapLoader
{
    public static LoadedKeymap Load(AppConfig config, string configPath)
    {
        if (config.Zmk.IsEnabled) return LoadFromZmk(config, configPath);

        var layoutPath = ConfigPaths.Resolve(configPath, config.LayoutFile);
        var layout = JsonStore.LoadLayout(layoutPath);
        var keymap = JsonStore.LoadKeymap(ConfigPaths.Resolve(configPath, config.KeymapFile));
        JsonStore.Validate(layout, keymap);

        return new LoadedKeymap
        {
            Layout = layout,
            Keymap = keymap,
            LayoutSource = LayoutSource.Json,
            LayoutPath = layoutPath,
        };
    }

    private static LoadedKeymap LoadFromZmk(AppConfig config, string configPath)
    {
        var zmk = config.Zmk;

        var keymapPath = ConfigPaths.Resolve(configPath, zmk.KeymapFile!);
        if (!File.Exists(keymapPath))
            throw new FileNotFoundException(Strings.KeymapNotFound(keymapPath));

        string? layoutPath = null;
        if (!string.IsNullOrWhiteSpace(zmk.PhysicalLayoutFile))
        {
            layoutPath = ConfigPaths.Resolve(configPath, zmk.PhysicalLayoutFile);
            if (!File.Exists(layoutPath))
                throw new FileNotFoundException(Strings.PhysicalLayoutNotFound(layoutPath));
        }

        var result = ZmkKeymapReader.Read(new ZmkReadOptions
        {
            KeymapPath = keymapPath,
            PhysicalLayoutPath = layoutPath,
            HostLayout = config.KeyboardLayout.Equals("us", StringComparison.OrdinalIgnoreCase)
                ? HostLayout.Us
                : HostLayout.Jis,
            LabelOverrides = zmk.LabelOverrides,
            LayerNames = zmk.ParseIndexed(zmk.LayerNames),
            SignalKeys = zmk.ParseIndexed(zmk.SignalKeys),
        });

        JsonStore.Validate(result.Layout, result.Keymap);

        return new LoadedKeymap
        {
            Layout = result.Layout,
            Keymap = result.Keymap,
            Warnings = result.Warnings,
            FromZmkSource = true,
            LayoutSource = result.LayoutSource,
            LayoutPath = result.LayoutPath,
        };
    }
}
