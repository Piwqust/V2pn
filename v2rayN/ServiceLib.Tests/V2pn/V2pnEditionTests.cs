namespace ServiceLib.Tests.V2pn;

/// <summary>
/// The marker of an older partner build (v2pn-partner.txt, read once and moved into the settings: the person came from
/// that partner, or from none when it is missing or does not read), the sign-ins and links recorded in the settings, and
/// the tab that is open.
/// </summary>
public class V2pnEditionTests
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "v2pn-edition-" + Guid.NewGuid().ToString("N")[..8]);

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Test]
    [Arguments("configra", "configra")]
    [Arguments("configra\r\n", "configra")]
    [Arguments("﻿configra\n", "configra")]
    [Arguments("# the build of Configra\n\n  configra  \n", "configra")]
    [Arguments("kestrel-2", "kestrel-2")]
    public async Task Marker_WithAPartnerId_IsThatPartnersBuild(string text, string id)
    {
        await V2pnEdition.Parse(text).Should().BeEqualTo(id);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   \r\n\n")]
    [Arguments("# only a comment")]
    [Arguments("Configra")]
    [Arguments("c")]
    [Arguments("../configra")]
    [Arguments("configra extra")]
    [Arguments("\u0000\u0001PK\u0003\u0004")]
    [Arguments("a-name-that-is-far-too-long-for-a-partner-id")]
    public async Task Marker_WithoutAPartnerId_IsPlainV2pn(string? text)
    {
        await (V2pnEdition.Parse(text) == null).Should().BeTrue();
    }

    [Test]
    public async Task Read_MissingEmptyGarbageOrLarge_IsPlain_AnIdIsAPartner()
    {
        await (V2pnEdition.Read(_dir) == null).Should().BeTrue();

        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, V2pnEdition.FileName);
        await (V2pnEdition.Read(_dir) == null).Should().BeTrue();

        File.WriteAllBytes(file, []);
        await (V2pnEdition.Read(_dir) == null).Should().BeTrue();

        File.WriteAllBytes(file, [0xFF, 0xFE, 0x00, 0x41, 0x89, 0x50]);
        await (V2pnEdition.Read(_dir) == null).Should().BeTrue();

        File.WriteAllText(file, "configra\n" + new string('#', 2000));
        await (V2pnEdition.Read(_dir) == null).Should().BeTrue();

        File.WriteAllText(file, "configra\n");
        await V2pnEdition.Read(_dir).Should().BeEqualTo("configra");

        // An id V2pn does not list still marks a partner: V2pn just never finds it (V2pnPartnerTests).
        File.WriteAllText(file, "nobody");
        await V2pnEdition.Read(_dir).Should().BeEqualTo("nobody");
    }

    [Test]
    public async Task NoMarkerNoSignIn_NoTabs_V2pnsTabIsOpen()
    {
        var edition = new V2pnEdition(null);
        await edition.Any.Should().BeFalse();
        await edition.CameFrom.Count.Should().BeEqualTo(0);
        await (edition.Tab == null).Should().BeTrue();
        await edition.IsOpen("configra").Should().BeFalse();
        await edition.Open("configra").Should().BeFalse();
        // A tab saved for a partner the person no longer comes from does not open it.
        await (new V2pnEdition(null, tab: "configra").Tab == null).Should().BeTrue();
    }

    [Test]
    public async Task Marker_IsCameFrom_AndItsTabOpensBeforeAnyChoice()
    {
        var edition = new V2pnEdition("configra");
        await edition.CameFrom.Should().BeEquivalentTo(["configra"]);
        await edition.Tab.Should().BeEqualTo("configra");
        await edition.IsOpen("configra").Should().BeTrue();
        // Once V2pn's tab was chosen, it is V2pn's: "" is a choice, null is none.
        await (new V2pnEdition("configra", tab: string.Empty).Tab == null).Should().BeTrue();
        // A marker cannot be forgotten: the build came from it.
        await edition.ForgetSignIn("configra").Should().BeFalse();
        await edition.IsFrom("configra").Should().BeTrue();
    }

    [Test]
    public async Task SignIn_IsRecordedInTheSettings_TheTabChoiceIsKept_AndBothComeBackAtTheNextStart()
    {
        (List<string> SignedIn, string? Tab) saved = ([], null);
        V2pnEdition Start() => new("configra", saved.SignedIn, saved.Tab, e => saved = (e.SignedIn.ToList(), e.SavedTab));

        var edition = Start();
        var changes = 0;
        edition.Changed += () => changes++;
        await edition.RecordSignIn("kestrel").Should().BeTrue();
        await edition.RecordSignIn("kestrel").Should().BeFalse();
        await edition.CameFrom.Should().BeEquivalentTo(["configra", "kestrel"]);
        await edition.CameFrom[0].Should().BeEqualTo("configra");
        await edition.Open("kestrel").Should().BeTrue();
        await saved.SignedIn.Should().BeEquivalentTo(["kestrel"]);
        await saved.Tab.Should().BeEqualTo("kestrel");
        await (changes >= 2).Should().BeTrue();

        var again = Start();
        await again.Tab.Should().BeEqualTo("kestrel");
        await again.CameFrom.Should().BeEquivalentTo(["configra", "kestrel"]);

        await again.Open(null).Should().BeTrue();
        await saved.Tab.Should().BeEqualTo(string.Empty);
        await (Start().Tab == null).Should().BeTrue();

        // Forgotten (its last subscription went): no longer came from, and its open tab goes back to V2pn's.
        var third = Start();
        third.Open("kestrel");
        await third.ForgetSignIn("kestrel").Should().BeTrue();
        await third.IsFrom("kestrel").Should().BeFalse();
        await (third.Tab == null).Should().BeTrue();
        await saved.SignedIn.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Settings_WithWhatIsNotAPartnerId_AreIgnored()
    {
        var edition = new V2pnEdition("Not An Id", ["kestrel", "../x", "kestrel"], "Bad Tab");
        await (edition.Marker == null).Should().BeTrue();
        await edition.CameFrom.Should().BeEquivalentTo(["kestrel"]);
        await (edition.SavedTab == null).Should().BeTrue();
        await edition.RecordSignIn("NOPE").Should().BeFalse();
    }
}
