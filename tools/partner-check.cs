#:project ../v2rayN/ServiceLib/ServiceLib.csproj
// Checks a provider's V2pn partnership the way the app does, with the app's own code and keys:
//
//   dotnet run tools/partner-check.cs -- <subscription link>
//       Downloads the subscription as V2pn would, reads its headers (v2pn-partner, v2pn-account-token, the
//       panel's name, links and traffic), downloads and checks the pack (certificate, signatures, term,
//       domains, pictures), then asks the partner API (account with the token, status) if the pack names one.
//
//   dotnet run tools/partner-check.cs -- --folder <pack folder> --subscription <subscription link>
//       The same checks for a pack on disk, before it is published.
//
// Every step prints OK or what is wrong; the exit code is 0 only when the look will show.

using System.Net;
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
        if (partner.Pack.Mark == null)
        {
            Note("no brand.mark: the subscription card in V2pn's list keeps a letter");
        }
        if (partner.Pack.Variants.Count == 0)
        {
            Note("no looks: the certificate lacks the edition grant, or the pack lists none V2pn knows");
        }
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
