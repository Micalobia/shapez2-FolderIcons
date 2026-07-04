using System;
using Core.Logging;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Data;
using Micalobia.Shapez2.FolderIcons.Services;
using ShapezShifter.Hijack;

namespace Micalobia.Shapez2.FolderIcons.Features;

[UsedImplicitly]
public sealed class CommandHandler(ILogger logger, FolderMetadataHandler metadataHandler, SortingHandler sortingHandler, IBlueprintLibrary blueprintLibrary) : ISessionService
{
    [LoggerField] private ILogger Logger { get; } = logger;
    private FolderMetadataHandler MetadataHandler { get; } = metadataHandler;
    private SortingHandler SortingHandler { get; } = sortingHandler;
    private IBlueprintLibrary BlueprintLibrary { get; } = blueprintLibrary;
    private IDebugConsole Console { get; set; }

    private void RegisterCommands(IDebugConsole console)
    {
        Console = console;
        Console.Register("folder-icons.cleanup", OutputUsage);
        RegisterCleanupCommand("folder-icons.cleanup-defaults.passive", CleanupMode.PassiveDefaults);
        RegisterCleanupCommand("folder-icons.cleanup-defaults.aggressive", CleanupMode.AggressiveDefaults);
        RegisterCleanupCommand("folder-icons.cleanup-all", CleanupMode.All);
    }

    private void RegisterCleanupCommand(string command, CleanupMode mode) =>
        Console.Register(command, new DebugConsole.StringOption("action"), context => RunCleanupCommand(context, mode));

    private void RunCleanupCommand(DebugConsole.CommandContext context, CleanupMode mode)
    {
        var action = context.GetString(0).ToLowerInvariant();
        if (action is not ("preview" or "commit"))
        {
            context.Output("Invalid action. Use 'preview' or 'commit'.");
            OutputUsage(context);
            return;
        }

        var commit = action == "commit";
        var result = CleanupMetadataFiles(BlueprintLibrary.RootEntry, mode, commit);

        if (commit && result.DeletedFiles > 0)
            BlueprintLibrary.Refresh();

        OutputCleanupResult(context, mode, commit, result);
        Logger.Info?.Log(
            $"Folder metadata cleanup ({mode}, {(commit ? "commit" : "preview")}): " +
            $"{result.MatchingFiles} matching, {result.DeletedFiles} deleted, {result.FailedFiles} failed.");
    }

    private CleanupResult CleanupMetadataFiles(BlueprintLibraryFolder root, CleanupMode mode, bool commit)
    {
        var result = new CleanupResult();
        if (root == null)
            return result;

        var cleanupRoot = root;
        if (BlueprintLibrary is BlueprintLibrary blueprintLibrary &&
            SortingHandler.TryScanDirectoryIncludingArchived(blueprintLibrary, root.SourcePath, 0, out var scannedRoot))
            cleanupRoot = scannedRoot;

        CleanupRecursive(cleanupRoot, mode, commit, result);
        return result;
    }

    private void CleanupRecursive(BlueprintLibraryFolder folder, CleanupMode mode, bool commit, CleanupResult result)
    {
        result.ScannedFolders++;
        CleanupOne(folder, mode, commit, result);

        foreach (var child in folder.Children)
            if (child is BlueprintLibraryFolder childFolder)
                CleanupRecursive(childFolder, mode, commit, result);
    }

    private void CleanupOne(BlueprintLibraryFolder folder, CleanupMode mode, bool commit, CleanupResult result)
    {
        if (!MetadataHandler.HasMetadataFile(folder)) return;

        result.MetadataFiles++;

        if (!NeedsCleanup(folder, mode, result))
        {
            result.SkippedFiles++;
            return;
        }

        result.MatchingFiles++;
        if (!commit)
            return;

        if (MetadataHandler.DeleteMetadataFile(folder))
            result.DeletedFiles++;
        else
            result.FailedFiles++;
    }

    private bool NeedsCleanup(BlueprintLibraryFolder folder, CleanupMode mode, CleanupResult result)
    {
        if (mode == CleanupMode.All) return true;
        if (MetadataHandler.TryReadMetadataFile(folder, out var metadata))
            return metadata.IsDefault && (mode == CleanupMode.AggressiveDefaults || !metadata.HasExtraData);

        result.FailedFiles++;
        return false;
    }

    private static void OutputCleanupResult(
        DebugConsole.CommandContext context,
        CleanupMode mode,
        bool commit,
        CleanupResult result)
    {
        var verb = commit ? "Deleted" : "Would delete";
        var count = commit ? result.DeletedFiles : result.MatchingFiles;
        context.Output(
            $"{verb} {count} folder metadata file(s). " +
            $"Scanned {result.ScannedFolders} folder(s), found {result.MetadataFiles} metadata file(s), " +
            $"skipped {result.SkippedFiles}, failed {result.FailedFiles}.");

        if (mode == CleanupMode.AggressiveDefaults)
            context.Output("Aggressive cleanup ignores extra unknown metadata when known fields are default.");
        else if (mode == CleanupMode.All)
            context.Output("Cleanup-all removes every folder metadata file, including files with custom icons or extra data.");
    }

    private static void OutputUsage(DebugConsole.CommandContext context)
    {
        context.Output("Usage:");
        context.Output("folder-icons.cleanup-defaults.passive <preview|commit>");
        context.Output("folder-icons.cleanup-defaults.aggressive <preview|commit>");
        context.Output("folder-icons.cleanup-all <preview|commit>");
    }

    private enum CleanupMode
    {
        PassiveDefaults,
        AggressiveDefaults,
        All,
    }

    private sealed class CleanupResult
    {
        public int ScannedFolders { get; set; }
        public int MetadataFiles { get; set; }
        public int MatchingFiles { get; set; }
        public int DeletedFiles { get; set; }
        public int SkippedFiles { get; set; }
        public int FailedFiles { get; set; }
    }

    [UsedImplicitly]
    public sealed class HookAdapter(FolderIcons mod) : HookAdapterBase(mod), IConsoleRewirer
    {
        protected override void Install() => Track(new RewirerRegistration(GameRewirers.AddRewirer<IConsoleRewirer>(this)));

        public void RegisterCommands(IDebugConsole console) =>
            Mod.ResolveSession<CommandHandler>().RegisterCommands(console);

        private sealed class RewirerRegistration(RewirerHandle handle) : IDisposable
        {
            public void Dispose() => GameRewirers.RemoveRewirer(handle);
        }
    }
}
