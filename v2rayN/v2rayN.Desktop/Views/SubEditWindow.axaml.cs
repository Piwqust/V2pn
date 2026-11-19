using v2rayN.Desktop.Base;

namespace v2rayN.Desktop.Views;

/// <summary>A subscription on its own, opened from the server list; the form itself is <see cref="SubEditView"/>.</summary>
public partial class SubEditWindow : WindowBase<SubEditViewModel>
{
    public SubEditWindow()
    {
        InitializeComponent();

        btnCancel.Click += (s, e) => Close();
        btnSave.Click += (s, e) =>
        {
            form.PrepareSave();
            ViewModel?.SaveCmd.Execute().Subscribe();
        };
        Closed += (s, e) => form.AnnounceIfAdded();
        Loaded += (_, _) =>
        {
            if (form.IsNew)
            {
                Title = L.NewSubscription;
                btnSave.Content = L.AddServer;
            }
        };
        this.WhenAnyValue(v => v.ViewModel).Subscribe(vm => form.ViewModel = vm);
    }
}
