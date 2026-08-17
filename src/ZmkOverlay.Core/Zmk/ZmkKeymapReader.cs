using ZmkOverlay.Core.Dts;
using ZmkOverlay.Core.Model;

namespace ZmkOverlay.Core.Zmk;

public sealed class ZmkReadOptions
{
    /// <summary><c>.keymap</c> ファイル。</summary>
    public string KeymapPath { get; init; } = "";

    /// <summary>
    /// 物理レイアウトを持つファイル。省略時はキーマップ側から探す。
    /// Pyuron のようにシールドの <c>.dtsi</c> に分かれている構成では必須。
    /// </summary>
    public string? PhysicalLayoutPath { get; init; }

    public HostLayout HostLayout { get; init; } = HostLayout.Jis;

    /// <summary>キーコード名の表示差し替え。</summary>
    public IReadOnlyDictionary<string, string>? LabelOverrides { get; init; }

    /// <summary>レイヤー番号 → 表示名の差し替え。</summary>
    public IReadOnlyDictionary<int, string>? LayerNames { get; init; }

    /// <summary>レイヤー番号 → 合図キー。</summary>
    public IReadOnlyDictionary<int, string>? SignalKeys { get; init; }
}

public sealed class ZmkReadResult
{
    public PhysicalLayout Layout { get; init; } = new();
    public Keymap Keymap { get; init; } = new();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// ZMK のソースから、表示に必要なものだけを取り出す。
/// </summary>
public static class ZmkKeymapReader
{
    private const string PhysicalLayoutCompatible = "zmk,physical-layout";
    private const string KeymapCompatible = "zmk,keymap";
    private const string CombosCompatible = "zmk,combos";
    private const string KeyAttrsBehavior = "key_physical_attrs";

    public static ZmkReadResult Read(ZmkReadOptions options)
    {
        var warnings = new List<string>();

        var keymapTree = ParseFile(options.KeymapPath, warnings);

        var layoutTree = keymapTree;
        if (!string.IsNullOrWhiteSpace(options.PhysicalLayoutPath))
        {
            var full = Path.GetFullPath(options.PhysicalLayoutPath);
            if (full != Path.GetFullPath(options.KeymapPath))
                layoutTree = ParseFile(full, warnings);
        }

        var layout = ReadPhysicalLayout(layoutTree, warnings)
                     ?? ReadPhysicalLayout(keymapTree, warnings)
                     ?? throw new InvalidDataException(
                         "物理レイアウト（compatible = \"zmk,physical-layout\"）が見つかりません。" +
                         "シールドの .dtsi を physicalLayoutFile に指定してください。");

        var behaviors = ReadBehaviors(keymapTree, layoutTree);

        var keymapNode = Find(keymapTree, KeymapCompatible)
                         ?? throw new InvalidDataException(
                             "キーマップ（compatible = \"zmk,keymap\"）が見つかりません。");

        var layerNodes = keymapNode.Children;
        var layerNames = BuildLayerNames(layerNodes, options.LayerNames);

        var formatter = new BindingFormatter(
            behaviors, layerNames, options.HostLayout, options.LabelOverrides);

        var keymap = new Keymap { Layout = layout.Name };

        for (var index = 0; index < layerNodes.Count; index++)
        {
            var node = layerNodes[index];
            var bindings = node.Property("bindings");

            if (bindings is null)
            {
                warnings.Add($"レイヤー '{node.Name}' に bindings がありません。");
                continue;
            }

            var keys = ZmkBinding.Split(bindings.AllCells).Select(formatter.Format).ToList();

            if (keys.Count != layout.Keys.Count)
                warnings.Add(
                    $"レイヤー '{node.Name}' のキー数が {keys.Count} で、" +
                    $"レイアウトの {layout.Keys.Count} と一致しません。");

            keymap.Layers.Add(new Layer
            {
                Index = index,
                Name = layerNames[index],
                Keys = keys,
                SignalKey = options.SignalKeys is not null
                            && options.SignalKeys.TryGetValue(index, out var signal)
                    ? signal
                    : null,
            });
        }

        keymap.Combos.AddRange(ReadCombos(keymapTree, formatter));

        return new ZmkReadResult { Layout = layout, Keymap = keymap, Warnings = warnings };
    }

    private static DtsNode ParseFile(string path, List<string> warnings)
    {
        var preprocessed = new Preprocessor().ProcessFile(path);

        foreach (var warning in preprocessed.Warnings)
            warnings.Add($"{Path.GetFileName(path)}: {warning}");

        return DtsParser.Parse(preprocessed.Text);
    }

    private static DtsNode? Find(DtsNode root, string compatible) =>
        root.Descendants().FirstOrDefault(n => n.Compatible == compatible);

    // ---- 物理レイアウト ----

    private static PhysicalLayout? ReadPhysicalLayout(DtsNode root, List<string> warnings)
    {
        var node = Find(root, PhysicalLayoutCompatible);
        if (node is null) return null;

        var keysProperty = node.Property("keys");
        if (keysProperty is null)
        {
            warnings.Add("physical-layout に keys がありません。");
            return null;
        }

        var layout = new PhysicalLayout
        {
            Name = node.Property("display-name")?.FirstString ?? node.Label ?? node.Name,
        };

        // <&key_physical_attrs w h x y r rx ry> の並び。1 つの <> にまとめて
        // 書かれることも、キーごとに <> を分けることもあるので、
        // 参照を区切りとして順に読む。
        var numbers = new List<int>();
        var started = false;

        void Flush()
        {
            if (!started) return;

            if (numbers.Count < 4)
            {
                warnings.Add($"key_physical_attrs の値が {numbers.Count} 個しかありません。");
                numbers.Clear();
                return;
            }

            layout.Keys.Add(new PhysicalKey
            {
                W = numbers[0],
                H = numbers[1],
                X = numbers[2],
                Y = numbers[3],
                R = numbers.Count > 4 ? numbers[4] : 0,
                Rx = numbers.Count > 5 ? numbers[5] : 0,
                Ry = numbers.Count > 6 ? numbers[6] : 0,
            });

            numbers.Clear();
        }

        foreach (var cell in keysProperty.AllCells)
        {
            if (cell.IsReference)
            {
                Flush();
                started = cell.Text == KeyAttrsBehavior;
                continue;
            }

            if (started && DtsValue.TryParseNumber(cell.Text, out var value)) numbers.Add(value);
        }

        Flush();

        return layout.Keys.Count > 0 ? layout : null;
    }

    // ---- ビヘイビア ----

    private static Dictionary<string, BehaviorInfo> ReadBehaviors(params DtsNode[] roots)
    {
        var result = new Dictionary<string, BehaviorInfo>(StringComparer.Ordinal);

        foreach (var root in roots)
        {
            foreach (var node in root.Descendants())
            {
                var compatible = node.Compatible;
                if (compatible is null || !compatible.StartsWith("zmk,behavior-", StringComparison.Ordinal))
                    continue;

                var label = node.Label ?? node.Name;
                if (label.Length == 0) continue;

                var bindings = node.Property("bindings");

                result[label] = new BehaviorInfo
                {
                    Label = label,
                    Compatible = compatible,
                    BindingCells = node.Property("#binding-cells")?.FirstNumber,
                    Bindings = bindings is null
                        ? Array.Empty<ZmkBinding>()
                        : ZmkBinding.Split(bindings.AllCells),
                };
            }
        }

        return result;
    }

    // ---- レイヤー名 ----

    private static Dictionary<int, string> BuildLayerNames(
        IReadOnlyList<DtsNode> layers, IReadOnlyDictionary<int, string>? overrides)
    {
        var names = new Dictionary<int, string>();

        for (var i = 0; i < layers.Count; i++)
        {
            if (overrides is not null && overrides.TryGetValue(i, out var custom))
            {
                names[i] = custom;
                continue;
            }

            names[i] = layers[i].Property("display-name")?.FirstString
                       ?? Prettify(layers[i].Name);
        }

        return names;
    }

    /// <summary><c>sym_layer</c> を <c>SYM</c> にする。</summary>
    private static string Prettify(string nodeName)
    {
        var name = nodeName;

        foreach (var suffix in new[] { "_layer", "_LAYER", "-layer" })
            if (name.EndsWith(suffix, StringComparison.Ordinal))
                name = name[..^suffix.Length];

        return name.Replace('_', ' ').Trim().ToUpperInvariant();
    }

    // ---- コンボ ----

    private static IEnumerable<Combo> ReadCombos(DtsNode root, BindingFormatter formatter)
    {
        var node = Find(root, CombosCompatible);
        if (node is null) yield break;

        foreach (var child in node.Children)
        {
            var bindings = child.Property("bindings");
            var positions = child.Property("key-positions");

            if (bindings is null || positions is null) continue;

            var binding = ZmkBinding.Split(bindings.AllCells).FirstOrDefault();
            if (binding is null) continue;

            var combo = new Combo { Label = formatter.Format(binding).Tap };

            foreach (var cell in positions.AllCells)
                if (DtsValue.TryParseNumber(cell.Text, out var position))
                    combo.KeyPositions.Add(position);

            var layersProperty = child.Property("layers");
            if (layersProperty is not null)
                foreach (var cell in layersProperty.AllCells)
                    if (DtsValue.TryParseNumber(cell.Text, out var layer))
                        combo.Layers.Add(layer);

            yield return combo;
        }
    }
}
