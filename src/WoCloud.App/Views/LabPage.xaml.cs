using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WoCloud.App.Services;
using WoCloud.App.ViewModels;

namespace WoCloud.App.Views;

public sealed partial class LabPage : Page
{
    public LabViewModel Vm { get; } = App.Current.Services.GetRequiredService<LabViewModel>();

    public LabPage() => InitializeComponent();

    private async void Invoke_Click(object sender, RoutedEventArgs e) => await Vm.InvokeAsync();
}
