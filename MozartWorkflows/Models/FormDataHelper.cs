using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MozartWorkflows.Models
{
    public static class FormDataHelper
    {
        // Keep a single regex to sanitize section titles into identifier-safe form.
        private static readonly Regex NonWord = new(@"[^\p{L}\p{Nd} ]+", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

        public static IDictionary<string, object?> Flatten(string caseDataJson)
        {
            var flat = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(caseDataJson))
                return flat;

            var sections = JsonConvert.DeserializeObject<List<FormSection>>(caseDataJson);
            if (sections == null || sections.Count == 0)
                return flat;

            foreach (var section in sections)
            {
                if (section?.Controls == null) continue;

                // 1) Make a safe SectionTitle for property names (no spaces/symbols)
                var rawTitle = section.SectionTitle ?? string.Empty;
                var titleNoSymbols = NonWord.Replace(rawTitle, "");    // keep letters/digits/spaces
                var sectionSafe = (titleNoSymbols.Replace(" ", ""));   // remove spaces → HospitalizationDetails

                foreach (var control in section.Controls)
                {
                    if (string.IsNullOrWhiteSpace(control?.Name))
                        continue;

                    var value = ConvertValue(control.Value, control.Type);

                    // 2) Composite key: SectionTitle_ControlName (valid identifier for dynamic rules)
                    var compositeKey = $"{sectionSafe}_{control.Name}"; // e.g., HospitalizationDetails_administrativeCharges
                    flat[compositeKey] = value;

                    // 3) Back-compat simple key (optional): control name alone
                    //    This lets current rules that use formParams.administrativeCharges keep working
                    if (!flat.ContainsKey(control.Name))
                        flat[control.Name] = value;
                }
            }

            return flat;
        }

        /// <summary>
        /// Converts a raw value into a strongly typed value based on control type.
        /// Ensures numerics are numerics so rules like '>' work.
        /// </summary>
#pragma warning disable S3776
        private static object? ConvertValue(object? rawValue, string? typeHint)
        {
            if (rawValue == null) return null;

            if (rawValue is JValue jv)
            {
                rawValue = jv.Value; // unwrap
                if (rawValue == null) return null;
            }

            // if value is already numeric/bool/DateTime, keep it
            if (rawValue is int || rawValue is long || rawValue is float || rawValue is double || rawValue is decimal
                || rawValue is bool || rawValue is DateTime)
                return rawValue;

            var str = rawValue.ToString()?.Trim();
            if (string.IsNullOrEmpty(str)) return null;

            // Strong hint first
            switch ((typeHint ?? "").ToLowerInvariant())
            {
                case "date":
                    if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d1))
                        return d1;
                    break;

                case "number":
                    if (decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec1)) return dec1;
                    if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var dbl1)) return dbl1;
                    if (long.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var lng1)) return lng1;
                    if (int.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var i1)) return i1;
                    break;
            }

            // Fallback inference
            if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d2)) return d2;
            if (bool.TryParse(str, out var b)) return b;
            if (decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec2)) return dec2;
            if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var dbl2)) return dbl2;
            if (long.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var lng2)) return lng2;
            if (int.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var i2)) return i2;

            return str; // keep original string if nothing else matched
        }
#pragma warning restore S3776

        private sealed class FormSection
        {
            public string SectionTitle { get; set; } = string.Empty;
            public List<FormControl> Controls { get; set; } = new();
        }

        private sealed class FormControl
        {
            public string Label { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public object? Value { get; set; } = null;
        }
    }
}
