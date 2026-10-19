// Appends one JSON line per blade event to Application.persistentDataPath/reps.jsonl.
// On the Quest: /sdcard/Android/data/com.alfakentavr.vrfencing/files/reps.jsonl
//   adb pull /sdcard/Android/data/com.alfakentavr.vrfencing/files/reps.jsonl
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace VrFencing.Prototypes.BladeFeel
{
    public sealed class RepLog : IDisposable
    {
        private readonly StreamWriter _writer;

        public string FilePath { get; }

        public RepLog(string fileName)
        {
            FilePath = Path.Combine(Application.persistentDataPath, fileName);
            try
            {
                _writer = new StreamWriter(FilePath, append: true, Encoding.UTF8) { AutoFlush = true };
                Debug.Log($"[BladeFeel] Rep log: {FilePath}");
            }
            catch (Exception exception)
            {
                // The prototype must keep running without its log; say so loudly.
                Debug.LogError($"[BladeFeel] Rep log disabled, cannot open {FilePath}: {exception.Message}");
            }
        }

        /// <summary>Writes a JSON object built from alternating key / value pairs.</summary>
        public void Write(params object[] keysAndValues)
        {
            if (_writer == null)
                return;

            var line = new StringBuilder("{");
            for (var index = 0; index + 1 < keysAndValues.Length; index += 2)
            {
                if (index > 0)
                    line.Append(',');
                line.Append('"').Append(keysAndValues[index]).Append("\":").Append(FormatValue(keysAndValues[index + 1]));
            }

            line.Append('}');
            try
            {
                _writer.WriteLine(line.ToString());
            }
            catch (IOException exception)
            {
                Debug.LogError($"[BladeFeel] Rep log write failed: {exception.Message}");
            }
        }

        public void Dispose()
        {
            _writer?.Dispose();
        }

        private static string FormatValue(object value)
        {
            switch (value)
            {
                case null:
                    return "null";
                case bool flag:
                    return flag ? "true" : "false";
                case float number:
                    return float.IsNaN(number) || float.IsInfinity(number)
                        ? "null"
                        : number.ToString("0.###", CultureInfo.InvariantCulture);
                case int integer:
                    return integer.ToString(CultureInfo.InvariantCulture);
                default:
                    return "\"" + value.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }
        }
    }
}
