using CommunityToolkit.Mvvm.Messaging;
using EspansoGo.Models;

namespace EspansoGo;

public partial class App : Application
{
    private Window window = null;
    public App()
    {
        InitializeComponent();

        MainPage = new MainPage();
    }
    protected override Window CreateWindow(IActivationState activationState)
    {
        if (window is null)
        {
            window = base.CreateWindow(activationState);
        }
        else
        {
            MainPage = new MainPage();
        }
        return window;
    }
    protected override void OnResume()
    {
        base.OnResume();
        WeakReferenceMessenger.Default.Send(new AppResumedMessage());
    }
}
