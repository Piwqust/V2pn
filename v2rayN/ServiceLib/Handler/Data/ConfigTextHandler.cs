namespace ServiceLib.Handler.Data;

public static class ConfigTextHandler
{
    private static readonly string _configRes = Global.ConfigFileName;
    private static readonly string _tag = "ConfigTextHandler";

    public static Config Config
    {
        get => ConfigState.Instance.Current;
        private set => ConfigState.Instance.SetCurrent(value);
    }

    #region Init

    public static bool Init()
    {
        var config = LoadConfig();
        if (config == null)
        {
            return false;
        }

        Config = config;
        return true;
    }

    /// <summary>
    /// Load the application configuration file
    /// If the file exists, deserialize it from JSON
    /// If not found, create a new Config object with default settings
    /// Initialize default values for missing configuration sections
    /// </summary>
    /// <returns>Config object containing application settings or null if there's an error</returns>
    private static Config? LoadConfig()
    {
        Config? config = null;
        var result = EmbedUtils.LoadResource(Utils.GetConfigPath(_configRes));
        if (result.IsNotEmpty())
        {
            config = JsonUtils.Deserialize<Config>(result);
        }
        else
        {
            if (File.Exists(Utils.GetConfigPath(_configRes)))
            {
                Logging.SaveLog("LoadConfig Exception");
                return null;
            }
        }

        config ??= new Config();

        config.CoreBasicItem ??= new()
        {
            LogEnabled = false,
            Loglevel = "warning",
        };

        if (config.Inbound == null)
        {
            config.Inbound = [];
            InItem inItem = new()
            {
                Protocol = nameof(EInboundProtocol.socks),
                LocalPort = 10808,
                UdpEnabled = true,
                SniffingEnabled = true,
                RouteOnly = false,
            };

            config.Inbound.Add(inItem);
        }
        else
        {
            if (config.Inbound.Count > 0)
            {
                config.Inbound.First().Protocol = nameof(EInboundProtocol.socks);
            }
        }

        config.RoutingBasicItem ??= new();
        if (config.RoutingBasicItem.DomainStrategy.IsNullOrEmpty())
        {
            config.RoutingBasicItem.DomainStrategy = Global.DomainStrategies.First();
        }

        config.KcpItem ??= new KcpItem
        {
            Mtu = 1350,
            Tti = 50,
            UplinkCapacity = 12,
            DownlinkCapacity = 100,
            CwndMultiplier = 1,
            MaxSendingWindow = 2 * 1024 * 1024,
        };
        config.KcpItem.CwndMultiplier = config.KcpItem.CwndMultiplier <= 0 ? 1 : config.KcpItem.CwndMultiplier;
        config.KcpItem.MaxSendingWindow = config.KcpItem.MaxSendingWindow <= 0 ? (2 * 1024 * 1024) : config.KcpItem.MaxSendingWindow;
        config.GrpcItem ??= new GrpcItem
        {
            IdleTimeout = 60,
            HealthCheckTimeout = 20,
            PermitWithoutStream = false,
            InitialWindowsSize = 0,
        };
        config.TunModeItem ??= new TunModeItem
        {
            EnableTun = false,
            Mtu = 9000,
            IcmpRouting = Global.TunIcmpRoutingPolicies.First(),
            EnableLegacyProtect = true,
        };
        // V2pn shows a live speed graph, so real-time speed is on for new installs.
        config.GuiItem ??= new() { DisplayRealTimeSpeed = true };
        if (!Global.RootCertProviders.Contains(config.GuiItem.RootCertProvider))
        {
            config.GuiItem.RootCertProvider = Global.RootCertProviders.First();
        }
        config.MsgUIItem ??= new();

        config.UiItem ??= new();
        config.UiItem.MainColumnItem ??= [];
        config.UiItem.WindowSizeItem ??= [];
        if (config.UiItem.FrozenColumnCount == null)
        {
            var lvColumnItem = config.UiItem.MainColumnItem.OrderBy(t => t.Index).ToList();
            if (lvColumnItem.Count == 0
                || (lvColumnItem.Count >= 2
                    && lvColumnItem[0].Name.Equals("ConfigType")
                    && lvColumnItem[1].Name.Equals("Remarks")))
            {
                config.UiItem.FrozenColumnCount = 2;
            }
            else
            {
                config.UiItem.FrozenColumnCount = 0;
            }
        }

        if (config.UiItem.CurrentLanguage.IsNullOrEmpty())
        {
            // V2pn: English is the language of a new install everywhere (v2rayN picked Chinese on a Chinese
            // system); the others stay one click away in Appearance.
            config.UiItem.CurrentLanguage = "en";
        }

        config.ConstItem ??= new ConstItem();

        config.SimpleDNSItem ??= ConfigDbHandler.InitBuiltinSimpleDNS();
        config.SimpleDNSItem.BlockAAAAQuery ??= false;
        config.SimpleDNSItem.FakeIPRange ??= Global.FakeIPRanges.FirstOrDefault();
        config.SimpleDNSItem.FakeIPv6 ??= false;
        config.SimpleDNSItem.FakeIPv6Range ??= Global.FakeIPv6Ranges.First();
        config.SimpleDNSItem.GlobalFakeIp ??= true;
        config.SimpleDNSItem.BootstrapDNS ??= Global.DomainPureIPDNSAddress.FirstOrDefault();
        config.SimpleDNSItem.ServeStale ??= false;
        config.SimpleDNSItem.ParallelQuery ??= false;
        config.SimpleDNSItem.EnableHappyEyeballs ??= false;

        config.SpeedTestItem ??= new();
        if (config.SpeedTestItem.SpeedTestTimeout < 10)
        {
            config.SpeedTestItem.SpeedTestTimeout = 10;
        }
        if (config.SpeedTestItem.SpeedTestUrl.IsNullOrEmpty())
        {
            config.SpeedTestItem.SpeedTestUrl = Global.SpeedTestUrls.First();
        }
        if (config.SpeedTestItem.SpeedPingTestUrl.IsNullOrEmpty())
        {
            config.SpeedTestItem.SpeedPingTestUrl = Global.SpeedPingTestUrls.First();
        }
        config.SpeedTestItem.MixedConcurrencyCount = Math.Max(config.SpeedTestItem.MixedConcurrencyCount, Global.SpeedTestConcurrencyCountMin);
        if (config.SpeedTestItem.UdpTestTarget.IsNullOrEmpty())
        {
            config.SpeedTestItem.UdpTestTarget = Global.UdpTestTargets.First();
        }

        config.Mux4RayItem ??= new()
        {
            Concurrency = 8,
            XudpConcurrency = 16,
            XudpProxyUDP443 = "reject"
        };

        config.Mux4SboxItem ??= new()
        {
            Protocol = Global.SingboxMuxs.First(),
            MaxConnections = 8
        };

        config.HysteriaItem ??= new()
        {
            UpMbps = 100,
            DownMbps = 100
        };
        config.ClashUIItem ??= new();
        config.ClashUIItem.ConnectionsColumnItem ??= [];
        config.SystemProxyItem ??= new();
        config.WebDavItem ??= new();
        config.CheckUpdateItem ??= new();
        config.Fragment4RayItem ??= new()
        {
            Packets = "tlshello",
        };
        config.Fragment4RayItem.MaxSplit ??= "0";
        config.V2pnBypassItem ??= new();

        config.HappyEyeballs4RayItem ??= new()
        {
            TryDelayMs = 250,
            PrioritizeIPv6 = false,
            Interleave = 1,
            MaxConcurrentTry = 4,
        };
        if ((config.Fragment4RayItem.Lengths ?? []).Count == 0)
        {
            config.Fragment4RayItem.Lengths = [config.Fragment4RayItem.Length ?? "50-100"];
        }
        if ((config.Fragment4RayItem.Delays ?? []).Count == 0)
        {
            config.Fragment4RayItem.Delays = [config.Fragment4RayItem.Interval ?? "10-20"];
        }
        config.GlobalHotkeys ??= [];

        if (config.SystemProxyItem.SystemProxyExceptions.IsNullOrEmpty())
        {
            config.SystemProxyItem.SystemProxyExceptions = Utils.IsWindows() ? Global.SystemProxyExceptionsWindows : Global.SystemProxyExceptionsLinux;
        }

        return config;
    }

    /// <summary>
    /// Save the configuration to a file
    /// First writes to a temporary file, then replaces the original file
    /// </summary>
    /// <param name="_config">Configuration object to be saved</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> SaveConfig()
    {
        // V2pn: saves come from timers, the UI and background tasks at once. One shared temp file
        // made concurrent saves fail (guiNConfig.json_temp vanished under File.Move) and lose writes.
        await _saveConfigLock.WaitAsync();
        var resPath = Utils.GetConfigPath(_configRes);
        var tempPath = $"{resPath}_{Guid.NewGuid():N}.tmp";
        try
        {
            var content = JsonUtils.Serialize(Config, true, true);
            if (content.IsNullOrEmpty())
            {
                return -1;
            }
            // Flushed to disk before the rename, so a power cut cannot leave an empty config behind.
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(content);
            }

            File.Move(tempPath, resPath, true);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
            }
            return -1;
        }
        finally
        {
            _saveConfigLock.Release();
        }

        return 0;
    }

    private static readonly SemaphoreSlim _saveConfigLock = new(1, 1);

    #endregion Init

    #region UIItem

    public static WindowSizeItem? GetWindowSizeItem(string typeName)
    {
        var sizeItem = Config?.UiItem?.WindowSizeItem?.FirstOrDefault(t => t.TypeName == typeName);
        if (sizeItem == null || sizeItem.Width <= 0 || sizeItem.Height <= 0)
        {
            return null;
        }

        return sizeItem;
    }

    public static int SaveWindowSizeItem(string typeName, double width, double height)
    {
        var sizeItem = Config?.UiItem?.WindowSizeItem?.FirstOrDefault(t => t.TypeName == typeName);
        if (sizeItem == null)
        {
            sizeItem = new WindowSizeItem { TypeName = typeName };
            Config.UiItem.WindowSizeItem.Add(sizeItem);
        }

        sizeItem.Width = (int)width;
        sizeItem.Height = (int)height;

        return 0;
    }

    /// <summary>
    /// V2pn: the main window's normal bounds and its maximized flag, so it reopens where it was left.
    /// A maximized window saves the size it restores to, never the screen-sized one.
    /// </summary>
    public static int SaveWindowPlacement(string typeName, double width, double height, int? x, int? y, bool maximized)
    {
        SaveWindowSizeItem(typeName, width, height);
        var sizeItem = Config.UiItem.WindowSizeItem.First(t => t.TypeName == typeName);
        sizeItem.X = x;
        sizeItem.Y = y;
        sizeItem.IsMaximized = maximized;
        return 0;
    }

    public static int SaveMainGirdHeight(double height1, double height2)
    {
        var uiItem = Config.UiItem ?? new();

        uiItem.MainGirdHeight1 = (int)(height1 + 0.1);
        uiItem.MainGirdHeight2 = (int)(height2 + 0.1);

        return 0;
    }

    #endregion UIItem

    #region DefaultServer

    public static async Task<int> ResetDefaultServer()
    {
        return await SetDefaultServerIndex(Config.IndexId);
    }

    /// <summary>
    /// Set the default server by its index ID
    /// Updates the configuration to use the specified server as default
    /// </summary>
    /// <param name="indexId">Index ID of the server to set as default</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> SetDefaultServerIndex(string? indexId)
    {
        if (indexId.IsNullOrEmpty())
        {
            return -1;
        }

        Config.PreIndexId = Config.IndexId;
        Config.IndexId = indexId;

        await SaveConfig();

        return 0;
    }

    /// <summary>
    /// Set a default server from the provided list of profiles
    /// Ensures there's always a valid default server selected
    /// </summary>
    /// <param name="lstProfile">List of profile models to choose from</param>
    /// <returns>Result of SetDefaultServerIndex operation</returns>
    public static async Task<int> SetDefaultServer(List<ProfileItemModel> lstProfile)
    {
        if (lstProfile.Exists(t => t.IndexId == Config.IndexId))
        {
            return 0;
        }

        if (await SQLiteHelper.Instance.TableAsync<ProfileItem>().FirstOrDefaultAsync(t => t.IndexId == Config.IndexId) != null)
        {
            return 0;
        }
        if (lstProfile.Count > 0)
        {
            return await SetDefaultServerIndex(lstProfile.FirstOrDefault(t => t.Port > 0)?.IndexId);
        }

        var item = await SQLiteHelper.Instance.TableAsync<ProfileItem>().FirstOrDefaultAsync(t => t.Port > 0);
        return await SetDefaultServerIndex(item?.IndexId);
    }

    /// <summary>
    /// Get the current default server profile
    /// If the current default is invalid, selects a new default
    /// </summary>
    /// <returns>The default profile item or null if none exists</returns>
    public static async Task<ProfileItem?> GetDefaultServer()
    {
        var item = await ConfigDbQuery.GetProfileItem(Config.IndexId);
        if (item is null)
        {
            var item2 = await SQLiteHelper.Instance.TableAsync<ProfileItem>().FirstOrDefaultAsync();
            await SetDefaultServerIndex(item2?.IndexId);
            return item2;
        }

        return item;
    }

    #endregion DefaultServer
}
