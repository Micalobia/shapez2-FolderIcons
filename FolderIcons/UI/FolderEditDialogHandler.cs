using System;
using System.Linq;
using FolderIcons.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using ShapezShifter.SharpDetour;

namespace FolderIcons.UI;

public class FolderEditDialogHandler : ModHandler
{
    private FolderMetadataFileHandler MetadataFileHandler { get; }

    public FolderEditDialogHandler(FolderIcons mod, FolderMetadataFileHandler metadataFileHandler) : base(mod)
    {
        MetadataFileHandler = metadataFileHandler;
        try
        {
            Track(DetourHelper.CreatePostfixHook(
                (HUDEditBlueprintLibraryEntryDialog dialog, IBlueprintLibraryEntry entry) => dialog.InitForExisting(entry),
                AfterInitForExisting
            ));
            Track(HookHelper.CreateILHook<HUDBlueprintLibrary, IBlueprintLibraryEntry>(
                nameof(HUDBlueprintLibrary.RequestEdit),
                RequestEditIL
            ));
            Logger.Debug?.Log("Folder edit dialog hook initialized.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void AfterInitForExisting(HUDEditBlueprintLibraryEntryDialog dialog, IBlueprintLibraryEntry entry)
    {
        if (entry is not BlueprintLibraryFolder folder)
            return;

        ShowConfigurator(dialog, MetadataFileHandler.GetFolderIcon(folder));
    }

    private void RequestEditIL(ILContext ctx)
    {
        var displayClassLocal = GetRequiredRequestEditDisplayClassLocal(ctx);
        var entryField = GetRequiredCapturedEntryField(ctx, displayClassLocal);
        var cursor = new ILCursor(ctx);

        // Wrap vanilla's OnEdited callback so the icon save runs after vanilla has accepted/applied the edit.
        if (!cursor.TryGotoNext(
                MoveType.After,
                instruction => MatchCall(instruction, nameof(HUDEditBlueprintLibraryEntryDialog), "get_OnEdited"),
                instruction => instruction.MatchLdloc(displayClassLocal.Index),
                instruction => MatchCallbackLoad(instruction, nameof(HUDBlueprintLibrary.RequestEdit)),
                MatchActionConstructor))
            throw new InvalidOperationException("Could not find the OnEdited callback registration in HUDBlueprintLibrary.RequestEdit.");

        cursor.Emit(OpCodes.Ldloc, displayClassLocal);
        cursor.Emit(OpCodes.Ldfld, entryField);
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate(WrapEditCallback);
    }

    private static void ShowConfigurator(HUDEditBlueprintLibraryEntryDialog dialog, BlueprintIcon icon)
    {
        dialog.UIFolderIcon.gameObject.SetActiveSelfExt(active: false);
        dialog.UIIconConfigurator.Icon = icon;
        dialog.UIIconConfigurator.gameObject.SetActiveSelfExt(active: true);
    }

    private Action<HUDEditBlueprintLibraryEntryDialog.EditResult> WrapEditCallback(
        Action<HUDEditBlueprintLibraryEntryDialog.EditResult> vanillaCallback,
        IBlueprintLibraryEntry entry,
        HUDBlueprintLibrary hud)
    {
        return result =>
        {
            vanillaCallback(result);
            SaveFolderIconIfEditAccepted(hud, entry, result);
        };
    }

    private void SaveFolderIconIfEditAccepted(
        HUDBlueprintLibrary hud,
        IBlueprintLibraryEntry entry,
        HUDEditBlueprintLibraryEntryDialog.EditResult result)
    {
        if (entry is not BlueprintLibraryFolder folder)
            return;

        if (folder.Title != result.Name)
            return;

        if (hud.BlueprintLibrary?.RootEntry?.TryFindParentFolder(folder, out var currentParent) != true)
            return;

        if (currentParent != result.Parent)
            return;

        SaveFolderIcon(hud, folder, result.Icon);
    }

    private void SaveFolderIcon(HUDBlueprintLibrary hud, BlueprintLibraryFolder folder, BlueprintIcon icon)
    {
        MetadataFileHandler.SetFolderIcon(folder, icon ?? new BlueprintIcon(new IBlueprintIconComponent[4]));
        folder._MetadataChanged.Invoke();
        if (hud.BlueprintLibrary is BlueprintLibrary blueprintLibrary)
            blueprintLibrary.RefreshToolbarIfEntryIsContained(folder);
    }

    private static VariableDefinition GetRequiredRequestEditDisplayClassLocal(ILContext ctx) =>
        ctx.Body.Variables.FirstOrDefault(variable =>
            variable.VariableType.Name.Contains("DisplayClass"))
        ?? throw new InvalidOperationException("Could not find the edit callback display class local in HUDBlueprintLibrary.RequestEdit.");

    private static FieldReference GetRequiredCapturedEntryField(ILContext ctx, VariableDefinition displayClassLocal) =>
        ctx.Instrs
            .Select(instruction => instruction.Operand)
            .OfType<FieldReference>()
            .FirstOrDefault(field =>
                field.Name == "entry" &&
                field.FieldType.Name == nameof(IBlueprintLibraryEntry) &&
                field.DeclaringType.FullName == displayClassLocal.VariableType.FullName)
        ?? throw new InvalidOperationException("Could not find the captured entry field in HUDBlueprintLibrary.RequestEdit.");

    private static bool MatchCall(Instruction instruction, string declaringTypeName, string methodName) =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
        instruction.Operand is MethodReference method &&
        method.Name == methodName &&
        method.DeclaringType.Name == declaringTypeName;

    private static bool MatchCallbackLoad(Instruction instruction, string methodName) =>
        instruction.OpCode == OpCodes.Ldftn &&
        instruction.Operand is MethodReference method &&
        method.Name.StartsWith("<" + methodName + ">");

    private static bool MatchActionConstructor(Instruction instruction) =>
        instruction.OpCode == OpCodes.Newobj &&
        instruction.Operand is MethodReference method &&
        method.DeclaringType.Name == "Action`1";
}