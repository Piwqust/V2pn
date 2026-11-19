using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace ServiceLib;

/// <summary>
/// V2pn: the kill switch's block in VPN mode, made of Windows Filtering Platform filters. Everything that leaves or
/// enters the computer is blocked except what the VPN itself needs: V2pn and its cores, traffic through the tunnel,
/// the computer talking to itself, DHCP, and (when allowed) the local network. DNS to anything but the tunnel is
/// blocked even inside the local network, so names are not asked of the router while the tunnel is down.
///
/// The filters live in a dynamic WFP session: Windows removes them when V2pn closes the session or exits for any
/// reason. A crashed V2pn therefore never leaves the computer without internet; the kill switch guards the
/// connection while V2pn runs (the core dying, restarting, or the server going away). It needs administrator
/// rights, which VPN mode has anyway.
/// </summary>
public static class V2pnFirewall
{
    /// <summary>What the block lets through.</summary>
    public sealed record Plan(
        IReadOnlyList<string> Programs,
        IReadOnlyList<(IPAddress Address, int Prefix)> TunNetworks,
        IReadOnlyList<ulong> TunInterfaces,
        bool AllowLan);

    public enum Layer { ConnectV4, ConnectV6, AcceptV4, AcceptV6 }

    public enum Field { AppId, AppNameSuffix, LoopbackFlag, LocalAddress, RemoteAddress, RemotePort, LocalPort, LocalInterface }

    public sealed record Condition(Field Field, object Value);

    /// <summary>One filter: its layer, its weight inside V2pn's sublayer (higher wins), permit or block.</summary>
    /// <param name="Key">A fixed key, for persistent filters that are later deleted by it.</param>
    public sealed record FilterSpec(string Name, Layer Layer, byte Weight, bool Permit, IReadOnlyList<Condition> Conditions, Guid? Key = null);

    // Weights inside V2pn's sublayer: the first matching filter by weight decides.
    public const byte WeightPrograms = 15;
    public const byte WeightLoopback = 14;
    public const byte WeightTunnel = 13;
    public const byte WeightBlockDns = 12;
    public const byte WeightLan = 11;
    public const byte WeightDhcp = 10;
    public const byte WeightBlockAll = 0;

    private static readonly (string Network, int Prefix)[] LanV4 =
    [
        ("10.0.0.0", 8), ("172.16.0.0", 12), ("192.168.0.0", 16), ("169.254.0.0", 16), ("224.0.0.0", 4), ("255.255.255.255", 32),
    ];

    private static readonly (string Network, int Prefix)[] LanV6 =
    [
        ("fe80::", 10), ("fc00::", 7), ("ff00::", 8),
    ];

    /// <summary>The filters for a plan. Kept apart from WFP itself so that what is let through can be tested.</summary>
    public static List<FilterSpec> Filters(Plan plan)
    {
        var filters = new List<FilterSpec>();
        foreach (var layer in Enum.GetValues<Layer>())
        {
            var v6 = layer is Layer.ConnectV6 or Layer.AcceptV6;
            var connect = layer is Layer.ConnectV4 or Layer.ConnectV6;

            foreach (var program in plan.Programs)
            {
                filters.Add(new($"V2pn kill switch: {Path.GetFileName(program)}", layer, WeightPrograms, true, [new(Field.AppId, program)]));
            }
            filters.Add(new("V2pn kill switch: loopback", layer, WeightLoopback, true, [new(Field.LoopbackFlag, 1u)]));

            var tunnel = plan.TunNetworks
                .Where(n => n.Address.AddressFamily == (v6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork))
                .Select(n => new Condition(Field.LocalAddress, n))
                .ToList();
            if (tunnel.Count > 0)
            {
                filters.Add(new("V2pn kill switch: tunnel address", layer, WeightTunnel, true, tunnel));
            }
            foreach (var luid in plan.TunInterfaces)
            {
                filters.Add(new("V2pn kill switch: tunnel adapter", layer, WeightTunnel, true, [new(Field.LocalInterface, luid)]));
            }

            if (connect)
            {
                filters.Add(new("V2pn kill switch: DNS outside the tunnel", layer, WeightBlockDns, false, [new(Field.RemotePort, (ushort)53)]));
            }
            if (plan.AllowLan)
            {
                var lan = (v6 ? LanV6 : LanV4)
                    .Select(n => new Condition(Field.RemoteAddress, (IPAddress.Parse(n.Network), n.Prefix)))
                    .ToList();
                filters.Add(new("V2pn kill switch: local network", layer, WeightLan, true, lan));
            }
            // The computer keeps its address while the tunnel is down.
            var (server, client) = v6 ? ((ushort)547, (ushort)546) : ((ushort)67, (ushort)68);
            filters.Add(new("V2pn kill switch: DHCP", layer, WeightDhcp, true,
                connect ? [new(Field.RemotePort, server), new(Field.LocalPort, client)] : [new(Field.LocalPort, client)]));

            filters.Add(new("V2pn kill switch: block", layer, WeightBlockAll, false, []));
        }
        return filters;
    }

    /// <summary>"172.18.0.1/30" as a network: the address with the host bits cleared, and the prefix.</summary>
    public static (IPAddress Address, int Prefix)? ParseNetwork(string? cidr)
    {
        if (cidr.IsNullOrEmpty())
        {
            return null;
        }
        var parts = cidr.Split('/');
        if (!IPAddress.TryParse(parts[0], out var address))
        {
            return null;
        }
        var max = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefix = parts.Length > 1 && int.TryParse(parts[1], out var p) ? Math.Clamp(p, 0, max) : max;
        var bytes = address.GetAddressBytes();
        for (var bit = prefix; bit < max; bit++)
        {
            bytes[bit / 8] &= unchecked((byte)~(0x80 >> (bit % 8)));
        }
        return (new IPAddress(bytes), prefix);
    }

    // ================================================================== the WFP session

    private static readonly Lock Gate = new();
    private static IntPtr _engine;

    public static bool IsActive
    {
        get
        {
            lock (Gate)
            {
                return _engine != IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Puts the plan's filters in place. The new session is filled before the old one closes, so there is no
    /// moment without a block. Returns null, or what went wrong (the old block then stays).
    /// </summary>
    public static string? Apply(Plan plan)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "The kill switch's block is made with Windows Filtering Platform.";
        }
        lock (Gate)
        {
            var engine = IntPtr.Zero;
            try
            {
                engine = Native.Open(dynamic: true);
                Native.Install(engine, Filters(plan), Native.KillSwitchSubLayer, "V2pn kill switch", persistent: false);
            }
            catch (Exception ex)
            {
                if (engine != IntPtr.Zero)
                {
                    Native.Close(engine);
                }
                Logging.SaveLog("V2pn kill switch: the block could not be put in place", ex);
                return ex.Message;
            }
            var old = _engine;
            _engine = engine;
            if (old != IntPtr.Zero)
            {
                Native.Close(old);
            }
            Logging.SaveLog($"V2pn kill switch: block in place ({plan.Programs.Count} programs, {plan.TunNetworks.Count} tunnel networks, {plan.TunInterfaces.Count} tunnel adapters, LAN {(plan.AllowLan ? "open" : "closed")})");
            return null;
        }
    }

    /// <summary>Lifts the block; closing the dynamic session removes every filter in it.</summary>
    public static void Remove()
    {
        lock (Gate)
        {
            if (_engine == IntPtr.Zero)
            {
                return;
            }
            Native.Close(_engine);
            _engine = IntPtr.Zero;
            Logging.SaveLog("V2pn kill switch: block lifted");
        }
    }

    /// <summary>The LUIDs of the tunnel adapters the cores create (singbox_tun, xray_tun), when they are up.</summary>
    public static List<ulong> TunnelAdapters()
    {
        var luids = new List<ulong>();
        if (!OperatingSystem.IsWindows())
        {
            return luids;
        }
        try
        {
            foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.Name is not ("singbox_tun" or "xray_tun"))
                {
                    continue;
                }
                var index = adapter.GetIPProperties().GetIPv4Properties()?.Index
                    ?? adapter.GetIPProperties().GetIPv6Properties()?.Index;
                if (index is int i && Native.ConvertInterfaceIndexToLuid((uint)i, out var luid) == 0)
                {
                    luids.Add(luid);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("V2pn kill switch: tunnel adapters", ex);
        }
        return luids;
    }

    // ================================================================== Windows Filtering Platform

    /// <summary>
    /// fwpuclnt.dll. The structures are laid out by hand with the offsets of the 64-bit headers; V2pn ships for
    /// x64 and ARM64 only. V2pnFirewallTests checks their sizes against the C ones.
    /// </summary>
    internal static class Native
    {
        internal static readonly Guid KillSwitchSubLayer = new("6a1f2d3e-8b4c-4f5a-9e7d-0c1b2a3f4e5d");
        internal static readonly Guid AppsSubLayer = new("6a1f2d3e-8b4c-4f5a-9e7d-0c1b2a3f4e5e");

        internal static readonly Guid LayerConnectV4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
        internal static readonly Guid LayerConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
        internal static readonly Guid LayerAcceptV4 = new("e1cd9fe7-f4b5-4273-96c0-592e487b8650");
        internal static readonly Guid LayerAcceptV6 = new("a3b42c97-9f04-4672-b87e-cee9c483257f");

        private static readonly Guid ConditionAppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");
        private static readonly Guid ConditionFlags = new("632ce23b-5167-435c-86d7-e903684aa80c");
        private static readonly Guid ConditionLocalAddress = new("d9ee00de-c1ef-4617-bfe3-ffd8f5a08957");
        private static readonly Guid ConditionRemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
        private static readonly Guid ConditionRemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");
        private static readonly Guid ConditionLocalPort = new("0c1ba1af-5765-453f-af22-a8f791ac775b");
        private static readonly Guid ConditionLocalInterface = new("4cd62a49-59c3-4969-b7f3-bda5d32890a4");

        private const uint SessionFlagDynamic = 0x1;
        private const uint RpcCAuthnWinNt = 10;
        private const uint ActionBlock = 0x1001;  // FWP_ACTION_BLOCK | FWP_ACTION_FLAG_TERMINATING
        private const uint ActionPermit = 0x1002; // FWP_ACTION_PERMIT | FWP_ACTION_FLAG_TERMINATING

        // FWP_DATA_TYPE
        private const uint TypeUint8 = 1;
        private const uint TypeUint16 = 2;
        private const uint TypeUint32 = 3;
        private const uint TypeUint64 = 4;
        private const uint TypeByteBlob = 12;
        private const uint TypeV4AddrMask = 0x100;
        private const uint TypeV6AddrMask = 0x101;

        // FWP_MATCH_TYPE
        private const uint MatchEqual = 0;
        private const uint MatchFlagsAllSet = 6;
        // Named "prefix", but it tests how the value ends (Microsoft documents the misnomer): "\claude.exe" matches
        // that program in any folder, whatever version its folder is named after.
        private const uint MatchSuffix = 11;

        private const uint FlagPersistent = 0x1;
        private const uint FwpEFilterNotFound = 0x80320003;
        private const uint FwpESubLayerNotFound = 0x80320007;

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        internal struct DisplayData
        {
            [FieldOffset(0)] public IntPtr Name;
            [FieldOffset(8)] public IntPtr Description;
        }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        internal struct ByteBlob
        {
            [FieldOffset(0)] public uint Size;
            [FieldOffset(8)] public IntPtr Data;
        }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        internal struct Value
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(8)] public ulong Data;
        }

        [StructLayout(LayoutKind.Explicit, Size = 72)]
        internal struct Session
        {
            [FieldOffset(0)] public Guid SessionKey;
            [FieldOffset(16)] public DisplayData DisplayData;
            [FieldOffset(32)] public uint Flags;
            [FieldOffset(36)] public uint TxnWaitTimeoutInMSec;
            [FieldOffset(40)] public uint ProcessId;
            [FieldOffset(48)] public IntPtr Sid;
            [FieldOffset(56)] public IntPtr Username;
            [FieldOffset(64)] public int KernelMode;
        }

        [StructLayout(LayoutKind.Explicit, Size = 72)]
        internal struct SubLayer
        {
            [FieldOffset(0)] public Guid SubLayerKey;
            [FieldOffset(16)] public DisplayData DisplayData;
            [FieldOffset(32)] public uint Flags;
            [FieldOffset(40)] public IntPtr ProviderKey;
            [FieldOffset(48)] public ByteBlob ProviderData;
            [FieldOffset(64)] public ushort Weight;
        }

        [StructLayout(LayoutKind.Explicit, Size = 40)]
        internal struct FilterCondition
        {
            [FieldOffset(0)] public Guid FieldKey;
            [FieldOffset(16)] public uint MatchType;
            [FieldOffset(24)] public Value ConditionValue;
        }

        [StructLayout(LayoutKind.Explicit, Size = 200)]
        internal struct Filter
        {
            [FieldOffset(0)] public Guid FilterKey;
            [FieldOffset(16)] public DisplayData DisplayData;
            [FieldOffset(32)] public uint Flags;
            [FieldOffset(40)] public IntPtr ProviderKey;
            [FieldOffset(48)] public ByteBlob ProviderData;
            [FieldOffset(64)] public Guid LayerKey;
            [FieldOffset(80)] public Guid SubLayerKey;
            [FieldOffset(96)] public Value Weight;
            [FieldOffset(112)] public uint NumFilterConditions;
            [FieldOffset(120)] public IntPtr FilterCondition;
            [FieldOffset(128)] public uint ActionType;
            [FieldOffset(132)] public Guid ActionFilterType;
            [FieldOffset(152)] public Guid ProviderContextKey;
            [FieldOffset(168)] public IntPtr Reserved;
            [FieldOffset(176)] public ulong FilterId;
            [FieldOffset(184)] public Value EffectiveWeight;
        }

        [StructLayout(LayoutKind.Explicit, Size = 8)]
        internal struct V4AddrMask
        {
            [FieldOffset(0)] public uint Address;
            [FieldOffset(4)] public uint Mask;
        }

        [StructLayout(LayoutKind.Sequential, Size = 17)]
        internal struct V6AddrMask
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] Address;
            public byte PrefixLength;
        }

        /// <param name="dynamic">Dynamic: everything added goes when the session closes. Persistent objects need a non-dynamic one.</param>
        public static IntPtr Open(bool dynamic)
        {
            var name = Marshal.StringToHGlobalUni("V2pn kill switch");
            try
            {
                var session = new Session
                {
                    DisplayData = new DisplayData { Name = name },
                    Flags = dynamic ? SessionFlagDynamic : 0,
                    TxnWaitTimeoutInMSec = 5000,
                };
                Check(FwpmEngineOpen0(null, RpcCAuthnWinNt, IntPtr.Zero, ref session, out var engine), "FwpmEngineOpen0");
                return engine;
            }
            finally
            {
                Marshal.FreeHGlobal(name);
            }
        }

        public static void Close(IntPtr engine) => FwpmEngineClose0(engine);

        /// <summary>Deletes filters by their keys and, when asked, the sublayer; missing ones are not an error.</summary>
        public static void Delete(IntPtr engine, IEnumerable<Guid> filterKeys, Guid? subLayer)
        {
            Check(FwpmTransactionBegin0(engine, 0), "FwpmTransactionBegin0");
            var committed = false;
            try
            {
                foreach (var key in filterKeys)
                {
                    var k = key;
                    var r = FwpmFilterDeleteByKey0(engine, ref k);
                    if (r != 0 && r != FwpEFilterNotFound)
                    {
                        Check(r, "FwpmFilterDeleteByKey0");
                    }
                }
                if (subLayer is { } sub)
                {
                    var r = FwpmSubLayerDeleteByKey0(engine, ref sub);
                    if (r != 0 && r != FwpESubLayerNotFound)
                    {
                        Check(r, "FwpmSubLayerDeleteByKey0");
                    }
                }
                Check(FwpmTransactionCommit0(engine), "FwpmTransactionCommit0");
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    FwpmTransactionAbort0(engine);
                }
            }
        }

        public static void Install(IntPtr engine, IReadOnlyList<FilterSpec> filters, Guid subLayerKey, string subLayerName, bool persistent)
        {
            var memory = new List<IntPtr>();
            IntPtr Keep(IntPtr p)
            {
                memory.Add(p);
                return p;
            }
            IntPtr Text(string s) => Keep(Marshal.StringToHGlobalUni(s));

            Check(FwpmTransactionBegin0(engine, 0), "FwpmTransactionBegin0");
            var committed = false;
            try
            {
                var subLayer = new SubLayer
                {
                    SubLayerKey = subLayerKey,
                    DisplayData = new DisplayData { Name = Text(subLayerName) },
                    Flags = persistent ? FlagPersistent : 0,
                    Weight = 0xFFFF,
                };
                var added = FwpmSubLayerAdd0(engine, ref subLayer, IntPtr.Zero);
                // A sublayer outlives nothing in a dynamic session, but another session of V2pn may still hold it.
                if (added != 0 && added != FwpEAlreadyExists)
                {
                    Check(added, "FwpmSubLayerAdd0");
                }

                var appIds = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
                foreach (var spec in filters)
                {
                    var conditions = spec.Conditions.Select(c => Build(c, Keep, appIds)).ToArray();
                    var array = IntPtr.Zero;
                    if (conditions.Length > 0)
                    {
                        var size = Marshal.SizeOf<FilterCondition>();
                        array = Keep(Marshal.AllocHGlobal(size * conditions.Length));
                        for (var i = 0; i < conditions.Length; i++)
                        {
                            Marshal.StructureToPtr(conditions[i], array + i * size, false);
                        }
                    }
                    var filter = new Filter
                    {
                        FilterKey = spec.Key ?? Guid.Empty,
                        DisplayData = new DisplayData { Name = Text(spec.Name) },
                        Flags = persistent ? FlagPersistent : 0,
                        LayerKey = LayerKey(spec.Layer),
                        SubLayerKey = subLayerKey,
                        Weight = new Value { Type = TypeUint8, Data = spec.Weight },
                        NumFilterConditions = (uint)conditions.Length,
                        FilterCondition = array,
                        ActionType = spec.Permit ? ActionPermit : ActionBlock,
                    };
                    Check(FwpmFilterAdd0(engine, ref filter, IntPtr.Zero, out _), $"FwpmFilterAdd0 ({spec.Name}, {spec.Layer})");
                }
                Check(FwpmTransactionCommit0(engine), "FwpmTransactionCommit0");
                committed = true;
                foreach (var appId in appIds.Values)
                {
                    var p = appId;
                    FwpmFreeMemory0(ref p);
                }
            }
            finally
            {
                if (!committed)
                {
                    FwpmTransactionAbort0(engine);
                }
                foreach (var p in memory)
                {
                    Marshal.FreeHGlobal(p);
                }
            }
        }

        /// <summary>What a program's app id ends with: a backslash, its lower-case file name in UTF-16, then the terminating zero.</summary>
        internal static byte[] SuffixBytes(string fileName) =>
            System.Text.Encoding.Unicode.GetBytes(@"\" + fileName.ToLowerInvariant() + (char)0);

        private static Guid LayerKey(Layer layer) => layer switch
        {
            Layer.ConnectV4 => LayerConnectV4,
            Layer.ConnectV6 => LayerConnectV6,
            Layer.AcceptV4 => LayerAcceptV4,
            _ => LayerAcceptV6,
        };

        private static FilterCondition Build(Condition condition, Func<IntPtr, IntPtr> keep, Dictionary<string, IntPtr> appIds)
        {
            switch (condition.Field)
            {
                case Field.AppId:
                    var path = (string)condition.Value;
                    if (!appIds.TryGetValue(path, out var blob))
                    {
                        Check(FwpmGetAppIdFromFileName0(path, out blob), $"FwpmGetAppIdFromFileName0 ({path})");
                        appIds[path] = blob;
                    }
                    return new FilterCondition { FieldKey = ConditionAppId, MatchType = MatchEqual, ConditionValue = new Value { Type = TypeByteBlob, Data = (ulong)blob.ToInt64() } };
                case Field.AppNameSuffix:
                    // The app id is the program's lower-case NT path in UTF-16 with its terminating zero.
                    var suffix = SuffixBytes((string)condition.Value);
                    var data = keep(Marshal.AllocHGlobal(suffix.Length));
                    Marshal.Copy(suffix, 0, data, suffix.Length);
                    var suffixBlob = keep(Marshal.AllocHGlobal(Marshal.SizeOf<ByteBlob>()));
                    Marshal.StructureToPtr(new ByteBlob { Size = (uint)suffix.Length, Data = data }, suffixBlob, false);
                    return new FilterCondition { FieldKey = ConditionAppId, MatchType = MatchSuffix, ConditionValue = new Value { Type = TypeByteBlob, Data = (ulong)suffixBlob.ToInt64() } };
                case Field.LoopbackFlag:
                    return new FilterCondition { FieldKey = ConditionFlags, MatchType = MatchFlagsAllSet, ConditionValue = new Value { Type = TypeUint32, Data = (uint)condition.Value } };
                case Field.RemotePort:
                case Field.LocalPort:
                    return new FilterCondition
                    {
                        FieldKey = condition.Field == Field.RemotePort ? ConditionRemotePort : ConditionLocalPort,
                        MatchType = MatchEqual,
                        ConditionValue = new Value { Type = TypeUint16, Data = (ushort)condition.Value },
                    };
                case Field.LocalInterface:
                    var luid = keep(Marshal.AllocHGlobal(8));
                    Marshal.WriteInt64(luid, (long)(ulong)condition.Value);
                    return new FilterCondition { FieldKey = ConditionLocalInterface, MatchType = MatchEqual, ConditionValue = new Value { Type = TypeUint64, Data = (ulong)luid.ToInt64() } };
                default:
                    var (address, prefix) = ((IPAddress, int))condition.Value;
                    var key = condition.Field == Field.LocalAddress ? ConditionLocalAddress : ConditionRemoteAddress;
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var bytes = address.GetAddressBytes();
                        var mask = new V4AddrMask
                        {
                            // Host byte order, as WFP takes IPv4 addresses.
                            Address = unchecked((uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3])),
                            Mask = prefix == 0 ? 0 : unchecked(uint.MaxValue << (32 - prefix)),
                        };
                        var p4 = keep(Marshal.AllocHGlobal(Marshal.SizeOf<V4AddrMask>()));
                        Marshal.StructureToPtr(mask, p4, false);
                        return new FilterCondition { FieldKey = key, MatchType = MatchEqual, ConditionValue = new Value { Type = TypeV4AddrMask, Data = (ulong)p4.ToInt64() } };
                    }
                    var v6 = new V6AddrMask { Address = address.GetAddressBytes(), PrefixLength = (byte)prefix };
                    var p6 = keep(Marshal.AllocHGlobal(Marshal.SizeOf<V6AddrMask>()));
                    Marshal.StructureToPtr(v6, p6, false);
                    return new FilterCondition { FieldKey = key, MatchType = MatchEqual, ConditionValue = new Value { Type = TypeV6AddrMask, Data = (ulong)p6.ToInt64() } };
            }
        }

        private const uint FwpEAlreadyExists = 0x80320009;

        private static void Check(uint result, string call)
        {
            if (result != 0)
            {
                throw new InvalidOperationException($"{call} failed: 0x{result:X8}");
            }
        }

        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
        private static extern uint FwpmEngineOpen0(string? serverName, uint authnService, IntPtr authIdentity, ref Session session, out IntPtr engineHandle);

        [DllImport("fwpuclnt.dll")]
        private static extern uint FwpmEngineClose0(IntPtr engineHandle);

        [DllImport("fwpuclnt.dll")]
        private static extern uint FwpmTransactionBegin0(IntPtr engineHandle, uint flags);

        [DllImport("fwpuclnt.dll")]
        private static extern uint FwpmTransactionCommit0(IntPtr engineHandle);

        [DllImport("fwpuclnt.dll")]
        private static extern uint FwpmTransactionAbort0(IntPtr engineHandle);

        [DllImport("fwpuclnt.dll")]
        private static extern uint FwpmSubLayerAdd0(IntPtr engineHandle, ref SubLayer subLayer, IntPtr sd);

        [DllImport("fwpuclnt.dll")]
        private static extern uint FwpmFilterAdd0(IntPtr engineHandle, ref Filter filter, IntPtr sd, out ulong id);

        [DllImport("fwpuclnt.dll")]
        private static extern uint FwpmFilterDeleteByKey0(IntPtr engineHandle, ref Guid key);

        [DllImport("fwpuclnt.dll")]
        private static extern uint FwpmSubLayerDeleteByKey0(IntPtr engineHandle, ref Guid key);

        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
        internal static extern uint FwpmGetAppIdFromFileName0(string fileName, out IntPtr appId);

        [DllImport("fwpuclnt.dll")]
        internal static extern void FwpmFreeMemory0(ref IntPtr p);

        [DllImport("iphlpapi.dll")]
        internal static extern uint ConvertInterfaceIndexToLuid(uint interfaceIndex, out ulong interfaceLuid);
    }
}
