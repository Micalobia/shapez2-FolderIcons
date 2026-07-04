using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Logging;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Data;
using Micalobia.Shapez2.FolderIcons.Services;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using static Micalobia.Shapez2.FolderIcons.HookHelper;

namespace Micalobia.Shapez2.FolderIcons.Features;

[UsedImplicitly]
public class SortingHandler(ILogger logger, FolderMetadataHandler metadataHandler) : ISessionService
{
    [LoggerField] private ILogger Logger { get; } = logger;
    private FolderMetadataHandler MetadataHandler { get; } = metadataHandler;
    private bool IncludeArchivedFoldersForNextScan { get; set; }
    private int IncludeArchivedFolderScanDepth { get; set; }

    public void SortChildren(BlueprintLibraryFolder folder) => folder._Children.Sort(new BlueprintLibraryEntryComparer(folder, CompareEntries));

    public bool ShowArchivedFolders { get; set; }

    public bool TryScanDirectoryIncludingArchived(BlueprintLibrary library, string path, int depth, out BlueprintLibraryFolder result)
    {
        IncludeArchivedFoldersForNextScan = true;
        try
        {
            return library.TryScanDirectory(path, depth, out result);
        }
        finally
        {
            IncludeArchivedFoldersForNextScan = false;
        }
    }

    private bool TryConsumeArchivedDirectoryScan()
    {
        if (!IncludeArchivedFoldersForNextScan)
            return false;

        IncludeArchivedFoldersForNextScan = false;
        return true;
    }

    private void BeginArchivedDirectoryScan() => ++IncludeArchivedFolderScanDepth;

    private void EndArchivedDirectoryScan() => --IncludeArchivedFolderScanDepth;

    private List<DirectoryInfo> FilterChildDirectories(List<DirectoryInfo> childDirectories) =>
        ShowArchivedFolders || IncludeArchivedFolderScanDepth > 0
            ? childDirectories
            : childDirectories.Where(directory => ShouldScanChildDirectory(directory.FullName)).ToList();

    private bool ShouldScanChildDirectory(string folderSourcePath) =>
        ShowArchivedFolders ||
        !MetadataHandler.TryReadMetadataFile(folderSourcePath, out var metadata) ||
        !metadata.Archived;

    private int CompareEntries(BlueprintLibraryFolder parent, IBlueprintLibraryEntry left, IBlueprintLibraryEntry right)
    {
        var favoriteCompare = CompareFavorites(left, right);
        return favoriteCompare != 0 ? favoriteCompare : left.CompareTo(right);
    }

    private int CompareFavorites(IBlueprintLibraryEntry left, IBlueprintLibraryEntry right)
    {
        if (!TryGetFavorited(left, out var leftFavorited) || !TryGetFavorited(right, out var rightFavorited))
            return 0;

        if (leftFavorited == rightFavorited)
            return 0;

        return leftFavorited ? -1 : 1;
    }

    private bool TryGetFavorited(IBlueprintLibraryEntry entry, out bool favorited)
    {
        if (entry is BlueprintLibraryFolder folder)
        {
            favorited = MetadataHandler.GetMetadata(folder).Favorited;
            return true;
        }

        favorited = false;
        return false;
    }

    [UsedImplicitly]
    public sealed class HookAdapter(FolderIcons mod) : HookAdapterBase(mod)
    {
        protected override void Install()
        {
            Track(CreateConstructorILHook<BlueprintLibraryFolder, string, string, IEnumerable<IBlueprintLibraryEntry>>(
                BlueprintLibraryFolder_Constructor_IL
            ));
            Track(CreateILHook<BlueprintLibraryFolder, IBlueprintLibraryEntry>(
                nameof(BlueprintLibraryFolder.HandleNewChild),
                BlueprintLibraryFolder_HandleNewChild_IL
            ));
            Track(CreateILHook<BlueprintLibrary, string, int, Ref<BlueprintLibraryFolder>>(
                nameof(BlueprintLibrary.TryScanDirectory),
                BlueprintLibrary_TryScanDirectory_IL
            ));
            Track(new Hook(
                GetMethod<BlueprintLibrary, string, int, Ref<BlueprintLibraryFolder>>(nameof(BlueprintLibrary.TryScanDirectory)),
                new TryScanDirectoryDetour(BlueprintLibrary_TryScanDirectory_Detour)
            ));
        }

        private void BlueprintLibraryFolder_Constructor_IL(ILContext ctx)
        {
            var cursor = new ILCursor(ctx);
            if (!TryReplaceChildrenSort(cursor))
                throw new InvalidOperationException("Could not find the child sort call in BlueprintLibraryFolder constructor.");
        }

        private void BlueprintLibraryFolder_HandleNewChild_IL(ILContext ctx)
        {
            var cursor = new ILCursor(ctx);

            if (!TryReplaceChildrenSort(cursor))
                throw new InvalidOperationException("Could not find the child sort call in BlueprintLibraryFolder.HandleNewChild.");
        }

        private bool TryReplaceChildrenSort(ILCursor cursor)
        {
            if (!cursor.TryGotoNext(
                    MoveType.Before,
                    instruction => instruction.MatchLdarg0(),
                    instruction => instruction.MatchLdfld<BlueprintLibraryFolder>(nameof(BlueprintLibraryFolder._Children)),
                    instruction => instruction.MatchCallvirt<List<IBlueprintLibraryEntry>>(nameof(List<>.Sort))))
                return false;

            cursor.RemoveRange(3);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Action<BlueprintLibraryFolder>>(folder => Mod.ResolveSession<SortingHandler>().SortChildren(folder));
            return true;
        }

        private void BlueprintLibrary_TryScanDirectory_IL(ILContext ctx)
        {
            var cursor = new ILCursor(ctx);
            VariableDefinition childDirectoriesLocal = null;

            if (!cursor.TryGotoNext(
                    MoveType.After,
                    instruction => instruction.MatchCall(typeof(Enumerable), nameof(Enumerable.ToList)),
                    instruction => instruction.MatchStloc<List<DirectoryInfo>>(ctx, out childDirectoriesLocal)))
                throw new InvalidOperationException("Could not find the child directory list in BlueprintLibrary.TryScanDirectory.");

            cursor.Emit(OpCodes.Ldloc, childDirectoriesLocal);
            cursor.EmitDelegate<Func<List<DirectoryInfo>, List<DirectoryInfo>>>(childDirectories => Mod.ResolveSession<SortingHandler>().FilterChildDirectories(childDirectories));
            cursor.Emit(OpCodes.Stloc, childDirectoriesLocal);
        }

        private bool BlueprintLibrary_TryScanDirectory_Detour(
            TryScanDirectoryOrig orig,
            BlueprintLibrary self,
            string path,
            int depth,
            out BlueprintLibraryFolder result)
        {
            var handler = Mod.ResolveSession<SortingHandler>();
            if (!handler.TryConsumeArchivedDirectoryScan())
                return orig(self, path, depth, out result);

            handler.BeginArchivedDirectoryScan();
            try
            {
                return orig(self, path, depth, out result);
            }
            finally
            {
                handler.EndArchivedDirectoryScan();
            }
        }
    }

    private sealed class BlueprintLibraryEntryComparer(
        BlueprintLibraryFolder parent,
        Func<BlueprintLibraryFolder, IBlueprintLibraryEntry, IBlueprintLibraryEntry, int> compare
    ) : IComparer<IBlueprintLibraryEntry>
    {
        public int Compare(IBlueprintLibraryEntry left, IBlueprintLibraryEntry right) => compare(parent, left, right);
    }

    private delegate bool TryScanDirectoryOrig(BlueprintLibrary self, string path, int depth, out BlueprintLibraryFolder result);

    private delegate bool TryScanDirectoryDetour(
        TryScanDirectoryOrig orig,
        BlueprintLibrary self,
        string path,
        int depth,
        out BlueprintLibraryFolder result);
}
