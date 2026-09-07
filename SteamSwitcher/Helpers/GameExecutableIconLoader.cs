using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SteamSwitcher.Helpers;

public static class GameExecutableIconLoader
{
    public static Task<ImageSource?> LoadAsync(string executable) => Task.Run<ImageSource?>(() =>
    {
        var info = new ShellFileInfo();
        try
        {
            if (SHGetFileInfo(executable, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x101) == IntPtr.Zero
                || info.Icon == IntPtr.Zero) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameIcon] {ex.Message}");
            return null;
        }
        finally { if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon); }
    });

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
