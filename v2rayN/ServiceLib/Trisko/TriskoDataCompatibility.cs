using System.Text.Json.Nodes;

namespace ServiceLib.Trisko;

/// <summary>Reads data written by Trisko connect 0.1.0 before its internal names were updated.</summary>
internal static class TriskoDataCompatibility
{
    // Persisted IDs/columns are deliberately stable; these are not product or source-code names.
    internal const string ManagedProfileId = "gek-sa-corporate-v3";
    internal const string TrafficUploadColumn = "GeksaTrafficUpload";
    internal const string TrafficDownloadColumn = "GeksaTrafficDownload";
    internal const string TrafficTotalColumn = "GeksaTrafficTotal";
    internal const string TrafficExpireColumn = "GeksaTrafficExpire";
    internal const string TrafficUpdatedColumn = "GeksaTrafficUpdatedAt";

    internal static void PrepareFiles(string directory)
    {
        foreach (var (previous, current) in new (string, string)[]
        {
            ("gek-sa-v3-active.json", "trisko-v3-active.json"),
            ("gek-sa-v3-user-resources.json", "trisko-v3-user-resources.json"),
            ("geksa-vnc-client.json", "trisko-vnc-client.json")
        })
        {
            var source = Path.Combine(directory, previous);
            var destination = Path.Combine(directory, current);
            if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination);
        }
    }

    internal static string NormalizeConfigJson(string json)
    {
        if (!json.Contains("geksa", StringComparison.OrdinalIgnoreCase)) return json;
        try
        {
            var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            RenameKeys(root);
            return root?.ToJsonString() ?? json;
        }
        catch (JsonException) { return json; } // The normal configuration reader reports malformed input.
    }

    private static void RenameKeys(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj.ToList())
            {
                RenameKeys(value);
                if (!key.StartsWith("Geksa", StringComparison.OrdinalIgnoreCase)) continue;
                var renamed = (char.IsLower(key[0]) ? "trisko" : "Trisko") + key[5..];
                obj.Remove(key);
                if (!obj.ContainsKey(renamed)) obj[renamed] = value;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array) RenameKeys(child);
        }
    }
}
