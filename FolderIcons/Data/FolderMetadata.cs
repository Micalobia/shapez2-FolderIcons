using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Micalobia.Shapez2.FolderIcons.Data;

public class FolderMetadata
{
    private static readonly BlueprintIcon DefaultIcon = new(new IBlueprintIconComponent[4]);

    public SerializedBlueprintIcon Icon
    {
        get;
        set => field = value ?? DefaultIcon.Serialize();
    } = DefaultIcon.Serialize();

    [JsonExtensionData] private IDictionary<string, JToken> _remainder = new Dictionary<string, JToken>();

    [JsonIgnore] public BlueprintIcon GetBlueprintIcon => Icon?.Data == null ? DefaultIcon : BlueprintIcon.FromSerialized(Icon);

    public bool Favorited { get; set; }
    public bool Archived { get; set; }

    [JsonIgnore] public bool HasIcon => Icon?.Data.Any(str => str is not null && str != "icon:Empty") == true;

    [JsonIgnore] public bool HasExtraData => _remainder.Count > 0;

    [JsonIgnore] public bool IsDefault => !HasIcon && !Favorited && !Archived;

    public void SetSerializedIconOrDefault(SerializedBlueprintIcon icon)
    {
        if (icon?.Data == null)
        {
            Icon = DefaultIcon.Serialize();
            return;
        }

        Icon = icon;
    }

    public void SetRemainder(IDictionary<string, JToken> remainder) => _remainder = remainder ?? new Dictionary<string, JToken>();

    public FolderMetadata Copy() => new()
    {
        Icon = new SerializedBlueprintIcon
        {
            Data = (Icon?.Data ?? DefaultIcon.Serialize().Data).ToArray(),
        },
        Favorited = Favorited,
        Archived = Archived,
        _remainder = _remainder.ToDictionary(
            pair => pair.Key,
            pair => pair.Value?.DeepClone()
        ),
    };
}
