using System;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Services;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using ILogger = Core.Logging.ILogger;
using static Micalobia.Shapez2.FolderIcons.HookHelper;

namespace Micalobia.Shapez2.FolderIcons.HUD;

[UsedImplicitly]
public class HUDBlueprintLibraryHandler(ILogger logger, EditDialogHandler editDialogHandler) : ISessionService
{
    [LoggerField] private ILogger Logger { get; } = logger;
    private EditDialogHandler EditDialogHandler { get; } = editDialogHandler;

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
            Track(CreateILHook<HUDBlueprintLibrary, IBlueprintLibraryEntry>(
                nameof(HUDBlueprintLibrary.RequestEdit),
                HUDBlueprintLibrary_RequestEdit_IL
            ));
        }

        private void HUDBlueprintLibrary_RequestEdit_IL(ILContext ctx)
        {
            var cursor = new ILCursor(ctx);

            // Wrap vanilla's OnEdited callback so the metadata save runs after vanilla has accepted/applied the edit.
            if (!cursor.TryGotoNext(
                    MoveType.After,
                    instruction => instruction.MatchGetter<HUDEditBlueprintLibraryEntryDialog>(nameof(HUDEditBlueprintLibraryEntryDialog.OnEdited))))
                throw new InvalidOperationException("Could not find the OnEdited event in HUDBlueprintLibrary.RequestEdit.");

            if (!cursor.TryGotoNext(
                    MoveType.After,
                    instruction => instruction.MatchNewobj<Action<HUDEditBlueprintLibraryEntryDialog.EditResult>>()))
                throw new InvalidOperationException("Could not find the OnEdited callback construction in HUDBlueprintLibrary.RequestEdit.");

            cursor.Emit(OpCodes.Ldarg_1);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<WrapEditCallbackDelegate>((vanillaCallback, entry, hud) => Mod.ResolveSession<HUDBlueprintLibraryHandler>().WrapEditCallback(vanillaCallback, entry, hud));
        }

        private delegate Action<HUDEditBlueprintLibraryEntryDialog.EditResult> WrapEditCallbackDelegate(
            Action<HUDEditBlueprintLibraryEntryDialog.EditResult> vanillaCallback,
            IBlueprintLibraryEntry entry,
            HUDBlueprintLibrary hud);
    }
}
