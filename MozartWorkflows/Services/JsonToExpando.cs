using System.Dynamic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MozartWorkflows.Services
{
    public static class JsonToExpando
    {
        public static ExpandoObject Convert(JsonNode? node)
        {
            if (node == null) return new ExpandoObject();

            if (node is JsonObject obj)
            {
                var result = new ExpandoObject();
                var dict = (IDictionary<string, object?>)result;
                foreach (var kvp in obj)
                {
                    dict[kvp.Key] = ConvertNode(kvp.Value);
                }
                return result;
            }

            var wrapper = new ExpandoObject();
            ((IDictionary<string, object?>)wrapper)["value"] = ConvertNode(node);
            return wrapper;
        }

        private static object? ConvertNode(JsonNode? node)
        {
            if (node == null) return null;

            switch (node)
            {
                case JsonValue jv:
                    return ConvertValue(jv);

                case JsonObject obj:
                    var eo = new ExpandoObject();
                    var dict = (IDictionary<string, object?>)eo;
                    foreach (var kvp in obj)
                    {
                        dict[kvp.Key] = ConvertNode(kvp.Value);
                    }
                    return eo;

                case JsonArray arr:
                    var list = new List<object?>();
                    foreach (var item in arr)
                    {
                        list.Add(ConvertNode(item));
                    }
                    return list;

                default:
                    return node.ToString();
            }
        }

        private static object? ConvertValue(JsonValue jv)
        {
            var s = jv.ToString();
            if (string.IsNullOrWhiteSpace(s)) return s;
            if (bool.TryParse(s, out var b)) return b;
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
            if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)) return dt;


            return s;
        }
    }
}
