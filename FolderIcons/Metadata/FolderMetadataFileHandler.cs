using System;
using System.Collections.Generic;
using System.IO;
using Core.Logging;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Services;
using ShapezShifter.SharpDetour;

namespace Micalobia.Shapez2.FolderIcons.Metadata;

[UsedImplicitly]
public sealed class FolderMetadataFileHandler(ILogger logger) : ISessionService
{
    private const string METADATA_FILENAME = ".spz2meta";
    private static readonly BlueprintIcon DefaultFolderIcon = new(new IBlueprintIconComponent[4]);

    [LoggerField] private ILogger Logger { get; } = logger;
    private readonly Dictionary<BlueprintLibraryFolder, FolderMetadata> _folderMetadataByFolder = new();

    public FolderMetadata GetFolderMetadata(BlueprintLibraryFolder folder)
    {
        if (_folderMetadataByFolder.TryGetValue(folder, out var cachedMetadata))
            return cachedMetadata;

        if (!TryReadFolderMetadata(folder, out var metadata))
        {
            metadata = CreateDefaultMetadata();
            WriteFolderMetadata(folder, metadata);
            Logger.Debug?.Log($"Created default folder metadata at '{GetMetadataPath(folder)}'.");
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
        Logger.Debug?.Log($"Loaded folder metadata for {_folderMetadataByFolder.Count} folders.");
    }

    private void CacheFolderMetadataRecursive(BlueprintLibraryFolder folder)
    {
        try
        {
            GetFolderMetadata(folder);
        }
        catch (Exception ex)
        {
            Logger.Warning?.Log($"Failed to cache folder metadata for '{folder.SourcePath}': {ex}");
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
            Logger.Warning?.Log($"Failed to read folder metadata from '{path}': {ex}");
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
            Logger.Debug?.Log($"Wrote folder metadata to '{path}'.");
        }
        catch (Exception ex)
        {
            Logger.Warning?.Log($"Failed to write folder metadata to '{path}': {ex}");
            throw;
        }
    }

    private static string GetMetadataPath(BlueprintLibraryFolder folder) => Path.Join(folder.SourcePath, METADATA_FILENAME);

    [UsedImplicitly]
    public sealed class HookAdapter(FolderIcons mod) : HookAdapterBase(mod)
    {
        protected override void Install()
        {
            Track(DetourHelper.CreatePostfixHook(
                (BlueprintLibrary blueprintLibrary) => blueprintLibrary.Refresh(),
                BlueprintLibrary_Refresh_Postfix
            ));
        }

        private void BlueprintLibrary_Refresh_Postfix(BlueprintLibrary blueprintLibrary) =>
            Mod.ResolveSession<FolderMetadataFileHandler>().AfterBlueprintLibraryRefresh(blueprintLibrary);
    }
}
