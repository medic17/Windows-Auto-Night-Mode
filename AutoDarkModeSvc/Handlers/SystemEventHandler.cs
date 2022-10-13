﻿using AutoDarkModeLib;
using AutoDarkModeSvc.Core;
using Microsoft.Win32;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.System.Power;

namespace AutoDarkModeSvc.Handlers
{
    static class SystemEventHandler
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static bool darkThemeOnBatteryEnabled;
        private static bool resumeEventEnabled;
        private static GlobalState state = GlobalState.Instance();
        private static AdmConfigBuilder builder = AdmConfigBuilder.Instance();

        public static void RegisterThemeEvent()
        {
            if (!darkThemeOnBatteryEnabled)
            {
                Logger.Info("enabling event handler for dark mode on battery state discharging");
                PowerManager.BatteryStatusChanged += PowerManager_BatteryStatusChanged;
                darkThemeOnBatteryEnabled = true;
            }
        }

        private static void PowerManager_BatteryStatusChanged(object sender, object e)
        {
            if (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline)
            {
                Logger.Info("battery discharging, enabling dark mode");
                ThemeManager.UpdateTheme(Theme.Dark, new(SwitchSource.BatteryStatusChanged));
            }
            else
            {
                ThemeManager.RequestSwitch(new(SwitchSource.BatteryStatusChanged));
            }
        }

        public static void DeregisterThemeEvent()
        {
            try
            {
                if (darkThemeOnBatteryEnabled)
                {
                    Logger.Info("disabling event handler for dark mode on battery state discharging");
                    PowerManager.BatteryStatusChanged -= PowerManager_BatteryStatusChanged;
                    darkThemeOnBatteryEnabled = false;
                    ThemeManager.RequestSwitch(new(SwitchSource.BatteryStatusChanged));
                }
            }
            catch (InvalidOperationException ex)
            {
                Logger.Error(ex, "while deregistering SystemEvents_PowerModeChanged ");
            }
        }

        public static void RegisterResumeEvent()
        {
            if (!resumeEventEnabled)
            {
                if (Environment.OSVersion.Version.Build >= Helper.Win11Build)
                {
                    Logger.Info("enabling theme refresh at system unlock (win 11)");
                    SystemEvents.SessionSwitch += SystemEvents_Windows11_SessionSwitch;
                }
                else
                {
                    Logger.Info("enabling theme refresh at system resume (win 10)");
                    SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
                    SystemEvents.SessionSwitch += SystemEvents_Windows10_SessionSwitch;
                }

                resumeEventEnabled = true;
            }
        }

        public static void DeregisterResumeEvent()
        {
            try
            {
                if (resumeEventEnabled)
                {
                    if (Environment.OSVersion.Version.Build >= Helper.Win11Build)
                    {
                        Logger.Info("disabling theme refresh at system unlock (win 11)");
                        SystemEvents.SessionSwitch += SystemEvents_Windows11_SessionSwitch;
                    }
                    else
                    {
                        Logger.Info("disabling theme refresh at system resume (win 10)");
                        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
                        SystemEvents.SessionSwitch -= SystemEvents_Windows10_SessionSwitch;
                        state.PostponeManager.Remove(new(Helper.PostponeItemSessionLock));
                    }
                    resumeEventEnabled = false;
                }
            }
            catch (InvalidOperationException ex)
            {
                Logger.Error(ex, "while deregistering SystemEvents_PowerModeChanged ");
            }
        }

        private static void SystemEvents_Windows11_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock)
            {                
                if (builder.Config.AutoSwitchNotify.Enabled)
                {
                    NotifyAtResume();
                }
                else
                {
                    state.PostponeManager.Remove(new(Helper.PostponeItemSessionLock));
                    if (!state.PostponeManager.IsSkipNextSwitch && !state.PostponeManager.IsUserDelayed)
                    {
                        ThemeManager.RequestSwitch(new(SwitchSource.SystemUnlock));
                        Logger.Info("system unlocked, refreshing theme");
                    }
                    else
                    {
                        Logger.Info($"system unlocked, no refresh due to active user postpones: {state.PostponeManager}");
                    }
                }                
            }
            else if (e.Reason == SessionSwitchReason.SessionLock)
            {
                state.PostponeManager.Add(new(Helper.PostponeItemSessionLock));
            }
        }

        private static void SystemEvents_Windows10_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                if (builder.Config.AutoSwitchNotify.Enabled)
                {
                    NotifyAtResume();
                }
            }
            else if (e.Reason == SessionSwitchReason.SessionLock)
            {
                if (builder.Config.AutoSwitchNotify.Enabled) state.PostponeManager.Add(new(Helper.PostponeItemSessionLock));
            }
        }


        private static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                if (builder.Config.AutoSwitchNotify.Enabled == false)
                {
                    if (!state.PostponeManager.IsSkipNextSwitch && !state.PostponeManager.IsUserDelayed)
                    {
                        ThemeManager.RequestSwitch(new(SwitchSource.SystemUnlock));
                        Logger.Info("system resuming from suspended state, refreshing theme");
                    }
                    else
                    {
                        Logger.Info($"system resuming from suspended state, no refresh due to active user postpones: {state.PostponeManager}");
                    }
                }
            }
        }


        private static void NotifyAtResume()
        {
            bool shouldNotify = false;
            if (builder.Config.Governor == Governor.NightLight)
            {
                if (state.NightLight.Current != state.RequestedTheme) shouldNotify = true;
            }
            else if (builder.Config.Governor == Governor.Default)
            {
                TimedThemeState ts = new();
                if (ts.TargetTheme != state.RequestedTheme) shouldNotify = true;
            }

            if (shouldNotify)
            {
                Logger.Info("system unlocked, prompting user for theme switch");
                Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(o =>
                {
                    ToastHandler.InvokeDelayAutoSwitchNotifyToast();
                    state.PostponeManager.Remove(new(Helper.PostponeItemSessionLock));
                });
            }
            else
            {
                Logger.Info("system unlocked, theme state valid, not sending notification");
                state.PostponeManager.Remove(new(Helper.PostponeItemSessionLock));
            }
        }
    }
}
