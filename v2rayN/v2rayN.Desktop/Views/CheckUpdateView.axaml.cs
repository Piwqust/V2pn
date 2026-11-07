using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Views;

public partial class CheckUpdateView : ReactiveUserControl<CheckUpdateViewModel>
{
    /// <summary>What each updatable part is called for a person, instead of its core type name.</summary>
    public static readonly FuncValueConverter<string?, string> PartName = new(name => name switch
    {
        nameof(ECoreType.v2rayN) => L.TlUpdApp,
        nameof(ECoreType.Xray) => L.TlUpdXray,
        nameof(ECoreType.mihomo) => L.TlUpdMihomo,
        nameof(ECoreType.sing_box) => L.TlUpdSingBox,
        "GeoFiles" => L.TlUpdGeo,
        _ => name ?? string.Empty,
    });

    public CheckUpdateView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.CheckUpdateModels, v => v.lstCheckUpdates.ItemsSource).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.EnableUpdateViaProxy, v => v.togEnableUpdateViaProxy.IsChecked).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CheckOnlyCmd, v => v.btnCheckOnly).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CheckUpdateCmd, v => v.btnCheckUpdate).DisposeWith(disposables);
        });
    }
}
