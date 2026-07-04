using System;
using System.Linq;
using Core.Dependency;
using Core.Localization;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Data;
using Micalobia.Shapez2.FolderIcons.Features;
using Micalobia.Shapez2.FolderIcons.Services;
using ShapezShifter.SharpDetour;
using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.Object;
using ILogger = Core.Logging.ILogger;

namespace Micalobia.Shapez2.FolderIcons.HUD;

[UsedImplicitly]
public class EditDialogHandler(ILogger logger, FolderMetadataHandler metadataHandler, SortingHandler sortingHandler) : IInitHUD
{
    private static readonly TranslationId FavoriteLabelId = new("folder-icons.favorite");
    private static readonly TranslationId ArchivedLabelId = new("folder-icons.archived");

    [LoggerField] private ILogger Logger { get; } = logger;
    private FolderMetadataHandler MetadataHandler { get; } = metadataHandler;
    private SortingHandler SortingHandler { get; } = sortingHandler;

    private IDependencyResolver HUDDependencyResolver { get; set; }
    private HUDToggleControl TemplateToggle { get; set; }
    private BlueprintLibraryFolder CurrentFolderEdit { get; set; }
    private FolderMetadata CurrentMetadataEdit { get; set; }
    private Transform HeadingRow { get; set; }
    private Transform ToggleControls { get; set; }

    private bool ToggleControlVisibility
    {
        get => ToggleControls != null && ToggleControls.gameObject.activeSelf;
        set
        {
            if (ToggleControls == null)
                return;

            ToggleControls.gameObject.SetActiveSelfExt(value);
        }
    }

#pragma warning disable CS0618
    public void InitHUD(GameSessionOrchestrator orchestrator)
    {
        if (orchestrator.SavegameOptionsManager.Headless) return;
        HUDDependencyResolver = orchestrator.HUD.DependencyContainer;
        var researchTree = orchestrator.HUD.Parts.OfType<HUDResearchTree>().Single();
        TemplateToggle = researchTree.UITabContentsShop.UIToggleShowCompleted;
    }
#pragma warning restore CS0618

    private void HUDEditBlueprintLibraryEntryDialog_InitForExisting_Postfix(HUDEditBlueprintLibraryEntryDialog dialog, IBlueprintLibraryEntry entry)
    {
        if (entry is not BlueprintLibraryFolder folder)
        {
            ClearCurrentFolderEdit();
            return;
        }

        CurrentFolderEdit = folder;
        CurrentMetadataEdit = MetadataHandler.GetMetadata(folder).Copy();

        dialog.UIFolderIcon.gameObject.SetActiveSelfExt(false);
        dialog.UIIconConfigurator.Icon = CurrentMetadataEdit.GetBlueprintIcon;
        dialog.UIIconConfigurator.gameObject.SetActiveSelfExt(true);

        EnsureHeadingRow(dialog, CurrentMetadataEdit.Favorited, CurrentMetadataEdit.Archived);
        ToggleControlVisibility = true;
    }

    private (string, BlueprintLibraryFolder, BlueprintIcon) HUDEditBlueprintLibraryEntryDialog_InitForNew_Prefix(
        HUDEditBlueprintLibraryEntryDialog dialog,
        string blueprintName,
        BlueprintLibraryFolder parent,
        BlueprintIcon icon)
    {
        ClearCurrentFolderEdit();

        return (blueprintName, parent, icon);
    }

    private (string, BlueprintLibraryFolder) HUDEditBlueprintLibraryEntryDialog_InitForNewFolder_Prefix(
        HUDEditBlueprintLibraryEntryDialog dialog,
        string folderName,
        BlueprintLibraryFolder parent)
    {
        ClearCurrentFolderEdit();

        return (folderName, parent);
    }

    private void ClearCurrentFolderEdit()
    {
        CurrentFolderEdit = null;
        CurrentMetadataEdit = null;
        ToggleControlVisibility = false;
    }

    [UsedImplicitly]
    public sealed class HookAdapter(FolderIcons mod) : HookAdapterBase(mod)
    {
        protected override void Install()
        {
            Track(DetourHelper.CreatePostfixHook(
                (HUDEditBlueprintLibraryEntryDialog dialog, IBlueprintLibraryEntry entry) => dialog.InitForExisting(entry),
                HUDEditBlueprintLibraryEntryDialog_InitForExisting_Postfix
            ));
            Track(DetourHelper.CreatePrefixHook(
                (HUDEditBlueprintLibraryEntryDialog dialog, string blueprintName, BlueprintLibraryFolder parent, BlueprintIcon icon) =>
                    dialog.InitForNew(blueprintName, parent, icon),
                HUDEditBlueprintLibraryEntryDialog_InitForNew_Prefix
            ));
            Track(DetourHelper.CreatePrefixHook(
                (HUDEditBlueprintLibraryEntryDialog dialog, string folderName, BlueprintLibraryFolder parent) =>
                    dialog.InitForNewFolder(folderName, parent),
                HUDEditBlueprintLibraryEntryDialog_InitForNewFolder_Prefix
            ));
        }

        private void HUDEditBlueprintLibraryEntryDialog_InitForExisting_Postfix(HUDEditBlueprintLibraryEntryDialog dialog, IBlueprintLibraryEntry entry) =>
            Mod.ResolveSession<EditDialogHandler>().HUDEditBlueprintLibraryEntryDialog_InitForExisting_Postfix(dialog, entry);

        private (string, BlueprintLibraryFolder, BlueprintIcon) HUDEditBlueprintLibraryEntryDialog_InitForNew_Prefix(
            HUDEditBlueprintLibraryEntryDialog dialog,
            string blueprintName,
            BlueprintLibraryFolder parent,
            BlueprintIcon icon) =>
            Mod.ResolveSession<EditDialogHandler>().HUDEditBlueprintLibraryEntryDialog_InitForNew_Prefix(dialog, blueprintName, parent, icon);

        private (string, BlueprintLibraryFolder) HUDEditBlueprintLibraryEntryDialog_InitForNewFolder_Prefix(
            HUDEditBlueprintLibraryEntryDialog dialog,
            string folderName,
            BlueprintLibraryFolder parent) =>
            Mod.ResolveSession<EditDialogHandler>().HUDEditBlueprintLibraryEntryDialog_InitForNewFolder_Prefix(dialog, folderName, parent);
    }

    private void EnsureHeadingRow(HUDEditBlueprintLibraryEntryDialog dialog, bool favorited, bool archived)
    {
        const string headingRowName = "HeadingRow";
        const string toggleControlsName = "ToggleControls";

        // Find the heading's layout parent.
        var mainPanel = dialog.UICloseButton.transform.parent.parent;
        var existingHeadingRow = mainPanel.Find(headingRowName);
        if (existingHeadingRow != null)
        {
            HeadingRow = existingHeadingRow;
            ToggleControls = HeadingRow.Find(toggleControlsName);
            RefreshToggleControls(dialog, favorited, archived);
            return;
        }

        // Capture the current heading layout contract.
        var heading = mainPanel.Find("HUDHeading1");
        var headingRect = heading?.GetComponent<RectTransform>();
        var headingLayout = heading?.GetComponent<LayoutElement>();

        // Create the row that will own the heading slot.
        var headingRow = new GameObject(headingRowName, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        headingRow.transform.SetParent(mainPanel, false);
        HeadingRow = headingRow.transform;
        var headingRowRect = headingRow.GetComponent<RectTransform>();
        if (headingRect != null)
        {
            headingRowRect.anchorMin = headingRect.anchorMin;
            headingRowRect.anchorMax = headingRect.anchorMax;
            headingRowRect.pivot = headingRect.pivot;
            headingRowRect.sizeDelta = headingRect.sizeDelta;
            headingRowRect.anchoredPosition = headingRect.anchoredPosition;
        }

        // Move the visible heading into the row.
        if (heading != null)
        {
            headingRow.transform.SetSiblingIndex(heading.GetSiblingIndex());
            heading.transform.SetParent(headingRow.transform, false);
        }

        // Configure horizontal child layout.
        var layoutGroup = headingRow.GetComponent<HorizontalLayoutGroup>();
        layoutGroup.childAlignment = TextAnchor.MiddleLeft;
        layoutGroup.spacing = 32f;
        layoutGroup.childControlWidth = true;
        layoutGroup.childControlHeight = true;
        layoutGroup.childForceExpandWidth = false;
        layoutGroup.childForceExpandHeight = false;

        // Transfer layout responsibility from the heading to the row.
        var layoutElement = headingRow.GetComponent<LayoutElement>();
        if (headingLayout != null)
        {
            layoutElement.ignoreLayout = headingLayout.ignoreLayout;
            layoutElement.minWidth = headingLayout.minWidth;
            layoutElement.minHeight = headingLayout.minHeight;
            layoutElement.preferredWidth = headingLayout.preferredWidth;
            layoutElement.preferredHeight = headingLayout.preferredHeight;
            layoutElement.flexibleWidth = headingLayout.flexibleWidth;
            layoutElement.flexibleHeight = headingLayout.flexibleHeight;
            layoutElement.layoutPriority = headingLayout.layoutPriority;
        }

        var toggleControls = new GameObject(toggleControlsName, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        toggleControls.transform.SetParent(HeadingRow, false);
        ToggleControls = toggleControls.transform;
        {
            var toggleControlsLayoutGroup = toggleControls.GetComponent<HorizontalLayoutGroup>();
            var toggleControlsLayoutElement = toggleControls.GetComponent<LayoutElement>();
            toggleControlsLayoutGroup.childAlignment = TextAnchor.MiddleLeft;
            toggleControlsLayoutGroup.spacing = 32f;
            toggleControlsLayoutGroup.childControlWidth = true;
            toggleControlsLayoutGroup.childControlHeight = true;
            toggleControlsLayoutGroup.childForceExpandWidth = false;
            toggleControlsLayoutGroup.childForceExpandHeight = false;
            toggleControlsLayoutElement.minHeight = 50f;
            toggleControlsLayoutElement.preferredHeight = 50f;
        }

        // Use the existing folder label styling so these controls blend into the vanilla dialog.
        var labelTemplate = mainPanel.Find("Contents/LabelFolder/HUDHeading4");
        SetDeleteButtonInteractable(dialog.UIDeleteButton, !favorited);
        AddToggle(ToggleControls, labelTemplate, "Favorite", FavoriteLabelId, favorited, value => SetCurrentEditFavorite(dialog, value));
        AddToggle(ToggleControls, labelTemplate, "Archive", ArchivedLabelId, archived, SetCurrentEditArchived);
    }

    private void RefreshToggleControls(HUDEditBlueprintLibraryEntryDialog dialog, bool favorited, bool archived)
    {
        // Dialog instances can be reused, so existing toggles must point at the current folder.
        SetDeleteButtonInteractable(dialog.UIDeleteButton, !favorited);
        RefreshToggle("Favorite", favorited, value => SetCurrentEditFavorite(dialog, value));
        RefreshToggle("Archive", archived, SetCurrentEditArchived);
    }

    private void RefreshToggle(string name, bool value, Action<bool> onValueChanged)
    {
        var toggle = ToggleControls.Find($"{name}Control/{name}Toggle").GetComponent<HUDToggleControl>();
        BindToggleAction(toggle, value, onValueChanged);
    }

    private static void BindToggleAction(HUDToggleControl toggle, bool value, Action<bool> onValueChanged)
    {
        toggle._OnChanged.UnregisterAll();
        toggle.SetValueInstantSilent(value);
        toggle.OnChanged.Register(() => onValueChanged(toggle.Value));
    }

    private void AddToggle(Transform parent, Transform templateLabel, string name, TranslationId translation, bool value, Action<bool> onValueChanged)
    {
        if (HUDDependencyResolver == null)
            throw new InvalidOperationException("Folder edit dialog HUD dependencies were not initialized.");
        if (TemplateToggle == null)
            throw new InvalidOperationException("Folder edit dialog toggle template was not initialized.");

        var control = new GameObject($"{name}Control", typeof(RectTransform));
        control.transform.SetParent(parent, false);
        {
            var layoutGroup = control.AddComponent<HorizontalLayoutGroup>();
            var layoutElement = control.AddComponent<LayoutElement>();
            layoutGroup.childAlignment = TextAnchor.MiddleLeft;
            layoutGroup.spacing = 8f;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.childForceExpandWidth = false;
            layoutGroup.childForceExpandHeight = false;
            layoutElement.minHeight = 50f;
            layoutElement.preferredHeight = 50f;
        }

        var label = Instantiate(templateLabel.gameObject, control.transform, false);
        label.name = $"{name}Label";
        {
            var local = label.GetComponent<HUDLocalizedText>();
            var layoutElement = label.GetComponent<LayoutElement>() ?? label.AddComponent<LayoutElement>();
            local._Translation.Id = translation;
            HUDDependencyResolver.Inject(local);
            layoutElement.minHeight = 50f;
            layoutElement.preferredHeight = 50f;
        }

        var toggle = Instantiate(TemplateToggle, control.transform, false);
        toggle.name = $"{name}Toggle";
        {
            const float toggleWidth = 63f;
            const float toggleHeight = 45f;
            var layoutElement = toggle.gameObject.GetComponent<LayoutElement>() ?? toggle.gameObject.AddComponent<LayoutElement>();
            var rect = toggle.GetComponent<RectTransform>();
            layoutElement.minWidth = toggleWidth;
            layoutElement.preferredWidth = toggleWidth;
            layoutElement.minHeight = toggleHeight;
            layoutElement.preferredHeight = toggleHeight;
            rect.sizeDelta = new Vector2(toggleWidth, toggleHeight);
            toggle.gameObject.SetActiveSelfExt(true);

            HUDDependencyResolver.Inject(toggle);
            BindToggleAction(toggle, value, onValueChanged);
        }
    }

    private void SetCurrentEditFavorite(HUDEditBlueprintLibraryEntryDialog dialog, bool favorited)
    {
        if (CurrentMetadataEdit == null)
            throw new InvalidOperationException("No folder metadata edit is active.");

        CurrentMetadataEdit.Favorited = favorited;
        SetDeleteButtonInteractable(dialog.UIDeleteButton, !favorited);
    }

    private void SetCurrentEditArchived(bool archived)
    {
        if (CurrentMetadataEdit == null)
            throw new InvalidOperationException("No folder metadata edit is active.");

        CurrentMetadataEdit.Archived = archived;
    }

    private void SetDeleteButtonInteractable(HUDTimedButton deleteButton, bool interactable)
    {
        if (interactable) deleteButton.StartTimer(0.5f);
        else
        {
            deleteButton.StopTimer();
            deleteButton.Interactable = false;
        }
    }

    public void SaveFolderMetadataIfEditAccepted(
        HUDBlueprintLibrary hud,
        IBlueprintLibraryEntry entry,
        HUDEditBlueprintLibraryEntryDialog.EditResult result)
    {
        if (entry is not BlueprintLibraryFolder folder)
            return;

        if (CurrentFolderEdit != folder || CurrentMetadataEdit == null)
            return;

        if (!TryGetAcceptedEditParent(hud, folder, result, out var parent))
            return;

        var currentMetadata = MetadataHandler.GetMetadata(folder);
        var serializedIcon = result.Icon.Serialize();
        var iconChanged = !AreSerializedIconsEqual(currentMetadata.Icon, serializedIcon);
        var favoritedChanged = currentMetadata.Favorited != CurrentMetadataEdit.Favorited;
        var archivedChanged = currentMetadata.Archived != CurrentMetadataEdit.Archived;

        CurrentMetadataEdit.Icon = serializedIcon;
        MetadataHandler.SetMetadata(folder, CurrentMetadataEdit);

        if (archivedChanged && !SortingHandler.ShowArchivedFolders)
        {
            hud.BlueprintLibrary.Refresh();
            CurrentFolderEdit = null;
            CurrentMetadataEdit = null;
            return;
        }

        if (iconChanged || favoritedChanged || archivedChanged)
            folder._MetadataChanged.Invoke();

        if (favoritedChanged || archivedChanged)
        {
            SortingHandler.SortChildren(parent);
            parent._ChildListChanged.Invoke();
        }

        if (iconChanged && hud.BlueprintLibrary is BlueprintLibrary blueprintLibrary)
            blueprintLibrary.RefreshToolbarIfEntryIsContained(folder);

        CurrentFolderEdit = null;
        CurrentMetadataEdit = null;
    }

    // ReSharper disable once ConvertIfStatementToReturnStatement
    private static bool AreSerializedIconsEqual(SerializedBlueprintIcon left, SerializedBlueprintIcon right)
    {
        if (left?.Data == null) return right?.Data == null;
        if (right?.Data == null) return false;
        if (left.Data.Length != right.Data.Length) return false;
        return left.Data.Zip(right.Data, (l, r) => l == r).All(equal => equal);
    }

    private static bool TryGetAcceptedEditParent(
        HUDBlueprintLibrary hud,
        BlueprintLibraryFolder folder,
        HUDEditBlueprintLibraryEntryDialog.EditResult result,
        out BlueprintLibraryFolder parent)
    {
        parent = null;

        if (folder.Title != result.Name)
            return false;

        if (hud.BlueprintLibrary?.RootEntry?.TryFindParentFolder(folder, out var currentParent) != true)
            return false;

        if (currentParent != result.Parent)
            return false;

        parent = currentParent;
        return true;
    }
}
