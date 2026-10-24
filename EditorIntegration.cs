using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Editor_Reader;

namespace OsuPatternCopier;

internal static class EditorIntegration
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    public static bool IsCtrlVDown =>
        (GetAsyncKeyState(0x11) & 0x8000) != 0 && (GetAsyncKeyState(0x56) & 0x8000) != 0;

    public static bool IsCtrlCDown =>
        (GetAsyncKeyState(0x11) & 0x8000) != 0 && (GetAsyncKeyState(0x43) & 0x8000) != 0;

    public static EditorState? TryReadActive(out string? error)
    {
        error = null;
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            error = "没有前台窗口。";
            return null;
        }

        try
        {
            var processId = GetWindowProcessId(window);
            using var process = Process.GetProcessById(processId);
            if (!process.ProcessName.Equals("osu!", StringComparison.OrdinalIgnoreCase))
            {
                error = $"前台窗口不是 osu!，而是 {process.ProcessName}。";
                return null;
            }

            var reader = new EditorReader();
            reader.FetchEditor();
            reader.FetchHOM();
            reader.FetchBeatmap();
            reader.FetchClipboard();
            var clipboardLines = reader.clipboardObjects
                .Take(reader.numClipboard)
                .Select(x => x.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            var path = ResolveBeatmapPath(reader, process);
            return new EditorState(
                process.Id,
                path,
                reader.EditorTime(),
                clipboardLines);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    private static string ResolveBeatmapPath(EditorReader reader, Process process)
    {
            var path = Path.Combine(reader.ContainingFolder ?? string.Empty, reader.Filename ?? string.Empty);
            if (File.Exists(path))
                return Path.GetFullPath(path);

            var filename = Path.GetFileName(reader.Filename);
            if (string.IsNullOrWhiteSpace(filename))
                return Path.GetFullPath(path);

            var roots = new List<string>();
            try
            {
                var executableDirectory = Path.GetDirectoryName(process.MainModule?.FileName);
                if (!string.IsNullOrWhiteSpace(executableDirectory))
                {
                    roots.Add(executableDirectory);
                    var parent = Directory.GetParent(executableDirectory)?.FullName;
                    if (!string.IsNullOrWhiteSpace(parent))
                        roots.Add(parent);
                }
            }
            catch (Win32Exception)
            {
            }

            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var match = Directory.EnumerateFiles(root, filename, SearchOption.AllDirectories).FirstOrDefault();
                    if (match is not null)
                        return match;
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }

            return Path.GetFullPath(path);
        }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private static int GetWindowProcessId(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return checked((int)processId);
    }

}

internal sealed record EditorState(int ProcessId, string Path, int Time, IReadOnlyList<string> ClipboardLines);
