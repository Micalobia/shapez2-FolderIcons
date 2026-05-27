using JetBrains.Annotations;
using System;
using FolderIcons.Features;
using FolderIcons.Metadata;
using FolderIcons.UI;
using ILogger = Core.Logging.ILogger;

namespace FolderIcons;

[UsedImplicitly]
public class FolderIcons : IMod
{
    internal ILogger Logger { get; }
    private FolderMetadataFileHandler FolderMetadataFileHandler { get; set; }
    private IconHandler IconHandler { get; set; }
    private FolderEditDialogHandler FolderEditDialogHandler { get; set; }

    public FolderIcons(ILogger logger)
    {
        Logger = logger;
        try
        {
            FolderMetadataFileHandler = new FolderMetadataFileHandler(this);
            IconHandler = new IconHandler(this, FolderMetadataFileHandler);
            FolderEditDialogHandler = new FolderEditDialogHandler(this, FolderMetadataFileHandler);
            logger.Info?.Log("Initialized handlers");
        }
        catch (Exception ex)
        {
            Dispose();
            Logger.Warning?.Log($"Mod failed to load. Installed handlers were removed. Reason: {ex}");
            throw;
        }
    }

    public void Dispose()
    {
        FolderEditDialogHandler?.Dispose();
        IconHandler?.Dispose();
        FolderMetadataFileHandler?.Dispose();
        FolderEditDialogHandler = null;
        IconHandler = null;
        FolderMetadataFileHandler = null;
    }
}
