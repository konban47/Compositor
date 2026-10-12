using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace Compositor.Desktop;

/// <summary>Windows Spell Checking API, using the installed language dictionaries (spellcheck.h).</summary>
internal static class WindowsSpelling
{
    internal sealed record Issue(int Start, int Length, string Word, string[] Suggestions);
    internal static IReadOnlyList<Issue> Check(string text, string language)
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("Windows spelling dictionaries are required.");
        return CheckWindows(text, language);
    }
    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<Issue> CheckWindows(string text, string language)
    {
        var type = Type.GetTypeFromCLSID(new Guid("7ab36653-1796-484b-bdfa-e74f1db7c1dc"), true)!;
        var factory = (IFactory)Activator.CreateInstance(type)!;
        try
        {
            if (!factory.IsSupported(language)) throw new NotSupportedException("Install the Windows spelling dictionary for this language.");
            var checker = factory.Create(language);
            try
            {
                var errors = checker.Check(text);
                try
                {
                    var result = new List<Issue>();
                    while (result.Count < 100 && errors.Next(out var error) == 0 && error is not null)
                    {
                        try
                        {
                            var start = checked((int)error.Start()); var length = checked((int)error.Length());
                            if (start < 0 || length < 0 || start + length > text.Length) continue;
                            var word = text.Substring(start, length); var suggestions = new List<string>();
                            if (error.Action() == 2) suggestions.Add(error.Replacement());
                            else if (error.Action() == 3) suggestions.Add("");
                            else
                            {
                                var words = checker.Suggest(word);
                                try { var item = new string[1]; while (suggestions.Count < 8 && words.Next(1, item, IntPtr.Zero) == 0) suggestions.Add(item[0]); }
                                finally { Marshal.ReleaseComObject(words); }
                            }
                            result.Add(new Issue(start, length, word, suggestions.ToArray()));
                        }
                        finally { Marshal.ReleaseComObject(error); }
                    }
                    return result;
                }
                finally { Marshal.ReleaseComObject(errors); }
            }
            finally { Marshal.ReleaseComObject(checker); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }
    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFactory
    {
        IEnumString Languages();
        [return: MarshalAs(UnmanagedType.Bool)] bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string language);
        IChecker Create([MarshalAs(UnmanagedType.LPWStr)] string language);
    }
    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IChecker
    {
        [return: MarshalAs(UnmanagedType.LPWStr)] string Language();
        IErrors Check([MarshalAs(UnmanagedType.LPWStr)] string text);
        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
    }
    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IErrors { [PreserveSig] int Next([MarshalAs(UnmanagedType.Interface)] out IError? error); }
    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IError
    {
        uint Start(); uint Length(); int Action();
        [return: MarshalAs(UnmanagedType.LPWStr)] string Replacement();
    }
}
