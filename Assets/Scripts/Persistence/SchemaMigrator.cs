using System;
using System.Collections.Generic;
using MediaTrip.Model;
using Newtonsoft.Json.Linq;

namespace MediaTrip.Persistence
{
    public enum DocumentKind { Trip, ShotList, Outline, Captures }

    public class SchemaTooNewException : Exception
    {
        public SchemaTooNewException(string message) : base(message) { }
    }

    /// <summary>
    /// The single place schema migrations go. Runs on the raw JObject before typed
    /// deserialization, so a migration can rename, move or split properties freely.
    /// Register one step per version: <c>Steps[N]</c> takes a document from N to N+1.
    /// </summary>
    public static class SchemaMigrator
    {
        /// <summary>Key = source version. Each step must upgrade exactly one version.</summary>
        private static readonly Dictionary<int, Action<JObject, DocumentKind>> Steps =
            new Dictionary<int, Action<JObject, DocumentKind>>
            {
                // Example for the future:
                // [1] = (doc, kind) => { if (kind == DocumentKind.Captures) RenameProperty(doc, "old", "new"); },
            };

        public static JObject Migrate(JObject doc, DocumentKind kind, string sourceName = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            int version = ReadVersion(doc);
            if (version > Schema.CurrentVersion)
                throw new SchemaTooNewException(
                    $"{sourceName ?? kind.ToString()} has schemaVersion {version}, but this app understands up to {Schema.CurrentVersion}. Update the app.");

            while (version < Schema.CurrentVersion)
            {
                if (!Steps.TryGetValue(version, out var step))
                    throw new InvalidOperationException(
                        $"No migration registered from schemaVersion {version} to {version + 1} for {kind}.");
                step(doc, kind);
                version++;
            }

            doc["schemaVersion"] = Schema.CurrentVersion;
            return doc;
        }

        /// <summary>A missing or unreadable schemaVersion is treated as version 1 (hand-authored file).</summary>
        private static int ReadVersion(JObject doc)
        {
            var token = doc["schemaVersion"];
            if (token == null || token.Type == JTokenType.Null) return 1;
            if (token.Type == JTokenType.Integer) return token.Value<int>();
            if (token.Type == JTokenType.Float) return (int)token.Value<double>();
            if (token.Type == JTokenType.String && int.TryParse(token.Value<string>(), out var parsed)) return parsed;
            return 1;
        }

        /// <summary>Helper for future steps.</summary>
        internal static void RenameProperty(JObject obj, string from, string to)
        {
            var prop = obj.Property(from);
            if (prop == null) return;
            prop.Remove();
            obj[to] = prop.Value;
        }
    }
}
