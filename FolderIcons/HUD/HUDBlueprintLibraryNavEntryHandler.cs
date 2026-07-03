using System;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Data;
using Micalobia.Shapez2.FolderIcons.Features;
using Micalobia.Shapez2.FolderIcons.Services;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using ShapezShifter.SharpDetour;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static Micalobia.Shapez2.FolderIcons.HookHelper;
using ILogger = Core.Logging.ILogger;

namespace Micalobia.Shapez2.FolderIcons.HUD;

[UsedImplicitly]
public class HUDBlueprintLibraryNavEntryHandler(ILogger logger, FolderMetadataHandler metadataHandler, AssetHandler assetHandler) : ISessionService
{
    private const string BADGE_ROOT_NAME = "BadgeRow";

    private static readonly BadgeDefinition[] BadgeDefinitions =
    [
        new("Archived", "badges/archived.png", metadata => metadata.Archived, new Color(1f, 0.55f, 0.1f, 0.8f)),
        new("Favorite", "badges/favorite.png", metadata => metadata.Favorited, new Color(1f, 0.8f, 0.25f, 0.8f)),
    ];

    [LoggerField] private ILogger Logger { get; } = logger;
    private FolderMetadataHandler MetadataHandler { get; } = metadataHandler;
    private AssetHandler AssetHandler { get; } = assetHandler;

    private void ApplyFolderIcon(HUDBlueprintLibraryNavEntry navEntry, BlueprintLibraryFolder folder)
    {
        var metadata = MetadataHandler.GetMetadata(folder);
        if (metadata.HasIcon)
        {
            navEntry.UIFolderIcon.gameObject.SetActiveSelfExt(false);
            navEntry.UIIconRenderer.Icon = metadata.GetBlueprintIcon;
            navEntry.UIIconRenderer.gameObject.SetActiveSelfExt(true);
            return;
        }

        navEntry.UIFolderIcon.sprite = folder.Children.Count > 0 ? navEntry.UISpriteFolder : navEntry.UISpriteFolderEmpty;
        navEntry.UIFolderIcon.gameObject.SetActiveSelfExt(true);
        navEntry.UIIconRenderer.gameObject.SetActiveSelfExt(false);
    }

    private void ApplyBadges(HUDBlueprintLibraryNavEntry navEntry)
    {
        if (navEntry._Entry is not BlueprintLibraryFolder folder)
        {
            SetBadgeRootVisible(navEntry.transform, false);
            return;
        }

        ApplyFolderBadges(navEntry.transform, MetadataHandler.GetMetadata(folder));
        ConfigureNavEntryListeners(navEntry.transform);
    }

    private void ApplyFolderBadges(Transform parent, FolderMetadata metadata)
    {
        var root = GetOrCreateBadgeRoot(parent);
        var anyVisible = false;

        foreach (var definition in BadgeDefinitions)
        {
            var image = GetOrCreateBadgeImage(root, definition);
            var visible = definition.IsVisible(metadata);
            if (visible)
            {
                visible = AssetHandler.TryGetSprite(definition.IconPath, out var sprite);
                if (visible)
                {
                    image.sprite = sprite;
                    image.color = definition.Color;
                }
            }

            image.gameObject.SetActiveSelfExt(visible);
            anyVisible |= visible;
        }

        root.gameObject.SetActiveSelfExt(anyVisible);
    }

    private RectTransform GetOrCreateBadgeRoot(Transform parent)
    {
        var existing = parent.Find(BADGE_ROOT_NAME);
        if (existing != null)
            return (RectTransform)existing;

        var root = new GameObject(BADGE_ROOT_NAME, typeof(RectTransform), typeof(CanvasGroup), typeof(HorizontalLayoutGroup), typeof(HUDBadgeRowFI));
        root.transform.SetParent(parent, false);

        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-24f, 0f);
        rect.sizeDelta = new Vector2(48f, 22f);

        var layout = root.GetComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperRight;
        layout.spacing = 2f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        root.GetComponent<HUDBadgeRowFI>().Init(root.GetComponent<CanvasGroup>(), 0.5f, 0.75f, 1.0f);
        return rect;
    }

    private Image GetOrCreateBadgeImage(RectTransform root, BadgeDefinition definition)
    {
        var badgeName = definition.Name + "Badge";
        var existing = root.Find(badgeName);
        if (existing != null)
        {
            var existingImage = existing.GetComponent<Image>();
            return existingImage;
        }

        var badge = new GameObject(badgeName, typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement), typeof(Image));
        badge.transform.SetParent(root, false);

        var layout = badge.GetComponent<LayoutElement>();
        layout.minWidth = 20f;
        layout.preferredWidth = 20f;
        layout.minHeight = 20f;
        layout.preferredHeight = 20f;

        var image = badge.GetComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;

        return image;
    }

    private static void ConfigureNavEntryListeners(Transform navEntry)
    {
        var badgeRow = navEntry.Find(BADGE_ROOT_NAME)?.GetComponent<HUDBadgeRowFI>();
        if (badgeRow == null)
            return;

        var hoverListener = navEntry.GetComponent<NavEntryHoverListenerFI>() ?? navEntry.gameObject.AddComponent<NavEntryHoverListenerFI>();
        hoverListener.BadgeRow = badgeRow;

        var selected = navEntry.Find("Selected");
        if (selected == null)
            return;

        var selectedListener = selected.GetComponent<NavEntrySelectedListenerFI>() ?? selected.gameObject.AddComponent<NavEntrySelectedListenerFI>();
        selectedListener.BadgeRow = badgeRow;
        badgeRow.SetSelected(selected.gameObject.activeSelf);
    }

    private static void SetBadgeRootVisible(Transform parent, bool visible)
    {
        var root = parent.Find(BADGE_ROOT_NAME);
        if (root != null)
            root.gameObject.SetActiveSelfExt(visible);
    }

    private readonly struct BadgeDefinition(string name, string iconPath, Func<FolderMetadata, bool> isVisible, Color color)
    {
        public string Name { get; } = name;
        public string IconPath { get; } = iconPath;
        public Func<FolderMetadata, bool> IsVisible { get; } = isVisible;
        public Color Color { get; } = color;
    }

    private sealed class HUDBadgeRowFI : MonoBehaviour
    {
        private CanvasGroup CanvasGroup { get; set; }
        private bool Hovered { get; set; }
        private bool Selected { get; set; }
        private float NormalAlpha { get; set; }
        private float HoveredAlpha { get; set; }
        private float SelectedAlpha { get; set; }

        public void Init(CanvasGroup canvasGroup, float normalAlpha, float hoveredAlpha, float selectedAlpha)
        {
            CanvasGroup = canvasGroup;
            NormalAlpha = normalAlpha;
            HoveredAlpha = hoveredAlpha;
            SelectedAlpha = selectedAlpha;
            ApplyAlpha();
        }

        public void SetHovered(bool hovered)
        {
            if (Hovered == hovered)
                return;

            Hovered = hovered;
            ApplyAlpha();
        }

        public void SetSelected(bool selected)
        {
            if (Selected == selected)
                return;

            Selected = selected;
            ApplyAlpha();
        }

        private void ApplyAlpha()
        {
            if (CanvasGroup == null)
                return;

            CanvasGroup.alpha = Selected ? SelectedAlpha : Hovered ? HoveredAlpha : NormalAlpha;
        }
    }

    private sealed class NavEntryHoverListenerFI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public HUDBadgeRowFI BadgeRow { get; set; }

        public void OnPointerEnter(PointerEventData eventData) => BadgeRow?.SetHovered(true);

        public void OnPointerExit(PointerEventData eventData) => BadgeRow?.SetHovered(false);
    }

    private sealed class NavEntrySelectedListenerFI : MonoBehaviour
    {
        public HUDBadgeRowFI BadgeRow { get; set; }

        private void OnEnable() => BadgeRow?.SetSelected(true);

        private void OnDisable() => BadgeRow?.SetSelected(false);
    }

    [UsedImplicitly]
    public sealed class HookAdapter(FolderIcons mod) : HookAdapterBase(mod)
    {
        protected override void Install()
        {
            Track(CreateILHook<HUDBlueprintLibraryNavEntry>(
                nameof(HUDBlueprintLibraryNavEntry.RebuildView),
                HUDBlueprintLibraryNavEntry_RebuildView_IL
            ));
            Track(DetourHelper.CreatePostfixHook(
                (HUDBlueprintLibraryNavEntry navEntry) => navEntry.RebuildView(),
                HUDBlueprintLibraryNavEntry_RebuildView_Postfix
            ));
        }

        private void HUDBlueprintLibraryNavEntry_RebuildView_IL(ILContext ctx)
        {
            VariableDefinition folderLocal = null;
            var cursor = new ILCursor(ctx);

            // Find the vanilla nav entry folder icon block: choose folder sprite, show folder icon, hide blueprint icon.
            if (!cursor.TryGotoNext(
                    MoveType.Before,
                    instruction => instruction.MatchLdarg0(),
                    instruction => instruction.MatchLdfld<HUDBlueprintLibraryNavEntry>(nameof(HUDBlueprintLibraryNavEntry.UIFolderIcon)),
                    instruction => instruction.MatchLdloc<BlueprintLibraryFolder>(ctx, out folderLocal),
                    instruction => instruction.MatchGetter<BlueprintLibraryFolder>(nameof(BlueprintLibraryFolder.Children))))
                throw new InvalidOperationException("Could not find the folder nav entry icon block in HUDBlueprintLibraryNavEntry.RebuildView.");

            var setActivePredicate = (Instruction instruction) => instruction.MatchCall(nameof(CustomUnityExtensions), nameof(CustomUnityExtensions.SetActiveSelfExt));

            // Replace it with ApplyFolderIcon(navEntry, folder)
            cursor.RemoveThroughNext(setActivePredicate, setActivePredicate, setActivePredicate);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldloc, folderLocal);
            cursor.EmitDelegate<Action<HUDBlueprintLibraryNavEntry, BlueprintLibraryFolder>>((navEntry, folder) =>
                Mod.ResolveSession<HUDBlueprintLibraryNavEntryHandler>().ApplyFolderIcon(navEntry, folder));
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, GetField<HUDBlueprintLibraryNavEntry>(nameof(HUDBlueprintLibraryNavEntry.UIFolderIndicator)));
            cursor.EmitTrue();
            cursor.EmitDelegate<Action<GameObject, bool>>((gameObject, active) => gameObject.SetActiveSelfExt(active));
        }

        private void HUDBlueprintLibraryNavEntry_RebuildView_Postfix(HUDBlueprintLibraryNavEntry navEntry) =>
            Mod.ResolveSession<HUDBlueprintLibraryNavEntryHandler>().ApplyBadges(navEntry);
    }
}
