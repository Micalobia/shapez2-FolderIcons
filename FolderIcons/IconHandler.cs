using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace FolderIcons;

public class IconHandler : IDisposable
{
    private FolderIcons Mod { get; }
    private FolderMetadataFileHandler MetadataFileHandler { get; }
    private ILHook BuildSlotsHook { get; set; }
    private ILHook UpdateViewHook { get; set; }
    private ILHook RebuildNavEntryViewHook { get; set; }

    public IconHandler(FolderIcons mod, FolderMetadataFileHandler metadataFileHandler)
    {
        Mod = mod;
        MetadataFileHandler = metadataFileHandler;
        try
        {
            BuildSlotsHook = HookHelper.CreateILHook<BlueprintsToolbarBuilder, IParentToolbarElement, IEnumerable<IBlueprintLibraryEntry>, bool, bool>(
                nameof(BlueprintsToolbarBuilder.BuildSlotsForBlueprintEntry),
                BuildSlotsForBlueprintEntryIL
            );
            UpdateViewHook = HookHelper.CreateILHook<HUDBlueprintLibrarySlot>(
                nameof(HUDBlueprintLibrarySlot.UpdateView),
                UpdateBlueprintLibrarySlotViewIL
            );
            RebuildNavEntryViewHook = HookHelper.CreateILHook<HUDBlueprintLibraryNavEntry>(
                nameof(HUDBlueprintLibraryNavEntry.RebuildView),
                RebuildBlueprintLibraryNavEntryViewIL
            );
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        RebuildNavEntryViewHook?.Dispose();
        UpdateViewHook?.Dispose();
        BuildSlotsHook?.Dispose();
        RebuildNavEntryViewHook = null;
        UpdateViewHook = null;
        BuildSlotsHook = null;
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
        cursor.Emit(OpCodes.Ldfld, HookHelper.GetField<HUDBlueprintLibrarySlot>(nameof(HUDBlueprintLibrarySlot._Entry)));
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
        cursor.Emit(OpCodes.Ldfld, HookHelper.GetField<HUDBlueprintLibraryNavEntry>(nameof(HUDBlueprintLibraryNavEntry.UIFolderIndicator)));
        cursor.Emit(OpCodes.Ldc_I4_1);
        cursor.Emit(OpCodes.Call, HookHelper.GetMethod(typeof(CustomUnityExtensions), nameof(CustomUnityExtensions.SetActiveSelfExt), typeof(UnityEngine.GameObject), typeof(bool)));
    }

    private IToolbarSlotIcon GetFolderToolbarIcon(BlueprintsToolbarBuilder builder, BlueprintLibraryFolder folder)
    {
        var icon = MetadataFileHandler.GetFolderIcon(folder);
        return IsEmptyIcon(icon)
            ? new ToolbarSlotSpriteIcon(builder.Resources.UIBlueprintFolderIcon)
            : new ToolbarSlotBlueprintIcon(icon);
    }

    private void ApplyFolderIcon(HUDBlueprintLibrarySlot slot, BlueprintLibraryFolder folder)
    {
        var icon = MetadataFileHandler.GetFolderIcon(folder);
        if (!IsEmptyIcon(icon))
        {
            slot.UIIconRenderer.Icon = icon;
            slot.UIFolderIcon.gameObject.SetActiveSelfExt(active: false);
            slot.UIIconRenderer.gameObject.SetActiveSelfExt(active: true);
            return;
        }

        slot.UIFolderIcon.gameObject.SetActiveSelfExt(active: true);
        slot.UIIconRenderer.gameObject.SetActiveSelfExt(active: false);
    }

    private void ApplyFolderIcon(HUDBlueprintLibraryNavEntry navEntry, BlueprintLibraryFolder folder)
    {
        var icon = MetadataFileHandler.GetFolderIcon(folder);
        if (!IsEmptyIcon(icon))
        {
            navEntry.UIFolderIcon.gameObject.SetActiveSelfExt(active: false);
            navEntry.UIIconRenderer.Icon = icon;
            navEntry.UIIconRenderer.gameObject.SetActiveSelfExt(active: true);
            return;
        }

        navEntry.UIFolderIcon.sprite = folder.Children.Count > 0 ? navEntry.UISpriteFolder : navEntry.UISpriteFolderEmpty;
        navEntry.UIFolderIcon.gameObject.SetActiveSelfExt(active: true);
        navEntry.UIIconRenderer.gameObject.SetActiveSelfExt(active: false);
    }

    private static bool IsEmptyIcon(BlueprintIcon icon)
    {
        if (icon == null)
            return true;

        foreach (var component in icon.Components)
            switch (component)
            {
                case null:
                case BlueprintIconComponentIcon { IconId.Id: "Empty" }:
                    continue;
                default:
                    return false;
            }

        return true;
    }
}
