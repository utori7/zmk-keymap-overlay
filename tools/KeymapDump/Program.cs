using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Zmk;

// ZMK のソースを読んだ結果をテキストで出す。
// オーバーレイは絵なので、どのバインディングがどう解釈されたかを
// 突き合わせるにはこちらのほうが速い。パーサをいじるときの確認用。

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0)
{
    Console.Error.WriteLine(
        "使い方:\n" +
        "  KeymapDump <config.json>\n" +
        "  KeymapDump <keymapファイル> [物理レイアウトファイル]");
    return 1;
}

try
{
    var loaded = Load(args);

    Console.WriteLine($"レイアウト : {loaded.Layout.Name}  ({loaded.Layout.Keys.Count} キー)");
    Console.WriteLine($"レイヤー   : {loaded.Keymap.Layers.Count}");
    Console.WriteLine($"コンボ     : {loaded.Keymap.Combos.Count}");
    Console.WriteLine();

    if (loaded.Warnings.Count > 0)
    {
        Console.WriteLine("--- 警告 ---");
        foreach (var warning in loaded.Warnings) Console.WriteLine("  " + warning);
        Console.WriteLine();
    }

    foreach (var layer in loaded.Keymap.Layers)
    {
        var signal = layer.SignalKey is null ? "" : $"  合図キー={layer.SignalKey}";
        Console.WriteLine($"=== L{layer.Index} {layer.Name}{signal}  ({layer.Keys.Count} キー) ===");

        for (var i = 0; i < layer.Keys.Count; i++)
        {
            var key = layer.Keys[i];
            var hold = key.Hold is null ? "" : $" [{key.Hold}]";
            Console.WriteLine($"  {i,3}: {key.Tap}{hold}  ({key.Kind})");
        }

        Console.WriteLine();
    }

    foreach (var combo in loaded.Keymap.Combos)
    {
        var layers = combo.Layers.Count == 0 ? "全レイヤー" : "L" + string.Join(",", combo.Layers);
        Console.WriteLine($"コンボ: {string.Join("+", combo.KeyPositions)} -> {combo.Label}  ({layers})");
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.ToString());
    return 1;
}

static LoadedKeymap Load(string[] args)
{
    if (args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    {
        var path = Path.GetFullPath(args[0]);
        return KeymapLoader.Load(AppConfig.Load(path), path);
    }

    var result = ZmkKeymapReader.Read(new ZmkReadOptions
    {
        KeymapPath = Path.GetFullPath(args[0]),
        PhysicalLayoutPath = args.Length > 1 ? Path.GetFullPath(args[1]) : null,
    });

    return new LoadedKeymap
    {
        Layout = result.Layout,
        Keymap = result.Keymap,
        Warnings = result.Warnings,
        FromZmkSource = true,
    };
}
