﻿using MSFSPopoutPanelManager.Model;
using MSFSPopoutPanelManager.Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MSFSPopoutPanelManager.Provider
{
    public class PanelPopOutManager
    {
        private const int RETRY_COUNT = 5;

        private UserProfileManager _userProfileManager;
        private SimConnectManager _simConnectManager;
        private IntPtr _simulatorHandle;
        private List<PanelConfig> _panels;
        private int _currentPanelIndex;

        public event EventHandler OnPopOutStarted;
        public event EventHandler<EventArgs<bool>> OnPopOutCompleted;

        public UserProfile UserProfile { get; set; }

        public AppSetting AppSetting { get; set; }

        public PanelPopOutManager(UserProfileManager userProfileManager, SimConnectManager simConnectManager)
        {
            _userProfileManager = userProfileManager;
            _simConnectManager = simConnectManager;
        }

        public void StartPopout()
        {
            var simulatorProcess = DiagnosticManager.GetSimulatorProcess();

            if(simulatorProcess != null)
                _simulatorHandle = simulatorProcess.Handle;
                
            _panels = new List<PanelConfig>();

            OnPopOutStarted?.Invoke(this, null);
                
            // If enable, load the current viewport into custom view by Ctrl-Alt-0
            if (AppSetting.UseAutoPanning)
            {
                var simualatorProcess = DiagnosticManager.GetSimulatorProcess();
                if (simualatorProcess != null)
                {
                    InputEmulationManager.LoadCustomView(simualatorProcess.Handle, AppSetting.AutoPanningKeyBinding);
                    Thread.Sleep(500);
                }
            }

            Task<List<PanelConfig>> popoutPanelTask = Task<List<PanelConfig>>.Factory.StartNew(() =>
            {
                return ExecutePopoutSeparation();
            });

            popoutPanelTask.Wait();
            var popoutResults = popoutPanelTask.Result;

            if (popoutResults != null)
            {
                if (UserProfile.PanelConfigs.Count > 0)
                {
                    LoadAndApplyPanelConfigs(popoutResults);
                    Logger.LogStatus("Panels have been popped out succesfully and saved panel settings have been applied.", StatusMessageType.Info);
                }
                else
                {
                    UserProfile.PanelConfigs = new ObservableCollection<PanelConfig>(popoutResults);
                    Logger.LogStatus("Panels have been popped out succesfully.", StatusMessageType.Info);
                }

                // Recenter the view port by Ctrl-Space, needs to click on game window
                var simualatorProcess = DiagnosticManager.GetSimulatorProcess();
                if (simualatorProcess != null && UserProfile.PanelSourceCoordinates.Count > 0)
                {
                    InputEmulationManager.CenterView(simualatorProcess.Handle, UserProfile.PanelSourceCoordinates[0].X, UserProfile.PanelSourceCoordinates[0].Y);
                }

                _userProfileManager.WriteUserProfiles();

                OnPopOutCompleted?.Invoke(this, new EventArgs<bool>(true));
            }
            else 
            {
                OnPopOutCompleted?.Invoke(this, new EventArgs<bool>(false));
            }
        }

        public List<PanelConfig> ExecutePopoutSeparation()
        {
            _currentPanelIndex = 0;

            _panels.Clear();

            // Must close out all existing custom pop out panels
            PInvoke.EnumWindows(new PInvoke.CallBack(EnumCustomPopoutCallBack), 0);
            if (_panels.Count > 0)
            {
                Logger.LogStatus("Please close all existing panel pop outs before continuing.", StatusMessageType.Error);
                return null;
            }

            _panels.Clear();

            if(_simulatorHandle != IntPtr.Zero)
            PInvoke.SetForegroundWindow(_simulatorHandle);

            try
            {
                // PanelIndex starts at 1
                for (var i = 1; i <= UserProfile.PanelSourceCoordinates.Count; i++)
                {
                    PopoutPanel(UserProfile.PanelSourceCoordinates[i - 1].X, UserProfile.PanelSourceCoordinates[i - 1].Y);

                    if (i > 1)
                    {
                        SeparatePanel(i - 1, _panels[0].PanelHandle);       // The joined panel is always the first panel that got popped out
                    }

                    var handle = PInvoke.FindWindow("AceApp", String.Empty);

                    if(handle == IntPtr.Zero && i == 1)
                        throw new PopoutManagerException("Unable to pop out the first panel. Please check the first panel's number circle is positioned inside the panel, check for panel obstruction, and check if panel can be popped out. Pop out process stopped.");
                    else if(handle == IntPtr.Zero)
                        throw new PopoutManagerException($"Unable to pop out panel number {i}. Please check panel's number circle is positioned inside the panel, check for panel obstruction, and check if panel can be popped out. Pop out process stopped.");

                    var panelInfo = GetPanelWindowInfo(handle);
                    panelInfo.PanelIndex = i;       
                    panelInfo.PanelName = $"Panel{i}";
                    _panels.Add(panelInfo);

                    PInvoke.SetWindowText(panelInfo.PanelHandle, panelInfo.PanelName + " (Custom)");
                    
                    if (i > 1)
                        PInvoke.MoveWindow(panelInfo.PanelHandle, 0, 0, 800, 600, true);
                }

                _currentPanelIndex = _panels.Count;

                // Performance validation, make sure the number of pop out panels is equal to the number of selected panel
                if (GetPopoutPanelCountByType(PanelType.CustomPopout) != UserProfile.PanelSourceCoordinates.Count)
                    throw new PopoutManagerException("Unable to pop out all panels. Please align all panel number circles with in-game panel locations.");

                // Add the built-in pop outs (ie. ATC, VFR Map) to the panel list
                if(AppSetting.IncludeBuiltInPanel)
                    PInvoke.EnumWindows(new PInvoke.CallBack(EnumBuiltinPopoutCallBack), 0);

                // Add the MSFS Touch Panel (My other github project) windows to the panel list
                PInvoke.EnumWindows(new PInvoke.CallBack(EnumMSFSTouchPanelPopoutCallBack), 0);

                if (_panels.Count == 0)
                    throw new PopoutManagerException("No panels have been found. Please select at least one in-game panel.");

                // Line up all the panels and fill in meta data
                for (var i = _panels.Count - 1; i >= 0; i--)
                {
                    if (_panels[i].PanelType == PanelType.CustomPopout)
                    {
                        var shift = _panels.Count - i - 1;
                        _panels[i].Top = shift * 30;
                        _panels[i].Left = shift * 30;
                        _panels[i].Width = 800;
                        _panels[i].Height = 600;

                        PInvoke.MoveWindow(_panels[i].PanelHandle, _panels[i].Top, _panels[i].Left, _panels[i].Width, _panels[i].Height, true);
                        PInvoke.SetForegroundWindow(_panels[i].PanelHandle);
                        Thread.Sleep(200);
                    }
                }

                return _panels;
            }
            catch (PopoutManagerException ex)
            {
                Logger.LogStatus(ex.Message, StatusMessageType.Error);
                return null;
            }
            catch
            {
                throw;
            }
        }

        private void LoadAndApplyPanelConfigs(List<PanelConfig> popoutResults)
        {
            int index;
            popoutResults.ForEach(resultPanel =>
            {
                if (resultPanel.PanelType == PanelType.CustomPopout)
                {
                    index = UserProfile.PanelConfigs.ToList().FindIndex(x => x.PanelIndex == resultPanel.PanelIndex);
                    if (index > -1)
                        UserProfile.PanelConfigs[index].PanelHandle = resultPanel.PanelHandle;
                }
                else
                {
                    index = UserProfile.PanelConfigs.ToList().FindIndex(x => x.PanelName == resultPanel.PanelName);
                    if (index > -1)
                        UserProfile.PanelConfigs[index].PanelHandle = resultPanel.PanelHandle;
                    else
                        UserProfile.PanelConfigs.Add(resultPanel);
                }

            });

            // Remove pop out that do not exist for this pop out iteration
            foreach(var panelConfig in UserProfile.PanelConfigs.ToList())
            {
                if(panelConfig.PanelHandle == IntPtr.Zero)
                {
                    UserProfile.PanelConfigs.Remove(panelConfig);
                }
            }

            Parallel.ForEach(UserProfile.PanelConfigs, panel =>
            {
                if (panel != null && panel.PanelHandle != IntPtr.Zero && panel.Width != 0 && panel.Height != 0)
                {
                    // Apply panel name
                    if (panel.PanelType == PanelType.CustomPopout)
                    {
                        var name = panel.PanelName;
                        if (name.IndexOf("(Custom)") == -1)
                            name = name + " (Custom)";

                        PInvoke.SetWindowText(panel.PanelHandle, name);
                        Thread.Sleep(500);
                    }

                    // Apply full screen (cannot combine with always on top or hide title bar
                    if (panel.FullScreen)
                    {
                        WindowManager.MoveWindow(panel.PanelHandle, panel.Left, panel.Top);
                        Thread.Sleep(1000);
                        InputEmulationManager.ToggleFullScreenPanel(panel.PanelHandle);
                        Thread.Sleep(1000);
                    }
                    else 
                    {
                        // Apply locations
                        PInvoke.ShowWindow(panel.PanelHandle, PInvokeConstant.SW_RESTORE);
                        Thread.Sleep(250);
                        PInvoke.MoveWindow(panel.PanelHandle, panel.Left, panel.Top, panel.Width, panel.Height, false);
                        Thread.Sleep(1000);

                        // Apply always on top
                        if (panel.AlwaysOnTop)
                        {
                            WindowManager.ApplyAlwaysOnTop(panel.PanelHandle, true, new Rectangle(panel.Left, panel.Top, panel.Width, panel.Height));
                            Thread.Sleep(1000);
                        }

                        // Apply hide title bar
                        if (panel.HideTitlebar)
                        {
                            WindowManager.ApplyHidePanelTitleBar(panel.PanelHandle, true);
                        }
                    }

                    PInvoke.ShowWindow(panel.PanelHandle, PInvokeConstant.SW_RESTORE);
                }
            });
        }

        private int GetPopoutPanelCountByType(PanelType panelType)
        {
            return _panels.FindAll(x => x.PanelType == panelType).Count;
        }

        private PanelConfig GetCustomPopoutPanelByIndex(int index)
        {
            return _panels.Find(x => x.PanelType == PanelType.CustomPopout && x.PanelIndex == index + 1);
        }

        private void PopoutPanel(int x, int y)
        {
            InputEmulationManager.PopOutPanel(x, y);
        }

        private void SeparatePanel(int index, IntPtr hwnd)
        {
            // Resize all windows to 800x600 when separating and shimmy the panel
            // MSFS draws popout panel differently at different time for same panel
            PInvoke.MoveWindow(hwnd, -8, 0, 800, 600, true);
            PInvoke.SetForegroundWindow(hwnd);
            Thread.Sleep(500);

            // Find the magnifying glass coordinate    
            var point = AnalyzeMergedWindows(hwnd);

            InputEmulationManager.LeftClick(point.X, point.Y);
        }

        public bool EnumCustomPopoutCallBack(IntPtr hwnd, int lParam)
        {
            var panelInfo = GetPanelWindowInfo(hwnd);

            if (panelInfo != null && panelInfo.PanelType == PanelType.CustomPopout)
            {
                if (!_panels.Exists(x => x.PanelHandle == hwnd))
                {
                    Interlocked.Increment(ref _currentPanelIndex);
                    panelInfo.PanelIndex = _currentPanelIndex;      
                    _panels.Add(panelInfo);
                }
            }

            return true;
        }

        public bool EnumBuiltinPopoutCallBack(IntPtr hwnd, int lParam)
        {
            var panelInfo = GetPanelWindowInfo(hwnd);

            if (panelInfo != null && panelInfo.PanelType == PanelType.BuiltInPopout)
            {
                if (!_panels.Exists(x => x.PanelHandle == hwnd))
                {
                    Interlocked.Increment(ref _currentPanelIndex);
                    panelInfo.PanelIndex = _currentPanelIndex;
                    _panels.Add(panelInfo);
                }
            }

            return true;
        }

        public bool EnumMSFSTouchPanelPopoutCallBack(IntPtr hwnd, int index)
        {
            var panelInfo = GetPanelWindowInfo(hwnd);

            if (panelInfo != null && panelInfo.PanelType == PanelType.MSFSTouchPanel)
            {
                if (!_panels.Exists(x => x.PanelHandle == hwnd))
                {
                    Interlocked.Increment(ref _currentPanelIndex);
                    panelInfo.PanelIndex = _currentPanelIndex;
                    _panels.Add(panelInfo);

                    // Apply always on top to these panels
                    WindowManager.ApplyAlwaysOnTop(panelInfo.PanelHandle, true);
                }
            }

            return true;
        }

        private PanelConfig GetPanelWindowInfo(IntPtr hwnd)
        {
            var className = PInvoke.GetClassName(hwnd);

            if (className == "AceApp")      // MSFS windows designation
            {
                var caption = PInvoke.GetWindowText(hwnd);

                Rectangle rectangle;
                PInvoke.GetWindowRect(hwnd, out rectangle);

                var panelInfo = new PanelConfig();
                panelInfo.PanelHandle = hwnd;
                panelInfo.PanelName = caption;
                panelInfo.Top = rectangle.Top;
                panelInfo.Left = rectangle.Left;
                panelInfo.Width = rectangle.Width;
                panelInfo.Height = rectangle.Height;

                if (String.IsNullOrEmpty(caption) || caption.IndexOf("Custom") > -1)
                    panelInfo.PanelType = PanelType.CustomPopout;
                else if (caption.IndexOf("Microsoft Flight Simulator") > -1)        // MSFS main game window
                    return null;
                else
                    panelInfo.PanelType = PanelType.BuiltInPopout;

                return panelInfo;
            }
            else  // For MSFS Touch Panel window
            {
                var caption = PInvoke.GetWindowText(hwnd);

                var panelInfo = new PanelConfig();
                panelInfo.PanelHandle = hwnd;
                panelInfo.PanelName = caption;

                if (caption.IndexOf("MSFS Touch Panel |") > -1)
                {
                    panelInfo.PanelType = PanelType.MSFSTouchPanel;
                    return panelInfo;
                }
                else
                    return null;
            }

            return null;
        }

        private Point AnalyzeMergedWindows(IntPtr hwnd)
        {
            var sourceImage = ImageOperation.TakeScreenShot(hwnd);

            if (sourceImage == null)
                return new Point(0, 0);

            Rectangle rectangle;
            PInvoke.GetClientRect(hwnd, out rectangle);

            var panelMenubarTop = GetPanelMenubarTop(sourceImage, rectangle);
            if (panelMenubarTop > sourceImage.Height)
                return Point.Empty;

            var panelMenubarBottom = GetPanelMenubarBottom(sourceImage, rectangle);
            if (panelMenubarTop > sourceImage.Height)
                return Point.Empty;

            var panelsStartingLeft = GetPanelMenubarStartingLeft(sourceImage, rectangle, panelMenubarTop + 5);

            // The center of magnifying glass icon is around (2.7 x height of menubar) to the right of the panel menubar starting left
            // But need to use higher number here to click the left side of magnifying glass icon because on some panel, the ratio is smaller
            var menubarHeight = panelMenubarBottom - panelMenubarTop;
            var magnifyingIconXCoor = panelsStartingLeft - Convert.ToInt32(menubarHeight * 3.2);        // ToDo: play around with this multiplier to find the best for all resolutions
            var magnifyingIconYCoor = panelMenubarTop + Convert.ToInt32(menubarHeight / 2);

            return new Point(magnifyingIconXCoor, magnifyingIconYCoor);
        }

        private int GetPanelMenubarTop(Bitmap sourceImage, Rectangle rectangle)
        {
            // Get a snippet of 1 pixel wide vertical strip of windows. We will choose the strip left of center.
            // This is to determine when the actual panel's vertical pixel starts in the window. This will allow accurate sizing of the template image
            var left = Convert.ToInt32((rectangle.Width) * 0.70);  // look at around 70% from the left
            var top = sourceImage.Height - rectangle.Height;

            if (top < 0 || left < 0)
                return -1;

            unsafe
            {
                var stripData = sourceImage.LockBits(new Rectangle(left, top, 1, rectangle.Height), ImageLockMode.ReadWrite, sourceImage.PixelFormat);

                int bytesPerPixel = Bitmap.GetPixelFormatSize(stripData.PixelFormat) / 8;
                int heightInPixels = stripData.Height;
                int widthInBytes = stripData.Width * bytesPerPixel;
                byte* ptrFirstPixel = (byte*)stripData.Scan0;

                for (int y = 0; y < heightInPixels; y++)
                {
                    byte* currentLine = ptrFirstPixel + (y * stripData.Stride);
                    for (int x = 0; x < widthInBytes; x = x + bytesPerPixel)
                    {
                        int red = currentLine[x + 2];
                        int green = currentLine[x + 1];
                        int blue = currentLine[x];

                        if (red == 255 && green == 255 && blue == 255)
                        {
                            sourceImage.UnlockBits(stripData);
                            return y + top;
                        }
                    }
                }

                sourceImage.UnlockBits(stripData);
            }

            return -1;
        }

        private int GetPanelMenubarBottom(Bitmap sourceImage, Rectangle rectangle)
        {
            // Get a snippet of 1 pixel wide vertical strip of windows. We will choose the strip about 25% from the left of the window
            var left = Convert.ToInt32((rectangle.Width) * 0.25);  // look at around 25% from the left
            var top = sourceImage.Height - rectangle.Height;

            if (top < 0 || left < 0)
                return -1;

            unsafe
            {
                var stripData = sourceImage.LockBits(new Rectangle(left, top, 1, rectangle.Height), ImageLockMode.ReadWrite, sourceImage.PixelFormat);

                int bytesPerPixel = Bitmap.GetPixelFormatSize(stripData.PixelFormat) / 8;
                int heightInPixels = stripData.Height;
                int widthInBytes = stripData.Width * bytesPerPixel;
                byte* ptrFirstPixel = (byte*)stripData.Scan0;

                int menubarBottom = -1;

                for (int y = 0; y < heightInPixels; y++)
                {
                    byte* currentLine = ptrFirstPixel + (y * stripData.Stride);
                    for (int x = 0; x < widthInBytes; x = x + bytesPerPixel)
                    {
                        int red = currentLine[x + 2];
                        int green = currentLine[x + 1];
                        int blue = currentLine[x];

                        if (red == 255 && green == 255 && blue == 255)
                        {
                            // found the top of menu bar
                            menubarBottom = y + top;
                        }
                        else if (menubarBottom > -1)     /// it is no longer white in color, we hit menubar bottom
                        {
                            sourceImage.UnlockBits(stripData);
                            return menubarBottom;
                        }
                    }
                }

                sourceImage.UnlockBits(stripData);
            }

            return -1;
        }

        private int GetPanelMenubarStartingLeft(Bitmap sourceImage, Rectangle rectangle, int top)
        {
            unsafe
            {
                var stripData = sourceImage.LockBits(new Rectangle(0, top, rectangle.Width, 1), ImageLockMode.ReadWrite, sourceImage.PixelFormat);

                int bytesPerPixel = Bitmap.GetPixelFormatSize(stripData.PixelFormat) / 8;
                int widthInPixels = stripData.Width;
                int heightInBytes = stripData.Height * bytesPerPixel;
                byte* ptrFirstPixel = (byte*)stripData.Scan0;

                for (int x = 0; x < widthInPixels; x++)
                {
                    byte* currentLine = ptrFirstPixel - (x * bytesPerPixel);
                    for (int y = 0; y < heightInBytes; y = y + bytesPerPixel)
                    {
                        int red = currentLine[y + 2];
                        int green = currentLine[y + 1];
                        int blue = currentLine[y];

                        if (red == 255 && green == 255 && blue == 255)
                        {
                            sourceImage.UnlockBits(stripData);
                            return sourceImage.Width - x;
                        }
                    }
                }

                sourceImage.UnlockBits(stripData);
            }

            return -1;
        }
    }
}