using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WoCloud.App.Services;
using WoCloud.App.ViewModels;

namespace WoCloud.App.Views;

public sealed partial class TransferPage : Page, INotifyPropertyChanged
{
    public TransferViewModel Vm { get; } = App.Current.Services.GetRequiredService<TransferViewModel>();

    public event PropertyChangedEventHandler? PropertyChanged;

    public TransferPage()
    {
        InitializeComponent();
        Vm.PropertyChanged += (_, _) => RaiseAll();
        Vm.Items.CollectionChanged += (_, _) => RaiseAll();
        RaiseAll();
    }

    /// <summary>空列表时才显示提示文案。</summary>
    public Visibility EmptyVisible => Vm.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void RaiseAll()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private void ClearFinished_Click(object sender, RoutedEventArgs e) => Vm.ClearFinished();

    private void ClearAll_Click(object sender, RoutedEventArgs e) => Vm.ClearAll();
}
