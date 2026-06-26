using Android.App;
using Android.Content.PM;
using System.Diagnostics;
using Microsoft.Maui;

namespace EspansoGo;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        // SZ-4: Forward Shizuku permission result to Shizuku.onRequestPermissionResult
        if (requestCode == 10001)
        {
            try
            {
                var shizukuClass = Java.Lang.Class.ForName("rikka.shizuku.Shizuku");
                var method = shizukuClass.GetMethod("onRequestPermissionResult",
                    Java.Lang.Integer.Type, Java.Lang.Integer.Type);
                method.Invoke(null, Java.Lang.Integer.ValueOf(requestCode), Java.Lang.Integer.ValueOf((int)resultCode));
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Shizuku onRequestPermissionResult failed: {e.Message}");
            }
        }
    }
}
