using Godot;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using V12.Basic.Components;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces;
using V12.Core.NetworkCable;
using V12.Core.Networking;
using V12.WorldML;

/// <summary>
/// Handles WorldSync, WorldArchive, and WorldUpdate network messages,
/// including world tree ID synchronisation and incremental component updates.
/// </summary>
public class WorldSyncHandler
{
    private readonly GameRoot _root;

    /// <summary>
    /// Archive-loaded world from a WorldArchive message. Used by WorldSync to
    /// provide full mesh data instead of relying on BSON serialization.
    /// </summary>
    private World _loadedArchiveWorld;

    public WorldSyncHandler(GameRoot root)
    {
        _root = root;
    }

    // ── WorldSync ───────────────────────────────────────────────────

    public void HandleWorldSync(MessageDTO message, bool debugMode)
    {
        var received = AncientCompressor.Decompress<World>(message.Message);

        // Preserve the local world by naming the server world differently
        var serverWorldName = $"Server_{received.WorldName}";

        // Remove old physics body references from the previous server world
        if (_root.SelectedWorld != null)
        {
            foreach (var el in _root.SelectedWorld.Root)
            {
                var pbc = el.GetComponent<PhysicsBodyComponent>();
                if (pbc != null) pbc.Body = null;
            }
        }

        World worldToUse;
        if (_loadedArchiveWorld != null)
        {
            _loadedArchiveWorld.WorldName = serverWorldName;
            SyncElementIds(received, _loadedArchiveWorld);
            worldToUse = _loadedArchiveWorld;
            GD.Print($"[Network] WorldSync: using locally-loaded archive world with synced IDs");
        }
        else
        {
            received.WorldName = serverWorldName;
            worldToUse = received;
            GD.Print($"[Network] WorldSync: using BSON world (no local archive available)");
        }

        // Find and untrack the old server world so DirtyTracker subscriptions
        // don't leak when we replace the element tree.
        var oldServerWorld = _root.Worlds.Find(w => w.WorldName == serverWorldName);
        var dt = _root.Registry.Get<DirtyTracker>("DirtyTracker");
        if (oldServerWorld != null)
            dt?.UntrackWorld(oldServerWorld);

        World selected;
        if (oldServerWorld != null)
        {
            oldServerWorld.ReplaceFrom(worldToUse);
            selected = oldServerWorld;
        }
        else
        {
            _root.Worlds.Add(worldToUse);
            selected = worldToUse;
        }
        _root.SelectWorld(selected);

        // Track the new world's elements for dirty-change propagation
        dt?.TrackWorld(selected);

        GD.Print($"[Network] WorldSync applied as '{serverWorldName}' ({selected.Root.Count} root elements). PersistentWorld (Player) untouched.");
    }

    // ── WorldArchive ────────────────────────────────────────────────

    public void HandleWorldArchive(MessageDTO message, bool debugMode)
    {
        try
        {
            var data = message.Message;
            int nameLen = BitConverter.ToInt32(data, 0);
            var fileName = Encoding.UTF8.GetString(data, 4, nameLen);
            var archiveBytes = data.AsSpan(4 + nameLen).ToArray();

            var worldName = Path.GetFileNameWithoutExtension(fileName);
            var tempDir = Path.Combine(Path.GetTempPath(), "V12Worlds", worldName + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var tempPath = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllBytes(tempPath, archiveBytes);
            ZipFile.ExtractToDirectory(tempPath, tempDir);
            File.Delete(tempPath);

            // Check if an AssetResolver already exists — if so, add a mount to it
            // instead of creating a new one (Registry.Register fails silently if name exists)
            var existingResolver = _root.Registry.Get<IAssetResolver>();
            if (existingResolver is V12AssetResolver vr)
            {
                vr.Mount(worldName, tempDir);
                GD.Print($"[Network] Added mount '{worldName}' → '{tempDir}' to existing AssetResolver");
            }
            else
            {
                var resolver = new V12AssetResolver();
                resolver.Mount(worldName, tempDir);
                _root.Registry.Register("AssetResolver", resolver);
                GD.Print($"[Network] Registered new AssetResolver with mount '{worldName}' → '{tempDir}'");
            }

            var templates = new WorldTemplateProvider();
            var templatesDir = Path.Combine(tempDir, "templates");
            if (Directory.Exists(templatesDir))
                templates.LoadFromDirectory(templatesDir);

            var existingTemplates = _root.Registry.Get<WorldTemplateProvider>();
            if (existingTemplates != null)
            {
                // Merge templates from the new archive
                if (Directory.Exists(templatesDir))
                    existingTemplates.LoadFromDirectory(templatesDir);
                GD.Print($"[Network] Merged templates into existing TemplateProvider");
            }
            else
            {
                _root.Registry.Register("TemplateProvider", templates);
                GD.Print($"[Network] Registered new TemplateProvider");
            }

            GD.Print($"[Network] V12World archive received: '{fileName}' ({archiveBytes.Length} bytes) → {tempDir}");

            // ── Also load the world from the extracted archive ──
            try
            {
                var worldXmlPath = Path.Combine(tempDir, "world.xml");
                if (!File.Exists(worldXmlPath))
                    worldXmlPath = Path.Combine(tempDir, "main.xml");
                if (File.Exists(worldXmlPath))
                {
                    var loadTemplates = existingTemplates ?? new WorldTemplateProvider();
                    var parser = new WorldMLParser { TemplateProvider = loadTemplates };
                    var parsedRoot = parser.ParseFile(worldXmlPath);
                    _loadedArchiveWorld = new World(parsedRoot.Name ?? worldName) { ExtractPath = tempDir, MountPoint = worldName };
                    _loadedArchiveWorld.AddElement(parsedRoot);
                    GD.Print($"[Network] Loaded world from archive XML: '{_loadedArchiveWorld.WorldName}' ({_loadedArchiveWorld.Root.Count} root elements, {CountElementsRecursive(_loadedArchiveWorld.Root)} total elements)");
                }
                else
                {
                    GD.PrintErr($"[Network] No world.xml or main.xml found in extracted archive at {tempDir}");
                    _loadedArchiveWorld = null;
                }
            }
            catch (Exception loadEx)
            {
                GD.PrintErr($"[Network] Failed to load world from archive: {loadEx.Message}");
                _loadedArchiveWorld = null;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Network] Error processing WorldArchive: {ex.Message}");
        }
    }

    // ── WorldUpdate ─────────────────────────────────────────────────

    public void HandleWorldUpdate(MessageDTO message)
    {
        ComponentBatchDTO batch = null;
        try { batch = AncientCompressor.Decompress<ComponentBatchDTO>(message.Message); }
        catch { }

        if (batch == null || batch.Components.Count == 0) return;

        lock (_root)
        {
            foreach (var snapshot in batch.Components)
            {
                if (snapshot.Payload == null || snapshot.Payload.Length == 0) continue;
                try
                {
                    var csDto = AncientCompressor.Decompress<ComponentSyncDTO>(snapshot.Payload);
                    var incoming = AncientCompressor.DecompressComponent(csDto);
                    if (incoming == null) continue;

                    ApplyComponentUpdateRecursive(incoming, snapshot.Id);
                }
                catch { }
            }
        }
    }

    // ── Helper methods ──────────────────────────────────────────────

    private void ApplyComponentUpdateRecursive(IComponent incoming, long targetId)
    {
        bool found = false;
        foreach (var w in _root.ActiveWorlds)
        {
            foreach (var element in w.Root)
            {
                if (TryApplyToElement(element, incoming, targetId))
                {
                    found = true;
                    break;
                }
            }
            if (found) break;
        }
    }

    private static bool TryApplyToElement(IWorldElement element, IComponent incoming, long targetId)
    {
        var target = element.Components.Find(c => c.Id == targetId);
        if (target != null)
        {
            foreach (var prop in incoming.GetType()
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (prop.Name == "Id" || !prop.CanRead || !prop.CanWrite) continue;
                try { prop.SetValue(target, prop.GetValue(incoming)); } catch { }
            }
            return true;
        }

        foreach (var child in element.Children)
        {
            if (TryApplyToElement(child, incoming, targetId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Copy server-authoritative element and component IDs from the BSON-serialized
    /// server world into the locally-loaded archive world, so future WorldUpdate
    /// patches match by ID. Elements are matched by name (recursively).
    /// </summary>
    private static void SyncElementIds(World serverWorld, World localWorld)
    {
        foreach (var serverRoot in serverWorld.Root)
        {
            foreach (var localRoot in localWorld.Root)
            {
                SyncElementIdsRecursive(serverRoot, localRoot);
            }
        }
    }

    private static void SyncElementIdsRecursive(IWorldElement serverEl, IWorldElement localEl)
    {
        if (!string.Equals(serverEl.Name, localEl.Name, StringComparison.OrdinalIgnoreCase))
            return;

        localEl.Id = serverEl.Id;

        foreach (var serverComp in serverEl.Components)
        {
            foreach (var localComp in localEl.Components)
            {
                if (localComp.GetType() == serverComp.GetType()
                    && string.Equals(localComp.Name ?? "", serverComp.Name ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    if (localComp is ComponentBase cb)
                        cb.Id = serverComp.Id;
                }
            }
        }

        foreach (var serverChild in serverEl.Children)
        {
            foreach (var localChild in localEl.Children)
            {
                SyncElementIdsRecursive(serverChild, localChild);
            }
        }
    }

    private static int CountElementsRecursive(List<IWorldElement> elements)
    {
        int count = 0;
        foreach (var el in elements)
        {
            count++;
            if (el.Children != null && el.Children.Count > 0)
                count += CountElementsRecursive(el.Children);
        }
        return count;
    }
}
