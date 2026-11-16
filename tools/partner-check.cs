#:project ../v2rayN/ServiceLib/ServiceLib.csproj
// Checks a V2pn partner the way the app does, with the app's own code and keys:
//
//   dotnet run tools/partner-check.cs -- partners/<id>
//       Offline, before publishing: listing.json and partner.json with every check of the app (the pack packed as
//       publish packs it, the pictures and their slots, the theme and what falls back, the old fields it ignores, the
//       server notes and buttons, the reviewed files against the grants of listing.json).
//
//   dotnet run tools/partner-check.cs -- <subscription link> [--index <folder or releases URL>] [--key <public key>]
//       A live subscription against the published index: downloads the subscription as V2pn would and reads its
//       headers, takes partner-index.json from where releases come from (or --index: partners/out, or a URL whose
//       latest/download/ has it), checks its signature against the built-in keys (or --key), finds the partner whose
//       domains hold the link, then downloads and checks its zip as V2pn does for a person who came from that partner,
//       in that partner's tab (a person who came from no partner takes none), then asks the partner API (account with
//       the token, status, servers, and recommend and card for the buttons that use them) if the pack names one.
//
// Every step prints OK, a note, or FAIL with what is wrong; the exit code is 0 only when the look will show and every
// reviewed file is used.

using System.Net;
using System.Text.Json.Nodes;
using ServiceLib;
using ServiceLib.Common;
using ServiceLib.Handler;

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var target = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && a != Option("--index") && a != Option("--key"));
if (target == null)
{
    Console.Error.WriteLine("usage: dotnet run tools/partner-check.cs -- partners/<id>");
    Console.Error.WriteLine("       dotnet run tools/partner-check.cs -- <subscription link> [--index <folder or releases URL>] [--key <public key>]");
    return 2;
}

var failed = false;
var reviewFailed = false;
void Ok(string text) => Console.WriteLine($"  OK   {text}");
void Bad(string text)
{
    failed = true;
    Console.WriteLine($"  FAIL {text}");
}
void Note(string text) => Console.WriteLine($"  --   {text}");

var today = DateOnly.FromDateTime(DateTime.UtcNow);
var offline = Directory.Exists(target);
var rootKeys = Option("--key") is { } testKey ? [testKey] : V2pnInfo.PartnerKeys;
Console.WriteLine($"V2pn {V2pnInfo.Version}, {rootKeys.Count} partner root key(s){(Option("--key") != null ? " (--key)" : " built in")}");

V2pnPartner? partner = null;
SubPanelInfo? panel = null;
string? folder = null;
var root = Path.Combine(Path.GetTempPath(), "v2pn-partner-check-" + Guid.NewGuid().ToString("N")[..8]);
var notesOf = new List<string>();
try
{
    if (offline)
    {
        folder = Path.GetFullPath(target);
        partner = CheckFolder(folder);
    }
    else
    {
        panel = await CheckSubscription(target);
        partner = await CheckPublished(target);
    }
    if (partner != null)
    {
        CheckPack(partner);
        CheckTheme(partner);
        CheckCatalog(partner, panel);
        CheckReviewed(partner);
        CheckLoginFlag(partner);
    }
}
catch (Exception ex)
{
    Bad(ex.Message);
}

// partners/<id>: what publish would put out for it, read back with the app's reader.
V2pnPartner? CheckFolder(string dir)
{
    Console.WriteLine($"listing and pack ({dir})");
    var published = V2pnPartnerPublisher.Build(dir, today, out var error);
    if (published == null)
    {
        Bad(error);
        return null;
    }
    var listing = published.Listing;
    Ok($"listing.json: {listing.Name} ({listing.Id}), domains {string.Join(", ", listing.Domains)}, grants {string.Join(", ", listing.Grants)}, until {listing.Until:yyyy-MM-dd}");
    if (published.Contact == null)
    {
        Note("no contact in listing.json: write how to reach the provider when its term runs out");
    }
    var pack = V2pnPartnerTrust.Unpack(published.Zip, listing, out var files, out error);
    if (pack == null)
    {
        Bad(error);
        return null;
    }
    Ok($"{listing.Zip.File}: {listing.Zip.Size / 1024.0:0.#} KB, {files.Count} file(s), unpacks as the app unpacks it");
    pack = V2pnPartnerSlots.Fit(pack, name => files.GetValueOrDefault(name), out var slotNotes);
    notesOf.AddRange(slotNotes);
    return new V2pnPartner(listing, pack, dir, V2pnPartnerFeatures.Read(listing, pack, name => files.GetValueOrDefault(name)));
}

// The subscription and its headers, as V2pn downloads it.
async Task<SubPanelInfo?> CheckSubscription(string subscription)
{
    Console.WriteLine("subscription");
    try
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        http.DefaultRequestHeaders.UserAgent.ParseAdd("v2rayN/7.16");
        using var response = await http.GetAsync(subscription);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            Bad($"{subscription} answered HTTP {(int)response.StatusCode}");
            return null;
        }
        var headers = SubPanelHeaders.Collect(response);
        var info = SubPanelHeaders.Parse(headers);
        Ok($"answered; name: {info.Title ?? "(none)"}, support: {info.SupportUrl ?? "(none)"}, cabinet: {info.WebPageUrl ?? "(none)"}");
        if (info.HasUserInfo)
        {
            Ok($"subscription-userinfo: {info.Download + info.Upload} of {info.Total} bytes, expires {info.Expire}");
        }
        else
        {
            Note("no subscription-userinfo: the look shows no traffic or term from headers");
        }
        if (headers.Keys.Any(k => string.Equals(k, SubPanelHeaders.Partner, StringComparison.OrdinalIgnoreCase)))
        {
            Note("v2pn-partner is sent but no longer read: the partner is the one whose domains hold the link (it can go)");
        }
        if (info.AccountToken == null)
        {
            Note("no v2pn-account-token: the look cannot ask the account API");
        }
        else
        {
            Ok("v2pn-account-token present");
        }
        return info;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
    {
        Bad($"{subscription}: {ex.Message}");
        return null;
    }
}

// The published index and the partner's zip, through the same code as the app.
async Task<V2pnPartner?> CheckPublished(string subscription)
{
    Console.WriteLine("partner index");
    var index = Option("--index");
    // As a copy whose marker names <cameFrom>, in that partner's tab; a person who came from no partner reads no index at all.
    V2pnPartners Manager(string cameFrom)
    {
        var manager = new V2pnPartners(rootKeys, () => root, new V2pnEdition(cameFrom))
        {
            SubscriptionUrls = () => Task.FromResult<IReadOnlyList<string?>>([subscription]),
        };
        if (index != null && Directory.Exists(index))
        {
            // A local publish (partners/out) served as a releases page would serve it.
            var local = Path.GetFullPath(index);
            manager.Sources = ["local"];
            manager.Fetch = (url, _) =>
            {
                var file = Path.Combine(local, url[(url.LastIndexOf('/') + 1)..]);
                return Task.FromResult(File.Exists(file) ? new V2pnHttp.Result(HttpStatusCode.OK, File.ReadAllBytes(file), null) : new V2pnHttp.Result(HttpStatusCode.NotFound, null, "HTTP 404"));
            };
        }
        else if (index != null)
        {
            manager.Sources = [index.TrimEnd('/')];
        }
        return manager;
    }
    // The index first, as from a partner no one lists (nothing is downloaded), to find whose domains hold the link.
    var probe = Manager("index-probe");
    Note($"from {string.Join(", ", probe.Sources)}");
    await probe.RefreshAsync(forceIndex: true);
    if (probe.Index == null)
    {
        Bad(probe.IndexError ?? "no index");
        return null;
    }
    Ok($"{V2pnPartnerTrust.IndexFile}: signed by the root key, {probe.Index.Partners.Count} partner(s): {string.Join(", ", probe.Index.Partners.Select(p => p.Id))}");
    foreach (var problem in probe.Index.Problems)
    {
        Bad($"index: {problem}");
    }
    if (probe.Index.ForUrl(subscription) is not { } whose)
    {
        Bad($"no partner lists {Utils.TryUri(subscription)?.IdnHost ?? subscription}: in no partner's tab is this subscription that partner's");
        return null;
    }
    Note($"checked in the tab of {whose.Id}, as for a person who came from it; V2pn's tab shows nothing of a partner");
    var manager = Manager(whose.Id);
    await manager.RefreshAsync(forceIndex: true);
    var listing = manager.ListingFor(subscription);
    if (listing == null)
    {
        Bad(manager.IndexError ?? $"the tab of {whose.Id} does not take {subscription}");
        return null;
    }
    Ok($"{listing.Name} ({listing.Id}): domains {string.Join(", ", listing.Domains)}, grants {string.Join(", ", listing.Grants)}, until {listing.Until:yyyy-MM-dd}");
    if (listing.Until < today)
    {
        Bad($"its term ran out on {listing.Until:yyyy-MM-dd}: V2pn drops it");
        return null;
    }
    var found = manager.ForUrl(subscription);
    if (found == null)
    {
        Bad($"{listing.Zip.File}: {manager.ErrorFor(listing.Id) ?? "not taken"}");
        return null;
    }
    Ok($"{listing.Zip.File}: {listing.Zip.Size / 1024.0:0.#} KB, its hash is the index's, every file passed");
    notesOf.AddRange(manager.SlotNotesFor(found.Id));
    return found;
}

void CheckPack(V2pnPartner p)
{
    Console.WriteLine("pack");
    var days = p.Listing.Until.DayNumber - today.DayNumber;
    if (days < 30)
    {
        Note($"the term runs out in {days} day(s): extend until in listing.json and publish");
    }
    Ok($"pack: {p.Name}, accent {p.Pack.Accent ?? "(none)"}, logo {p.Pack.Logo ?? "(none)"}, on light {p.Pack.LogoOnLight ?? "(the logo)"}, mark {p.Pack.Mark ?? "(none)"}");
    var pictures = p.Pack.Assets.Where(a => !V2pnReviewedFiles.All.Contains(a.Name)).Select(a => $"{a.Name} {a.Size / 1024} KB").ToList();
    Ok($"pictures: {(pictures.Count == 0 ? "(none)" : string.Join(", ", pictures))}");
    foreach (var note in notesOf)
    {
        Bad($"picture not shown: {note}");
    }
    if (p.Pack.Mark == null)
    {
        Note("no brand.mark: the subscription card in V2pn's list keeps a letter");
    }
    if (!p.HasEdition)
    {
        Note("no edition grant: V2pn keeps its own look; only the name and links are used");
    }
    foreach (var note in p.Pack.Notes)
    {
        Note(note);
    }
}

// The look as V2pn will draw it: Accent or Scene, what it took, what fell back and why, what it did to the colours.
void CheckTheme(V2pnPartner p)
{
    if (p.Pack.Theme is not { } theme)
    {
        return;
    }
    var look = theme.Background.Kind == V2pnThemeBackgroundKinds.Solid
        ? "Accent (no background: the logo, the colour, the card)"
        : $"Scene on its {theme.Background.Kind} background{(theme.Background.Colors.Count > 0 ? " " + string.Join(" ", theme.Background.Colors) : "")}{(theme.Background.Art is { } art ? " " + art : "")}";
    Ok($"look: {look}; brand {string.Join(" ", theme.Brand.DefaultIfEmpty("(the pack's accent)"))}");
    foreach (var note in theme.Notes)
    {
        Note($"theme: {note}");
    }
    foreach (var note in V2pnThemePalette.Describe(theme))
    {
        Note($"theme colours: {note}");
    }
}

// The API, if the pack names one; a live check only.
if (partner?.Pack.ApiUrl != null && !offline)
{
    Console.WriteLine("api");
    var account = await V2pnPartnerApi.GetAccountAsync(partner, panel?.AccountToken);
    if (panel?.AccountToken == null)
    {
        Note("account: not asked, the subscription sent no token");
    }
    else if (account == null)
    {
        Bad($"account: {partner.Pack.ApiUrl}account gave no readable answer for this token");
    }
    else
    {
        Ok($"account: plan {account.Plan ?? "-"}, servers {account.Healthy?.ToString() ?? "-"} of {account.Total?.ToString() ?? "-"}, devices {account.Devices?.ToString() ?? "-"}, until {account.Until?.ToString("yyyy-MM-dd") ?? "-"}");
    }
    var incidents = await V2pnPartnerApi.GetStatusAsync(partner);
    if (incidents == null)
    {
        Note("status: no readable answer (optional)");
    }
    else
    {
        Ok($"status: {incidents.Count} incident(s)");
    }
    if (partner.Listing.Allows(V2pnPartnerGrants.Actions))
    {
        var live = await V2pnPartnerApi.GetServersAsync(partner, panel?.AccountToken);
        if (live == null)
        {
            Note("servers: no readable answer (optional; the pack's notes stay)");
        }
        else
        {
            Ok($"servers: {live.Count} note(s), {live.Count(n => n.Load != null)} with load, {live.Count(n => n.Healthy == false)} down");
            if (!live.Any(n => n.Load != null) && partner.Pack.ActionList.Any(a => a.Strategy == V2pnStrategies.Load))
            {
                Note("a button picks by load, but no server has a load: it falls back to ping");
            }
        }
    }
    foreach (var action in partner.Pack.ActionList.Where(a => a.Do == V2pnActionKinds.ConnectRecommend))
    {
        var recommended = await V2pnPartnerApi.GetRecommendAsync(partner, panel?.AccountToken, action.Purpose!);
        if (recommended == null)
        {
            Bad($"recommend?purpose={action.Purpose} (button {action.Id}): no readable answer; V2pn falls back to the fastest server for {action.Purpose}");
        }
        else
        {
            Ok($"recommend?purpose={action.Purpose} (button {action.Id}): {string.Join(", ", recommended.Take(5))}{(recommended.Count > 5 ? ", ..." : "")}");
        }
    }
    foreach (var action in partner.Pack.ActionList.Where(a => a.Do == V2pnActionKinds.InfoCard))
    {
        var card = await V2pnPartnerApi.GetCardAsync(partner, panel?.AccountToken, action.Id);
        if (card == null)
        {
            Bad($"card?id={action.Id}: no readable answer; the button says the partner did not answer");
        }
        else
        {
            Ok($"card?id={action.Id}: {card.Title ?? "(no title)"}, {card.Text.Length} characters");
        }
    }
    // Sign-in by code: one code asked for and its shape checked. login/poll is never asked, so no sign-in is completed;
    // the code simply runs out at the partner.
    if (V2pnPartnerLogin.Offers(partner))
    {
        var (code, problem) = await V2pnPartnerLogin.StartAsync(partner);
        if (code == null)
        {
            Bad(problem == V2pnLoginProblem.NoAnswer
                ? $"login/start: {partner.Pack.ApiUrl}login/start gave no answer with status 200; V2pn says the provider does not answer"
                : "login/start: the answer is not six digits in \"code\" and 32 to 256 url-safe characters in \"token\"");
        }
        else
        {
            Ok($"login/start: code of six digits, token of {code.Token.Length} characters, term {code.Expires.TotalMinutes:0} min");
            if (code.Url == null)
            {
                Note("login/start: no url, or one V2pn does not open (it must be https on t.me or the listing's domains): V2pn shows the digits without a QR code");
            }
            else
            {
                Ok($"login/start: url {code.Url}");
            }
            Note("login/poll: not asked, so no sign-in is completed (the code runs out on its own)");
        }
    }
}
else if (partner?.Pack.ApiUrl != null)
{
    Note($"api {partner.Pack.ApiUrl}: not asked offline; check it with a subscription link");
}
else if (partner != null)
{
    Note("no api in the pack, or no account grant (optional): the look shows the panel's headers only");
}

try
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, true);
    }
}
catch (IOException)
{
}
Console.WriteLine(failed || partner == null ? "RESULT: V2pn will not show this partner's look" : "RESULT: V2pn will show this partner's look");
if (reviewFailed)
{
    Console.WriteLine("RESULT: some reviewed files are not used (see \"reviewed files\" above)");
}
return failed || reviewFailed || partner == null ? 1 : 0;

// "login": the flag, and whether the pack has what the sign-in needs (an api under the account grant).
void CheckLoginFlag(V2pnPartner p)
{
    JsonNode? flag = null;
    try
    {
        flag = (JsonNode.Parse(File.ReadAllBytes(Path.Combine(p.Folder, "partner.json"))) as JsonObject)?["login"];
    }
    catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
    {
    }
    Console.WriteLine("sign-in by code");
    if (flag == null)
    {
        Note("no login (optional): V2pn does not offer to sign in with this partner by code");
        return;
    }
    if (flag is not JsonValue value || !value.TryGetValue<bool>(out var on))
    {
        Bad("login is not true or false; V2pn reads it as false");
        return;
    }
    if (!on)
    {
        Note("login is false: V2pn does not offer to sign in by code");
    }
    else if (V2pnPartnerLogin.Offers(p))
    {
        Ok($"login: V2pn offers to sign in with {p.Name} by code, through {p.Pack.ApiUrl}login/start and login/poll{(offline ? " (asked only in a live check)" : "")}");
    }
    else
    {
        Bad("login is true, but the pack has no api under the account grant on the listing's domains: V2pn does not offer the sign-in");
    }
}

// routing.json, transport.json, core.json: V2pn's checks of each, and whether the index's reviewed hash matches.
void CheckReviewed(V2pnPartner partner)
{
    var listed = partner.Pack.Assets.Where(a => V2pnReviewedFiles.All.Contains(a.Name)).ToList();
    var unlisted = folder == null ? new List<string>() : V2pnReviewedFiles.All.Where(n => File.Exists(Path.Combine(folder, n)) && listed.All(a => a.Name != n)).ToList();
    if (listed.Count == 0 && unlisted.Count == 0 && partner.Listing.ReviewedFiles.Count == 0)
    {
        return;
    }
    Console.WriteLine("reviewed files");
    foreach (var name in unlisted)
    {
        Bad($"{name} is in the folder but not in partner.json's assets: run tools/partner.cs pack");
        reviewFailed = true;
    }
    foreach (var asset in listed)
    {
        var content = File.ReadAllBytes(Path.Combine(partner.Folder, asset.Name));
        var grant = V2pnReviewedFiles.GrantOf(asset.Name);
        string problem;
        var valid = asset.Name switch
        {
            V2pnReviewedFiles.Routing => V2pnRoutingPreset.Parse(content, out problem) != null,
            V2pnReviewedFiles.Transport => V2pnTransport.Parse(content, out problem) != null,
            _ => V2pnCoreFragment.Parse(content, out problem) != null,
        };
        if (valid)
        {
            Ok($"{asset.Name}: format and filter pass (sha256 {asset.Sha256})");
        }
        else
        {
            Bad($"{asset.Name}: {problem}");
            reviewFailed = true;
        }
        var state = partner.Features.File(asset.Name)?.State;
        switch (state)
        {
            case V2pnReviewState.Ready:
                Ok($"{asset.Name}: the {grant} grant and the index's hash name this very file; V2pn uses it");
                break;
            case V2pnReviewState.Changed:
                Bad($"{asset.Name}: changed after the review (index {partner.Listing.ReviewedFiles[asset.Name][..12]}..., pack {asset.Sha256[..12]}...); V2pn does not use it until it is reviewed and published again");
                reviewFailed = true;
                break;
            case V2pnReviewState.NotReviewed:
                Note($"{asset.Name}: not reviewed yet (no {grant} grant in listing.json); read it with tools/partner.cs review, then add the grant");
                reviewFailed = true;
                break;
            default:
                Bad($"{asset.Name}: reviewed, but this V2pn refuses it: {partner.Features.File(asset.Name)?.Problem}");
                reviewFailed = true;
                break;
        }
    }
    foreach (var (name, hash) in partner.Listing.ReviewedFiles.Where(r => listed.All(a => a.Name != r.Key)))
    {
        Note($"the index names {name} ({hash[..12]}...), but the pack has no such file");
    }
}

// The pack's server notes and buttons: what V2pn took, and what it left out and why.
void CheckCatalog(V2pnPartner partner, SubPanelInfo? panelInfo)
{
    Console.WriteLine("servers and buttons");
    JsonObject? raw = null;
    try
    {
        raw = JsonNode.Parse(File.ReadAllBytes(Path.Combine(partner.Folder, "partner.json"))) as JsonObject;
    }
    catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
    {
    }
    var rawServerList = (raw?["servers"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
    var rawActionList = (raw?["actions"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
    if (!partner.Listing.Allows(V2pnPartnerGrants.Actions))
    {
        if (rawServerList.Count + rawActionList.Count > 0)
        {
            Bad("the pack has servers or actions, but the listing lacks the actions grant: V2pn shows none of them");
        }
        else
        {
            Note("no actions grant: no buttons or server notes");
        }
    }
    else
    {
        var notes = partner.Pack.ServerNotes;
        if (rawServerList.Count > 0)
        {
            var groups = notes.Select(n => n.Group).Where(g => g != null).Distinct().ToList();
            Ok($"servers: {notes.Count} of {rawServerList.Count} read; groups: {(groups.Count == 0 ? "none" : string.Join(", ", groups))}; hidden: {notes.Count(n => n.IsHidden)}");
            for (var i = 0; i < rawServerList.Count && i < V2pnPartnerCatalog.MaxServers; i++)
            {
                var item = rawServerList[i];
                string Field(string key) => item[key] is JsonValue v && v.TryGetValue<string>(out var text) ? text.Trim() : string.Empty;
                if (Field("name").Length == 0 && Field("host").Length == 0)
                {
                    Bad($"servers[{i}]: no name or host to match a server by; left out");
                    continue;
                }
                if (Field("group").Length > V2pnPartnerCatalog.MaxGroup) Bad($"servers[{i}].group: longer than {V2pnPartnerCatalog.MaxGroup} characters, left out");
                if (Field("city").Length > V2pnPartnerCatalog.MaxCity) Bad($"servers[{i}].city: longer than {V2pnPartnerCatalog.MaxCity} characters, left out");
                if (Field("note").Length > V2pnPartnerCatalog.MaxNote) Bad($"servers[{i}].note: longer than {V2pnPartnerCatalog.MaxNote} characters, left out");
                if (Field("country").Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(Field("country"), "^[A-Za-z]{2}$")) Bad($"servers[{i}].country: not an ISO 3166 two-letter code, left out");
                var purposes = item["purpose"] is JsonArray list ? list.Select(n => n?.ToString().ToLowerInvariant()) : [Field("purpose").ToLowerInvariant()];
                foreach (var unknown in purposes.Where(x => !string.IsNullOrEmpty(x) && !V2pnPurposes.All.Contains(x)))
                {
                    Note($"servers[{i}].purpose: {unknown} is not one V2pn knows ({string.Join(", ", V2pnPurposes.All)}); left out");
                }
            }
        }
        var actions = partner.Pack.ActionList;
        if (rawActionList.Count > 0)
        {
            Ok($"buttons: {(actions.Count == 0 ? "none usable" : string.Join("; ", actions.Select(a => $"{a.Id} '{a.Label}' {a.Do}{(a.Strategy != null ? " " + a.Strategy : "")}{(a.Purpose != null ? " " + a.Purpose : "")}")))}");
            if (rawActionList.Count > V2pnPartnerCatalog.MaxActions)
            {
                Note($"{rawActionList.Count} actions listed: V2pn shows the first {V2pnPartnerCatalog.MaxActions} that work");
            }
            foreach (var item in rawActionList.Take(32))
            {
                var id = item["id"]?.ToString() ?? "?";
                if (actions.Any(a => a.Id == id))
                {
                    if (item["icon"]?.ToString() is { } icon && !V2pnActionIcons.All.Contains(icon))
                    {
                        Note($"action {id}: icon {icon} is not one of V2pn's ({string.Join(", ", V2pnActionIcons.All)}); the button has none");
                    }
                    continue;
                }
                if (actions.Count >= V2pnPartnerCatalog.MaxActions)
                {
                    break;
                }
                var kind = item["do"]?.ToString();
                var why = kind == null || !V2pnActionKinds.All.Contains(kind) ? $"do {kind ?? "(none)"} is not one V2pn knows ({string.Join(", ", V2pnActionKinds.All)})"
                    : (item["label"]?.ToString()?.Trim().Length ?? 0) is 0 or > V2pnPartnerCatalog.MaxLabel ? $"the label needs 1 to {V2pnPartnerCatalog.MaxLabel} characters"
                    : kind is "connect.recommend" or "info.card" && partner.Pack.ApiUrl == null ? "it needs the API: the account grant and api in the pack"
                    : kind == "open.link" ? "the link must be https on the listing's domains or t.me, or tg:"
                    : "a field is missing or not allowed (id, strategy, purpose, scope, server or group)";
                Bad($"action {id} left out: {why}");
            }
        }
        if (rawServerList.Count == 0 && rawActionList.Count == 0)
        {
            Note("no servers or actions in the pack (optional)");
        }
    }
    var lockAsked = raw?["lock"]?.ToString() == "true" || panelInfo?.HideSettings == true || panelInfo?.HideUrl == true;
    if (lockAsked)
    {
        if (partner.Listing.Allows(V2pnPartnerGrants.Lock))
        {
            Ok($"lock: settings {(partner.Pack.Lock || panelInfo?.HideSettings == true ? "closed" : "open")}, link {(panelInfo?.HideUrl == true ? "hidden" : "shown")}");
        }
        else
        {
            Bad("lock, hide-settings or hide-url asked for, but the listing lacks the lock grant: V2pn keeps everything open");
        }
    }
}
