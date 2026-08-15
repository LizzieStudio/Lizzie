using System.Text.Json;

public static class LizzieJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new ColorJsonConverter(), new Vector3JsonConverter() },
    };
}
