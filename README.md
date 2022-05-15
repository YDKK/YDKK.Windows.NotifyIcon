# YDKK.Windows.NotifyIcon

Windows Shell NotifyIcon Library

Confirmed to be usable from WinUI 3 applications.

[![#](https://img.shields.io/nuget/v/YDKK.Windows.NotifyIcon.svg)](https://www.nuget.org/packages/YDKK.Windows.NotifyIcon/)

## Usage

```cs
using YDKK.Windows;
```

### Show NotifyIcon in the task tray and receive mouse events

```cs
var icon = Icon.FromFile("path-to-icon.ico");
// You can specify null for second argument if the icon is not needed or for testing.
var notifyIcon = new NotifyIcon("Tooltip-Text", icon);
notifyIcon.LButtonDoubleClick += (args) =>
{
    Console.WriteLine("NotifyIcon double clicked!");
    Console.WriteLine($"Mouse cursor position: ({args.xPos}, {args.yPos})");
};
```

### Show context menu when NotifyIcon is right-clicked and receive results

At this time, the Win32 API is required to achieve this.

```cs
// Prepare popup menu
var exitCommandId = 0;
var exitLabel = "Exit";
var menu = PInvoke.CreatePopupMenu();
var info = new MENUITEMINFOW
{
    cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>(),
    fMask = MENU_ITEM_MASK.MIIM_ID | MENU_ITEM_MASK.MIIM_STRING,
    wID = exitCommandId,
    cch = (uint)(exitLabel.Length * sizeof(char)),
};
fixed (char* ptr = exitLabel)
{
    info.dwTypeData = ptr;
}

PInvoke.InsertMenuItem(PopupMenu, 0, true, &info);

// Show context menu
notifyIcon.RButtonUp += (args) =>
{
    var hWnd = (HWND)notifyIcon.WindowHandle;
    PInvoke.SetForegroundWindow(hWnd);
    PInvoke.TrackPopupMenuEx(PopupMenu, (uint)TRACK_POPUP_MENU_FLAGS.TPM_LEFTALIGN, args.xPos, args.yPos, hWnd);
};

// Receive results
NotifyIcon.MenuCommand += (args) =>
{
    switch (args)
    {
        case exitCommandId:
            Console.WriteLine("Exit menu command selected.")
            break;
    }
};

```

## Note

At this time, supported build platform is `x64` only.

## License

MIT
