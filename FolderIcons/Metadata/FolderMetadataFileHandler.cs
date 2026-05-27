using System;
using System.Collections.Generic;
using System.IO;
using MonoMod.RuntimeDetour;
using ShapezShifter.SharpDetour;

namespace FolderIcons.Metadata;

public sealed class FolderMetadataFileHandler : IDisposable
{
    private const string METADATA_FILENAME = ".spz2meta";
    private static readonly BlueprintIcon DefaultFolderIcon = new(new IBlueprintIconComponent[4]);

    private readonly Dictionary<BlueprintLibraryFolder, FolderMetadata> _folderMetadataByFolder = new();

    private FolderIcons Mod { get; }
    private Hook RefreshHook { get; set; }

    public FolderMetadataFileHandler(FolderIcons mod)
    {
        Mod = mod;
        try
        {
            RefreshHook = DetourHelper.CreatePostfixHook(
                (BlueprintLibrary blueprintLibrary) => blueprintLibrary.Refresh(),
                AfterBlueprintLibraryRefresh
            );
            Mod.Logger.Debug?.Log("Folder metadata refresh hook initialized.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        RefreshHook?.Dispose();
        RefreshHook = null;
        _folderMetadataByFolder.Clear();
    }

    public FolderMetadata GetFolderMetadata(BlueprintLibraryFolder folder)
    {
        if (_folderMetadataByFolder.TryGetValue(folder, out var cachedMetadata))
            return cachedMetadata;

        if (!TryReadFolderMetadata(folder, out var metadata))
        {
            metadata = CreateDefaultMetadata();
            WriteFolderMetadata(folder, metadata);
            Mod.Logger.Debug?.Log($"Created default folder metadata at '{GetMetadataPath(folder)}'.");
        }

        _folderMetadataByFolder[folder] = metadata;
        return metadata;
    }

    public BlueprintIcon GetFolderIcon(BlueprintLibraryFolder folder)
    {
        var metadata = GetFolderMetadata(folder);
        return BlueprintIcon.FromSerialized(metadata.Icon);
    }

    public void SetFolderIcon(BlueprintLibraryFolder folder, BlueprintIcon icon)
    {
        var metadata = GetFolderMetadata(folder);
        var previousIcon = metadata.Icon;
        metadata.Icon = icon.Serialize();
        try
        {
            WriteFolderMetadata(folder, metadata);
        }
        catch
        {
            metadata.Icon = previousIcon;
            throw;
        }
    }

    private void AfterBlueprintLibraryRefresh(BlueprintLibrary blueprintLibrary)
    {
        _folderMetadataByFolder.Clear();

        if (blueprintLibrary.RootEntry == null)
            return;

        CacheFolderMetadataRecursive(blueprintLibrary.RootEntry);
        Mod.Logger.Debug?.Log($"Loaded folder metadata for {_folderMetadataByFolder.Count} folders.");
    }

    private void CacheFolderMetadataRecursive(BlueprintLibraryFolder folder)
    {
        try
        {
            GetFolderMetadata(folder);
        }
        catch (Exception ex)
        {
            Mod.Logger.Warning?.Log($"Failed to cache folder metadata for '{folder.SourcePath}': {ex}");
        }

        foreach (var child in folder.Children)
            if (child is BlueprintLibraryFolder childFolder)
                CacheFolderMetadataRecursive(childFolder);
    }

    private static FolderMetadata CreateDefaultMetadata() => new()
    {
        Icon = DefaultFolderIcon.Serialize(),
    };

    private bool TryReadFolderMetadata(BlueprintLibraryFolder folder, out FolderMetadata metadata)
    {
        metadata = null;
        var path = GetMetadataPath(folder);
        if (!File.Exists(path))
            return false;

        try
        {
            metadata = FolderMetadataSerializer.Deserialize(File.ReadAllText(path));
            if (metadata.Icon == null)
                return false;

            BlueprintIcon.FromSerialized(metadata.Icon);
            return true;
        }
        catch (Exception ex)
        {
            Mod.Logger.Warning?.Log($"Failed to read folder metadata from '{path}': {ex}");
            return false;
        }
    }

    private void WriteFolderMetadata(BlueprintLibraryFolder folder, FolderMetadata metadata)
    {
        var path = GetMetadataPath(folder);

        try
        {
            Directory.CreateDirectory(folder.SourcePath);
            File.WriteAllText(path, FolderMetadataSerializer.Serialize(metadata));
            Mod.Logger.Debug?.Log($"Wrote folder metadata to '{path}'.");
        }
        catch (Exception ex)
        {
            Mod.Logger.Warning?.Log($"Failed to write folder metadata to '{path}': {ex}");
            throw;
        }
    }

    private static string GetMetadataPath(BlueprintLibraryFolder folder) => Path.Join(folder.SourcePath, METADATA_FILENAME);
}
