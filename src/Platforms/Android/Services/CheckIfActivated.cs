using Android;
using Android.AccessibilityServices;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Android.Views.Accessibility;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using EspansoGo.Models;
using Microsoft.Maui.ApplicationModel;

namespace EspansoGo.Services
{
    internal class CheckIfActivated : ICheckIfActivated
    {
        public bool IsActivated()
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity.BaseContext;
            AccessibilityManager am = (AccessibilityManager)context.GetSystemService(Context.AccessibilityService);
            IList<AccessibilityServiceInfo> enabledServices = am.GetEnabledAccessibilityServiceList(FeedbackFlags.Generic);

            foreach (AccessibilityServiceInfo enabledService in enabledServices)
            {
                ServiceInfo enabledServiceInfo = enabledService.ResolveInfo.ServiceInfo;
                if (enabledServiceInfo.PackageName.Equals(context.PackageName))
                    return true;
            }

            return false;
        }
        public void OpenSettings()
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity.BaseContext;
            var componentName = new ComponentName(context.PackageName, context.PackageName + ".ExpanderAccessibilityservice");
            var flattened = componentName.FlattenToString();

            // Try ACCESSIBILITY_DETAIL_SETTINGS (Android 12+) with component name to open service detail page directly
            if (!TryStartActivity(context, "android.settings.ACCESSIBILITY_DETAIL_SETTINGS", flattened))
            {
                // Fallback: open accessibility settings list with component name to highlight our service
                if (!TryStartActivity(context, Settings.ActionAccessibilitySettings, flattened))
                {
                    // Last resort: open accessibility settings list without component name
                    try
                    {
                        var intent = new Intent(Settings.ActionAccessibilitySettings);
                        intent.SetFlags(ActivityFlags.NewTask);
                        context.StartActivity(intent);
                    }
                    catch (Exception e)
                    {
                        System.Diagnostics.Debug.WriteLine($"OpenSettings all attempts failed: {e.Message}");
                    }
                }
            }
        }

        private static bool TryStartActivity(Android.Content.Context context, string action, string componentNameExtra)
        {
            try
            {
                var intent = new Intent(action);
                if (!string.IsNullOrEmpty(componentNameExtra))
                    intent.PutExtra(Intent.ExtraComponentName, componentNameExtra);
                intent.SetFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public bool RequestPermission()
        {
            var activity = Platform.CurrentActivity ?? throw new NullReferenceException("Current activity is null");

            if (ContextCompat.CheckSelfPermission(activity, Manifest.Permission.WriteExternalStorage) == Permission.Granted)
            {
                return true;
            }

            if (ActivityCompat.ShouldShowRequestPermissionRationale(activity, Manifest.Permission.WriteExternalStorage))
            {
                //await Toast.Make("Please grant access to external storage", ToastDuration.Short, 12).Show();
            }

            ActivityCompat.RequestPermissions(activity, new[] { Manifest.Permission.WriteExternalStorage }, 1);

            return false;
        }
    }
}
