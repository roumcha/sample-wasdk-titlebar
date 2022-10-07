using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WinRT;
using WinRT.Interop;

namespace App1;

public sealed partial class MainWindow : Window
{
    AppWindow _appWindow;
    WindowsSystemDispatcherQueueHelper _wsdqHelper; // See below for implementation.
    MicaController _backdropController;
    SystemBackdropConfiguration _configurationSource;

    public MainWindow()
    {
        this.InitializeComponent();
        TrySetSystemBackdrop();
        _appWindow = GetAppWindowForCurrentWindow();

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = _appWindow.TitleBar;
            titleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(64, 128, 128, 128);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(128, 128, 128, 128);

            AppTitleBar.Loaded += AppTitleBar_Loaded;
            AppTitleBar.SizeChanged += AppTitleBar_SizeChanged;
        }
    }

    AppWindow GetAppWindowForCurrentWindow()
    {
        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
        return AppWindow.GetFromWindowId(wndId);
    }

    void AppTitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        // Check to see if customization is supported.
        // Currently only supported on Windows 11.
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            SetDragRegionForCustomTitleBar(_appWindow);
        }
    }

    void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Check to see if customization is supported.
        // Currently only supported on Windows 11.
        if (AppWindowTitleBar.IsCustomizationSupported()
            && _appWindow.TitleBar.ExtendsContentIntoTitleBar)
        {
            // Update drag region if the size of the title bar changes.
            SetDragRegionForCustomTitleBar(_appWindow);
        }
    }

    [DllImport("Shcore.dll", SetLastError = true)]
    internal static extern int GetDpiForMonitor(
        IntPtr hmonitor, Monitor_DPI_Type dpiType, out uint dpiX, out uint dpiY);

    internal enum Monitor_DPI_Type : int
    {
        MDT_Effective_DPI = 0,
        MDT_Angular_DPI = 1,
        MDT_Raw_DPI = 2,
        MDT_Default = MDT_Effective_DPI
    }

    double GetScaleAdjustment()
    {
        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
        DisplayArea displayArea = DisplayArea.GetFromWindowId(wndId, DisplayAreaFallback.Primary);
        IntPtr hMonitor = Win32Interop.GetMonitorFromDisplayId(displayArea.DisplayId);

        // Get DPI.
        int result = GetDpiForMonitor(
            hMonitor, Monitor_DPI_Type.MDT_Default, out uint dpiX, out uint _);

        if (result != 0) throw new Exception("Could not get DPI for monitor.");

        uint scaleFactorPercent = (uint)(((long)dpiX * 100 + (96 >> 1)) / 96);
        return scaleFactorPercent / 100.0;
    }

    void SetDragRegionForCustomTitleBar(AppWindow appWindow)
    {
        if (AppWindowTitleBar.IsCustomizationSupported()
            && appWindow.TitleBar.ExtendsContentIntoTitleBar)
        {
            double scaleAdjustment = GetScaleAdjustment();

            RightPaddingColumn.Width = new GridLength(
                appWindow.TitleBar.RightInset / scaleAdjustment);
            LeftPaddingColumn.Width = new GridLength(
                appWindow.TitleBar.LeftInset / scaleAdjustment);

            List<Windows.Graphics.RectInt32> dragRectsList = new();

            Windows.Graphics.RectInt32 dragRectL;
            dragRectL.X = (int)((LeftPaddingColumn.ActualWidth) * scaleAdjustment);
            dragRectL.Y = 0;
            dragRectL.Height = (int)(AppTitleBar.ActualHeight * scaleAdjustment);
            dragRectL.Width = (int)((IconColumn.ActualWidth
                                    + TitleColumn.ActualWidth
                                    + LeftDragColumn.ActualWidth) * scaleAdjustment);
            dragRectsList.Add(dragRectL);

            Windows.Graphics.RectInt32 dragRectR;
            dragRectR.X = (int)((LeftPaddingColumn.ActualWidth
                                + IconColumn.ActualWidth
                                + TitleTextBlock.ActualWidth
                                + LeftDragColumn.ActualWidth
                                + SearchColumn.ActualWidth) * scaleAdjustment);
            dragRectR.Y = 0;
            dragRectR.Height = (int)(AppTitleBar.ActualHeight * scaleAdjustment);
            dragRectR.Width = (int)(RightDragColumn.ActualWidth * scaleAdjustment);
            dragRectsList.Add(dragRectR);

            Windows.Graphics.RectInt32[] dragRects = dragRectsList.ToArray();

            appWindow.TitleBar.SetDragRectangles(dragRects);
        }
    }

    bool TrySetSystemBackdrop()
    {
        if (MicaController.IsSupported())
        {
            _wsdqHelper = new WindowsSystemDispatcherQueueHelper();
            _wsdqHelper.EnsureWindowsSystemDispatcherQueueController();

            // Create the policy object.
            _configurationSource = new SystemBackdropConfiguration();
            this.Activated += Window_Activated;
            this.Closed += Window_Closed;
            ((FrameworkElement)this.Content).ActualThemeChanged += Window_ThemeChanged;

            // Initial configuration state.
            _configurationSource.IsInputActive = true;
            SetConfigurationSourceTheme();

            _backdropController = new MicaController();

            // Enable the system backdrop.
            // Note: Be sure to have "using WinRT;" to support the Window.As<...>() call.
            _backdropController.AddSystemBackdropTarget(
                this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
            _backdropController.SetSystemBackdropConfiguration(_configurationSource);
            return true; // succeeded
        }

        return false; // Mica is not supported on this system
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        _configurationSource.IsInputActive =
            args.WindowActivationState != WindowActivationState.Deactivated;
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        // Make sure any Mica/Acrylic controller is disposed
        // so it doesn't try to use this closed window.
        if (_backdropController != null)
        {
            _backdropController.Dispose();
            _backdropController = null;
        }
        this.Activated -= Window_Activated;
        _configurationSource = null;
    }

    private void Window_ThemeChanged(FrameworkElement sender, object args)
    {
        if (_configurationSource != null)
        {
            SetConfigurationSourceTheme();
        }
    }

    private void SetConfigurationSourceTheme()
    {
        switch (((FrameworkElement)this.Content).ActualTheme)
        {
            case ElementTheme.Dark:
                _configurationSource.Theme = SystemBackdropTheme.Dark; break;
            case ElementTheme.Light:
                _configurationSource.Theme = SystemBackdropTheme.Light; break;
            case ElementTheme.Default:
                _configurationSource.Theme = SystemBackdropTheme.Default; break;
        }
    }
}

class WindowsSystemDispatcherQueueHelper
{
    [StructLayout(LayoutKind.Sequential)]
    struct DispatcherQueueOptions
    {
        internal int dwSize;
        internal int threadType;
        internal int apartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        [In] DispatcherQueueOptions options,
        [In, Out, MarshalAs(UnmanagedType.IUnknown)] ref object dispatcherQueueController);

    object _dispatcherQueueController = null;

    public void EnsureWindowsSystemDispatcherQueueController()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() != null)
        {
            // one already exists, so we'll just use it.
            return;
        }

        if (_dispatcherQueueController == null)
        {
            DispatcherQueueOptions options;
            options.dwSize = Marshal.SizeOf(typeof(DispatcherQueueOptions));
            options.threadType = 2;    // DQTYPE_THREAD_CURRENT
            options.apartmentType = 2; // DQTAT_COM_STA

            CreateDispatcherQueueController(options, ref _dispatcherQueueController);
        }
    }
}