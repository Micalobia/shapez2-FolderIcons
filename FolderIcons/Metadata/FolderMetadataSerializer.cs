using System;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;

namespace FolderIcons.Metadata;

public static class FolderMetadataSerializer
{
    private const string PREFIX = "SHAPEZ2-FOLDER-";
    private const string SUFFIX = "$";
    private const int VERSION = 1;

    private class SerializedPayload
    {
        public int V;
        public FolderMetadata Metadata;
    }

    public static string Serialize(FolderMetadata metadata)
    {
        var payload = new SerializedPayload
        {
            V = VERSION,
            Metadata = metadata,
        };

        var json = JsonConvert.SerializeObject(payload, SavegameSerializer.JsonSettings);
        var jsonBytes = SavegameSerializer.Encoding.GetBytes(json);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(jsonBytes, 0, jsonBytes.Length);

        return PREFIX + VERSION + "-" + Convert.ToBase64String(output.ToArray()) + SUFFIX;
    }

    public static FolderMetadata Deserialize(string serializedMetadata)
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
        var payload = JsonConvert.DeserializeObject<SerializedPayload>(json, SavegameSerializer.JsonSettings);
        return payload?.Metadata ?? new FolderMetadata();
    }
}