using System;
using System.Collections.Generic;
using System.IO;
using Core.Logging;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Services;
using ShapezShifter.SharpDetour;

namespace Micalobia.Shapez2.FolderIcons.Data;

[UsedImplicitly]
public sealed class FolderMetadataHandler(ILogger logger, FolderMetadataSerializer serializer) : ISessionService
{
    private const string METADATA_FILENAME = ".spz2meta";

    [LoggerField] private ILogger Logger { get; } = logger;
    private FolderMetadataSerializer Serializer { get; } = serializer;
    private readonly Dictionary<BlueprintLibraryFolder, FolderMetadata> _folderMetadataByFolder = new();

    public FolderMetadata GetMetadata(BlueprintLibraryFolder folder)
    {
        if (_folderMetadataByFolder.TryGetValue(folder, out var cachedMetadata))
            return cachedMetadata;

        if (!TryReadMetadataFile(folder, out var metadata))
            metadata = new FolderMetadata();

        _folderMetadataByFolder[folder] = metadata;
        return metadata;
    }

    public void SetMetadata(BlueprintLibraryFolder folder, FolderMetadata metadata)
    {
        WriteMetadataFile(folder, metadata);
        _folderMetadataByFolder[folder] = metadata;
    }

    private void RefreshCache(BlueprintLibrary blueprintLibrary)
    {
        _folderMetadataByFolder.Clear();

        if (blueprintLibrary.RootEntry == null)
            return;

        CacheMetadataRecursive(blueprintLibrary.RootEntry);
        Logger.Debug?.Log($"Loaded folder metadata for {_folderMetadataByFolder.Count} folders.");
    }

    private void CacheMetadataRecursive(BlueprintLibraryFolder folder)
    {
        try
        {
            GetMetadata(folder);
        }
        catch (Exception ex)
        {
            Logger.Warning?.Log($"Failed to cache folder metadata for '{folder.SourcePath}': {ex}");
        }

        foreach (var child in folder.Children)
            if (child is BlueprintLibraryFolder childFolder)
                CacheMetadataRecursive(childFolder);
    }

    public bool HasMetadataFile(BlueprintLibraryFolder folder) => HasMetadataFile(folder.SourcePath);

    public bool HasMetadataFile(string folderSourcePath) => File.Exists(GetMetadataPath(folderSourcePath));

    public bool TryReadMetadataFile(BlueprintLibraryFolder folder, out FolderMetadata metadata) =>
        TryReadMetadataFile(folder.SourcePath, out metadata);

    public bool TryReadMetadataFile(string folderSourcePath, out FolderMetadata metadata)
    {
        metadata = null;
        var path = GetMetadataPath(folderSourcePath);
        if (!File.Exists(path))
            return false;

        try
        {
            metadata = Serializer.Deserialize(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning?.Log($"Failed to read folder metadata from '{path}': {ex}");
            return false;
        }
    }

    public bool DeleteMetadataFile(BlueprintLibraryFolder folder)
    {
        var path = GetMetadataPath(folder.SourcePath);

        try
        {
            File.Delete(path);
            _folderMetadataByFolder.Remove(folder);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning?.Log($"Failed to delete folder metadata at '{path}': {ex}");
            return false;
        }
    }

    private void WriteMetadataFile(BlueprintLibraryFolder folder, FolderMetadata metadata)
    {
        var path = GetMetadataPath(folder.SourcePath);
        var tempPath = GetTempMetadataPath(path);

        try
        {
            Directory.CreateDirectory(folder.SourcePath);
            File.WriteAllText(tempPath, Serializer.Serialize(metadata));
            MoveMetadataFileIntoPlace(tempPath, path);
            Logger.Debug?.Log($"Wrote folder metadata to '{path}'.");
        }
        catch (Exception ex)
        {
            Logger.Warning?.Log($"Failed to write folder metadata to '{path}': {ex}");
            throw;
        }
    }

    private static string GetMetadataPath(string folderSourcePath) => Path.Join(folderSourcePath, METADATA_FILENAME);

    private static string GetTempMetadataPath(string metadataPath) => metadataPath + ".temp";

    private static void MoveMetadataFileIntoPlace(string tempPath, string path)
    {
        if (File.Exists(path)) File.Replace(tempPath, path, null);
        else File.Move(tempPath, path);
    }

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
            Mod.ResolveSession<FolderMetadataHandler>().RefreshCache(blueprintLibrary);
    }
}
