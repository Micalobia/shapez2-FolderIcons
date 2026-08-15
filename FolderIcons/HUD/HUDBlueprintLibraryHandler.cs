using System;
using Core.Events;
using Core.Localization;
using Game.Core.Blueprint.Exporter;
using Game.Core.Blueprint.Importer;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Data;
using Micalobia.Shapez2.FolderIcons.Features;
using Micalobia.Shapez2.FolderIcons.Services;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using UnityEngine;
using ILogger = Core.Logging.ILogger;
using static Micalobia.Shapez2.FolderIcons.HookHelper;
using static UnityEngine.Object;

namespace Micalobia.Shapez2.FolderIcons.HUD;

[UsedImplicitly]
public class HUDBlueprintLibraryHandler(ILogger logger, SortingHandler sortingHandler, AssetHandler assetHandler, EditDialogHandler editDialogHandler)
    : ISessionService
{
    private static readonly Vector3 ButtonLocalPosition = new(87f, 0f, 0f);

    [LoggerField] private ILogger Logger { get; } = logger;
    private SortingHandler SortingHandler { get; } = sortingHandler;
    private AssetHandler AssetHandler { get; } = assetHandler;
    private EditDialogHandler EditDialogHandler { get; } = editDialogHandler;
    private Sprite ArchivedActiveIcon { get; set; }
    private Sprite ArchivedInactiveIcon { get; set; }

    private void AddShowArchivedButton(HUDBlueprintLibrary library)
    {
        if (!AssetHandler.TryGetSprite("icons/archived_active.png", out var archivedActiveIcon) ||
            !AssetHandler.TryGetSprite("icons/archived_inactive.png", out var archivedInactiveIcon))
            return;

        ArchivedActiveIcon = archivedActiveIcon;
        ArchivedInactiveIcon = archivedInactiveIcon;

        var template = library.UIButtonRefresh;
        var parent = template.transform.parent;
        var button = Instantiate(template, parent, false);
        button.name = "ShowArchived";
        button.transform.localPosition = ButtonLocalPosition;
        button.transform.SetAsLastSibling();

        button.OnClick.RemoveAllListeners();
        library.AddChildViewInternal(button);

        button.Icon = archivedInactiveIcon;
        button.UIIcon.color = Color.white;
        button.HasTooltip = true;
        button.TooltipTitle = "folder-icons.show-archived.title".T();
        button.TooltipText = "folder-icons.show-archived.tooltip".T();
        button.OnClick.AddListener(() => ToggleShowArchived(library, button));
        RefreshButtonState(button);
    }

    private void ToggleShowArchived(HUDBlueprintLibrary library, HUDIconButton button)
    {
        SortingHandler.ShowArchivedFolders = !SortingHandler.ShowArchivedFolders;
        RefreshButtonState(button);
        library.BlueprintLibrary.Refresh();
    }

    private void RefreshButtonState(HUDIconButton button)
    {
        var showArchived = SortingHandler.ShowArchivedFolders;
        button.Active = showArchived;
        button.Icon = showArchived ? ArchivedActiveIcon : ArchivedInactiveIcon;
    }

    private Action<HUDEditBlueprintLibraryEntryDialog.EditResult> WrapEditCallback(
        Action<HUDEditBlueprintLibraryEntryDialog.EditResult> vanillaCallback,
        IBlueprintLibraryEntry entry,
        HUDBlueprintLibrary hud) =>
        result =>
        {
            vanillaCallback(result);
            EditDialogHandler.SaveFolderMetadataIfEditAccepted(hud, entry, result);
        };

    [UsedImplicitly]
    public sealed class HookAdapter(FolderIcons mod) : HookAdapterBase(mod)
    {
        protected override void Install()
        {
            // shapez 2 1.2.0-rc3 split the old `BlueprintSerializer` parameter of HUDBlueprintLibrary.Construct
            // into two separate services, in this order: IBlueprintExporter, then IBlueprintImporter
            // (confirmed against Game.Hud.dll: Construct(IBlueprintLibrary, IHUDDialogStack, IUISoundPlayer, PlayerActionManager, IBlueprintExporter, IBlueprintImporter, IEventSender, IBlueprintStarter, IEntityPlacementRunner)).
            Track(GetRuntimeMethod((HUDBlueprintLibrary library,
                IBlueprintLibrary blueprintLibrary,
                IHUDDialogStack dialogStack,
                IUISoundPlayer soundPlayer,
                PlayerActionManager actionManager,
                IBlueprintExporter blueprintExporter,
                IBlueprintImporter blueprintImporter,
                IEventSender eventSender,
                IBlueprintStarter starter,
                IEntityPlacementRunner placementRunner
            ) => library.Construct(blueprintLibrary, dialogStack, soundPlayer, actionManager, blueprintExporter, blueprintImporter, eventSender, starter, placementRunner
            )).CreateILHook(HUDBlueprintLibrary_Construct_IL));
            Track(CreateILHook<HUDBlueprintLibrary, IBlueprintLibraryEntry>(
                nameof(HUDBlueprintLibrary.RequestEdit),
                HUDBlueprintLibrary_RequestEdit_IL
            ));
        }

        private void HUDBlueprintLibrary_Construct_IL(ILContext ctx)
        {
            var cursor = new ILCursor(ctx);
            if (!cursor.TryGotoNext(MoveType.Before, instruction => instruction.MatchRet()))
                throw new InvalidOperationException("Could not find return in HUDBlueprintLibrary.Construct.");

            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Action<HUDBlueprintLibrary>>(library => Mod.ResolveSession<HUDBlueprintLibraryHandler>().AddShowArchivedButton(library));
        }

        private void HUDBlueprintLibrary_RequestEdit_IL(ILContext ctx)
        {
            var cursor = new ILCursor(ctx);

            // Wrap vanilla's OnEdited callback so the metadata save runs after vanilla has accepted/applied the edit.
            // We could technically just add another listener to the event instead of wrapping this one, but the locals are more annoying that way
            if (!cursor.TryGotoNext(
                    MoveType.After,
                    instruction => instruction.MatchGetter<HUDEditBlueprintLibraryEntryDialog>(nameof(HUDEditBlueprintLibraryEntryDialog.OnEdited))))
                throw new InvalidOperationException("Could not find the OnEdited event in HUDBlueprintLibrary.RequestEdit.");

            if (!cursor.TryGotoNext(
                    MoveType.After,
                    instruction => instruction.MatchNewobj<Action<HUDEditBlueprintLibraryEntryDialog.EditResult>>()))
                throw new InvalidOperationException("Could not find the OnEdited callback construction in HUDBlueprintLibrary.RequestEdit.");

            cursor.Emit(OpCodes.Ldarg_1); // entry
            cursor.Emit(OpCodes.Ldarg_0); // hud
            cursor.EmitDelegate<WrapEditCallbackDelegate>((vanillaCallback, entry, hud) => Mod.ResolveSession<HUDBlueprintLibraryHandler>().WrapEditCallback(vanillaCallback, entry, hud));
        }

        private delegate Action<HUDEditBlueprintLibraryEntryDialog.EditResult> WrapEditCallbackDelegate(
            Action<HUDEditBlueprintLibraryEntryDialog.EditResult> vanillaCallback,
            IBlueprintLibraryEntry entry,
            HUDBlueprintLibrary hud);
    }
}
