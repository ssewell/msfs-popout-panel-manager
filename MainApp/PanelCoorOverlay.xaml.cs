﻿using Microsoft.Extensions.DependencyInjection;
using MSFSPopoutPanelManager.DomainModel.Profile;
using MSFSPopoutPanelManager.MainApp.ViewModel;
using MSFSPopoutPanelManager.WindowsAgent;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MSFSPopoutPanelManager.MainApp
{
    public partial class PanelCoorOverlay : Window
    {
        private PanelCoorOverlayViewModel _viewModel;

        private const int WINDOW_ADJUSTMENT = 15;      // half of window height with shadow adjustment
        private int _xCoor;
        private int _yCoor;

        public bool IsEditingPanelLocation { get; set; }

        public Guid PanelId { get; set; }

        public IntPtr WindowHandle { get; set; }

        public event EventHandler<System.Drawing.Point> WindowLocationChanged;

        public PanelCoorOverlay(Guid id)
        {
            _viewModel = App.AppHost.Services.GetRequiredService<PanelCoorOverlayViewModel>();
            _viewModel.SetPanelId(id);
            PanelId = id;

            InitializeComponent();
            Loaded += PanelCoorOverlay_Loaded;

            OverlayCircle.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_viewModel.Panel.PanelSource.Color));

            if (!_viewModel.ProfileData.ActiveProfile.IsEditingPanelSource)
                OverlayBlinkingCircle.Visibility = Visibility.Collapsed;

            IsEditingPanelLocation = false;
            this.Topmost = true;
            this.Left = 0;
            this.Top = 0;

            this.MouseUp += PanelCoorOverlay_MouseUp;   // detect location change when user release mouse button when dragging the overlay window
        }

        private void PanelCoorOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            this.DataContext = _viewModel;
        }

        private void PanelCoorOverlay_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (this.Top is double.NaN || this.Left is double.NaN)
                return;

            // Fixed broken window left/top coordinate for DPI Awareness Per Monitor
            var handle = new WindowInteropHelper(this).Handle;
            var rect = WindowActionManager.GetWindowRectangle(handle);
            WindowLocationChanged?.Invoke(this, new System.Drawing.Point(rect.X + WINDOW_ADJUSTMENT, rect.Y + WINDOW_ADJUSTMENT));

            if (_viewModel.Panel != null)
                _viewModel.Panel.IsSelectedPanelSource = false;
        }

        public void SetWindowCoor(int x, int y)
        {
            _xCoor = x;
            _yCoor = y;
        }

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (IsEditingPanelLocation && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                this.DragMove();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Fixed broken window left/top coordinate for DPI Awareness Per Monitor
            var handle = new WindowInteropHelper(this).Handle;

            WindowActionManager.MoveWindow(handle, _xCoor - WINDOW_ADJUSTMENT, _yCoor - WINDOW_ADJUSTMENT, Convert.ToInt32(this.Width), Convert.ToInt32(this.Height));
            WindowActionManager.ApplyAlwaysOnTop(handle, PanelType.PanelSourceWindow, true);
        }

        private void Canvas_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && _viewModel.Panel != null)
                _viewModel.Panel.IsSelectedPanelSource = true;
        }
    }
}
