using YamlDotNet.RepresentationModel;

namespace ServiceLib;

/// <summary>
/// The readers, one per client family. Each takes a folder and answers what it holds, or null when the
/// folder is not that client's. Formats were checked against each client's source and real installs
/// (November 2026); a client that changes its format simply stops being offered, nothing breaks.
/// </summary>
public static partial class V2pnForeignImport
{
    private delegate ForeignApp? Reader(string folder);

    private static IEnumerable<(string Name, Func<IEnumerable<string>> Folders, Reader Read)> Readers()
    {
        var portable = new Lazy<Dictionary<string, List<string>>>(() => PortableIndex(
            "nekoray.exe", "nekobox.exe", "Throne.exe", "GUI.for.Clash.exe", "GUI.for.SingBox.exe",
            "clash-verge.exe", "Clash Verge.exe", "clash-nyanpasu.exe", "Clash Nyanpasu.exe"));
        IEnumerable<string> Portable(params string[] exes) =>
            exes.SelectMany(e => portable.Value.TryGetValue(e, out var dirs) ? dirs : []);

        yield return ("Clash Verge", () => AppData("io.github.clash-verge-rev.clash-verge-rev", "io.github.clash-verge")
                .Append(Path.Combine(Home, ".config", "clash-verge"))
                .Concat(Portable("clash-verge.exe", "Clash Verge.exe").SelectMany(d => new[]
                {
                    Path.Combine(d, ".config", "io.github.clash-verge-rev.clash-verge-rev"),
                    Path.Combine(d, ".config", "clash-verge"),
                })),
            folder => ReadVergeLike(folder, "Clash Verge", "profiles.yaml", [Path.Combine(folder, "profiles")]));

        yield return ("Clash Nyanpasu", () => AppData(Path.Combine("Clash Nyanpasu", "config"), "clash-nyanpasu")
                .Concat(Portable("clash-nyanpasu.exe", "Clash Nyanpasu.exe").Select(d => Path.Combine(d, ".config", "clash-nyanpasu"))),
            folder => ReadVergeLike(folder, "Clash Nyanpasu", "profiles.yaml",
            [
                Path.Combine(folder, "profiles"),
                Path.Combine(Path.GetDirectoryName(folder) ?? folder, "data", "profiles"),
                Path.Combine(Local, "Clash Nyanpasu", "data", "profiles"),
                Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(folder) ?? folder) ?? folder, ".data", "clash-nyanpasu", "profiles"),
            ]));

        yield return ("Clash Party", () => AppData("mihomo-party", "clash-party", "Mihomo Party", "Clash Party"),
            folder => ReadVergeLike(folder, "Clash Party", "profile.yaml", [Path.Combine(folder, "profiles")]));

        yield return ("Clash for Windows", () => [Path.Combine(Home, ".config", "clash")], ReadClashForWindows);

        yield return ("FlClash", () => AppData(Path.Combine("com.follow", "clash"), Path.Combine("com.follow", "clashx"), "com.follow.clash"),
            ReadFlClash);

        yield return ("Throne", () => AppData("Throne", Path.Combine("Throne", "config"))
                .Append(Path.Combine(Home, "Library", "Preferences", "Throne"))
                .Concat(Portable("Throne.exe")),
            folder => ReadNekoLike(folder, "Throne"));

        yield return ("NekoRay", () => Portable("nekoray.exe", "nekobox.exe").Concat(AppData("nekoray", "nekobox")),
            folder => ReadNekoLike(folder, "NekoRay"));

        yield return ("Hiddify", () => AppData(Path.Combine("Hiddify", "Hiddify"), Path.Combine("hiddify", "hiddify"), "Hiddify", "hiddify", "app.hiddify.com"),
            ReadHiddify);

        yield return ("v2RayTun", () => AppData(Path.Combine("v2RayTun.net", "v2RayTun"), "v2RayTun"), ReadV2RayTun);

        yield return ("Karing", () => AppData(Path.Combine("karing", "karing"), "karing", "com.nebula.karing"), ReadKaring);

        yield return ("GUI.for.Cores", () => Portable("GUI.for.Clash.exe", "GUI.for.SingBox.exe"), ReadGuiForCores);

        yield return ("mihomo", () => [Path.Combine(Home, ".config", "mihomo"), Path.Combine(Home, ".config", "clash.meta")], ReadBareMihomo);

        yield return ("Happ", () => AppData("Happ"), ReadHapp);
    }

    #region Clash family

    /// <summary>
    /// Clash Verge (Rev), Clash Nyanpasu and Clash Party keep a YAML list of profiles: remote ones with a
    /// url, local ones with only a file. The list is <c>items</c> with uid/id, type, name, url and file.
    /// </summary>
    private static ForeignApp? ReadVergeLike(string folder, string name, string listFile, string[] profileDirs)
    {
        if (ReadYaml(Path.Combine(folder, listFile)) is not { } root || Child(root, "items") is not YamlSequenceNode items)
        {
            return null;
        }
        var app = new ForeignApp { Name = name, Folder = folder };
        foreach (var item in items.Children.OfType<YamlMappingNode>())
        {
            var type = Str(item, "type")?.ToLowerInvariant();
            // Merge and script profiles only change other profiles; they hold no servers.
            if (type is "merge" or "script" or "rules" or "proxies" or "groups" or "transform")
            {
                continue;
            }
            var url = Str(item, "url") ?? (Child(item, "source") is YamlMappingNode source ? Str(source, "url") : null);
            if (V2pnSubs.IsHttpLink(url))
            {
                app.Subscriptions.Add(new ForeignSub(Str(item, "name") ?? string.Empty, url!));
                continue;
            }
            var file = Str(item, "file") ?? (Str(item, "id") ?? Str(item, "uid")) + ".yaml";
            var path = profileDirs.Select(d => Path.Combine(d, file)).FirstOrDefault(File.Exists);
            AddContent(app, path == null ? null : ReadText(path));
        }
        return app;
    }

    /// <summary>Clash for Windows: profiles/list.yml with <c>files</c> of time (the file name), url and name.</summary>
    private static ForeignApp? ReadClashForWindows(string folder)
    {
        var profiles = Path.Combine(folder, "profiles");
        if (ReadYaml(Path.Combine(profiles, "list.yml")) is not { } root || Child(root, "files") is not YamlSequenceNode files)
        {
            return null;
        }
        var app = new ForeignApp { Name = "Clash for Windows", Folder = folder };
        foreach (var item in files.Children.OfType<YamlMappingNode>())
        {
            var url = Str(item, "url");
            if (V2pnSubs.IsHttpLink(url))
            {
                app.Subscriptions.Add(new ForeignSub(Str(item, "name") ?? string.Empty, url!));
            }
            else if (Str(item, "time") is { } file)
            {
                AddContent(app, ReadText(Path.Combine(profiles, file)));
            }
        }
        return app;
    }

    /// <summary>
    /// FlClash and FlClashX: profiles with id, label and url, in database.sqlite (newer) or in
    /// shared_preferences.json under flutter.config (older); each profile's YAML is profiles/&lt;id&gt;.yaml.
    /// </summary>
    private static ForeignApp? ReadFlClash(string folder)
    {
        var profiles = new List<(string Id, string Label, string Url)>();
        var database = Path.Combine(folder, "database.sqlite");
        if (File.Exists(database))
        {
            profiles.AddRange(QueryCopy(database, "SELECT CAST(id AS TEXT) AS Id, label AS Label, url AS Url FROM profiles")
                .Select(r => (r.Id, r.Label, r.Url)));
        }
        if (profiles.Count == 0 && JsonUtils.ParseJson(ReadText(Path.Combine(folder, "shared_preferences.json")) ?? string.Empty) is JsonObject prefs
            && JsonUtils.ParseJson(prefs["flutter.config"]?.ToString() ?? string.Empty)?["profiles"] is JsonArray list)
        {
            profiles.AddRange(list.OfType<JsonObject>().Select(p => (p["id"]?.ToString() ?? string.Empty, JStr(p, "label") ?? string.Empty, JStr(p, "url") ?? string.Empty)));
        }
        if (profiles.Count == 0)
        {
            return null;
        }
        var app = new ForeignApp { Name = Path.GetFileName(folder).Equals("clashx", StringComparison.OrdinalIgnoreCase) ? "FlClashX" : "FlClash", Folder = folder };
        foreach (var (id, label, url) in profiles)
        {
            if (V2pnSubs.IsHttpLink(url))
            {
                app.Subscriptions.Add(new ForeignSub(label, url));
            }
            else
            {
                AddContent(app, ReadText(Path.Combine(folder, "profiles", $"{id}.yaml")));
            }
        }
        return app;
    }

    /// <summary>GUI.for.Clash / GUI.for.SingBox: data/subscribes.yaml, a list with name, url and path.</summary>
    private static ForeignApp? ReadGuiForCores(string folder)
    {
        var path = Path.Combine(folder, "data", "subscribes.yaml");
        if (!File.Exists(path))
        {
            return null;
        }
        var stream = new YamlStream();
        using (var reader = new StringReader(ReadText(path) ?? string.Empty))
        {
            stream.Load(reader);
        }
        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlSequenceNode list)
        {
            return null;
        }
        var app = new ForeignApp { Name = File.Exists(Path.Combine(folder, "GUI.for.SingBox.exe")) ? "GUI.for.SingBox" : "GUI.for.Clash", Folder = folder };
        foreach (var item in list.Children.OfType<YamlMappingNode>())
        {
            var url = Str(item, "url");
            if (V2pnSubs.IsHttpLink(url))
            {
                app.Subscriptions.Add(new ForeignSub(Str(item, "name") ?? string.Empty, url!));
            }
            else if (Str(item, "path") is { } file)
            {
                AddContent(app, ReadText(Path.IsPathRooted(file) ? file : Path.Combine(folder, file)));
            }
        }
        return app;
    }

    /// <summary>A bare mihomo core with its config.yaml.</summary>
    private static ForeignApp? ReadBareMihomo(string folder)
    {
        var app = new ForeignApp { Name = "mihomo", Folder = folder };
        AddContent(app, ReadText(Path.Combine(folder, "config.yaml")));
        return app;
    }

    private static YamlMappingNode? ReadYaml(string path)
    {
        var text = ReadText(path);
        if (text.IsNullOrEmpty())
        {
            return null;
        }
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(text);
            stream.Load(reader);
            return stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode;
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{Tag}: {path}: {ex.Message}");
            return null;
        }
    }

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string? Str(YamlMappingNode map, string key) =>
        Child(map, key) is YamlScalarNode { Value: { Length: > 0 } value } ? value : null;

    #endregion Clash family

    #region NekoRay and Throne

    /// <summary>
    /// NekoRay, NekoBox and Throne up to 1.0 keep config/groups/N.json (a subscription has a url) and
    /// config/profiles/N.json (type, gid and a "bean"; Throne 1.0 adds a sing-box "outbound"). Throne
    /// 1.1+ keeps the same in config/throne.db. Servers of subscription groups come with the subscription.
    /// </summary>
    private static ForeignApp? ReadNekoLike(string folder, string name)
    {
        var config = Directory.Exists(Path.Combine(folder, "config")) ? Path.Combine(folder, "config") : folder;
        if (File.Exists(Path.Combine(config, "throne.db")))
        {
            return ReadThroneDb(config);
        }
        var groupsDir = Path.Combine(config, "groups");
        var profilesDir = Path.Combine(config, "profiles");
        if (!Directory.Exists(groupsDir) || !Directory.Exists(profilesDir))
        {
            return null;
        }
        var isThrone = name == "Throne" || File.Exists(Path.Combine(folder, "Throne.exe")) || File.Exists(Path.Combine(config, "configs.json"));
        var root = config == folder ? Path.GetDirectoryName(folder) ?? folder : folder;
        var app = new ForeignApp { Name = isThrone ? "Throne" : File.Exists(Path.Combine(root, "nekobox.exe")) ? "NekoBox" : "NekoRay", Folder = root };
        var subGroups = new HashSet<int>();
        foreach (var file in Directory.EnumerateFiles(groupsDir, "*.json"))
        {
            if (JsonUtils.ParseJson(ReadText(file) ?? string.Empty) is JsonObject group && V2pnSubs.IsHttpLink(JStr(group, "url")))
            {
                app.Subscriptions.Add(new ForeignSub(JStr(group, "name") ?? string.Empty, JStr(group, "url")!));
                if (JInt(group, "id") is { } id)
                {
                    subGroups.Add(id);
                }
            }
        }
        foreach (var file in Directory.EnumerateFiles(profilesDir, "*.json"))
        {
            if (JsonUtils.ParseJson(ReadText(file) ?? string.Empty) is not JsonObject profile
                || (JInt(profile, "gid") is { } gid && subGroups.Contains(gid)))
            {
                continue;
            }
            AddLink(app, NekoProfileLink(JStr(profile, "type"), profile["outbound"] as JsonObject, profile["bean"] as JsonObject));
        }
        return app;
    }

    private static ForeignApp? ReadThroneDb(string config)
    {
        var db = Path.Combine(config, "throne.db");
        var app = new ForeignApp { Name = "Throne", Folder = Path.GetDirectoryName(config) ?? config };
        var subGroups = new HashSet<string>();
        foreach (var row in QueryCopy(db, "SELECT CAST(id AS TEXT) AS Id, name AS Label, url AS Url FROM groups"))
        {
            if (V2pnSubs.IsHttpLink(row.Url))
            {
                app.Subscriptions.Add(new ForeignSub(row.Label, row.Url));
                subGroups.Add(row.Id);
            }
        }
        foreach (var row in QueryCopy(db, "SELECT CAST(gid AS TEXT) AS Id, type AS Label, outbound_json AS Url FROM profiles"))
        {
            if (!subGroups.Contains(row.Id))
            {
                AddLink(app, NekoProfileLink(row.Label, JsonUtils.ParseJson(row.Url) as JsonObject, null));
            }
        }
        return app;
    }

    /// <summary>A NekoRay / Throne profile as a link: from its sing-box outbound when it has one, else from its bean.</summary>
    internal static string? NekoProfileLink(string? type, JsonObject? outbound, JsonObject? bean)
    {
        type = type?.ToLowerInvariant();
        if (outbound != null && outbound["server"] != null)
        {
            var copy = (JsonObject)outbound.DeepClone();
            copy["type"] = type is "hysteria" or "hysteria2" ? "hysteria2" : type ?? JStr(copy, "type");
            copy["tag"] ??= JStr(copy, "name") ?? (bean == null ? null : JStr(bean, "name"));
            return V2pnForeignLinks.ToLink(V2pnForeignLinks.FromSingboxOutbound(copy));
        }
        return bean == null ? null : V2pnForeignLinks.ToLink(FromNekoBean(type, bean));
    }

    /// <summary>NekoRay's bean (fmt/*Bean.hpp): addr, port, name, then per kind id/aid/sec, pass/flow, method/plugin…</summary>
    private static V2pnForeignLinks.Node? FromNekoBean(string? type, JsonObject bean)
    {
        var node = new V2pnForeignLinks.Node
        {
            Type = type switch { "socks" when JInt(bean, "v") is null or 5 or 4 => "socks", _ => type ?? string.Empty },
            Name = JStr(bean, "name") ?? string.Empty,
            Server = JStr(bean, "addr") ?? string.Empty,
            Port = JInt(bean, "port") ?? 0,
            Flow = JStr(bean, "flow"),
            Method = JStr(bean, "method"),
            Plugin = JStr(bean, "plugin"),
            Username = JStr(bean, "username"),
            AlterId = JStr(bean, "aid"),
            VmessCipher = JStr(bean, "sec"),
            ObfsPassword = JStr(bean, "obfsPassword"),
            Ports = JStr(bean, "hopPort"),
            Congestion = JStr(bean, "congestionControl"),
        };
        switch (node.Type)
        {
            case "vmess":
                node.Uuid = JStr(bean, "id");
                break;
            case "vless":
                node.Uuid = JStr(bean, "pass");
                break;
            case "trojan" or "shadowsocks":
                node.Password = JStr(bean, "pass");
                break;
            case "hysteria2" or "tuic":
                node.Uuid = JStr(bean, "uuid");
                node.Password = JStr(bean, "password");
                node.Sni = JStr(bean, "sni");
                node.Alpn = JStr(bean, "alpn");
                node.Insecure = JBool(bean, "allowInsecure");
                node.Obfs = node.ObfsPassword.IsNotEmpty() ? "salamander" : null;
                return node;
            case "socks":
                node.Password = JStr(bean, "password");
                return node;
            default:
                return null;
        }
        if (node.Type == "shadowsocks" && node.Plugin.IsNotEmpty() && !node.Plugin!.StartsWith("obfs-local") && !node.Plugin.StartsWith("v2ray-plugin"))
        {
            return null;
        }
        if (bean["stream"] is JsonObject stream)
        {
            var sec = JStr(stream, "sec");
            node.PublicKey = JStr(stream, "pbk");
            node.ShortId = JStr(stream, "sid");
            node.Tls = sec is "tls" or "reality";
            node.Sni = JStr(stream, "sni");
            node.Alpn = JStr(stream, "alpn");
            node.Fingerprint = JStr(stream, "utls");
            node.Insecure = JBool(stream, "insecure");
            node.Network = JStr(stream, "net") ?? "tcp";
            node.Path = JStr(stream, "path");
            node.Host = JStr(stream, "host");
            if (node.Network == "grpc")
            {
                node.ServiceName = node.Path;
            }
            if (node.Network == "tcp" && JStr(stream, "h_type") == "http")
            {
                node.HeaderType = "http";
            }
            if (node.Network == "http")
            {
                node.Network = "h2";
            }
        }
        return node;
    }

    #endregion NekoRay and Throne

    #region Flutter clients

    /// <summary>
    /// Hiddify: db.sqlite with the profile_entries table (type remote/local, name, url); a local profile's
    /// sing-box config is configs/&lt;id&gt;.json in the working folder next to the database.
    /// </summary>
    private static ForeignApp? ReadHiddify(string folder)
    {
        var db = new[] { folder, Path.Combine(folder, "hiddify"), Path.Combine(folder, "Hiddify") }
            .Select(d => Path.Combine(d, "db.sqlite"))
            .FirstOrDefault(File.Exists);
        if (db == null)
        {
            return null;
        }
        var dbDir = Path.GetDirectoryName(db)!;
        var app = new ForeignApp { Name = "Hiddify", Folder = dbDir };
        foreach (var row in QueryCopy(db, "SELECT id AS Id, name AS Label, IFNULL(url, '') AS Url FROM profile_entries"))
        {
            if (V2pnSubs.IsHttpLink(row.Url))
            {
                app.Subscriptions.Add(new ForeignSub(row.Label, row.Url));
                continue;
            }
            var config = new[] { dbDir, Path.GetDirectoryName(dbDir) ?? dbDir }
                .Select(d => Path.Combine(d, "configs", $"{row.Id}.json"))
                .FirstOrDefault(File.Exists);
            AddContent(app, config == null ? null : ReadText(config));
        }
        return app;
    }

    /// <summary>
    /// v2RayTun: shared_preferences.json, a subscription per flutter.sub_&lt;id&gt; (remarks, url) and a server
    /// per flutter.config_&lt;id&gt; (remarks, subscriptionId, singBoxOutbound).
    /// </summary>
    private static ForeignApp? ReadV2RayTun(string folder)
    {
        if (JsonUtils.ParseJson(ReadText(Path.Combine(folder, "shared_preferences.json")) ?? string.Empty) is not JsonObject prefs)
        {
            return null;
        }
        var app = new ForeignApp { Name = "v2RayTun", Folder = folder };
        foreach (var (key, value) in prefs)
        {
            if (JsonUtils.ParseJson(value?.ToString() ?? string.Empty) is not JsonObject entry)
            {
                continue;
            }
            if (key.StartsWith("flutter.sub_", StringComparison.Ordinal) && V2pnSubs.IsHttpLink(JStr(entry, "url")))
            {
                app.Subscriptions.Add(new ForeignSub(JStr(entry, "remarks") ?? string.Empty, JStr(entry, "url")!));
            }
            else if (key.StartsWith("flutter.config_", StringComparison.Ordinal) && JStr(entry, "subscriptionId").IsNullOrEmpty()
                     && entry["singBoxOutbound"] is JsonObject outbound)
            {
                var copy = (JsonObject)outbound.DeepClone();
                copy["tag"] = JStr(entry, "remarks") ?? JStr(copy, "tag");
                AddLink(app, V2pnForeignLinks.ToLink(V2pnForeignLinks.FromSingboxOutbound(copy)));
            }
        }
        return app;
    }

    /// <summary>Karing: karing_subscribe.json, items with remark and urlOrPath; only remote ones come over.</summary>
    private static ForeignApp? ReadKaring(string folder)
    {
        if (JsonUtils.ParseJson(ReadText(Path.Combine(folder, "karing_subscribe.json")) ?? string.Empty)?["items"] is not JsonArray items)
        {
            return null;
        }
        var app = new ForeignApp { Name = "Karing", Folder = folder };
        foreach (var item in items.OfType<JsonObject>())
        {
            if (V2pnSubs.IsHttpLink(JStr(item, "urlOrPath")))
            {
                app.Subscriptions.Add(new ForeignSub(JStr(item, "remark") ?? string.Empty, JStr(item, "urlOrPath")!));
            }
        }
        return app;
    }

    /// <summary>
    /// Happ keeps its subscriptions encrypted (subs.db), so nothing can be read; it is reported as locked
    /// for the window to say how to move by hand.
    /// </summary>
    private static ForeignApp? ReadHapp(string folder) =>
        File.Exists(Path.Combine(folder, "subs.db")) ? new ForeignApp { Name = "Happ", Folder = folder, Locked = true } : null;

    #endregion Flutter clients

    #region Helpers

    private static void AddContent(ForeignApp app, string? content)
    {
        var links = LinksFromContent(content, out var skipped);
        app.Links.AddRange(links);
        app.Skipped += skipped;
        foreach (var url in ClashProviderUrls(content))
        {
            app.Subscriptions.Add(new ForeignSub(string.Empty, url));
        }
    }

    private static void AddLink(ForeignApp app, string? link)
    {
        if (link == null)
        {
            app.Skipped++;
        }
        else
        {
            app.Links.Add(link);
        }
    }

    /// <summary>The http proxy-providers of a Clash config: subscriptions in all but name.</summary>
    internal static List<string> ClashProviderUrls(string? yaml)
    {
        var urls = new List<string>();
        if (yaml.IsNullOrEmpty() || !yaml.Contains("proxy-providers", StringComparison.Ordinal))
        {
            return urls;
        }
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            if (stream.Documents.FirstOrDefault()?.RootNode is YamlMappingNode root && Child(root, "proxy-providers") is YamlMappingNode providers)
            {
                urls.AddRange(providers.Children.Values.OfType<YamlMappingNode>()
                    .Select(p => Str(p, "url"))
                    .Where(V2pnSubs.IsHttpLink)
                    .Select(u => u!));
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{Tag}: proxy-providers: {ex.Message}");
        }
        return urls;
    }

    private sealed class Row
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>Rows of a query run on a copy of the database; empty when it fails (another schema, say).</summary>
    private static List<Row> QueryCopy(string path, string sql)
    {
        var copy = CopyDatabase(path);
        if (copy == null)
        {
            return [];
        }
        try
        {
            using var db = new SQLiteConnection(copy, false);
            return db.Query<Row>(sql).Select(r => new Row { Id = r.Id ?? string.Empty, Label = r.Label ?? string.Empty, Url = r.Url ?? string.Empty }).ToList();
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{Tag}: {path}: {ex.Message}");
            return [];
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(copy)!, true);
            }
            catch
            {
                // A leftover in guiTemps is harmless.
            }
        }
    }

    private static string? JStr(JsonObject o, string key)
    {
        try
        {
            return o[key] is JsonValue value && value.ToString() is { Length: > 0 } text ? text : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? JInt(JsonObject o, string key) => int.TryParse(JStr(o, key), out var value) ? value : null;

    private static bool JBool(JsonObject o, string key) => JStr(o, key)?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Folders holding each of <paramref name="exeNames"/>, found in one walk: running copies first, then
    /// the usual places a portable app is unpacked to, a few levels down.
    /// </summary>
    private static Dictionary<string, List<string>> PortableIndex(params string[] exeNames)
    {
        var result = exeNames.ToDictionary(e => e, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }
        foreach (var exe in exeNames)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
            {
                using (process)
                {
                    try
                    {
                        if (Path.GetDirectoryName(process.MainModule?.FileName) is { } dir)
                        {
                            result[exe].Add(dir);
                        }
                    }
                    catch
                    {
                        // A copy running as administrator does not describe itself to us.
                    }
                }
            }
        }
        foreach (var root in SearchRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var (exe, dir) in SearchExe(root, exeNames, 3))
            {
                result[exe].Add(dir);
            }
        }
        return result;
    }

    #endregion Helpers
}
