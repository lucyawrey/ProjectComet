using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace Comet.ZoneFiles
{
    /// <summary>
    /// Zone TOML through Tomlyn's table model, which needs no reflection or source generator, so the Unity editor
    /// runs the same code as the content build. Reading collects errors by file and key rather than throwing.
    /// </summary>
    internal sealed class ZoneToml
    {
        private readonly string _file;
        private readonly List<ZoneFileError> _errors;

        public ZoneToml(string file, List<ZoneFileError> errors)
        {
            _file = file;
            _errors = errors;
        }

        /// <summary>Parses a file's text; null (with an error) if it isn't valid TOML.</summary>
        public TomlTable? Parse(string text)
        {
            try
            {
                return TomlSerializer.Deserialize<TomlTable>(text, TomlSerializerOptions.Default);
            }
            catch (TomlException e)
            {
                Error(e.Message);
                return null;
            }
        }

        /// <summary>The table as TOML, with one blank line before each table header so the blocks read apart.</summary>
        public static string Write(TomlTable table)
        {
            var lines = TomlSerializer.Serialize(table, TomlSerializerOptions.Default).Replace("\r\n", "\n").Split('\n');
            var text = new StringBuilder();
            foreach (var line in lines)
            {
                var previousBlank = text.Length == 0 || (text.Length >= 2 && text[text.Length - 1] == '\n' && text[text.Length - 2] == '\n');
                if (line.StartsWith("[", StringComparison.Ordinal) && !previousBlank)
                {
                    text.Append('\n');
                }

                if (line.Length > 0 || !previousBlank)
                {
                    text.Append(line).Append('\n');
                }
            }

            return text.ToString().TrimEnd('\n') + "\n";
        }

        public void Error(string message) => _errors.Add(new ZoneFileError(_file, message));

        public void Error(string path, string message) => Error($"{path}: {message}");

        /// <summary>Reports keys of <paramref name="table"/> outside <paramref name="known"/>.</summary>
        public void CheckKeys(TomlTable table, string path, params string[] known)
        {
            foreach (var pair in table)
            {
                if (Array.IndexOf(known, pair.Key) < 0)
                {
                    Error(Join(path, pair.Key), "unknown key.");
                }
            }
        }

        /// <summary>The tables of an array of tables (<c>[[name]]</c>), or none.</summary>
        public IEnumerable<(TomlTable Table, string Path)> Tables(TomlTable table, string key)
        {
            if (!table.TryGetValue(key, out var value))
            {
                return Enumerable.Empty<(TomlTable, string)>();
            }

            if (value is TomlTableArray array)
            {
                return array.Select((t, i) => (t, $"{key}[{i}]"));
            }

            Error(key, "should be an array of tables ([[" + key + "]]).");
            return Enumerable.Empty<(TomlTable, string)>();
        }

        public string String(TomlTable table, string path, string key)
        {
            if (Get(table, path, key, required: true) is { } value)
            {
                if (value is string text)
                {
                    return text;
                }

                Error(Join(path, key), "should be a string.");
            }

            return "";
        }

        public bool Bool(TomlTable table, string path, string key, bool fallback)
        {
            var value = Get(table, path, key, required: false);
            if (value is null)
            {
                return fallback;
            }

            if (value is bool flag)
            {
                return flag;
            }

            Error(Join(path, key), "should be true or false.");
            return fallback;
        }

        public float Float(TomlTable table, string path, string key, float? fallback = null)
        {
            var value = Get(table, path, key, required: fallback is null);
            return value is null ? fallback ?? 0 : Number(value, Join(path, key));
        }

        public int Int(object? value, string path)
        {
            if (value is long number && number >= int.MinValue && number <= int.MaxValue)
            {
                return (int)number;
            }

            Error(path, "should be a whole number.");
            return 0;
        }

        public Vector3 Vector(TomlTable table, string path, string key, Vector3? fallback = null)
        {
            var value = Get(table, path, key, required: fallback is null);
            if (value is null)
            {
                return fallback ?? Vector3.Zero;
            }

            var numbers = Numbers(value, Join(path, key), 3);
            return numbers is null ? fallback ?? Vector3.Zero : new Vector3(numbers[0], numbers[1], numbers[2]);
        }

        /// <summary>A pair of whole numbers, such as a chunk or a sample position.</summary>
        public (int X, int Z) Pair(TomlTable table, string path, string key)
        {
            if (Get(table, path, key, required: true) is TomlArray array && array.Count == 2)
            {
                return (Int(array[0], Join(path, key)), Int(array[1], Join(path, key)));
            }

            if (table.ContainsKey(key))
            {
                Error(Join(path, key), "should be two whole numbers, [x, z].");
            }

            return (0, 0);
        }

        /// <summary>An enum value written in snake_case (<c>toward_point</c>).</summary>
        public T Enum<T>(TomlTable table, string path, string key, T? fallback = null)
            where T : struct, System.Enum
        {
            var value = Get(table, path, key, required: fallback is null);
            if (value is null)
            {
                return fallback ?? default;
            }

            foreach (T option in System.Enum.GetValues(typeof(T)))
            {
                if (value is string text && text == SnakeCase(option.ToString()))
                {
                    return option;
                }
            }

            var options = string.Join(", ", System.Enum.GetValues(typeof(T)).Cast<T>().Select(o => SnakeCase(o.ToString())));
            Error(Join(path, key), $"should be one of: {options}.");
            return fallback ?? default;
        }

        /// <summary><c>TowardPoint</c> as <c>toward_point</c>.</summary>
        public static string SnakeCase(string name)
        {
            var text = new StringBuilder();
            foreach (var c in name)
            {
                if (char.IsUpper(c) && text.Length > 0)
                {
                    text.Append('_');
                }

                text.Append(char.ToLowerInvariant(c));
            }

            return text.ToString();
        }

        /// <summary>A float for a file, rounded to <paramref name="decimals"/> places (and never -0).</summary>
        public static double Round(float value, int decimals)
        {
            var rounded = Math.Round((double)value, decimals);
            return rounded == 0 ? 0 : rounded;
        }

        public static TomlArray Array3(Vector3 value, int decimals) =>
            new TomlArray { Round(value.X, decimals), Round(value.Y, decimals), Round(value.Z, decimals) };

        private object? Get(TomlTable table, string path, string key, bool required)
        {
            if (table.TryGetValue(key, out var value))
            {
                return value;
            }

            if (required)
            {
                Error(Join(path, key), "is missing.");
            }

            return null;
        }

        private float Number(object value, string path)
        {
            double number;
            switch (value)
            {
                case long whole:
                    number = whole;
                    break;
                case double real:
                    number = real;
                    break;
                default:
                    Error(path, "should be a number.");
                    return 0;
            }

            // TOML allows nan and inf, but no zone number means either.
            if (double.IsNaN(number) || double.IsInfinity(number) || Math.Abs(number) > float.MaxValue)
            {
                Error(path, $"{number.ToString(CultureInfo.InvariantCulture)} isn't allowed: zone numbers must be finite.");
                return 0;
            }

            return (float)number;
        }

        private float[]? Numbers(object value, string path, int count)
        {
            if (value is TomlArray array && array.Count == count)
            {
                var errors = _errors.Count;
                var numbers = array.Select(v => Number(v!, path)).ToArray();
                return _errors.Count == errors ? numbers : null;
            }

            Error(path, $"should be {count} numbers.");
            return null;
        }

        private static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";
    }
}
