using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace MediaTrip.Persistence
{
    /// <summary>
    /// The one place JSON settings live. camelCase properties, camelCase string enums, nulls
    /// written explicitly, dates left as plain strings (never auto-parsed to DateTime, which
    /// would silently reformat "2026-09-14" on the way back out).
    /// </summary>
    public static class TripJson
    {
        public static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Include,
                DefaultValueHandling = DefaultValueHandling.Include,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                Culture = CultureInfo.InvariantCulture,
            };
            settings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
            return settings;
        }

        public static JsonSerializer CreateSerializer() => JsonSerializer.Create(CreateSettings());

        /// <summary>Parse text to a JObject without date auto-detection.</summary>
        public static JObject ParseObject(string json)
        {
            using (var sr = new StringReader(json))
            using (var reader = new JsonTextReader(sr)
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                Culture = CultureInfo.InvariantCulture,
            })
            {
                var token = JToken.ReadFrom(reader);
                if (!(token is JObject obj))
                    throw new JsonSerializationException("Expected a JSON object at the document root, got " + token.Type);
                // Guard against trailing garbage.
                if (reader.Read())
                    throw new JsonSerializationException("Unexpected content after the JSON document root.");
                return obj;
            }
        }

        public static T FromJObject<T>(JObject obj) => obj.ToObject<T>(CreateSerializer());

        public static T Deserialize<T>(string json) => FromJObject<T>(ParseObject(json));

        public static JObject ToJObject(object value) => JObject.FromObject(value, CreateSerializer());

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, CreateSettings());

        /// <summary>Deep copy through JSON (objects or lists). Handy for duplicating entities and for tests.</summary>
        public static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(Serialize(value), CreateSettings());

        // ------------------------------------------------------------------
        // Semantic comparison
        // ------------------------------------------------------------------

        /// <summary>
        /// Canonical form for semantic comparison: object properties sorted; properties whose
        /// value is null, an empty array, or an object with nothing left in it are removed
        /// (absent, null, [] and {} all mean "none" for every optional field in this schema);
        /// array order kept; integer-valued floats folded to integers.
        /// </summary>
        public static JToken Canonicalize(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                {
                    var result = new JObject();
                    foreach (var prop in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        if (prop.Value == null || prop.Value.Type == JTokenType.Null) continue;
                        if (prop.Value is JArray arr && arr.Count == 0) continue;
                        var value = Canonicalize(prop.Value);
                        if (value is JObject o && o.Count == 0) continue;
                        result.Add(prop.Name, value);
                    }
                    return result;
                }
                case JArray arr:
                {
                    var result = new JArray();
                    foreach (var item in arr) result.Add(Canonicalize(item));
                    return result;
                }
                case JValue val:
                    // Fold integer-valued floats into integers so 5.0 == 5.
                    if (val.Type == JTokenType.Float && val.Value is double d && Math.Floor(d) == d && Math.Abs(d) < 1e15)
                        return new JValue((long)d);
                    return new JValue(val.Value);
                default:
                    return token.DeepClone();
            }
        }

        public static bool SemanticallyEqual(string jsonA, string jsonB, out string firstDifference)
        {
            var a = Canonicalize(ParseObject(jsonA));
            var b = Canonicalize(ParseObject(jsonB));
            firstDifference = FirstDifference(a, b, "$");
            return firstDifference == null;
        }

        /// <summary>Path of the first difference between two canonical tokens, or null if equal.</summary>
        public static string FirstDifference(JToken a, JToken b, string path)
        {
            if (a.Type != b.Type)
                return path + ": type " + a.Type + " vs " + b.Type;

            switch (a)
            {
                case JObject oa:
                {
                    var ob = (JObject)b;
                    var names = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var p in oa.Properties()) names.Add(p.Name);
                    foreach (var p in ob.Properties()) names.Add(p.Name);
                    foreach (var name in names)
                    {
                        var pa = oa.Property(name);
                        var pb = ob.Property(name);
                        if (pa == null) return path + "." + name + ": missing on left";
                        if (pb == null) return path + "." + name + ": missing on right";
                        var diff = FirstDifference(pa.Value, pb.Value, path + "." + name);
                        if (diff != null) return diff;
                    }
                    return null;
                }
                case JArray aa:
                {
                    var ab = (JArray)b;
                    if (aa.Count != ab.Count) return path + ": array length " + aa.Count + " vs " + ab.Count;
                    for (int i = 0; i < aa.Count; i++)
                    {
                        var diff = FirstDifference(aa[i], ab[i], path + "[" + i + "]");
                        if (diff != null) return diff;
                    }
                    return null;
                }
                default:
                    return JToken.DeepEquals(a, b) ? null : path + ": " + Short(a) + " vs " + Short(b);
            }
        }

        private static string Short(JToken t)
        {
            var s = t.ToString(Formatting.None);
            return s.Length > 80 ? s.Substring(0, 77) + "..." : s;
        }
    }
}
