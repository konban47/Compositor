using System.Globalization;
using System.Text;

namespace Compositor.Core.IO.PSD;

/// <summary>Bounded readers for Adobe descriptors and the PostScript-like text engine dictionary.</summary>
internal static class PsdMetadata
{
    public static object? Get(object? value, params string[] keys)
    {
        foreach (var key in keys) value = value is Dictionary<string, object?> map ? map.GetValueOrDefault(key) : null;
        return value;
    }
    public static double Number(object? value, double fallback = 0) => value is double n && double.IsFinite(n) ? n : fallback;
    public static List<object?> Array(object? value) => value as List<object?> ?? [];

    internal ref struct Descriptor(ReadOnlySpan<byte> bytes)
    {
        internal PsdCursor Cursor = new(bytes);
        private int _nodes;
        public double Double() => BitConverter.Int64BitsToDouble(unchecked((long)Cursor.U64()));
        private int Count(int max)
        {
            var count = Cursor.U32();
            if (count > max) throw new InvalidDataException("PSD metadata count exceeds its limit.");
            return (int)count;
        }
        public string Unicode() => Encoding.BigEndianUnicode.GetString(Cursor.Bytes(checked(Count(1_000_000) * 2))).TrimEnd('\0');
        public string Identifier() { var count = Count(10000); return Cursor.Ascii(count == 0 ? 4 : count); }
        public Dictionary<string, object?> Read(bool versioned = true, int depth = 0)
        {
            if (depth > 64 || (versioned && Cursor.U32() != 16)) throw new InvalidDataException("Invalid PSD descriptor.");
            Unicode(); Identifier(); var count = Count(10000);
            var values = new Dictionary<string, object?>();
            for (var i = 0; i < count; i++) { var key = Identifier(); values[key] = Value(Cursor.Ascii(4), depth + 1); }
            return values;
        }
        private object? Value(string type, int depth)
        {
            if (++_nodes > 200000 || depth > 64) throw new InvalidDataException("PSD descriptor is too complex.");
            switch (type)
            {
                case "doub": return Double();
                case "UntF": Cursor.Skip(4); return Double();
                case "long": return (double)Cursor.I32();
                case "comp": return (double)unchecked((long)Cursor.U64());
                case "bool": return Cursor.U8() != 0;
                case "TEXT": return Unicode();
                case "enum": Identifier(); return Identifier();
                case "tdta": case "alis": return Cursor.Bytes(Count(8_000_000)).ToArray();
                case "Objc": case "GlbO": return Read(false, depth);
                case "type": case "GlbC": Unicode(); return Identifier();
                case "VlLs":
                    var count = Count(10000); var list = new List<object?>();
                    for (var i = 0; i < count; i++) list.Add(Value(Cursor.Ascii(4), depth + 1));
                    return list;
                case "obj ":
                    var references = Count(10000);
                    for (var i = 0; i < references; i++)
                    {
                        switch (Cursor.Ascii(4))
                        {
                            case "prop": Unicode(); Identifier(); Identifier(); break;
                            case "Clss": Unicode(); Identifier(); break;
                            case "Enmr": Unicode(); Identifier(); Identifier(); Identifier(); break;
                            case "rele": Unicode(); Identifier(); Cursor.Skip(4); break;
                            case "Idnt": case "indx": Cursor.Skip(4); break;
                            case "name": Unicode(); break;
                            default: throw new InvalidDataException("Unknown descriptor reference.");
                        }
                    }
                    return null;
                default: throw new InvalidDataException("Unknown PSD descriptor value.");
            }
        }
    }

    internal sealed class Engine(byte[] bytes)
    {
        private int _at, _nodes;
        private byte Peek => _at < bytes.Length ? bytes[_at] : (byte)0;
        private bool Take(string value)
        {
            if (!bytes.AsSpan(_at).StartsWith(Encoding.ASCII.GetBytes(value))) return false;
            _at += value.Length; return true;
        }
        private void White()
        {
            while (_at < bytes.Length)
            {
                if (Peek <= 32) _at++;
                else if (Peek == '%') { while (_at < bytes.Length && Peek is not (10 or 13)) _at++; }
                else break;
            }
        }
        private static bool Delimiter(byte b) => b <= 32 || b is (byte)'/' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'(' or (byte)')';
        private string Token()
        {
            var start = _at;
            while (_at < bytes.Length && !Delimiter(Peek)) _at++;
            if (start == _at) throw new InvalidDataException("Invalid text-engine token.");
            return Encoding.ASCII.GetString(bytes.AsSpan(start, _at - start));
        }
        public object? Read(int depth = 0)
        {
            if (++_nodes > 200000 || depth > 64) throw new InvalidDataException("Text engine is too complex.");
            White();
            if (Take("<<"))
            {
                var map = new Dictionary<string, object?>();
                while (true)
                {
                    White(); if (Take(">>")) return map;
                    if (!Take("/")) throw new InvalidDataException("Invalid text-engine dictionary.");
                    var key = Token(); map[key] = Read(depth + 1);
                }
            }
            if (Take("["))
            {
                var values = new List<object?>();
                while (true) { White(); if (Take("]")) return values; values.Add(Read(depth + 1)); }
            }
            if (Take("(")) return String();
            if (Take("<"))
            {
                var hex = new StringBuilder();
                while (_at < bytes.Length && Peek != '>') { if (Peek > 32) hex.Append((char)Peek); _at++; }
                if (!Take(">")) throw new InvalidDataException("Unclosed text-engine string.");
                if (hex.Length % 2 != 0) hex.Append('0');
                return Decode(Convert.FromHexString(hex.ToString()));
            }
            if (Take("/")) return Token();
            var token = Token();
            if (token == "true") return true;
            if (token == "false") return false;
            if (token == "null") return null;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n)) return n;
            throw new InvalidDataException("Unknown text-engine token.");
        }
        private string String()
        {
            var data = new List<byte>(); var level = 1;
            while (_at < bytes.Length)
            {
                var b = bytes[_at++];
                if (b == '\\')
                {
                    if (_at >= bytes.Length) break;
                    b = bytes[_at++];
                    if (b is >= (byte)'0' and <= (byte)'7')
                    {
                        var value = b - '0';
                        for (var i = 0; i < 2 && _at < bytes.Length && Peek is >= (byte)'0' and <= (byte)'7'; i++) value = value * 8 + bytes[_at++] - '0';
                        data.Add((byte)value); continue;
                    }
                    if (b is 10 or 13) { if (b == 13 && Peek == 10) _at++; continue; }
                    data.Add(b switch { (byte)'n' => 10, (byte)'r' => 13, (byte)'t' => 9, (byte)'b' => 8, (byte)'f' => 12, _ => b });
                    continue;
                }
                if (b == '(') level++;
                else if (b == ')' && --level == 0) return Decode(data.ToArray());
                data.Add(b);
            }
            throw new InvalidDataException("Unclosed text-engine string.");
        }
        private static string Decode(byte[] data) => data.Length >= 2 && data[0] == 254 && data[1] == 255
            ? Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2) : Encoding.Latin1.GetString(data);
    }
}
