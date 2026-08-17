using System.Text.Json;
using ZmkOverlay.Core.Config;

namespace ZmkOverlay.Core.Model;

/// <summary>
/// Phase 0 の取り込み口。手書き JSON を読む。
/// Phase 1 で .keymap パーサが入ったら、そちらの出力もこの型に落とす。
/// </summary>
public static class JsonStore
{
    public static PhysicalLayout LoadLayout(string path) =>
        JsonSerializer.Deserialize<PhysicalLayout>(
            File.ReadAllText(path), AppConfig.SerializerOptions)
        ?? throw new InvalidDataException($"レイアウトを読めません: {path}");

    public static Keymap LoadKeymap(string path) =>
        JsonSerializer.Deserialize<Keymap>(
            File.ReadAllText(path), AppConfig.SerializerOptions)
        ?? throw new InvalidDataException($"キーマップを読めません: {path}");

    /// <summary>
    /// キー数の食い違いは描画時に黙って位置ずれになるので、読み込んだ直後に弾く。
    /// </summary>
    public static void Validate(PhysicalLayout layout, Keymap keymap)
    {
        if (layout.Keys.Count == 0)
            throw new InvalidDataException("レイアウトにキーが 1 個もありません。");

        if (keymap.Layers.Count == 0)
            throw new InvalidDataException("キーマップにレイヤーが 1 個もありません。");

        foreach (var layer in keymap.Layers)
        {
            if (layer.Keys.Count != layout.Keys.Count)
                throw new InvalidDataException(
                    $"レイヤー '{layer.Name}' のキー数が {layer.Keys.Count} ですが、" +
                    $"レイアウト '{layout.Name}' は {layout.Keys.Count} キーです。");
        }
    }
}
