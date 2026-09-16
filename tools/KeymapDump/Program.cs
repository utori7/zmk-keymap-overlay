using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ZmkOverlay.Core.Config;
using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Text;
using ZmkOverlay.Core.Zmk;

// ZMK のソースを読んだ結果をテキストで出す。
// オーバーレイは絵なので、どのバインディングがどう解釈されたかを
// 突き合わせるにはこちらのほうが速い。パーサをいじるときの確認用。
//
// --json は、読んだ結果を手書き JSON と同じ形で書き出す。同梱サンプル（data/）はこれで作る。

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0)
{
    Console.Error.WriteLine(
        "使い方:\n" +
        "  KeymapDump <config.json>\n" +
        "  KeymapDump <keymapファイル> [物理レイアウトファイル]\n" +
        "  KeymapDump --json <keymapファイル> <物理レイアウトファイル> <出力レイアウト.json> <出力キーマップ.json> <注記>");
    return 1;
}

if (args[0] == "--json")
{
    if (args.Length < 6)
    {
        Console.Error.WriteLine("--json には 5 つの引数が要ります。");
        return 1;
    }

    return ExportJson(args[1], args[2], args[3], args[4], args[5]);
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

// 同梱サンプルは画面の言語や PC の配列で変わらないよう、英語・US 配列のラベルで固定する。
static int ExportJson(string keymap, string layout, string layoutOut, string keymapOut, string note)
{
    Strings.Language = UiLanguage.En;

    var result = ZmkKeymapReader.Read(new ZmkReadOptions
    {
        KeymapPath = Path.GetFullPath(keymap),
        PhysicalLayoutPath = Path.GetFullPath(layout),
        HostLayout = HostLayout.Us,
    });

    if (result.Warnings.Count > 0)
    {
        foreach (var warning in result.Warnings) Console.Error.WriteLine(warning);
        return 1;
    }

    var options = new JsonSerializerOptions(AppConfig.SerializerOptions)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    Write(layoutOut, JsonSerializer.SerializeToNode(result.Layout, options)!.AsObject(), note, options);
    Write(keymapOut, JsonSerializer.SerializeToNode(result.Keymap, options)!.AsObject(), note, options);

    Console.WriteLine(layoutOut);
    Console.WriteLine(keymapOut);
    return 0;
}

static void Write(string path, JsonObject body, string note, JsonSerializerOptions options)
{
    var document = new JsonObject { ["_comment"] = note };
    foreach (var (key, value) in body.ToList())
    {
        body.Remove(key);
        document[key] = value;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    File.WriteAllText(path, document.ToJsonString(options) + "\n");
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
