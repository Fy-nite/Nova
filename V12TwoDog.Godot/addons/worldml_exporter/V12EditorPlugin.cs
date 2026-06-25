#if TOOLS
using Godot;

namespace V12TwoDog.Editor;

[Tool]
public partial class V12EditorPlugin : EditorPlugin
{
    private const string CacheFileName = "user://v12_export_cache.cfg";
    private EditorDock _dock;

    public override void _EnterTree()
    {
        _dock = new EditorDock();
        _dock.DefaultSlot = (EditorDock.DockSlot)DockSlot.Bottom;

        var hb = new HBoxContainer();
        var exportBtn = new Button { Text = "Export WorldML" };
        exportBtn.Pressed += OnExportPressed;
        hb.AddChild(exportBtn);

        var exportAsBtn = new Button { Text = "Export As..." };
        exportAsBtn.Pressed += OnExportAsPressed;
        hb.AddChild(exportAsBtn);

        var openBtn = new Button { Text = "Open Output" };
        openBtn.Pressed += () =>
        {
            var path = GetCachedPath();
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                OS.ShellShowInFileManager(path);
        };
        hb.AddChild(openBtn);

        _dock.AddChild(hb);
        AddDock(_dock);
    }

    public override void _ExitTree()
    {
        RemoveDock(_dock);
        _dock?.Free();
    }

    private static string CacheRealPath =>
        ProjectSettings.GlobalizePath(CacheFileName);

    private string GetCachedPath()
    {
        var p = CacheRealPath;
        return System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p).Trim() : "";
    }

    private void SetCachedPath(string path)
    {
        System.IO.File.WriteAllText(CacheRealPath, path);
    }

    private static Node GetSceneRoot()
    {
        return EditorInterface.Singleton.GetEditedSceneRoot();
    }

    private void OnExportPressed()
    {
        var root = GetSceneRoot();
        if (root == null)
        {
            ShowDialog("Open a scene to export.");
            return;
        }

        var lastPath = GetCachedPath();
        if (!string.IsNullOrEmpty(lastPath) && System.IO.File.Exists(lastPath))
            DoExport(root, lastPath);
        else
            ShowExportDialog(root);
    }

    private void OnExportAsPressed()
    {
        var root = GetSceneRoot();
        if (root == null)
        {
            ShowDialog("Open a scene to export.");
            return;
        }
        ShowExportDialog(root);
    }

    private void ShowExportDialog(Node root)
    {
        var dialog = new FileDialog();
        dialog.FileMode = FileDialog.FileModeEnum.SaveFile;
        dialog.Access = FileDialog.AccessEnum.Filesystem;
        dialog.AddFilter("*.V12World", "V12 World Archive");
        dialog.AddFilter("*.xml", "WorldML XML");

        var lastPath = GetCachedPath();
        if (!string.IsNullOrEmpty(lastPath))
            dialog.CurrentPath = lastPath;

        dialog.FileSelected += (path) =>
        {
            SetCachedPath(path);
            DoExport(root, path);
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;

        _dock.AddChild(dialog);
        dialog.PopupCentered(new Vector2I(700, 500));
    }

    private void DoExport(Node root, string path)
    {
        if (path.EndsWith(".V12World", System.StringComparison.OrdinalIgnoreCase))
            V12WorldPacker.PackWorld(root, path);
        else
        {
            var worldName = System.IO.Path.GetFileNameWithoutExtension(path);
            System.IO.File.WriteAllText(path, WorldMLExporter.ExportWorld(root, worldName));
        }

        SetCachedPath(path);
        ShowDialog($"Exported to:\n{path}");
    }

    private void ShowDialog(string text)
    {
        var d = new AcceptDialog();
        d.DialogText = text;
        d.CloseRequested += d.QueueFree;
        _dock.AddChild(d);
        d.PopupCentered(new Vector2I(500, 160));
    }
}
#endif
