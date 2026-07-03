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
using static Micalobia.Shapez2.FolderIcons.HookHelper;

namespace Micalobia.Shapez2.FolderIcons.Features;

[UsedImplicitly]
public class IconHandler(ILogger logger, FolderMetadataHandler metadataHandler, SortingHandler sortingHandler) : ISessionService
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

    [UsedImplicitly]
    public sealed class HookAdapter(FolderIcons mod) : HookAdapterBase(mod)
    {
        protected override void Install()
        {
            Track(CreateILHook<BlueprintsToolbarBuilder, IParentToolbarElement, IEnumerable<IBlueprintLibraryEntry>, bool, bool>(
                nameof(BlueprintsToolbarBuilder.BuildSlotsForBlueprintEntry),
                ctx => Mod.ResolveSession<IconHandler>().BuildSlotsForBlueprintEntryIL(ctx)
            ));
            Track(CreateILHook<HUDBlueprintLibrarySlot>(
                nameof(HUDBlueprintLibrarySlot.UpdateView),
                ctx => Mod.ResolveSession<IconHandler>().UpdateBlueprintLibrarySlotViewIL(ctx)
            ));
            Track(CreateILHook<HUDBlueprintLibraryNavEntry>(
                nameof(HUDBlueprintLibraryNavEntry.RebuildView),
                ctx => Mod.ResolveSession<IconHandler>().RebuildBlueprintLibraryNavEntryViewIL(ctx)
            ));
            Track(CreateILHook<BlueprintLibrary>(
                nameof(BlueprintLibrary.DeserializeBlueprintToolbarUnsafe),
                ctx => Mod.ResolveSession<IconHandler>().BlueprintLibrary_DeserializeBlueprintToolbarUnsafe_IL(ctx)
            ));
        }
    }

    private void BuildSlotsForBlueprintEntryIL(ILContext ctx)
    {
        var folderLocal = ctx.Body.Variables.First(variable => variable.VariableType.Name == nameof(BlueprintLibraryFolder));
        var cursor = new ILCursor(ctx);

        // Find the vanilla folder icon construction: new ToolbarSlotSpriteIcon(builder.Resources.UIBlueprintFolderIcon).
        if (!cursor.TryGotoNext(
                MoveType.Before,
                instruction => instruction.MatchLdarg(0),
                instruction => instruction.MatchLdfld(nameof(BlueprintsToolbarBuilder), nameof(BlueprintsToolbarBuilder.Resources)),
                instruction => instruction.MatchLdfld(nameof(BlueprintToolbarSlotsResources), nameof(BlueprintToolbarSlotsResources.UIBlueprintFolderIcon)),
                instruction => instruction.MatchNewobj(typeof(ToolbarSlotSpriteIcon))))
        {
            throw new InvalidOperationException("Could not find the folder toolbar icon creation in BlueprintsToolbarBuilder.BuildSlotsForBlueprintEntry.");
        }

        // Replace it with GetFolderToolbarIcon(builder, folder)
        cursor.RemoveRange(4);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldloc, folderLocal);
        cursor.EmitDelegate<Func<BlueprintsToolbarBuilder, BlueprintLibraryFolder, IToolbarSlotIcon>>(GetFolderToolbarIcon);
    }

    private void UpdateBlueprintLibrarySlotViewIL(ILContext ctx)
    {
        var cursor = new ILCursor(ctx);

        // Find the vanilla folder slot visibility toggles: folder icon active, blueprint icon inactive.
        if (!cursor.TryGotoNext(
                MoveType.Before,
                instruction => instruction.MatchLdarg(0),
                instruction => instruction.MatchLdfld(nameof(HUDBlueprintLibrarySlot), nameof(HUDBlueprintLibrarySlot.UIFolderIcon)),
                instruction => instruction.MatchCallvirt(typeof(UnityEngine.Component), "get_gameObject"),
                instruction => instruction.MatchLdcI4(1),
                instruction => instruction.MatchCall(nameof(CustomUnityExtensions), nameof(CustomUnityExtensions.SetActiveSelfExt)),
                instruction => instruction.MatchLdarg(0),
                instruction => instruction.MatchLdfld(nameof(HUDBlueprintLibrarySlot), nameof(HUDBlueprintLibrarySlot.UIIconRenderer)),
                instruction => instruction.MatchCallvirt(typeof(UnityEngine.Component), "get_gameObject"),
                instruction => instruction.MatchLdcI4(0),
                instruction => instruction.MatchCall(nameof(CustomUnityExtensions), nameof(CustomUnityExtensions.SetActiveSelfExt))))
        {
            throw new InvalidOperationException("Could not find the folder slot icon visibility block in HUDBlueprintLibrarySlot.UpdateView.");
        }

        // Replace it with ApplyFolderIcon(slot, folder)
        cursor.RemoveRange(10);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldfld, GetField<HUDBlueprintLibrarySlot>(nameof(HUDBlueprintLibrarySlot._Entry)));
        cursor.Emit(OpCodes.Castclass, typeof(BlueprintLibraryFolder));
        cursor.EmitDelegate<Action<HUDBlueprintLibrarySlot, BlueprintLibraryFolder>>(ApplyFolderIcon);
    }

    private void RebuildBlueprintLibraryNavEntryViewIL(ILContext ctx)
    {
        var folderLocal = ctx.Body.Variables.First(variable => variable.VariableType.Name == nameof(BlueprintLibraryFolder));
        var cursor = new ILCursor(ctx);

        // Find the vanilla nav entry folder icon block: choose folder sprite, show folder icon, hide blueprint icon.
        if (!cursor.TryGotoNext(
                MoveType.Before,
                instruction => instruction.MatchLdarg(0),
                instruction => instruction.MatchLdfld(nameof(HUDBlueprintLibraryNavEntry), nameof(HUDBlueprintLibraryNavEntry.UIFolderIcon)),
                instruction => instruction.MatchLdloc(folderLocal.Index),
                instruction => instruction.MatchCallvirt(typeof(BlueprintLibraryFolder), "get_Children")))
        {
            throw new InvalidOperationException("Could not find the folder nav entry icon block in HUDBlueprintLibraryNavEntry.RebuildView.");
        }

        var endCursor = cursor.Clone();
        if (!endCursor.TryGotoNext(
                MoveType.After,
                instruction => instruction.MatchLdarg(0),
                instruction => instruction.MatchLdfld(nameof(HUDBlueprintLibraryNavEntry), nameof(HUDBlueprintLibraryNavEntry.UIIconRenderer)),
                instruction => instruction.MatchCallvirt(typeof(UnityEngine.Component), "get_gameObject"),
                instruction => instruction.MatchLdcI4(0),
                instruction => instruction.MatchCall(nameof(CustomUnityExtensions), nameof(CustomUnityExtensions.SetActiveSelfExt))))
        {
            throw new InvalidOperationException("Could not find the end of the folder nav entry icon block in HUDBlueprintLibraryNavEntry.RebuildView.");
        }

        // Replace it with ApplyFolderIcon(navEntry, folder)
        cursor.RemoveRange(endCursor.Index - cursor.Index);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldloc, folderLocal);
        cursor.EmitDelegate<Action<HUDBlueprintLibraryNavEntry, BlueprintLibraryFolder>>(ApplyFolderIcon);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldfld, GetField<HUDBlueprintLibraryNavEntry>(nameof(HUDBlueprintLibraryNavEntry.UIFolderIndicator)));
        cursor.Emit(OpCodes.Ldc_I4_1);
        cursor.Emit(OpCodes.Call, typeof(CustomUnityExtensions).GetMethod(nameof(CustomUnityExtensions.SetActiveSelfExt), [typeof(UnityEngine.GameObject), typeof(bool)]));
    }

    private IToolbarSlotIcon GetFolderToolbarIcon(BlueprintsToolbarBuilder builder, BlueprintLibraryFolder folder)
    {
        var metadata = MetadataHandler.GetMetadata(folder);
        return !metadata.HasIcon
            ? new ToolbarSlotSpriteIcon(builder.Resources.UIBlueprintFolderIcon)
            : new ToolbarSlotBlueprintIcon(metadata.GetBlueprintIcon);
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

    private void ApplyFolderIcon(HUDBlueprintLibrarySlot slot, BlueprintLibraryFolder folder)
    {
        var metadata = MetadataHandler.GetMetadata(folder);
        if (metadata.HasIcon)
        {
            slot.UIIconRenderer.Icon = metadata.GetBlueprintIcon;
            slot.UIFolderIcon.gameObject.SetActiveSelfExt(active: false);
            slot.UIIconRenderer.gameObject.SetActiveSelfExt(active: true);
            return;
        }

        slot.UIFolderIcon.gameObject.SetActiveSelfExt(active: true);
        slot.UIIconRenderer.gameObject.SetActiveSelfExt(active: false);
    }

    private void ApplyFolderIcon(HUDBlueprintLibraryNavEntry navEntry, BlueprintLibraryFolder folder)
    {
        var metadata = MetadataHandler.GetMetadata(folder);
        if (metadata.HasIcon)
        {
            navEntry.UIFolderIcon.gameObject.SetActiveSelfExt(active: false);
            navEntry.UIIconRenderer.Icon = metadata.GetBlueprintIcon;
            navEntry.UIIconRenderer.gameObject.SetActiveSelfExt(active: true);
            return;
        }

        navEntry.UIFolderIcon.sprite = folder.Children.Count > 0 ? navEntry.UISpriteFolder : navEntry.UISpriteFolderEmpty;
        navEntry.UIFolderIcon.gameObject.SetActiveSelfExt(active: true);
        navEntry.UIIconRenderer.gameObject.SetActiveSelfExt(active: false);
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
        cursor.EmitDelegate<TryResolveToolbarShortcutFallbackDelegate>(TryResolveToolbarShortcutFallback);
    }

    private delegate bool TryResolveToolbarShortcutFallbackDelegate(
        bool found,
        BlueprintLibrary blueprintLibrary,
        string relativePath,
        ref IBlueprintLibraryEntry shortcut);

}
