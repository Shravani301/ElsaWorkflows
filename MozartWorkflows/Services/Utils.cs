using System.Text.Json.Nodes;
using System.Text.Json;

namespace MozartWorkflows.Services
{
    public static class Utils
    {
        private static readonly JsonSerializerOptions UnsafeJsonSerializerOptions = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static JsonNode? ParseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                json = json.Replace("\r", string.Empty).Replace("\n", string.Empty);
                return JsonNode.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid JSON string provided.", ex);
            }
        }

        public static JsonArray JsonNodeToArray(JsonNode jn)
        {
            if (jn is JsonArray jsonArray)
                return jsonArray;

            if (jn is JsonObject obj)
            {
                var arr = new JsonArray();
                arr.Add(obj);
                return arr;
            }

            if (jn == null)
                return new JsonArray();

            var single = new JsonArray();
            single.Add(jn);
            return single;
        }

        public static string Stringify(object json)
        {
            if (json == null)
                return "null";

            if (json is JsonNode jsonNode)
                return jsonNode.ToString();

            return JsonSerializer.Serialize(json, UnsafeJsonSerializerOptions);
        }

        public static JsonNode UpdateJsonNode(JsonNode jsonNode, JsonNode updates)
        {
            ArgumentNullException.ThrowIfNull(jsonNode);
            ArgumentNullException.ThrowIfNull(updates);

            foreach (var updateNode in updates.AsArray())
            {
                if (updateNode is not JsonObject update) continue;
                ApplyUpdate(jsonNode, update);
            }

            return jsonNode;
        }

        private static void ApplyUpdate(JsonNode jsonNode, JsonObject update)
        {
            string path = update["path"]?.ToString() ?? string.Empty;
            JsonNode? finalValue = PrepareValueNode(update["value"]);
            string[] parts = path.Split('.');
            JsonObject current = jsonNode as JsonObject ?? throw new InvalidOperationException("Root node must be a JsonObject");

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];

                if (i == parts.Length - 1)
                {
                    current[part] = CloneNode(finalValue);
                    continue;
                }

                if (current[part] is JsonObject next)
                {
                    current = next;
                }
                else
                {
                    var newObj = new JsonObject();
                    current[part] = newObj;
                    current = newObj;
                }
            }
        }

        private static JsonNode? PrepareValueNode(JsonNode? valueNode)
        {
            if (valueNode == null)
                return null;

            if (valueNode is JsonObject || valueNode is JsonArray)
                return valueNode;

            string str = valueNode.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(str))
                return JsonValue.Create(str);

            if (decimal.TryParse(str, out decimal decimalValue))
                return JsonValue.Create(decimalValue);

            if (LooksLikeArray(str))
            {
                var parsedArray = TryParseArray(str);
                if (parsedArray != null)
                    return NormalizeArrayItems(parsedArray);
            }

            if (LooksLikeJson(str))
            {
                try
                {
                    return JsonNode.Parse(str);
                }
                catch
                {
                    // If parsing fails, keep the original string value.
                }
            }

            return JsonValue.Create(str);
        }

        private static JsonArray? TryParseArray(string value)
        {
            try
            {
                return JsonNode.Parse(value) as JsonArray;
            }
            catch
            {
                // Non-array input should fall back to scalar processing.
                return null;
            }
        }

        private static JsonArray NormalizeArrayItems(JsonArray parsedArray)
        {
            var resultArray = new JsonArray();
            foreach (var item in parsedArray)
            {
                resultArray.Add(NormalizeArrayItem(item));
            }

            return resultArray;
        }

        private static JsonNode? NormalizeArrayItem(JsonNode? item)
        {
            if (item is not JsonValue val)
                return item;

            string itemString = val.ToString().Trim();
            if (!LooksLikeJson(itemString))
                return val;

            try
            {
                return JsonNode.Parse(itemString);
            }
            catch
            {
                // Keep the original scalar if the nested JSON fragment is invalid.
                return val;
            }
        }

        private static bool LooksLikeArray(string value) => value.StartsWith('[') && value.EndsWith(']');

        private static bool LooksLikeJson(string value) =>
            (value.StartsWith('{') && value.EndsWith('}')) || LooksLikeArray(value);

        public static string CleanString(object? value)
        {
            return value?.ToString()?.Trim('"').Trim() ?? string.Empty;
        }

        private static JsonNode? CloneNode(JsonNode? node)
        {
            if (node == null) return null;
            return JsonNode.Parse(node.ToJsonString());
        }

        public static decimal ParseDecimal(JsonNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            string strValue = CleanString(node);

            if (decimal.TryParse(strValue, out decimal result))
                return result;

            throw new ArgumentException($"Cannot convert '{strValue}' to decimal.");
        }
    }
}
