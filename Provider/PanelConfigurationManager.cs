﻿using MSFSPopoutPanelManager.Model;
using System;
using System.Drawing;
using System.Linq;

namespace MSFSPopoutPanelManager.Provider
{
    public class PanelConfigurationManager
    {
        private UserProfileManager _userProfileManager;
        private IntPtr _winEventHook;
        private static PInvoke.WinEventProc _winEvent;      // keep this as static to prevent garbage collect or the app will crash
        private Rectangle _lastWindowRectangle;

        public UserProfile UserProfile { get; set; }

        public bool AllowEdit { get; set; }

        public PanelConfigurationManager(UserProfileManager userProfileManager)
        {
            _userProfileManager = userProfileManager;
            _winEvent = new PInvoke.WinEventProc(EventCallback);
            AllowEdit = true;
        }

        public void HookWinEvent()
        {
              // Setup panel config event hooks
            _winEventHook = PInvoke.SetWinEventHook(PInvokeConstant.EVENT_SYSTEM_MOVESIZEEND, PInvokeConstant.EVENT_OBJECT_LOCATIONCHANGE, DiagnosticManager.GetApplicationProcess().Handle, _winEvent, 0, 0, PInvokeConstant.WINEVENT_OUTOFCONTEXT);
        }

        public void UnhookWinEvent()
        {
            // Unhook all Win API events
            PInvoke.UnhookWinEvent(_winEventHook);
        }

        public void LockPanelsUpdated()
        {
            UserProfile.IsLocked = !UserProfile.IsLocked;
            _userProfileManager.WriteUserProfiles();
        }

        public void PanelConfigPropertyUpdated(PanelConfigItem panelConfigItem)
        {
            if (!AllowEdit || UserProfile.IsLocked)
                return;

            var panelConfig = UserProfile.PanelConfigs.ToList().Find(p => p.PanelIndex == panelConfigItem.PanelIndex);

            if (panelConfig != null)
            {
                if (panelConfigItem.PanelConfigProperty == PanelConfigPropertyName.FullScreen)
                {
                    InputEmulationManager.ToggleFullScreenPanel(panelConfig.PanelHandle);
                    panelConfig.HideTitlebar = false;
                    panelConfig.AlwaysOnTop = false;
                }
                else if (panelConfigItem.PanelConfigProperty == PanelConfigPropertyName.PanelName)
                {
                    var name = panelConfig.PanelName;
                    if (name.IndexOf("(Custom)") == -1)
                        name = name + " (Custom)";
                    PInvoke.SetWindowText(panelConfig.PanelHandle, name);
                }
                else if(!panelConfig.FullScreen)
                {
                    switch (panelConfigItem.PanelConfigProperty)
                    {
                        case PanelConfigPropertyName.Left:
                        case PanelConfigPropertyName.Top:
                            PInvoke.MoveWindow(panelConfig.PanelHandle, panelConfig.Left, panelConfig.Top, panelConfig.Width, panelConfig.Height, true);
                            break;
                        case PanelConfigPropertyName.Width:
                        case PanelConfigPropertyName.Height:
                            if (panelConfig.HideTitlebar)
                                WindowManager.ApplyHidePanelTitleBar(panelConfig.PanelHandle, false);

                            int orignalLeft = panelConfig.Left;
                            PInvoke.MoveWindow(panelConfig.PanelHandle, panelConfig.Left, panelConfig.Top, panelConfig.Width, panelConfig.Height, true);
                            MSFSBugPanelShiftWorkaround(panelConfig.PanelHandle, orignalLeft, panelConfig.Top, panelConfig.Width, panelConfig.Height);

                            if (panelConfig.HideTitlebar)
                                WindowManager.ApplyHidePanelTitleBar(panelConfig.PanelHandle, true);

                            break;
                        case PanelConfigPropertyName.AlwaysOnTop:
                            WindowManager.ApplyAlwaysOnTop(panelConfig.PanelHandle, panelConfig.AlwaysOnTop, new Rectangle(panelConfig.Left, panelConfig.Top, panelConfig.Width, panelConfig.Height));
                            break;
                        case PanelConfigPropertyName.HideTitlebar:
                            WindowManager.ApplyHidePanelTitleBar(panelConfig.PanelHandle, panelConfig.HideTitlebar);
                            break;
                    }
                }

                _userProfileManager.WriteUserProfiles();
            }
        }

        public void PanelConfigIncreaseDecrease(PanelConfigItem panelConfigItem, int changeAmount)
        {
            if (!AllowEdit || UserProfile.IsLocked || UserProfile.PanelConfigs == null || UserProfile.PanelConfigs.Count == 0)
                return;

            var index = UserProfile.PanelConfigs.ToList().FindIndex(p => p.PanelIndex == panelConfigItem.PanelIndex);

            if (index > -1)
            {
                var panelConfig = UserProfile.PanelConfigs[index];

                // Cannot apply any other settings if panel is full screen mode
                if (panelConfig.FullScreen)
                    return;

                int orignalLeft = panelConfig.Left;

                switch (panelConfigItem.PanelConfigProperty)
                {
                    case PanelConfigPropertyName.Left:
                        panelConfig.Left += changeAmount;
                        PInvoke.MoveWindow(panelConfig.PanelHandle, panelConfig.Left, panelConfig.Top, panelConfig.Width, panelConfig.Height, false);
                        break;
                    case PanelConfigPropertyName.Top:
                        panelConfig.Top += changeAmount;
                        PInvoke.MoveWindow(panelConfig.PanelHandle, panelConfig.Left, panelConfig.Top, panelConfig.Width, panelConfig.Height, false);
                        break;
                    case PanelConfigPropertyName.Width:
                        panelConfig.Width += changeAmount;

                        if (panelConfig.HideTitlebar)
                            WindowManager.ApplyHidePanelTitleBar(panelConfig.PanelHandle, false);

                        PInvoke.MoveWindow(panelConfig.PanelHandle, panelConfig.Left, panelConfig.Top, panelConfig.Width, panelConfig.Height, false);
                        MSFSBugPanelShiftWorkaround(panelConfig.PanelHandle, orignalLeft, panelConfig.Top, panelConfig.Width, panelConfig.Height);

                        if (panelConfig.HideTitlebar)
                            WindowManager.ApplyHidePanelTitleBar(panelConfig.PanelHandle, true);

                        break;
                    case PanelConfigPropertyName.Height:
                        panelConfig.Height += changeAmount;

                        if (panelConfig.HideTitlebar)
                            WindowManager.ApplyHidePanelTitleBar(panelConfig.PanelHandle, false);

                        PInvoke.MoveWindow(panelConfig.PanelHandle, panelConfig.Left, panelConfig.Top, panelConfig.Width, panelConfig.Height, false);
                        MSFSBugPanelShiftWorkaround(panelConfig.PanelHandle, orignalLeft, panelConfig.Top, panelConfig.Width, panelConfig.Height);

                        if (panelConfig.HideTitlebar)
                            WindowManager.ApplyHidePanelTitleBar(panelConfig.PanelHandle, true);

                        break;
                    default:
                        return;
                }

                _userProfileManager.WriteUserProfiles();
            }
        }

        private void MSFSBugPanelShiftWorkaround(IntPtr handle, int originalLeft, int top, int width, int height)
        {
            // Fixed MSFS bug, create workaround where on 2nd or later instance of width adjustment, the panel shift to the left by itself
            // Wait for system to catch up on panel coordinate that were just applied
            System.Threading.Thread.Sleep(200);

            Rectangle rectangle;
            PInvoke.GetWindowRect(handle, out rectangle);

            if (rectangle.Left != originalLeft)
                PInvoke.MoveWindow(handle, originalLeft, top, width, height, false);
        }

        private void EventCallback(IntPtr hWinEventHook, uint iEvent, IntPtr hWnd, int idObject, int idChild, int dwEventThread, int dwmsEventTime)
        {
            PanelConfig panelConfig;

            // check by priority to minimize escaping constraint
            if (hWnd == IntPtr.Zero
                || idObject != 0
                || hWinEventHook != _winEventHook
                || !AllowEdit
                || !(iEvent == PInvokeConstant.EVENT_OBJECT_LOCATIONCHANGE || iEvent == PInvokeConstant.EVENT_SYSTEM_MOVESIZEEND)
                || UserProfile.PanelConfigs == null || UserProfile.PanelConfigs.Count == 0)
            {
                return;
            }

            if(UserProfile.IsLocked)
            {
                panelConfig = UserProfile.PanelConfigs.FirstOrDefault(panel => panel.PanelHandle == hWnd);

                if (panelConfig != null && panelConfig.PanelType == PanelType.CustomPopout)
                {
                    // Move window back to original location if user profile is locked
                    if (iEvent == PInvokeConstant.EVENT_SYSTEM_MOVESIZEEND)
                    {
                        PInvoke.MoveWindow(panelConfig.PanelHandle, panelConfig.Left, panelConfig.Top, panelConfig.Width, panelConfig.Height, false);
                        return;
                    }

                    if (iEvent == PInvokeConstant.EVENT_OBJECT_LOCATIONCHANGE)
                    {
                        // Detect if window is maximized, if so, save settings
                        WINDOWPLACEMENT wp = new WINDOWPLACEMENT();
                        wp.length = System.Runtime.InteropServices.Marshal.SizeOf(wp);
                        PInvoke.GetWindowPlacement(hWnd, ref wp);
                        if (wp.showCmd == PInvokeConstant.SW_SHOWMAXIMIZED || wp.showCmd == PInvokeConstant.SW_SHOWMINIMIZED || wp.showCmd == PInvokeConstant.SW_SHOWNORMAL)
                        {
                            PInvoke.ShowWindow(hWnd, PInvokeConstant.SW_RESTORE);
                        }
                        return;
                    }
                }

                return;
            }

            panelConfig = UserProfile.PanelConfigs.FirstOrDefault(panel => panel.PanelHandle == hWnd);

            if (panelConfig != null)
            {
                switch (iEvent)
                {
                    case PInvokeConstant.EVENT_OBJECT_LOCATIONCHANGE:
                        Rectangle winRectangle;
                        PInvoke.GetWindowRect(panelConfig.PanelHandle, out winRectangle);

                        if (_lastWindowRectangle == winRectangle)       // ignore duplicate callback messages
                            return;

                        _lastWindowRectangle = winRectangle;
                        Rectangle clientRectangle;
                        PInvoke.GetClientRect(panelConfig.PanelHandle, out clientRectangle);

                        panelConfig.Left = winRectangle.Left;
                        panelConfig.Top = winRectangle.Top;

                        if (panelConfig.HideTitlebar)
                        {
                            panelConfig.Width = clientRectangle.Width;
                            panelConfig.Height = clientRectangle.Height;
                        }
                        else
                        {
                            panelConfig.Width = clientRectangle.Width + 16;
                            panelConfig.Height = clientRectangle.Height + 39;
                        }

                        // Detect if window is maximized, if so, save settings
                        WINDOWPLACEMENT wp = new WINDOWPLACEMENT();
                        wp.length = System.Runtime.InteropServices.Marshal.SizeOf(wp);
                        PInvoke.GetWindowPlacement(hWnd, ref wp);
                        if (wp.showCmd == PInvokeConstant.SW_SHOWMAXIMIZED || wp.showCmd == PInvokeConstant.SW_SHOWMINIMIZED)
                        {
                            _userProfileManager.WriteUserProfiles();
                        }

                        break;
                    case PInvokeConstant.EVENT_SYSTEM_MOVESIZEEND:
                        _userProfileManager.WriteUserProfiles();
                        break;
                }
            }
        }
    }
}