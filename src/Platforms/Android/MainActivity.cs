using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using System.Diagnostics;
using Microsoft.Maui;

namespace Expandroid;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
        {
            Window?.SetFlags(WindowManagerFlags.LayoutNoLimits, WindowManagerFlags.LayoutNoLimits);
            Window?.DecorView?.SystemUiVisibility = (StatusBarVisibility)(SystemUiFlags.LayoutStable | SystemUiFlags.LayoutFullscreen);
            Window?.SetStatusBarColor(Android.Graphics.Color.Transparent);
        }
    }
}
