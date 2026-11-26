using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NotepadApp;

internal static class TaskbarAppearance
{
    private static readonly Guid AppUserModelFormat = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private const uint RelaunchCommand = 2;
    private const uint RelaunchIcon = 3;
    private const uint RelaunchDisplayName = 4;
    private const uint AppUserModelId = 5;

    internal static void Apply(IntPtr window)
    {
        string? executable = Environment.ProcessPath;
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Taskbar.ico");
        if (executable is null || !File.Exists(icon)) return;

        string command = $"\"{executable}\"";
        if (string.Equals(Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            string? assembly = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(assembly)) return;
            command += $" \"{assembly}\"";
        }

        Guid interfaceId = typeof(IPropertyStore).GUID;
        int result = SHGetPropertyStoreForWindow(window, in interfaceId, out IPropertyStore store);
        if (result < 0)
        {
            Debug.WriteLine($"Could not open taskbar properties: 0x{result:X8}");
            return;
        }

        try
        {
            SetString(store, RelaunchCommand, command);
            SetString(store, RelaunchDisplayName, "NotepadApp");
            SetString(store, RelaunchIcon, $"{icon},0");
            // Setting the ID last notifies the shell that the relaunch information is ready.
            SetString(store, AppUserModelId, "NotepadApp.Desktop");
        }
        catch (COMException error)
        {
            Debug.WriteLine(error);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static void SetString(IPropertyStore store, uint id, string text)
    {
        var key = new PropertyKey { Format = AppUserModelFormat, Id = id };
        var value = new PropertyValue { Type = (ushort)VarEnum.VT_LPWSTR, String = Marshal.StringToCoTaskMemUni(text) };
        try
        {
            store.SetValue(in key, in value);
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.String);
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr window, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyValue
    {
        public ushort Type;
        private ushort reserved1;
        private ushort reserved2;
        private ushort reserved3;
        public IntPtr String;
        // The native union also holds two pointer-sized fields on a 64-bit process.
        private IntPtr unionTail;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(in PropertyKey key, out PropertyValue value);
        void SetValue(in PropertyKey key, in PropertyValue value);
    }
}
