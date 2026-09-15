using ZmkOverlay.Core.Dts;
using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Text;

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

    /// <summary>
    /// レイヤー番号 → 合図キー。キーマップから読み取れた値より優先する。
    /// 空文字は「このレイヤーは追従しない」。
    /// </summary>
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
                     ?? throw new InvalidDataException(Strings.PhysicalLayoutMissing);

        var behaviors = ReadBehaviors(keymapTree, layoutTree);

        var keymapNode = Find(keymapTree, KeymapCompatible)
                         ?? throw new InvalidDataException(Strings.KeymapNodeMissing);

        var layerNodes = keymapNode.Children;
        var layerNames = BuildLayerNames(layerNodes, options.LayerNames);

        var formatter = new BindingFormatter(
            behaviors, layerNames, options.HostLayout, options.LabelOverrides);

        var keymap = new Keymap { Layout = layout.Name };

        var layerBindings = layerNodes
            .Select(n => n.Property("bindings") is { } p ? ZmkBinding.Split(p.AllCells) : null)
            .ToList();

        var detectedSignals = DetectSignalKeys(
            layerBindings.Where(b => b is not null).SelectMany(b => b!), behaviors);

        for (var index = 0; index < layerNodes.Count; index++)
        {
            var node = layerNodes[index];
            var bindings = layerBindings[index];

            if (bindings is null)
            {
                warnings.Add(Strings.LayerHasNoBindings(node.Name));
                continue;
            }

            var keys = bindings.Select(formatter.Format).ToList();

            if (keys.Count != layout.Keys.Count)
                warnings.Add(Strings.LayerKeyCountDiffers(node.Name, keys.Count, layout.Keys.Count));

            var detected = detectedSignals.TryGetValue(index, out var found) ? found : null;

            keymap.Layers.Add(new Layer
            {
                Index = index,
                Name = layerNames[index],
                Keys = keys,
                DetectedSignalKey = detected,
                SignalKey = ChooseSignalKey(options.SignalKeys, index, detected),
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
            warnings.Add(Strings.PhysicalLayoutHasNoKeys);
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
                warnings.Add(Strings.KeyAttrsTooFew(numbers.Count));
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

    // ---- 合図キー ----

    /// <summary>設定に書かれていればそれを優先する。空文字は「このレイヤーは追従しない」。</summary>
    private static string? ChooseSignalKey(IReadOnlyDictionary<int, string>? configured, int layer, string? detected)
    {
        if (configured is not null && configured.TryGetValue(layer, out var value))
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        return detected;
    }

    /// <summary>
    /// キーマップに仕込まれた合図キーを探す。設定に対応表を手で書かなくて済むようにするため。
    ///
    /// 合図キーは、レイヤーに入るマクロの中で押される。zmk/layer-signal.dtsi も、
    /// アプリが生成する書き換えも、この形をしている。
    ///
    ///   &lt;&amp;macro_press &amp;macro_param_1to1 &amp;mo MACRO_PLACEHOLDER&gt;, &lt;&amp;macro_press &amp;kp F13&gt;, ...
    ///
    /// マクロはキーマップから直接呼ばれるか、hold-tap の hold 側から呼ばれる。
    /// レイヤー番号はマクロの引数で渡されるので、呼び出し側の値から決める。
    /// </summary>
    internal static Dictionary<int, string> DetectSignalKeys(
        IEnumerable<ZmkBinding> bindings, IReadOnlyDictionary<string, BehaviorInfo> behaviors)
    {
        var found = new Dictionary<int, string>();

        foreach (var binding in bindings)
        {
            foreach (var (macro, argument) in MacroCalls(binding, behaviors))
            {
                if (SignalOf(macro, argument) is { } signal)
                    found.TryAdd(signal.Layer, signal.Key);
            }
        }

        return found;
    }

    /// <summary>そのキーを押したときに呼ばれるマクロと、マクロに渡る第 1 引数。</summary>
    private static IEnumerable<(BehaviorInfo Macro, string? Argument)> MacroCalls(
        ZmkBinding binding, IReadOnlyDictionary<string, BehaviorInfo> behaviors)
    {
        if (!behaviors.TryGetValue(binding.Behavior, out var info)) yield break;

        if (info.IsMacro)
        {
            yield return (info, binding.Param(0));
            yield break;
        }

        // hold-tap は第 1 引数を hold 側に、第 2 引数を tap 側に渡す。
        if (info.IsHoldTap
            && info.Bindings.Count >= 1
            && behaviors.TryGetValue(info.Bindings[0].Behavior, out var hold)
            && hold.IsMacro)
        {
            yield return (hold, binding.Param(0));
        }
    }

    private static (int Layer, string Key)? SignalOf(BehaviorInfo macro, string? argument)
    {
        string? key = null;
        int? layer = null;

        foreach (var inner in macro.Bindings)
        {
            if (inner.Behavior == "kp" && inner.Param(0) is { } code && IsSignalKey(code))
                key ??= code.Trim().ToUpperInvariant();

            if (inner.Behavior == "mo" && inner.Param(0) is { } target)
            {
                // 番号が書いてあればそれ。MACRO_PLACEHOLDER なら呼び出し側の引数。
                var value = DtsValue.TryParseNumber(target, out _) ? target : argument;
                if (value is not null && DtsValue.TryParseNumber(value, out var number))
                    layer ??= number;
            }
        }

        return key is not null && layer is not null ? (layer.Value, key) : null;
    }

    /// <summary>F13〜F24。普通のキーボードに無く、他のアプリも使わないので合図に使える。</summary>
    private static bool IsSignalKey(string code)
    {
        var name = code.Trim();

        return name.Length == 3
               && (name[0] == 'F' || name[0] == 'f')
               && int.TryParse(name[1..], out var n)
               && n is >= 13 and <= 24;
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
