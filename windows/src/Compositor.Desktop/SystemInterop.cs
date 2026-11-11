using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Compositor.Desktop;

/// <summary>General preferences' Beep When Done: the system's own "done" sound.</summary>
internal static class SystemSound
{
    [DllImport("user32.dll")] private static extern bool MessageBeep(uint type);
    internal static void Done() { if (OperatingSystem.IsWindows()) MessageBeep(0); }
}

/// <summary>General preferences' Color Picker set to Windows: the system color dialog in place of Compositor's own.</summary>
internal static class WindowsColorDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ChooseColorInfo
    {
        public int Size; public IntPtr Owner, Instance; public int Result; public IntPtr Custom; public int Flags; public IntPtr Data, Hook; public string? Template;
    }
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChooseColorW")] private static extern bool ChooseColor(ref ChooseColorInfo info);
    private static readonly int[] Custom = new int[16];
    internal static bool Chosen => EditorPreferences.Current.ColorPicker == 1 && OperatingSystem.IsWindows();
    /// <summary>The color picked, or null when the dialog was cancelled.</summary>
    internal static (double Red, double Green, double Blue)? Pick(Window owner, (double Red, double Green, double Blue) start)
    {
        static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        var custom = GCHandle.Alloc(Custom, GCHandleType.Pinned);
        try
        {
            var info = new ChooseColorInfo
            {
                Size = Marshal.SizeOf<ChooseColorInfo>(), Owner = owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, Custom = custom.AddrOfPinnedObject(),
                Result = Byte(start.Red) | Byte(start.Green) << 8 | Byte(start.Blue) << 16, Flags = 0x1 | 0x2,
            };
            if (!ChooseColor(ref info)) return null;
            return ((info.Result & 0xFF) / 255d, (info.Result >> 8 & 0xFF) / 255d, (info.Result >> 16 & 0xFF) / 255d);
        }
        finally { custom.Free(); }
    }
}

/// <summary>What Performance preferences report about the machine.</summary>
internal static class MachineInfo
{
    /// <summary>
    /// The display adapter with the most dedicated memory, as Photoshop's "Detected Graphics Processor" names it: a virtual
    /// display driver, which reports none, only when it is all there is.
    /// </summary>
    internal static string GraphicsProcessor()
    {
        if (!OperatingSystem.IsWindows()) return "";
        try
        {
            using var adapters = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (adapters is null) return "";
            var best = ""; long most = -1;
            foreach (var name in adapters.GetSubKeyNames().Where(n => n.All(char.IsDigit)).Order())
            {
                using var adapter = adapters.OpenSubKey(name);
                if (adapter?.GetValue("DriverDesc") is not string description || description.Length == 0) continue;
                var memory = adapter.GetValue("HardwareInformation.qwMemorySize") switch { long q => q, byte[] { Length: >= 8 } b => BitConverter.ToInt64(b), _ => 0L };
                if (memory == 0) memory = adapter.GetValue("HardwareInformation.MemorySize") switch { int d => (uint)d, byte[] { Length: >= 4 } b => BitConverter.ToUInt32(b), _ => 0L };
                if (memory <= most) continue;
                most = memory;
                var version = adapter.GetValue("DriverVersion") is string v ? $" ({v})" : "";
                best = description + version + (memory > 0 ? $" - {memory / 1073741824d:0.#} GB" : "");
            }
            return best;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return "";
    }
    /// <summary>The memory the machine has, in megabytes.</summary>
    internal static long MemoryMB => Math.Max(1, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576);
}
