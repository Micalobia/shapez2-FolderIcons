using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Data;
using Micalobia.Shapez2.FolderIcons.Services;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using UnityEngine;
using static Micalobia.Shapez2.FolderIcons.HookHelper;
using ILogger = Core.Logging.ILogger;

namespace Micalobia.Shapez2.FolderIcons.Features;

[UsedImplicitly]
public class IconHandler(ILogger logger, FolderMetadataHandler metadataHandler, SortingHandler sortingHandler)
    : ISessionService
{
    [LoggerField] private ILogger Logger { get; } = logger;
    private FolderMetadataHandler MetadataHandler { get; } = metadataHandler;
    private SortingHandler SortingHandler { get; } = sortingHandler;

    private bool TryResolveToolbarShortcutFallback(
        bool found,
        BlueprintLibrary blueprintLibrary,
        string relativePath,
        ref IBlueprintLibraryEntry shortcut) =>
        found || TryResolveToolbarShortcut(blueprintLibrary, relativePath, out shortcut);

    private IToolbarSlotIcon GetFolderToolbarIcon(BlueprintsToolbarBuilder builder, BlueprintLibraryFolder folder)
    {
        var metadata = MetadataHandler.GetMetadata(folder);
        return metadata.HasIcon
            ? new ToolbarSlotBlueprintIcon(metadata.GetBlueprintIcon)
            : new ToolbarSlotSpriteIcon(builder.Resources.UIBlueprintFolderIcon);
    }

    private void ApplyFolderIcon(HUDBlueprintLibrarySlot slot, BlueprintLibraryFolder folder)
    {
        var metadata = MetadataHandler.GetMetadata(folder);
        if (metadata.HasIcon)
        {
            slot.UIIconRenderer.Icon = metadata.GetBlueprintIcon;
            slot.UIFolderIcon.gameObject.SetActiveSelfExt(false);
            slot.UIIconRenderer.gameObject.SetActiveSelfExt(true);
            return;
        }

        slot.UIFolderIcon.gameObject.SetActiveSelfExt(true);
        slot.UIIconRenderer.gameObject.SetActiveSelfExt(false);
    }

    private bool TryResolveToolbarShortcut(BlueprintLibrary blueprintLibrary, string relativePath, out IBlueprintLibraryEntry shortcut)
    {
        shortcut = null;
        var blueprintLibraryPath = blueprintLibrary.RootEntry.SourcePath;
        var path = Path.GetFullPath(Path.Join(blueprintLibraryPath, relativePath));
        if (!IsInsideBlueprintLibrary(blueprintLibraryPath, path))
            return false;

        if (string.Equals(Path.GetExtension(path), BlueprintLibrary.FilenameSuffix, StringComparison.OrdinalIgnoreCase))
            return TryResolveToolbarBlueprintShortcut(blueprintLibrary, blueprintLibraryPath, path, relativePath, out shortcut);

        if (!Directory.Exists(path))
            return false;
        if (!SortingHandler.TryScanDirectoryIncludingArchived(blueprintLibrary, path, GetDirectoryDepth(relativePath), out var folder))
            return false;

        shortcut = folder;
        return true;
    }

    private bool TryResolveToolbarBlueprintShortcut(
        BlueprintLibrary blueprintLibrary,
        string blueprintLibraryPath,
        string path,
        string relativePath,
        out IBlueprintLibraryEntry entry)
    {
        entry = null;
        if (!File.Exists(path))
            return false;

        var parentPath = Path.GetDirectoryName(path);
        var parentDepth = GetDirectoryDepth(Path.GetRelativePath(blueprintLibraryPath, parentPath));
        return SortingHandler.TryScanDirectoryIncludingArchived(blueprintLibrary, parentPath, parentDepth, out var parentFolder) &&
               BlueprintLibrary.TryFindEntryByRelativePath(parentFolder, relativePath, out entry);
    }

    private static bool IsInsideBlueprintLibrary(string blueprintLibraryPath, string path)
    {
        var rootPath = Path.GetFullPath(blueprintLibraryPath);
        if (!rootPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            rootPath += Path.DirectorySeparatorChar;

        return path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetDirectoryDepth(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || relativePath == ".")
            return 0;

        return relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
    }

    [UsedImplicitly]
    public sealed class HookAdapter(FolderIcons mod) : HookAdapterBase(mod)
    {
        protected override void Install()
        {
            Track(CreateILHook<BlueprintsToolbarBuilder, IParentToolbarElement, IEnumerable<IBlueprintLibraryEntry>, bool, bool>(
                nameof(BlueprintsToolbarBuilder.BuildSlotsForBlueprintEntry),
                BlueprintsToolbarBuilder_BuildSlotsForBlueprintEntry_IL
            ));
            Track(CreateILHook<HUDBlueprintLibrarySlot>(
                nameof(HUDBlueprintLibrarySlot.UpdateView),
                HUDBlueprintLibrarySlot_UpdateView_IL
            ));
            Track(CreateILHook<BlueprintLibrary>(
                nameof(BlueprintLibrary.DeserializeBlueprintToolbarUnsafe),
                BlueprintLibrary_DeserializeBlueprintToolbarUnsafe_IL
            ));
        }

        private void BlueprintsToolbarBuilder_BuildSlotsForBlueprintEntry_IL(ILContext ctx)
        {
            var folderLocal = ctx.Body.Variables.First(variable => variable.VariableType.Name == nameof(BlueprintLibraryFolder));
            var cursor = new ILCursor(ctx);

            // Find the vanilla folder icon construction: new ToolbarSlotSpriteIcon(builder.Resources.UIBlueprintFolderIcon).
            if (!cursor.TryGotoNext(
                    MoveType.Before,
                    instruction => instruction.MatchLdarg0(),
                    instruction => instruction.MatchLdfld<BlueprintsToolbarBuilder>(nameof(BlueprintsToolbarBuilder.Resources)),
                    instruction => instruction.MatchLdfld<BlueprintToolbarSlotsResources>(nameof(BlueprintToolbarSlotsResources.UIBlueprintFolderIcon)),
                    instruction => instruction.MatchNewobj<ToolbarSlotSpriteIcon>()))
                throw new InvalidOperationException("Could not find the folder toolbar icon creation in BlueprintsToolbarBuilder.BuildSlotsForBlueprintEntry.");

            // Replace it with GetFolderToolbarIcon(builder, folder)
            cursor.RemoveRange(4);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldloc, folderLocal);
            cursor.EmitDelegate<Func<BlueprintsToolbarBuilder, BlueprintLibraryFolder, IToolbarSlotIcon>>((builder, folder) =>
                Mod.ResolveSession<IconHandler>().GetFolderToolbarIcon(builder, folder));
        }

        private void HUDBlueprintLibrarySlot_UpdateView_IL(ILContext ctx)
        {
            var cursor = new ILCursor(ctx);

            // Find the vanilla folder slot visibility toggles: folder icon active, blueprint icon inactive.
            if (!cursor.TryGotoNext(
                    MoveType.Before,
                    instruction => instruction.MatchLdarg0(),
                    instruction => instruction.MatchLdfld<HUDBlueprintLibrarySlot>(nameof(HUDBlueprintLibrarySlot.UIFolderIcon)),
                    instruction => instruction.MatchGetter<Component>(nameof(Component.gameObject)),
                    instruction => instruction.MatchTrue()))
                throw new InvalidOperationException("Could not find the folder slot icon visibility block in HUDBlueprintLibrarySlot.UpdateView.");

            var setActivePredicate = (Instruction instruction) => instruction.MatchCall(nameof(CustomUnityExtensions), nameof(CustomUnityExtensions.SetActiveSelfExt));

            // Replace it with ApplyFolderIcon(slot, folder)
            cursor.RemoveThroughNext(setActivePredicate, setActivePredicate);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, GetField<HUDBlueprintLibrarySlot>(nameof(HUDBlueprintLibrarySlot._Entry)));
            cursor.Emit(OpCodes.Castclass, typeof(BlueprintLibraryFolder));
            cursor.EmitDelegate<Action<HUDBlueprintLibrarySlot, BlueprintLibraryFolder>>((slot, folder) => Mod.ResolveSession<IconHandler>().ApplyFolderIcon(slot, folder));
        }

        private void BlueprintLibrary_DeserializeBlueprintToolbarUnsafe_IL(ILContext ctx)
        {
            VariableDefinition relativePathLocal = null;
            VariableDefinition shortcutLocal = null;
            var cursor = new ILCursor(ctx);

            if (!cursor.TryGotoNext(
                    MoveType.After,
                    instruction => instruction.MatchLdarg0(),
                    instruction => instruction.MatchGetter<BlueprintLibrary>(nameof(BlueprintLibrary.RootEntry)),
                    instruction => instruction.MatchLdloc<string>(ctx, out relativePathLocal),
                    instruction => instruction.MatchLdloca<IBlueprintLibraryEntry>(ctx, out shortcutLocal),
                    instruction => instruction.MatchCall<BlueprintLibrary>(nameof(BlueprintLibrary.TryFindEntryByRelativePath))))
                throw new InvalidOperationException("Could not find the toolbar shortcut lookup in BlueprintLibrary.DeserializeBlueprintToolbarUnsafe.");

            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldloc, relativePathLocal);
            cursor.Emit(OpCodes.Ldloca, shortcutLocal);
            cursor.EmitDelegate<TryResolveToolbarShortcutFallbackDelegate>((found,
                    blueprintLibrary,
                    relativePath,
                    ref shortcut) =>
                Mod.ResolveSession<IconHandler>().TryResolveToolbarShortcutFallback(found, blueprintLibrary, relativePath, ref shortcut));
        }

        private delegate bool TryResolveToolbarShortcutFallbackDelegate(
            bool found,
            BlueprintLibrary blueprintLibrary,
            string relativePath,
            ref IBlueprintLibraryEntry shortcut);
    }
}
