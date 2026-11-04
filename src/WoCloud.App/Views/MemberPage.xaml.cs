using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WoCloud.App.Services;
using WoCloud.App.ViewModels;

namespace WoCloud.App.Views;

public sealed partial class MemberPage : Page
{
    public MemberViewModel Vm { get; } = App.Current.Services.GetRequiredService<MemberViewModel>();

    public MemberPage()
    {
        InitializeComponent();
        _ = Vm.LoadAsync();
    }
}
