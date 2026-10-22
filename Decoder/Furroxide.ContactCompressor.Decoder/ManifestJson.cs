using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Furroxide.ContactCompressor
{
    /// <summary>
    /// Reads a Contact Compressor manifest without pulling in a JSON library.
    ///
    /// The consumer here is a .NET Framework app that ILRepacks itself into a single executable,
    /// so every added package is a packaging risk. The manifest schema is small, fixed, and written
    /// by Unity's <c>JsonUtility</c>, so a focused reader is cheaper and more predictable than a
    /// general-purpose dependency.
    /// </summary>
    public static class ManifestJson
    {
        public static ContactCompressorManifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Manifest is empty.", nameof(json));

            var reader = new Reader(json);
            object root = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
                throw new FormatException($"Unexpected trailing content at offset {reader.Position}.");

            if (!(root is Dictionary<string, object> map))
                throw new FormatException("Manifest root must be an object.");

            double version = GetNumber(map, "version", ContactCompressorManifest.CurrentVersion);
            if (version < 1 || version > int.MaxValue || version != Math.Truncate(version))
                throw new FormatException("Manifest version must be a positive integer.");

            var manifest = new ContactCompressorManifest
            {
                version = (int)version,
                prefix = GetString(map, "prefix", ContactParameterNames.DefaultPrefix),
                generator = GetString(map, "generator", ""),
                regions = new List<ContactRegionManifest>()
            };

            if (map.TryGetValue("regions", out object regions))
            {
                if (!(regions is List<object> regionList))
                    throw new FormatException("Manifest regions must be an array.");
                foreach (object entry in regionList)
                {
                    if (!(entry is Dictionary<string, object> r))
                        throw new FormatException("Each manifest region must be an object.");

                    var region = new ContactRegionManifest
                    {
                        id = GetString(r, "id", ""),
                        axes = GetString(r, "axes", "XYZ"),
                        boxExtents = GetFloats(r, "boxExtents"),
                        regionExtents = GetFloats(r, "regionExtents"),
                        points = new List<ContactPointManifest>()
                    };

                    if (r.TryGetValue("points", out object points))
                    {
                        if (!(points is List<object> pointList))
                            throw new FormatException("Region points must be an array.");
                        foreach (object p in pointList)
                        {
                            if (!(p is Dictionary<string, object> pt))
                                throw new FormatException("Each manifest point must be an object.");
                            region.points.Add(new ContactPointManifest
                            {
                                id = GetString(pt, "id", ""),
                                u = GetFloat(pt, "u", 0.5),
                                v = GetFloat(pt, "v", 0.5),
                                w = GetFloat(pt, "w", 0.5),
                                radius = GetFloat(pt, "radius", 0.0)
                            });
                        }
                    }

                    manifest.regions.Add(region);
                }
            }

            return manifest;
        }

        // ---- accessors ----

        static string GetString(Dictionary<string, object> map, string key, string fallback)
        {
            if (!map.TryGetValue(key, out object value)) return fallback;
            if (value is string text) return text;
            throw new FormatException($"'{key}' must be a string.");
        }

        static double GetNumber(Dictionary<string, object> map, string key, double fallback)
        {
            if (!map.TryGetValue(key, out object value)) return fallback;
            if (value is double number) return number;
            throw new FormatException($"'{key}' must be a number.");
        }

        static float GetFloat(Dictionary<string, object> map, string key, double fallback)
            => ToFloat(GetNumber(map, key, fallback), key);

        static float ToFloat(double value, string key)
        {
            float result = (float)value;
            if (float.IsNaN(result) || float.IsInfinity(result))
                throw new FormatException($"'{key}' must be a finite float.");
            return result;
        }

        static float[] GetFloats(Dictionary<string, object> map, string key)
        {
            var result = new float[3];
            if (!map.TryGetValue(key, out object value)) return result;
            if (!(value is List<object> list) || list.Count != 3)
                throw new FormatException($"'{key}' must contain exactly three numbers.");
            for (int i = 0; i < 3; i++)
            {
                if (!(list[i] is double number))
                    throw new FormatException($"'{key}' must contain only numbers.");
                result[i] = ToFloat(number, key);
            }
            return result;
        }

        // ---- reader ----

        sealed class Reader
        {
            readonly string _text;
            int _index;
            int _depth;
            static readonly Regex NumberSyntax = new Regex(
                @"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z",
                RegexOptions.CultureInvariant);

            public Reader(string text) { _text = text; }

            public int Position => _index;
            public bool AtEnd => _index >= _text.Length;

            public void SkipWhitespace()
            {
                while (_index < _text.Length && (_text[_index] == ' ' || _text[_index] == '\t'
                       || _text[_index] == '\r' || _text[_index] == '\n')) _index++;
            }

            public object ReadValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new FormatException("Unexpected end of manifest.");

                char c = _text[_index];
                switch (c)
                {
                    case '{':
                    case '[':
                        // Manifests are shallow. Bound recursion so a corrupt config cannot
                        // terminate the companion with an uncatchable StackOverflowException.
                        if (++_depth > 64) throw new FormatException("Manifest nesting exceeds 64 levels.");
                        try { return c == '{' ? (object)ReadObject() : ReadArray(); }
                        finally { _depth--; }
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _index++;                       // '{'
                SkipWhitespace();

                if (!AtEnd && _text[_index] == '}') { _index++; return result; }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != '"')
                        throw new FormatException($"Expected a key at offset {_index}.");

                    string key = ReadString();
                    SkipWhitespace();

                    if (AtEnd || _text[_index] != ':')
                        throw new FormatException($"Expected ':' at offset {_index}.");
                    _index++;

                    if (result.ContainsKey(key))
                        throw new FormatException($"Duplicate object key '{key}'.");
                    result[key] = ReadValue();
                    SkipWhitespace();

                    if (AtEnd) throw new FormatException("Unterminated object.");
                    if (_text[_index] == ',') { _index++; continue; }
                    if (_text[_index] == '}') { _index++; return result; }

                    throw new FormatException($"Expected ',' or '}}' at offset {_index}.");
                }
            }

            List<object> ReadArray()
            {
                var result = new List<object>();
                _index++;                       // '['
                SkipWhitespace();

                if (!AtEnd && _text[_index] == ']') { _index++; return result; }

                while (true)
                {
                    result.Add(ReadValue());
                    SkipWhitespace();

                    if (AtEnd) throw new FormatException("Unterminated array.");
                    if (_text[_index] == ',') { _index++; continue; }
                    if (_text[_index] == ']') { _index++; return result; }

                    throw new FormatException($"Expected ',' or ']' at offset {_index}.");
                }
            }

            string ReadString()
            {
                _index++;                       // opening quote
                var sb = new StringBuilder();

                while (true)
                {
                    if (AtEnd) throw new FormatException("Unterminated string.");
                    char c = _text[_index++];

                    if (c == '"') return sb.ToString();

                    if (c < ' ') throw new FormatException("Unescaped control character in string.");
                    if (c != '\\') { sb.Append(c); continue; }

                    if (AtEnd) throw new FormatException("Unterminated escape sequence.");
                    char e = _text[_index++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _text.Length) throw new FormatException("Truncated \\u escape.");
                            sb.Append((char)ushort.Parse(_text.Substring(_index, 4), NumberStyles.HexNumber,
                                                         CultureInfo.InvariantCulture));
                            _index += 4;
                            break;
                        default:
                            throw new FormatException($"Unknown escape '\\{e}' at offset {_index - 1}.");
                    }
                }
            }

            double ReadNumber()
            {
                int start = _index;
                if (!AtEnd && (_text[_index] == '-' || _text[_index] == '+')) _index++;

                while (!AtEnd)
                {
                    char c = _text[_index];
                    if (char.IsDigit(c) || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') _index++;
                    else break;
                }

                string token = _text.Substring(start, _index - start);

                // Invariant culture matters: this runs on machines whose decimal separator is ','.
                if (!NumberSyntax.IsMatch(token)
                    || !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    || double.IsNaN(value) || double.IsInfinity(value))
                    throw new FormatException($"Invalid number '{token}' at offset {start}.");

                return value;
            }

            void Expect(string literal)
            {
                if (_index + literal.Length > _text.Length ||
                    string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
                {
                    throw new FormatException($"Expected '{literal}' at offset {_index}.");
                }
                _index += literal.Length;
            }
        }
    }
}
