#:project ../v2rayN/ServiceLib/ServiceLib.csproj
// Checks a provider's V2pn partnership the way the app does, with the app's own code and keys:
//
//   dotnet run tools/partner-check.cs -- <subscription link>
//       Downloads the subscription as V2pn would, reads its headers (v2pn-partner, v2pn-account-token, the
//       panel's name, links and traffic), downloads and checks the pack (certificate, signatures, term,
//       domains, pictures), reads its server notes and buttons (servers, actions, lock), then asks the partner API
//       (account with the token, status, servers, and recommend and card for the buttons that use them) if the
//       pack names one.
//
//   dotnet run tools/partner-check.cs -- --folder <pack folder> --subscription <subscription link>
//       The same checks for a pack on disk, before it is published.
//
// Every step prints OK or what is wrong; the exit code is 0 only when the look will show.

using System.Net;
using System.Text.Json.Nodes;
using ServiceLib;
using ServiceLib.Handler;

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var folder = Option("--folder");
var subscription = Option("--subscription") ?? args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && a != folder);
if (subscription == null)
{
    Console.Error.WriteLine("usage: dotnet run tools/partner-check.cs -- <subscription link>");
    Console.Error.WriteLine("       dotnet run tools/partner-check.cs -- --folder <pack folder> --subscription <subscription link>");
    return 2;
}

var failed = false;
void Ok(string text) => Console.WriteLine($"  OK   {text}");
void Bad(string text)
{
    failed = true;
    Console.WriteLine($"  FAIL {text}");
}
void Note(string text) => Console.WriteLine($"  --   {text}");

Console.WriteLine($"V2pn {V2pnInfo.Version}, {V2pnInfo.PartnerKeys.Count} partner root key(s) built in{(V2pnInfo.IsRelease ? "" : "; a development build also takes http on 127.0.0.1")}");

// 1. The subscription and its headers.
Console.WriteLine("subscription");
SubPanelInfo? panel = null;
try
{
    using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
    http.DefaultRequestHeaders.UserAgent.ParseAdd("v2rayN/7.16");
    using var response = await http.GetAsync(subscription);
    if (response.StatusCode != HttpStatusCode.OK)
    {
        Bad($"{subscription} answered HTTP {(int)response.StatusCode}");
    }
    else
    {
        panel = SubPanelHeaders.Parse(SubPanelHeaders.Collect(response));
        Ok($"answered; name: {panel.Title ?? "(none)"}, support: {panel.SupportUrl ?? "(none)"}, cabinet: {panel.WebPageUrl ?? "(none)"}");
        if (panel.HasUserInfo)
        {
            Ok($"subscription-userinfo: {panel.Download + panel.Upload} of {panel.Total} bytes, expires {panel.Expire}");
        }
        else
        {
            Note("no subscription-userinfo: the look shows no traffic or term from headers");
        }
        if (panel.PartnerUrl == null)
        {
            Bad("no valid v2pn-partner header (https, no query, ending in /); V2pn stays plain");
        }
        else
        {
            Ok($"v2pn-partner: {panel.PartnerUrl}");
        }
        if (panel.AccountToken == null)
        {
            Note("no v2pn-account-token: the look cannot ask the account API");
        }
        else
        {
            Ok("v2pn-account-token present");
        }
    }
}
catch (Exception ex)
{
    Bad($"{subscription}: {ex.Message}");
}

// 2. The pack, from its address or from disk.
Console.WriteLine("pack");
var root = Path.Combine(Path.GetTempPath(), "v2pn-partner-check-" + Guid.NewGuid().ToString("N")[..8]);
var partners = new V2pnPartners(V2pnInfo.PartnerKeys, () => root);
V2pnPartner? partner = null;
try
{
    if (folder != null)
    {
        var name = new DirectoryInfo(folder).Name;
        var target = Path.Combine(root, "check");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(folder))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        var cert = V2pnPartnerTrust.ReadCert(File.ReadAllBytes(Path.Combine(target, "cert.json")), File.ReadAllText(Path.Combine(target, "cert.json.sig")).Trim(), V2pnInfo.PartnerKeys, DateOnly.FromDateTime(DateTime.UtcNow), out var certError);
        if (cert == null)
        {
            Bad($"cert.json: {certError}");
        }
        else
        {
            File.WriteAllText(Path.Combine(target, "source.url"), panel?.PartnerUrl ?? subscription);
            var renamed = Path.Combine(root, cert.Partner);
            Directory.Move(target, renamed);
            partner = partners.ReadFolder(renamed, out var error);
            if (partner == null)
            {
                Bad($"{name}: {error}");
            }
        }
    }
    else if (panel?.PartnerUrl != null)
    {
        partner = await partners.RefreshAsync(subscription, panel.PartnerUrl);
        if (partner == null)
        {
            Bad(partners.ErrorFor(panel.PartnerUrl) ?? "refused");
        }
    }
    if (partner != null)
    {
        Ok($"certificate: {partner.Cert.Name} ({partner.Id}), domains {string.Join(", ", partner.Cert.Domains)}, grants {string.Join(", ", partner.Cert.Grants)}, until {partner.Cert.Expires:yyyy-MM-dd}");
        if (!V2pnPartnerTrust.DomainAllowed(partner.Cert, subscription))
        {
            Bad($"the subscription's domain is not in the certificate: V2pn will not dress this subscription");
        }
        var days = partner.Cert.Expires.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
        if (days < 30)
        {
            Note($"the certificate runs out in {days} day(s): ask V2pn's owner for a new one");
        }
        Ok($"pack: {partner.Name}, accent {partner.Pack.Accent ?? "(none)"}, looks {string.Join(", ", partner.Pack.Variants)} (opens on {partner.Pack.DefaultVariant})");
        Ok($"pictures: {string.Join(", ", partner.Pack.Assets.Select(a => $"{a.Name} {a.Size / 1024} KB"))}");
        foreach (var note in partners.SlotNotesFor(partner.Id))
        {
            Bad($"picture not shown: {note}");
        }
        if (partner.Pack.Mark == null)
        {
            Note("no brand.mark: the subscription card in V2pn's list keeps a letter");
        }
        if (partner.Pack.Variants.Count == 0)
        {
            Note("no looks: the certificate lacks the edition grant, or the pack lists none V2pn knows");
        }
        CheckCatalog(partner, panel);
    }
}
catch (Exception ex)
{
    Bad(ex.Message);
}

// 3. The API, if the pack names one.
if (partner?.Pack.ApiUrl != null)
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
    if (partner.Cert.Allows(V2pnPartnerGrants.Actions))
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
}
else if (partner != null)
{
    Note("no api in the pack (optional): the look shows the panel's headers only");
}

try
{
    Directory.Delete(root, true);
}
catch (IOException)
{
}
Console.WriteLine(failed ? "RESULT: V2pn will not show this partner's look" : "RESULT: V2pn will show this partner's look");
return failed ? 1 : 0;

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
    if (!partner.Cert.Allows(V2pnPartnerGrants.Actions))
    {
        if (rawServerList.Count + rawActionList.Count > 0)
        {
            Bad("the pack has servers or actions, but the certificate lacks the actions grant: V2pn shows none of them");
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
                    : kind == "open.link" ? "the link must be https on the certificate's domains or t.me, or tg:"
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
        if (partner.Cert.Allows(V2pnPartnerGrants.Lock))
        {
            Ok($"lock: settings {(partner.Pack.Lock || panelInfo?.HideSettings == true ? "closed" : "open")}, link {(panelInfo?.HideUrl == true ? "hidden" : "shown")}");
        }
        else
        {
            Bad("lock, hide-settings or hide-url asked for, but the certificate lacks the lock grant: V2pn keeps everything open");
        }
    }
}
