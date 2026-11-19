using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// The map's pin: where the active server stands and, while V2pn is off, where the person is. Cities come from
/// the server's name or from the last "My IP" lookup the person ran; the map never asks the network itself.
/// </summary>
public partial class MainWindow
{
    private string? _mapRemarks;
    private int _mapDelay;
    private ProtectionState _mapState;
    private bool _mapConnected;
    private bool _mapLookupHooked;
    // Set by the first verdict: until then a ping or a lookup must not place the pin in a guessed state.
    private bool _mapPainted;
    // The exit address looked up while this server was active, and the person's own address from any lookup.
    private V2pnIpLookup.Details? _mapExit;
    private string? _mapExitIndexId;
    private V2pnIpLookup.Details? _mapDirect;

    private void FocusMap(ProtectionState state, bool connected)
    {
        _mapState = state;
        _mapConnected = connected;
        _mapPainted = true;
        if (!_mapLookupHooked)
        {
            _mapLookupHooked = true;
            V2pnIpLookup.Completed += report => Dispatcher.UIThread.Post(() => TakeLookup(report));
        }
        mapView.Focus(_mapCode, state, connected, ServerPlace(), HomePlace(), _mapDelay);
    }

    private void TakeLookup(V2pnIpLookup.Report report)
    {
        if (report.Exit != null)
        {
            _mapExit = report.Exit;
            _mapExitIndexId = _activeIndexId;
        }
        if (report.Direct != null)
        {
            _mapDirect = report.Direct;
        }
        if (_mapPainted)
        {
            FocusMap(_mapState, _mapConnected);
        }
    }

    /// <summary>The status card's server line and the ping beside the map pin, from one call.</summary>
    private void ShowServerDelay(ProfileItem? item, int delay)
    {
        _statusView?.SetServer(item, delay);
        if (_mapDelay != delay)
        {
            _mapDelay = delay;
            if (_mapPainted)
            {
                FocusMap(_mapState, _mapConnected);
            }
        }
    }

    private MapPlace? ServerPlace()
    {
        // The exit address seen through this very server says where it really is, better than its name.
        if (_mapConnected
            && _mapExit is { Latitude: { } lat, Longitude: { } lon }
            && _mapExitIndexId == _activeIndexId
            && string.Equals(_mapExit.CountryCode, _mapCode, StringComparison.OrdinalIgnoreCase))
        {
            return new MapPlace(lat, lon, _mapExit.City, _mapCode);
        }
        return MapCities.Find(_mapRemarks, _mapCode);
    }

    // Only while V2pn is really off: a failed check while connected does not mean sites see the person's own address.
    private MapPlace? HomePlace() =>
        !_mapConnected && _mapDirect is { Latitude: { } lat, Longitude: { } lon }
            ? new MapPlace(lat, lon, _mapDirect.City ?? _mapDirect.Country, _mapDirect.CountryCode?.ToUpperInvariant())
            : null;
}
