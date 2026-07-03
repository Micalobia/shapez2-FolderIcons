using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Services;
using ShapezShifter.Kit;
using UnityEngine;
using ILogger = Core.Logging.ILogger;
using Object = UnityEngine.Object;

namespace Micalobia.Shapez2.FolderIcons.Data;

[UsedImplicitly]
public class AssetHandler(ILogger logger) : ISessionService, IDisposable
{
    private readonly Dictionary<string, SpriteAsset> _spriteCache = [];

    private ILogger Logger { get; } = logger;

    private string AssetRootPath { get; } = Path.Combine(ModDirectoryLocator.GetDirectoryLocation<FolderIcons>(), "assets");

    public bool TryGetSprite(string relativePath, out Sprite sprite)
    {
        if (_spriteCache.TryGetValue(relativePath, out var cached))
        {
            sprite = cached.Sprite;
            return true;
        }

        var assetPath = Path.Combine(AssetRootPath, relativePath);
        if (!File.Exists(assetPath))
        {
            Logger.Warning?.Log($"Asset was not found: {assetPath}");
            sprite = null;
            return false;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = Path.GetFileNameWithoutExtension(assetPath),
        };

        if (!texture.LoadImage(File.ReadAllBytes(assetPath)))
        {
            Object.Destroy(texture);
            Logger.Warning?.Log($"Asset could not be loaded as an image: {assetPath}");
            sprite = null;
            return false;
        }

        sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f
        );
        sprite.name = Path.GetFileNameWithoutExtension(assetPath);
        _spriteCache[relativePath] = new SpriteAsset(texture, sprite);
        return true;
    }

    public void Dispose()
    {
        foreach (var asset in _spriteCache.Values)
        {
            if (asset.Sprite != null)
                Object.Destroy(asset.Sprite);
            if (asset.Texture != null)
                Object.Destroy(asset.Texture);
        }

        _spriteCache.Clear();
    }

    private readonly struct SpriteAsset(Texture2D texture, Sprite sprite)
    {
        public Texture2D Texture { get; } = texture;
        public Sprite Sprite { get; } = sprite;
    }
}
