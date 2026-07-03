using System;
using System.Collections.Generic;
using Core.Logging;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Data;
using Micalobia.Shapez2.FolderIcons.Services;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using static Micalobia.Shapez2.FolderIcons.HookHelper;

namespace Micalobia.Shapez2.FolderIcons.Features;

[UsedImplicitly]
public class SortingHandler(ILogger logger, FolderMetadataHandler metadataHandler) : ISessionService
{
    [LoggerField] private ILogger Logger { get; } = logger;
    private FolderMetadataHandler MetadataHandler { get; } = metadataHandler;

    public void SortChildren(BlueprintLibraryFolder folder) => folder._Children.Sort(new BlueprintLibraryEntryComparer(folder, CompareEntries));

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

    }

    private sealed class BlueprintLibraryEntryComparer(
        BlueprintLibraryFolder parent,
        Func<BlueprintLibraryFolder, IBlueprintLibraryEntry, IBlueprintLibraryEntry, int> compare
    ) : IComparer<IBlueprintLibraryEntry>
    {
        public int Compare(IBlueprintLibraryEntry left, IBlueprintLibraryEntry right) => compare(parent, left, right);
    }

}
