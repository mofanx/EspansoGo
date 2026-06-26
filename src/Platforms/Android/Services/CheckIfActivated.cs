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
using System;
using Android.OS;

namespace EspansoGo.Services
{
    internal class CheckIfActivated : ICheckIfActivated
    {
        private const string ShizukuPermission = "moe.shizuku.manager.permission.API_V23";
        private const string WriteSecureSettingsPermission = "android.permission.WRITE_SECURE_SETTINGS";
        private const int ShizukuRequestCode = 10001;

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
                if (!string.IsNullOrEmpty(componentNameExtra) && Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
                    intent.PutExtra("android.intent.extra.COMPONENT_NAME", componentNameExtra);
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

        public bool IsShizukuAvailable()
        {
            try
            {
                // SZ-2: Use official Shizuku.pingBinder() via JNI
                var shizukuClass = Java.Lang.Class.ForName("rikka.shizuku.Shizuku");
                var pingMethod = shizukuClass.GetMethod("pingBinder");
                var result = pingMethod.Invoke(null);
                return result is Java.Lang.Boolean boolResult && boolResult.BooleanValue();
            }
            catch
            {
                return false;
            }
        }

        public bool IsShizukuAuthorized()
        {
            try
            {
                // SZ-2: Use official Shizuku.checkSelfPermission() via JNI
                var shizukuClass = Java.Lang.Class.ForName("rikka.shizuku.Shizuku");
                var checkMethod = shizukuClass.GetMethod("checkSelfPermission");
                var result = checkMethod.Invoke(null);
                if (result is Java.Lang.Integer intResult)
                    return intResult.IntValue() == 0; // PackageManager.PERMISSION_GRANTED == 0
                return false;
            }
            catch
            {
                return false;
            }
        }

        public Task<bool> RequestShizukuAuthorization()
        {
            try
            {
                // SZ-4: Use official Shizuku.requestPermission() via JNI
                var shizukuClass = Java.Lang.Class.ForName("rikka.shizuku.Shizuku");
                var requestMethod = shizukuClass.GetMethod("requestPermission", Java.Lang.Integer.Type);
                requestMethod.Invoke(null, Java.Lang.Integer.ValueOf(ShizukuRequestCode));
                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"RequestShizukuAuthorization failed: {e.Message}");
                return Task.FromResult(false);
            }
        }

        public async Task<bool> TryEnableAccessibility()
        {
            try
            {
                var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.BaseContext;
                if (context == null) return false;

                var packageName = context.PackageName;
                var serviceName = $"{packageName}.ExpanderAccessibilityservice";

                // Check if already has WRITE_SECURE_SETTINGS permission
                bool hasWriteSecureSettings = ContextCompat.CheckSelfPermission(context, WriteSecureSettingsPermission) == Permission.Granted;

                if (!hasWriteSecureSettings)
                {
                    // Try to grant WRITE_SECURE_SETTINGS via Shizuku
                    if (!IsShizukuAuthorized())
                    {
                        return false;
                    }

                    // Grant WRITE_SECURE_SETTINGS permission via shell command through Shizuku
                    bool granted = await GrantPermissionViaShizuku(WriteSecureSettingsPermission);
                    if (!granted)
                    {
                        Android.Util.Log.Error("ShizukuA11y", "Failed to grant WRITE_SECURE_SETTINGS via Shizuku");
                        return false;
                    }

                    // Wait for permission to take effect
                    await Task.Delay(500);
                    hasWriteSecureSettings = HasWriteSecureSettingsPermission();
                }

                if (!hasWriteSecureSettings)
                {
                    return false;
                }

                // Enable accessibility service using Settings.Secure
                try
                {
                    var enabledServices = Settings.Secure.GetString(context.ContentResolver,
                        Settings.Secure.EnabledAccessibilityServices);

                    var component = $"{packageName}/{serviceName}";

                    if (!string.IsNullOrEmpty(enabledServices) && enabledServices.Contains(component))
                        return true;

                    var newEnabledServices = string.IsNullOrEmpty(enabledServices)
                        ? component
                        : $"{enabledServices}:{component}";

                    Settings.Secure.PutString(context.ContentResolver,
                        Settings.Secure.EnabledAccessibilityServices, newEnabledServices);
                    Settings.Secure.PutInt(context.ContentResolver,
                        Settings.Secure.AccessibilityEnabled, 1);

                    return true;
                }
                catch (Exception ex)
                {
                    Android.Util.Log.Error("ShizukuA11y", $"Settings modification failed: {ex.Message}");
                    return false;
                }
            }
            catch (Exception e)
            {
                Android.Util.Log.Error("ShizukuA11y", $"TryEnableAccessibility failed: {e.Message}");
                return false;
            }
        }

        private async Task<bool> GrantPermissionViaShizuku(string permission)
        {
            try
            {
                var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.BaseContext;
                if (context == null) return false;

                var packageName = context.PackageName;

                // Use Shizuku SystemServiceHelper to get PackageManager service
                // Then call grantRuntimePermission through the Binder
                var granted = await GrantRuntimePermissionViaShizuku(packageName, permission);
                return granted;
            }
            catch (Exception e)
            {
                Android.Util.Log.Error("ShizukuA11y", $"GrantPermissionViaShizuku failed: {e.Message}");
                return false;
            }
        }

        private Task<bool> GrantRuntimePermissionViaShizuku(string packageName, string permission)
        {
            try
            {
                // SZ-5: Use Shizuku SystemServiceHelper to get PackageManager binder
                var systemServiceHelperClass = Java.Lang.Class.ForName("rikka.shizuku.SystemServiceHelper");
                var getSystemServiceMethod = systemServiceHelperClass.GetMethod("getSystemService",
                    Java.Lang.Class.FromType(typeof(Java.Lang.String)));

                var rawBinder = getSystemServiceMethod.Invoke(null, new Java.Lang.String("package"));
                if (rawBinder == null)
                {
                    Android.Util.Log.Error("ShizukuA11y", "Failed to get PackageManager binder");
                    return Task.FromResult(false);
                }

                // SZ-5: Wrap binder with ShizukuBinderWrapper before using
                var wrapperClass = Java.Lang.Class.ForName("rikka.shizuku.ShizukuBinderWrapper");
                var wrapperConstructor = wrapperClass.GetConstructor(Java.Lang.Class.FromType(typeof(Android.OS.IBinder)));
                var wrappedBinder = wrapperConstructor.NewInstance(rawBinder);

                if (wrappedBinder == null)
                {
                    Android.Util.Log.Error("ShizukuA11y", "Failed to wrap binder with ShizukuBinderWrapper");
                    return Task.FromResult(false);
                }

                // Get IPackageManager interface via Stub.asInterface
                var stubClass = Java.Lang.Class.ForName("android.content.pm.IPackageManager$Stub");
                var asInterfaceMethod = stubClass.GetMethod("asInterface",
                    Java.Lang.Class.FromType(typeof(Android.OS.IBinder)));
                var iPackageManager = asInterfaceMethod.Invoke(null, wrappedBinder);

                if (iPackageManager == null)
                {
                    Android.Util.Log.Error("ShizukuA11y", "Failed to get IPackageManager");
                    return Task.FromResult(false);
                }

                // Call grantRuntimePermission(packageName, permission, userId=0)
                var iPackageManagerClass = Java.Lang.Class.ForName("android.content.pm.IPackageManager");
                var grantMethod = iPackageManagerClass.GetMethod("grantRuntimePermission",
                    Java.Lang.Class.FromType(typeof(Java.Lang.String)),
                    Java.Lang.Class.FromType(typeof(Java.Lang.String)),
                    Java.Lang.Integer.Type);

                grantMethod.Invoke(iPackageManager,
                    new Java.Lang.String(packageName),
                    new Java.Lang.String(permission),
                    Java.Lang.Integer.ValueOf(0));

                Android.Util.Log.Debug("ShizukuA11y", $"Granted permission {permission} for {packageName}");
                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Android.Util.Log.Error("ShizukuA11y", $"GrantRuntimePermissionViaShizuku failed: {e.Message}");
                return Task.FromResult(false);
            }
        }

        public async Task<bool> TryDisableAccessibility()
        {
            try
            {
                var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.BaseContext;
                if (context == null) return false;

                var packageName = context.PackageName;
                var serviceName = $"{packageName}.ExpanderAccessibilityservice";

                // Check if already has WRITE_SECURE_SETTINGS permission
                bool hasWriteSecureSettings = ContextCompat.CheckSelfPermission(context, WriteSecureSettingsPermission) == Permission.Granted;

                if (!hasWriteSecureSettings)
                {
                    // Try to grant WRITE_SECURE_SETTINGS via Shizuku
                    if (!IsShizukuAuthorized())
                    {
                        return false;
                    }

                    // Grant WRITE_SECURE_SETTINGS permission via shell command through Shizuku
                    bool granted = await GrantPermissionViaShizuku(WriteSecureSettingsPermission);
                    if (!granted)
                    {
                        Android.Util.Log.Error("ShizukuA11y", "Failed to grant WRITE_SECURE_SETTINGS via Shizuku");
                        return false;
                    }

                    // Wait for permission to take effect
                    await Task.Delay(500);
                    hasWriteSecureSettings = HasWriteSecureSettingsPermission();
                }

                if (!hasWriteSecureSettings)
                {
                    return false;
                }

                // Disable accessibility service using Settings.Secure
                try
                {
                    var enabledServices = Settings.Secure.GetString(context.ContentResolver,
                        Settings.Secure.EnabledAccessibilityServices);

                    if (string.IsNullOrEmpty(enabledServices))
                        return true;

                    var component = $"{packageName}/{serviceName}";

                    if (!enabledServices.Contains(component))
                        return true;

                    var newEnabledServices = enabledServices.Split(':')
                        .Where(s => s != component)
                        .ToList();

                    if (newEnabledServices.Count == 0)
                    {
                        Settings.Secure.PutString(context.ContentResolver,
                            Settings.Secure.EnabledAccessibilityServices, "");
                        Settings.Secure.PutInt(context.ContentResolver,
                            Settings.Secure.AccessibilityEnabled, 0);
                    }
                    else
                    {
                        Settings.Secure.PutString(context.ContentResolver,
                            Settings.Secure.EnabledAccessibilityServices,
                            string.Join(":", newEnabledServices));
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Android.Util.Log.Error("ShizukuA11y", $"Settings modification failed: {ex.Message}");
                    return false;
                }
            }
            catch (Exception e)
            {
                Android.Util.Log.Error("ShizukuA11y", $"TryDisableAccessibility failed: {e.Message}");
                return false;
            }
        }

        public bool HasWriteSecureSettingsPermission()
        {
            try
            {
                var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.BaseContext;
                if (context == null) return false;

                return ContextCompat.CheckSelfPermission(context, WriteSecureSettingsPermission) == Permission.Granted;
            }
            catch
            {
                return false;
            }
        }
    }
}
