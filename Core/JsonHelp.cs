using System;
using System.Text.Json;

namespace FastCraft.Core
{
    public static class JsonHelp
    {
        public static string GetString(JsonElement element, string name)
        {
            JsonElement value;
            if (element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return null;
        }

        public static long GetLong(JsonElement element, string name)
        {
            JsonElement value;
            if (element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Number)
            {
                long result;
                if (value.TryGetInt64(out result)) return result;
            }
            return 0;
        }

        public static bool GetBool(JsonElement element, string name)
        {
            JsonElement value;
            if (element.TryGetProperty(name, out value))
            {
                if (value.ValueKind == JsonValueKind.True) return true;
                if (value.ValueKind == JsonValueKind.False) return false;
            }
            return false;
        }
    }
}
