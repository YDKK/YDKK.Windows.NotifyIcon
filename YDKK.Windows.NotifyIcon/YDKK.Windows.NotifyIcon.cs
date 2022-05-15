using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32.UI.Shell;
using Microsoft.Win32.SafeHandles;

namespace YDKK.Windows
{
    public class Icon
    {
        internal readonly SafeFileHandle Handle;

        private Icon(SafeFileHandle handle)
        {
            Handle = handle;
        }

        public static Icon FromFile(string file)
        {
            var ext = Path.GetExtension(file).ToLower();
            var type = ext switch
            {
                ".ico" => GDI_IMAGE_TYPE.IMAGE_ICON,
                ".bmp" => GDI_IMAGE_TYPE.IMAGE_BITMAP,
                ".cur" => GDI_IMAGE_TYPE.IMAGE_CURSOR,
                _ => throw new ArgumentException("Unsupported file type"),
            };

            var handle = PInvoke.LoadImage(null, file, type, 0, 0, IMAGE_FLAGS.LR_LOADFROMFILE);

            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                throw new Exception($"LoadImage() result is null, code: {error}");
            }

            return new Icon(handle);
        }
    }

    public class NotifyIcon : IDisposable
    {
        //タスクバーが再作成された時の通知
        //https://docs.microsoft.com/en-us/windows/win32/shell/taskbar#taskbar-creation-notification
        private static readonly uint TaskbarRestartMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");
        private const uint CallbackMessageId = 0x400;

        private HWND hWnd;
        private WNDCLASSW wc;
        private NOTIFYICONDATAW NotifyIconData;
        private readonly Icon Icon;

        private bool IsDisposed = false;

        private readonly Dictionary<uint, Action<(int xPos, int yPos)>> ButtonActions = new();

        public event Action<(int xPos, int yPos)> LButtonDoubleClick;
        public event Action<(int xPos, int yPos)> LButtonDown;
        public event Action<(int xPos, int yPos)> LButtonUp;
        public event Action<(int xPos, int yPos)> MButtonDoubleClick;
        public event Action<(int xPos, int yPos)> MButtonDown;
        public event Action<(int xPos, int yPos)> MButtonUp;
        public event Action<(int xPos, int yPos)> RButtonDoubleClick;
        public event Action<(int xPos, int yPos)> RButtonDown;
        public event Action<(int xPos, int yPos)> RButtonUp;

        public event Action<uint> MenuCommand;

        public IntPtr WindowHandle => hWnd;

        public NotifyIcon(string tooltip, Icon icon)
        {
            Icon = icon;
            InitializeButtonActions();

            CreateMessageWindow();

            tooltip += '\0';
            var szTip = new NOTIFYICONDATAW.__char_128();
            tooltip.CopyTo(szTip.AsSpan());

            NotifyIconData = new NOTIFYICONDATAW()
            {
                Anonymous = new NOTIFYICONDATAW._Anonymous_e__Union
                {
                    uVersion = PInvoke.NOTIFYICON_VERSION_4
                },
                hWnd = hWnd,
                szTip = szTip,
                hIcon = Icon != null ? (HICON)Icon.Handle.DangerousGetHandle() : default,
                uCallbackMessage = CallbackMessageId,
                uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | (Icon != null ? NOTIFY_ICON_DATA_FLAGS.NIF_ICON : 0),
            };

            AddNotifyIcon();
        }

        ~NotifyIcon()
        {
            Dispose();
        }

        private void InitializeButtonActions()
        {
            ButtonActions.Add(PInvoke.WM_LBUTTONDBLCLK, (args) => LButtonDoubleClick?.Invoke(args));
            ButtonActions.Add(PInvoke.WM_LBUTTONDOWN, (args) => LButtonDown?.Invoke(args));
            ButtonActions.Add(PInvoke.WM_LBUTTONUP, (args) => LButtonUp?.Invoke(args));
            ButtonActions.Add(PInvoke.WM_MBUTTONDBLCLK, (args) => MButtonDoubleClick?.Invoke(args));
            ButtonActions.Add(PInvoke.WM_MBUTTONDOWN, (args) => MButtonDown?.Invoke(args));
            ButtonActions.Add(PInvoke.WM_MBUTTONUP, (args) => MButtonUp?.Invoke(args));
            ButtonActions.Add(PInvoke.WM_RBUTTONDBLCLK, (args) => RButtonDoubleClick?.Invoke(args));
            ButtonActions.Add(PInvoke.WM_RBUTTONDOWN, (args) => RButtonDown?.Invoke(args));
            ButtonActions.Add(PInvoke.WM_RBUTTONUP, (args) => RButtonUp?.Invoke(args));
        }

        private unsafe void CreateMessageWindow()
        {
            var id = $"YDKK_Windows_NotifyIcon_{Guid.NewGuid()}";

            wc = new WNDCLASSW
            {
                lpfnWndProc = Wndproc,
            };
            fixed (char* idPtr = id)
            {
                wc.lpszClassName = idPtr;
            }

            var result = PInvoke.RegisterClass(wc);

            if (result == 0)
            {
                var error = Marshal.GetLastWin32Error();
                throw new Exception($"RegisterClass() result is null, code: {error}");
            }

            hWnd = PInvoke.CreateWindowEx(0, id, "", 0, 0, 0, 0, 0, PInvoke.HWND_MESSAGE, null, null, null);

            if (hWnd == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                throw new Exception($"CreateWindowEx() result is null, code: {error}");
            }
        }

        #region Macros
        private int GET_X_LPARAM(LPARAM param) => unchecked((short)(long)param.Value);
        private int GET_Y_LPARAM(LPARAM param) => unchecked((short)((long)param.Value >> 16));
        private int GET_X_LPARAM(WPARAM param) => unchecked((short)(long)param.Value);
        private int GET_Y_LPARAM(WPARAM param) => unchecked((short)((long)param.Value >> 16));
        private uint LOWORD(LPARAM param) => unchecked((ushort)(long)param.Value);
        private uint HIWORD(LPARAM param) => unchecked((ushort)((long)param.Value >> 16));
        private uint LOWORD(WPARAM param) => unchecked((ushort)(long)param.Value);
        private uint HIWORD(WPARAM param) => unchecked((ushort)((long)param.Value >> 16));
        #endregion

        private LRESULT Wndproc(HWND hWnd, uint uMsg, WPARAM wParam, LPARAM lParam)
        {
            switch (uMsg)
            {
                case uint when uMsg == TaskbarRestartMessage:
                    {
                        AddNotifyIcon();
                    }
                    break;
                case uint when uMsg == CallbackMessageId:
                    {
                        var messageId = LOWORD(lParam);
                        if (ButtonActions.ContainsKey(messageId))
                        {
                            var xPos = GET_X_LPARAM(wParam);
                            var yPos = GET_Y_LPARAM(wParam);
                            ButtonActions[messageId]((xPos, yPos));
                        }
                    }
                    break;
                case uint when uMsg == PInvoke.WM_COMMAND:
                    {
                        var menuId = LOWORD(wParam);
                        MenuCommand?.Invoke(menuId);
                    }
                    break;
                case uint when uMsg == PInvoke.WM_CLOSE:
                    {
                        PInvoke.DestroyWindow(hWnd);
                    }
                    break;
                case uint when uMsg == PInvoke.WM_DESTROY:
                    {
                        DeleteNotifyIcon();
                        PInvoke.PostQuitMessage(0);
                    }
                    break;
            }

            return PInvoke.DefWindowProc(hWnd, uMsg, wParam, lParam);
        }

        private void AddNotifyIcon()
        {
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, NotifyIconData);
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, NotifyIconData);
        }

        private void DeleteNotifyIcon()
        {
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, NotifyIconData);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;

            DeleteNotifyIcon();
            PInvoke.DestroyWindow(hWnd);

            GC.SuppressFinalize(this);
        }
    }
}
