using ZmkOverlay.Core.Dts;
using ZmkOverlay.Core.Model;
using ZmkOverlay.Core.Text;

namespace ZmkOverlay.Core.Zmk;

public sealed class ZmkReadOptions
{
    /// <summary><c>.keymap</c> ファイル。</summary>
    public string KeymapPath { get; init; } = "";

    /// <summary>
    /// 物理レイアウトを持つファイル。省略時は、キーマップ自身 → キーマップのフォルダ以下 →
    /// キーマップの書き方からの推定 の順で探す。
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

    /// <summary>
    /// キーマップの近くで見つからなかったときに物理レイアウトを探すフォルダ。
    /// ZMK 本体から取ってきたシールドの定義（<see cref="ZmkShieldSource"/>）がここに入る。
    /// </summary>
    public IReadOnlyList<string>? ExtraLayoutFolders { get; init; }
}

public sealed class ZmkReadResult
{
    public PhysicalLayout Layout { get; init; } = new();
    public Keymap Keymap { get; init; } = new();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>物理レイアウトをどこから得たか。</summary>
    public LayoutSource LayoutSource { get; init; }

    /// <summary>物理レイアウトを読んだファイル。推定したときは null。</summary>
    public string? LayoutPath { get; init; }
}

/// <summary>
/// ZMK のソースから、表示に必要なものだけを取り出す。
/// </summary>
public static class ZmkKeymapReader
{
    private const string PhysicalLayoutCompatible = "zmk,physical-layout";
    private const string KeymapCompatible = "zmk,keymap";
    private const string CombosCompatible = "zmk,combos";
    private const string ConditionalLayersCompatible = "zmk,conditional-layers";
    private const string KeyAttrsBehavior = "key_physical_attrs";

    /// <summary>キーマップのフォルダ以下で物理レイアウトを探すとき、中身を見るファイルの上限。</summary>
    private const int MaxLayoutCandidates = 200;

    public static ZmkReadResult Read(ZmkReadOptions options)
    {
        var warnings = new List<string>();
        var skipped = new List<string>();

        var keymapTree = ParseFile(options.KeymapPath, warnings, skipped);
        var keymapNode = RequireKeymap(keymapTree, skipped);

        var layerNodes = keymapNode.Children;

        // 物理レイアウトの候補はキー数で絞るので、先に最初のレイヤーのキー数を数えておく。
        var keyCount = layerNodes.Select(n => n.Property("bindings")).FirstOrDefault(p => p is not null) is { } first
            ? ZmkBinding.Split(first.AllCells).Count
            : 0;

        var (layout, layoutTree, layoutSource, layoutPath) = ResolveLayout(options, keymapTree, keyCount, warnings);

        var behaviors = ReadBehaviors(keymapTree, layoutTree);
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
        keymap.ConditionalLayers.AddRange(ReadConditionalLayers(keymapTree));

        return new ZmkReadResult
        {
            Layout = layout,
            Keymap = keymap,
            Warnings = warnings,
            LayoutSource = layoutSource,
            LayoutPath = layoutPath,
        };
    }

    /// <summary>
    /// 物理レイアウトを次の順で探す。見つけた場所も返し、設定画面で利用者に見せる。
    ///
    ///   1. 指定されたファイル
    ///   2. キーマップ自身
    ///   3. キーマップのフォルダ以下の .dtsi / .overlay（zmk-config のシールド定義など）
    ///   4. ZMK 本体から取ってきたシールドの定義（Corne のように、定義が ZMK 本体にあるキーボード）
    ///   5. キーマップの書き方からの推定
    ///
    /// 一般の利用者に「シールドの .dtsi を指定してください」と求めるのは難しい。
    /// 自作系の zmk-config は 3 で、定番のキーボードは 4 で見つかり、どちらも無くても 5 でおおよその絵は出せる。
    /// </summary>
    private static (PhysicalLayout Layout, DtsNode Tree, LayoutSource Source, string? Path) ResolveLayout(
        ZmkReadOptions options, DtsNode keymapTree, int keyCount, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(options.PhysicalLayoutPath))
        {
            var full = Path.GetFullPath(options.PhysicalLayoutPath);
            var tree = full == Path.GetFullPath(options.KeymapPath) ? keymapTree : ParseFile(full, warnings);

            if (ReadPhysicalLayout(tree, warnings, keyCount) is { } specified)
                return (specified, tree, LayoutSource.SpecifiedFile, full);
        }

        if (ReadPhysicalLayout(keymapTree, warnings, keyCount) is { } inKeymap)
            return (inKeymap, keymapTree, LayoutSource.Keymap, Path.GetFullPath(options.KeymapPath));

        if (Path.GetDirectoryName(Path.GetFullPath(options.KeymapPath)) is { } keymapFolder
            && FindLayoutIn(keymapFolder, keyCount) is { } nearby)
            return (nearby.Layout, nearby.Tree, LayoutSource.FoundNearby, nearby.Path);

        foreach (var folder in options.ExtraLayoutFolders ?? Array.Empty<string>())
        {
            if (Directory.Exists(folder) && FindLayoutIn(folder, keyCount) is { } fromZmk)
                return (fromZmk.Layout, fromZmk.Tree, LayoutSource.ZmkRepository, fromZmk.Path);
        }

        if (keyCount > 0 && LayoutGuesser.Guess(File.ReadAllText(options.KeymapPath), keyCount) is { } guessed)
        {
            warnings.Add(Strings.LayoutGuessed);
            return (guessed, keymapTree, LayoutSource.Guessed, null);
        }

        throw new InvalidDataException(Strings.PhysicalLayoutMissing);
    }

    /// <summary>
    /// フォルダ以下から、キー数の合う物理レイアウトを探す。
    /// zmk-config では、物理レイアウトはシールドの .dtsi（config/boards/shields/...）にあることが多い。
    ///
    /// キーマップをダウンロードフォルダのような大きな場所に置かれることもあるので、
    /// 見に行く深さとファイル数に上限を設けている。
    /// </summary>
    private static (PhysicalLayout Layout, DtsNode Tree, string Path)? FindLayoutIn(string folder, int keyCount)
    {
        if (keyCount == 0) return null;

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MaxRecursionDepth = 6,
        };

        List<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(folder, "*.dtsi", enumeration)
                .Concat(Directory.EnumerateFiles(folder, "*.overlay", enumeration))
                .Take(MaxLayoutCandidates)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var path in candidates)
        {
            try
            {
                // 解析は重いので、物理レイアウトを持っていそうなファイルだけにする。
                // ZMK 本体のシールドは、定義を <layouts/...> から読み込んでいることが多い。
                var text = File.ReadAllText(path);
                if (!text.Contains(PhysicalLayoutCompatible, StringComparison.Ordinal)
                    && !text.Contains("<layouts/", StringComparison.Ordinal))
                    continue;

                // 候補を調べる途中の警告は、採用しなかったファイルのものが混ざるので捨てる。
                var ignored = new List<string>();
                var tree = ParseFile(path, ignored);

                if (ReadPhysicalLayout(tree, ignored, keyCount) is { } layout && layout.Keys.Count == keyCount)
                    return (layout, tree, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or DtsParseException or InvalidDataException)
            {
                // 解釈できないファイルは候補から外すだけ。
            }
        }

        return null;
    }

    /// <param name="skipped">トップレベルで読み飛ばした名前（展開できなかったマクロなど）を受け取る。</param>
    private static DtsNode ParseFile(string path, List<string> warnings, List<string>? skipped = null)
    {
        var preprocessed = new Preprocessor().ProcessFile(path);

        foreach (var warning in preprocessed.Warnings)
            warnings.Add($"{Path.GetFileName(path)}: {warning}");

        var names = new List<string>();
        var tree = DtsParser.Parse(preprocessed.Text, names);

        foreach (var name in names.Distinct(StringComparer.Ordinal))
            warnings.Add($"{Path.GetFileName(path)}: {Strings.TopLevelSkipped(name)}");

        skipped?.AddRange(names);
        return tree;
    }

    internal static DtsNode? Find(DtsNode root, string compatible) =>
        root.Descendants().FirstOrDefault(n => n.Compatible == compatible);

    /// <summary>
    /// キーマップのノード。無ければ理由を付けて断る。
    /// zmk-helpers の ZMK_LAYER(...) のような書き方は展開できないので、それと分かるように伝える。
    /// </summary>
    internal static DtsNode RequireKeymap(DtsNode tree, IEnumerable<string> skipped) =>
        Find(tree, KeymapCompatible)
        ?? throw new InvalidDataException(
            skipped.Any(n => n.StartsWith("ZMK_", StringComparison.Ordinal))
                ? Strings.KeymapUsesHelperMacros
                : Strings.KeymapNodeMissing);

    // ---- 物理レイアウト ----

    /// <summary>
    /// 物理レイアウトを読む。1 つのファイルに複数ある（Corne の 5 列 / 6 列など）ときは、
    /// chosen で選ばれていてキー数が合うもの → キー数が合う最初のもの → chosen のもの → 最初のもの、の順で選ぶ。
    /// </summary>
    private static PhysicalLayout? ReadPhysicalLayout(DtsNode root, List<string> warnings, int keyCount)
    {
        var nodes = root.Descendants().Where(n => n.Compatible == PhysicalLayoutCompatible).ToList();
        if (nodes.Count == 0) return null;

        var chosenLabel = root.Descendants()
            .Where(n => n.Name == "chosen")
            .Select(n => n.Property("zmk,physical-layout"))
            .SelectMany(p => p?.AllCells ?? Enumerable.Empty<DtsCell>())
            .FirstOrDefault(c => c.IsReference)?.Text;

        // 採用しなかったレイアウトの警告は混ぜない。
        var candidates = nodes
            .Select(node =>
            {
                var own = new List<string>();
                return (Node: node, Layout: ReadLayoutNode(node, own), Warnings: own);
            })
            .Where(c => c.Layout is not null)
            .ToList();

        if (candidates.Count == 0)
        {
            warnings.Add(Strings.PhysicalLayoutHasNoKeys);
            return null;
        }

        bool Fits(PhysicalLayout layout) => keyCount <= 0 || layout.Keys.Count == keyCount;

        var chosen = candidates.FirstOrDefault(c => chosenLabel is not null && c.Node.Label == chosenLabel);

        var picked =
            chosen.Layout is not null && Fits(chosen.Layout) ? chosen
            : candidates.FirstOrDefault(c => Fits(c.Layout!)) is { Layout: not null } fitting ? fitting
            : chosen.Layout is not null ? chosen
            : candidates[0];

        warnings.AddRange(picked.Warnings);
        return picked.Layout;
    }

    private static PhysicalLayout? ReadLayoutNode(DtsNode node, List<string> warnings)
    {
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

    internal static Dictionary<string, BehaviorInfo> ReadBehaviors(params DtsNode[] roots)
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
    /// 合図キーは、レイヤーに入るマクロの中で押される。docs/examples/pyuron/layer-signal.dtsi も、
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

    // ---- 条件付きレイヤー ----

    internal static IEnumerable<ConditionalLayer> ReadConditionalLayers(DtsNode root)
    {
        foreach (var node in root.Descendants().Where(n => n.Compatible == ConditionalLayersCompatible))
        {
            foreach (var child in node.Children)
            {
                var ifLayers = (child.Property("if-layers")?.AllCells ?? Enumerable.Empty<DtsCell>())
                    .Select(c => DtsValue.TryParseNumber(c.Text, out var n) ? n : (int?)null)
                    .OfType<int>()
                    .ToList();

                var then = child.Property("then-layer")?.AllCells
                    .Select(c => DtsValue.TryParseNumber(c.Text, out var n) ? n : (int?)null)
                    .FirstOrDefault(n => n is not null);

                if (ifLayers.Count > 0 && then is { } thenLayer)
                    yield return new ConditionalLayer { IfLayers = ifLayers, ThenLayer = thenLayer };
            }
        }
    }
}
