using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using JetBrains.Annotations;
using Micalobia.Shapez2.FolderIcons.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Micalobia.Shapez2.FolderIcons.Data;

[UsedImplicitly]
public class FolderMetadataSerializer : ISessionService
{
    private const string PREFIX = "SHAPEZ2-FOLDER-";
    private const string SUFFIX = "$";
    private const int VERSION = 1;

    public string Serialize(FolderMetadata metadata)
    {
        var json = JsonConvert.SerializeObject(metadata, SavegameSerializer.JsonSettings);
        var jsonBytes = SavegameSerializer.Encoding.GetBytes(json);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true)) gzip.Write(jsonBytes, 0, jsonBytes.Length);

        return PREFIX + VERSION + "-" + Convert.ToBase64String(output.ToArray()) + SUFFIX;
    }

    public FolderMetadata Deserialize(string serializedMetadata)
    {
        serializedMetadata = serializedMetadata.Trim();
        if (!serializedMetadata.StartsWith(PREFIX))
            throw new FormatException($"Folder metadata is missing the {PREFIX} prefix.");
        if (!serializedMetadata.EndsWith(SUFFIX))
            throw new FormatException("Folder metadata is missing the suffix.");

        var divider = serializedMetadata.IndexOf("-", PREFIX.Length, StringComparison.Ordinal);
        if (divider < 0)
            throw new FormatException("Folder metadata is missing the version divider.");

        var versionText = serializedMetadata.Substring(PREFIX.Length, divider - PREFIX.Length);
        if (!int.TryParse(versionText, out var version))
            throw new FormatException("Folder metadata version is invalid.");
        if (version != VERSION)
            throw new FormatException("Unsupported folder metadata version: " + version);

        var contentBase64 = serializedMetadata.Substring(divider + 1, serializedMetadata.Length - divider - 1 - SUFFIX.Length);
        var compressedBytes = Convert.FromBase64String(contentBase64);

        using var input = new MemoryStream(compressedBytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);

        var json = SavegameSerializer.Encoding.GetString(output.ToArray());
        return DeserializeMetadata(JObject.Parse(json));
    }

    private static FolderMetadata DeserializeMetadata(JObject json)
    {
        var metadata = new FolderMetadata();
        var remainder = new Dictionary<string, JToken>();
        var serializer = JsonSerializer.Create(SavegameSerializer.JsonSettings);

        foreach (var property in json.Properties())
            switch (property.Name)
            {
                case "Icon":
                    TrySetIcon(metadata, property.Value, serializer);
                    break;
                case nameof(FolderMetadata.Favorited):
                    metadata.Favorited = TryReadBoolean(property.Value, serializer, false);
                    break;
                case nameof(FolderMetadata.Archived):
                    metadata.Archived = TryReadBoolean(property.Value, serializer, false);
                    break;
                default:
                    remainder[property.Name] = property.Value.DeepClone();
                    break;
            }

        metadata.SetRemainder(remainder);
        return metadata;
    }

    private static void TrySetIcon(FolderMetadata metadata, JToken value, JsonSerializer serializer)
    {
        try
        {
            metadata.SetSerializedIconOrDefault(value.ToObject<SerializedBlueprintIcon>(serializer));
        }
        catch
        {
            metadata.Icon = null;
        }
    }

    private static bool TryReadBoolean(JToken value, JsonSerializer serializer, bool defaultValue)
    {
        try
        {
            return value.ToObject<bool>(serializer);
        }
        catch
        {
            return defaultValue;
        }
    }
}
