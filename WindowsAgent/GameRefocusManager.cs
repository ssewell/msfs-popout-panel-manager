﻿using MSFSPopoutPanelManager.DomainModel.Profile;
using MSFSPopoutPanelManager.DomainModel.Setting;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace MSFSPopoutPanelManager.WindowsAgent
{
    public class GameRefocusManager
    {
        private static int _winEventClickLock = 0;
        private static object _hookLock = new object();
        private static bool _isHookMouseDown = false;

        public static ApplicationSetting ApplicationSetting { get; set; }

        public static void HandleMouseDownEvent(PanelConfig panelConfig)
        {
            Debug.WriteLine("Handling touch down event...");
            if (!_isHookMouseDown)
            {
                Debug.WriteLine("Executing touch down event...");
                lock (_hookLock)
                {
                    Point point;
                    PInvoke.GetCursorPos(out point);

                    // Disable left clicking if user is touching the title bar area or the borders (with 5 extra pixels for margin of error)
                    // Title bar
                    if (point.Y - panelConfig.Top < (panelConfig.HideTitlebar ? 5 : 50))
                        return;

                    // Bottom border
                    if (panelConfig.Top + panelConfig.Height - point.Y < 15)
                        return;

                    // Left border
                    if (point.X - panelConfig.Left < 15)
                        return;

                    // Right border
                    if (panelConfig.Left + panelConfig.Width - point.X < 15)
                        return;

                    _isHookMouseDown = true;
                }
            }
        }

        public static void HandleMouseUpEvent(PanelConfig panelConfig)
        {
            Debug.WriteLine("Handling touch up event...");
            if (_isHookMouseDown)
            {
                Debug.WriteLine("Executing touch up event...");
                Thread.Sleep(ApplicationSetting.TouchSetting.TouchDownUpDelay);

                lock (_hookLock)
                {
                    _isHookMouseDown = false;

                    Point point;
                    PInvoke.GetCursorPos(out point);

                    // Disable left clicking if user is touching the title bar area
                    if (point.Y - panelConfig.Top > (panelConfig.HideTitlebar ? 5 : 31))
                    {
                        var prevWinEventClickLock = ++_winEventClickLock;

                        // Use click event refocus only if panel is not a touch panel
                        if (prevWinEventClickLock == _winEventClickLock && ApplicationSetting.RefocusSetting.RefocusGameWindow.IsEnabled && panelConfig.AutoGameRefocus && !panelConfig.TouchEnabled)
                        {
                            Task.Run(() => RefocusMsfs(prevWinEventClickLock));
                        }
                    }
                }
            }
        }

        private static void RefocusMsfs(int prevWinEventClickLock)
        {
            Thread.Sleep(Convert.ToInt32(ApplicationSetting.RefocusSetting.RefocusGameWindow.Delay * 1000));

            if (prevWinEventClickLock == _winEventClickLock)
            {
                if (!_isHookMouseDown)
                {
                    // ToDo: Fix this for POPM overlap game window
                    var rect = WindowActionManager.GetWindowRectangle(WindowProcessManager.SimulatorProcess.Handle);
                    PInvoke.SetCursorPos(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                }
            }
        }
    }
}
