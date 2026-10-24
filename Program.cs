using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OsuPatternCopier;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon notifyIcon;
    private readonly System.Windows.Forms.Timer clipboardTimer;
    private readonly ToolStripMenuItem sourceItem;
    private readonly ToolStripMenuItem targetItem;
    private string? sourcePath;
    private string? targetPath;
    private string? lastClipboardText;
    private string? lastClipboardDiagnostic;
    private OsuClipboard? pendingCopy;
    private IReadOnlyList<string>? pendingObjects;
    private bool wasCtrlCDown;
    private bool wasCtrlVDown;
    private bool pasteQueued;
    private EditorState? queuedTarget;

    public TrayApplicationContext()
    {
        sourceItem = new ToolStripMenuItem("选择源谱面...", null, SelectSource);
        targetItem = new ToolStripMenuItem("选择目标谱面...", null, SelectTarget);
        var menu = new ContextMenuStrip();
        menu.Items.Add(sourceItem);
        menu.Items.Add(targetItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("恢复目标谱面备份...", null, RestoreTarget);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "osu_patterncopier",
            Visible = true,
            ContextMenuStrip = menu
        };
        notifyIcon.DoubleClick += (_, _) => SelectSource(this, EventArgs.Empty);

        clipboardTimer = new System.Windows.Forms.Timer { Interval = 120 };
        clipboardTimer.Tick += ClipboardTimerOnTick;
        clipboardTimer.Start();
        notifyIcon.ShowBalloonTip(2500, "osu_patterncopier 已启动",
            "请先保存目标谱面，再在目标位置按 Ctrl+V。本程序不会模拟键盘输入。", ToolTipIcon.Info);
    }

    private void SelectSource(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "osu! beatmap (*.osu)|*.osu|所有文件 (*.*)|*.*",
            Title = "选择复制来源的 .osu 谱面",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        sourcePath = dialog.FileName;
        sourceItem.Text = $"源谱面：{Path.GetFileName(sourcePath)}";
        notifyIcon.ShowBalloonTip(1800, "源谱面已选择", sourcePath, ToolTipIcon.Info);
    }

    private void SelectTarget(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "osu! beatmap (*.osu)|*.osu|所有文件 (*.*)|*.*",
            Title = "选择要写入的目标 .osu 谱面",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        targetPath = dialog.FileName;
        targetItem.Text = $"目标谱面：{Path.GetFileName(targetPath)}";
        notifyIcon.ShowBalloonTip(1800, "目标谱面已选择", targetPath, ToolTipIcon.Info);
    }

    private void RestoreTarget(object? sender, EventArgs e)
    {
        if (targetPath is null)
        {
            notifyIcon.ShowBalloonTip(2200, "无法恢复备份",
                "尚未选择目标谱面。请先选择目标 .osu 文件。", ToolTipIcon.Warning);
            return;
        }

        var backupPath = targetPath + ".slidercopier.bak";
        if (!File.Exists(backupPath))
        {
            notifyIcon.ShowBalloonTip(2200, "无法恢复备份",
                $"未找到备份文件：{backupPath}", ToolTipIcon.Warning);
            return;
        }

        File.Copy(backupPath, targetPath, overwrite: true);
        notifyIcon.ShowBalloonTip(2200, "目标谱面已恢复",
            "请在 osu! 编辑器中手动按 Ctrl+L 重新载入。", ToolTipIcon.Info);
    }

    private void ClipboardTimerOnTick(object? sender, EventArgs e)
    {
        var ctrlCDown = EditorIntegration.IsCtrlCDown;
        string? text;
        try
        {
            if (!Clipboard.ContainsText())
            {
                CaptureCopyIfPressed(ctrlCDown, null);
                HandlePasteShortcut();
                return;
            }
            text = Clipboard.GetText();
        }
        catch (ExternalException)
        {
            CaptureCopyIfPressed(ctrlCDown, null);
            HandlePasteShortcut();
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            CaptureCopyIfPressed(ctrlCDown, text);
            HandlePasteShortcut();
            return;
        }

        if (!OsuClipboard.TryParse(text, out var copied))
        {
            lastClipboardText = text;
            CaptureCopyIfPressed(ctrlCDown, text);
            HandlePasteShortcut();
            return;
        }

        var newCopyShortcut = ctrlCDown && !wasCtrlCDown;
        if (text != lastClipboardText || newCopyShortcut)
            CaptureCopy(copied, ctrlCDown);
        lastClipboardText = text;
        wasCtrlCDown = ctrlCDown;
        HandlePasteShortcut();
    }

    private void CaptureCopyIfPressed(bool ctrlCDown, string? clipboardText)
    {
        if (ctrlCDown && !wasCtrlCDown && clipboardText is not null &&
            OsuClipboard.TryParse(clipboardText, out var copied))
            CaptureCopy(copied);
        wasCtrlCDown = ctrlCDown;
    }

    private void CaptureCopy(OsuClipboard copied, bool ctrlCDown = false)
    {
        var sourceEditor = EditorIntegration.TryReadActive(out var error);
        if (sourceEditor is null)
        {
            if (error != lastClipboardDiagnostic)
                notifyIcon.ShowBalloonTip(3000, "无法读取源编辑器",
                    error ?? "未能获取当前 osu! 编辑器信息。", ToolTipIcon.Warning);
            lastClipboardDiagnostic = error;
            return;
        }

        var unchanged = pendingCopy is not null
            && pendingCopy.Time == copied.Time
            && pendingCopy.Indices.SequenceEqual(copied.Indices)
            && string.Equals(sourcePath, sourceEditor.Path, StringComparison.OrdinalIgnoreCase);
        pendingCopy = copied;
        sourcePath = sourceEditor.Path;
        pendingObjects = sourceEditor.ClipboardLines;
        if (!unchanged)
            notifyIcon.ShowBalloonTip(1200, "源对象已记录",
                $"谱面：{Path.GetFileName(sourcePath)}；对象数量：{copied.Indices.Count}。", ToolTipIcon.Info);
    }

    private void HandlePasteShortcut()
    {
        var ctrlVDown = EditorIntegration.IsCtrlVDown;
        if (ctrlVDown && !wasCtrlVDown)
        {
            queuedTarget = EditorIntegration.TryReadActive(out var error);
            if (queuedTarget is null && error != lastClipboardDiagnostic)
            {
                notifyIcon.ShowBalloonTip(3000, "无法读取目标编辑器",
                    error ?? "未能获取当前 osu! 编辑器信息。", ToolTipIcon.Warning);
                lastClipboardDiagnostic = error;
            }
            pasteQueued = queuedTarget is not null && pendingCopy is not null && sourcePath is not null;
        }
        wasCtrlVDown = ctrlVDown;
        if (!pasteQueued || ctrlVDown)
            return;

        pasteQueued = false;
        var targetEditor = queuedTarget;
        queuedTarget = null;
        if (targetEditor is null || pendingCopy is null || sourcePath is null)
            return;

        targetPath = targetEditor.Path;
        try
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("源谱面文件不存在。", sourcePath);
            if (!File.Exists(targetPath))
                throw new FileNotFoundException("目标谱面文件不存在。", targetPath);

            var selected = pendingObjects is { Count: > 0 }
                ? OsuFile.ParseObjects(pendingObjects)
                : Array.Empty<OsuHitObject>();
            if (selected.Count == 0)
                selected = pendingCopy.Select(OsuFile.ReadHitObjects(sourcePath));
            if (selected.Count == 0)
            {
                notifyIcon.ShowBalloonTip(2500, "未找到源对象",
                    "源谱面中没有找到剪贴板所引用的对象。", ToolTipIcon.Warning);
                return;
            }

            var delta = targetEditor.Time - selected.Min(x => x.Time);
            var translated = selected.Select(x => x.WithTime(x.Time + delta)).ToArray();
            OsuFile.AppendObjects(targetPath, translated);
            notifyIcon.ShowBalloonTip(3000, "跨谱复制完成",
                $"已写入 {selected.Count} 个对象，目标时间：{targetEditor.Time} ms。请在 osu! 中手动按 Ctrl+L 重新载入。", ToolTipIcon.Info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            notifyIcon.ShowBalloonTip(5000, "跨谱复制失败",
                $"{ex.Message}\n源谱面：{sourcePath}\n目标谱面：{targetPath}", ToolTipIcon.Error);
        }
        pendingCopy = null;
        pendingObjects = null;
    }

    protected override void ExitThreadCore()
    {
        clipboardTimer.Stop();
        clipboardTimer.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed record OsuClipboard(int Time, IReadOnlyList<int> Indices)
{
    private static readonly Regex Pattern = new(
        @"^(?<minutes>\d{1,3}):(?<seconds>\d{2}):(?<milliseconds>\d{3})(?:\s+\((?<indices>[^)]*)\))?",
        RegexOptions.Compiled);

    public static bool TryParse(string text, out OsuClipboard result)
    {
        var match = Pattern.Match(text.Trim());
        if (!match.Success)
        {
            result = null!;
            return false;
        }

        var time = int.Parse(match.Groups["minutes"].Value) * 60_000
            + int.Parse(match.Groups["seconds"].Value) * 1_000
            + int.Parse(match.Groups["milliseconds"].Value);
        var indices = match.Groups["indices"].Success
            ? match.Groups["indices"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => int.TryParse(value, out _))
                .Select(int.Parse)
                .ToArray()
            : Array.Empty<int>();
        result = new OsuClipboard(time, indices);
        return true;
    }

    public IReadOnlyList<OsuHitObject> Select(IReadOnlyList<OsuHitObject> objects)
    {
        var atTime = objects.Where(x => x.Time == Time).ToArray();
        if (Indices.Count == 0)
            return atTime.Where(x => x.IsSlider).DefaultIfEmpty(atTime.FirstOrDefault()).Where(x => x is not null).ToArray()!;

        var selected = objects
            .Where(x => Indices.Contains(x.ComboNumber))
            .ToArray();
        return selected.Length > 0 ? selected : atTime;
    }
}

internal sealed record OsuHitObject(int Time, bool IsSlider, int ComboNumber, string RawLine)
{
    public OsuHitObject WithTime(int time)
    {
        var fields = RawLine.Split(',');
        fields[2] = time.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return this with { Time = time, RawLine = string.Join(',', fields) };
    }
}

internal static class OsuFile
{
    public static IReadOnlyList<OsuHitObject> ParseObjects(IEnumerable<string> lines)
    {
        var result = new List<OsuHitObject>();
        foreach (var line in lines)
        {
            var fields = line.Split(',');
            if (fields.Length >= 5 && int.TryParse(fields[2], out var time) &&
                int.TryParse(fields[3], out var type))
                result.Add(new OsuHitObject(time, (type & 2) != 0, 0, line));
        }
        return result;
    }

    public static IReadOnlyList<OsuHitObject> ReadHitObjects(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var inHitObjects = false;
        var result = new List<OsuHitObject>();
        var comboNumber = 0;
        foreach (var line in lines)
        {
            if (line.Equals("[HitObjects]", StringComparison.OrdinalIgnoreCase))
            {
                inHitObjects = true;
                continue;
            }
            if (inHitObjects && line.StartsWith('['))
                break;
            if (!inHitObjects || string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                continue;

            var fields = line.Split(',');
            if (fields.Length < 5 || !int.TryParse(fields[2], out var time) ||
                !int.TryParse(fields[3], out var type))
                continue;
            if (result.Count == 0 || (type & 4) != 0)
                comboNumber++;
            result.Add(new OsuHitObject(time, (type & 2) != 0, comboNumber, line));
        }
        return result;
    }

    public static void AppendObjects(string path, IReadOnlyList<OsuHitObject> objects)
    {
        var backupPath = path + ".slidercopier.bak";
        if (!File.Exists(backupPath))
            File.Copy(path, backupPath);

        var content = File.ReadAllText(path, Encoding.UTF8);
        var additions = objects
            .Select(x => x.RawLine)
            .Where(line => !content.Contains(line, StringComparison.Ordinal))
            .ToArray();
        if (additions.Length == 0)
            return;

        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var marker = content.IndexOf("[HitObjects]", StringComparison.OrdinalIgnoreCase);
        var insertAt = content.Length;
        if (marker >= 0)
        {
            var sectionEnd = content.IndexOf('[', marker + 1);
            insertAt = sectionEnd >= 0 ? sectionEnd : content.Length;
        }
        var before = content[..insertAt].TrimEnd('\r', '\n');
        var after = content[insertAt..];
        var updated = before + newline + string.Join(newline, additions) + newline + after;
        File.WriteAllText(path, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
