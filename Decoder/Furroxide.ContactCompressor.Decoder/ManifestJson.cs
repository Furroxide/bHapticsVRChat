using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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

            var manifest = new ContactCompressorManifest
            {
                version = (int)GetNumber(map, "version", ContactCompressorManifest.CurrentVersion),
                prefix = GetString(map, "prefix", ContactParameterNames.DefaultPrefix),
                generator = GetString(map, "generator", ""),
                regions = new List<ContactRegionManifest>()
            };

            if (map.TryGetValue("regions", out object regions) && regions is List<object> regionList)
            {
                foreach (object entry in regionList)
                {
                    if (!(entry is Dictionary<string, object> r)) continue;

                    var region = new ContactRegionManifest
                    {
                        id = GetString(r, "id", ""),
                        axes = GetString(r, "axes", "XYZ"),
                        boxExtents = GetFloats(r, "boxExtents"),
                        regionExtents = GetFloats(r, "regionExtents"),
                        points = new List<ContactPointManifest>()
                    };

                    if (r.TryGetValue("points", out object points) && points is List<object> pointList)
                    {
                        foreach (object p in pointList)
                        {
                            if (!(p is Dictionary<string, object> pt)) continue;
                            region.points.Add(new ContactPointManifest
                            {
                                id = GetString(pt, "id", ""),
                                u = (float)GetNumber(pt, "u", 0.5),
                                v = (float)GetNumber(pt, "v", 0.5),
                                w = (float)GetNumber(pt, "w", 0.5),
                                radius = (float)GetNumber(pt, "radius", 0.0)
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
            => map.TryGetValue(key, out object v) && v is string s ? s : fallback;

        static double GetNumber(Dictionary<string, object> map, string key, double fallback)
            => map.TryGetValue(key, out object v) && v is double d ? d : fallback;

        static float[] GetFloats(Dictionary<string, object> map, string key)
        {
            var result = new float[3];
            if (map.TryGetValue(key, out object v) && v is List<object> list)
                for (int i = 0; i < 3 && i < list.Count; i++)
                    if (list[i] is double d)
                        result[i] = (float)d;
            return result;
        }

        // ---- reader ----

        sealed class Reader
        {
            readonly string _text;
            int _index;

            public Reader(string text) { _text = text; }

            public int Position => _index;
            public bool AtEnd => _index >= _text.Length;

            public void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index])) _index++;
            }

            public object ReadValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new FormatException("Unexpected end of manifest.");

                char c = _text[_index];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
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
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
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
