using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.V2pn;

/// <summary>"Start in the tray" applies to the sign-in launch, and the main window reopens where it was left.</summary>
public class V2pnWindowLifecycleTests
{
    [Test]
    public async Task StartsHidden_OnlyWhenTheSettingIsOn()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        config.UiItem.V2pnAutostartArg = true;

        await AutoStartupHandler.StartsHidden(config, autostartArg: true).Should().BeFalse();
        await AutoStartupHandler.StartsHidden(config, autostartArg: false).Should().BeFalse();
    }

    [Test]
    public async Task StartsHidden_AtSignInAlways_ByHandOnlyWhileTheEntryIsOld()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        config.UiItem.AutoHideStartup = true;

        // An entry written before the argument existed: a launch without it may still be the sign-in one.
        config.UiItem.V2pnAutostartArg = false;
        await AutoStartupHandler.StartsHidden(config, autostartArg: false).Should().BeTrue();
        await AutoStartupHandler.StartsHidden(config, autostartArg: true).Should().BeTrue();

        // The entry carries the argument: a launch without it is the user's, and shows the window
        // (other platforms write no argument, so there every launch keeps starting hidden).
        config.UiItem.V2pnAutostartArg = true;
        await AutoStartupHandler.StartsHidden(config, autostartArg: true).Should().BeTrue();
        await AutoStartupHandler.StartsHidden(config, autostartArg: false).Should().BeEqualTo(!Utils.IsWindows());
    }

    [Test]
    public async Task AutostartCommand_QuotesThePathAndAddsTheArgument()
    {
        await AutoStartupHandler.AutostartCommand(@"C:\Program Files\V2pn\V2pn.exe")
            .Should().BeEqualTo("\"C:\\Program Files\\V2pn\\V2pn.exe\" --autostart");
    }

    [Test]
    public async Task SaveWindowPlacement_KeepsNormalBoundsAndTheMaximizedFlag()
    {
        var config = CoreConfigTestFactory.CreateConfig();

        ConfigHandler.SaveWindowPlacement(config, "MainWindow", 1100.6, 700.2, -1500, 120, maximized: true);
        var item = ConfigHandler.GetWindowSizeItem(config, "MainWindow");

        await item.Should().NotBeNull();
        await item!.Width.Should().BeEqualTo(1100);
        await item.Height.Should().BeEqualTo(700);
        await item.X.Should().BeEqualTo(-1500);
        await item.Y.Should().BeEqualTo(120);
        await item.IsMaximized.Should().BeTrue();

        // Saved again while normal and hidden (no position known): the flag clears, the entry stays single.
        ConfigHandler.SaveWindowPlacement(config, "MainWindow", 1000, 640, null, null, maximized: false);
        await config.UiItem.WindowSizeItem.Count(t => t.TypeName == "MainWindow").Should().BeEqualTo(1);
        await ConfigHandler.GetWindowSizeItem(config, "MainWindow")!.IsMaximized.Should().BeFalse();
        await ConfigHandler.GetWindowSizeItem(config, "MainWindow")!.X.Should().BeNull();
    }
}
