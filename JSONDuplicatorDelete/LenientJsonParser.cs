using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonDuplicateKeyCleaner;

internal static class LenientJsonParser
{
    public static JsonNode? Parse(string json, List<string>? duplicateLog = null)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(json);

        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        if (!reader.Read())
        {
            throw new InvalidOperationException("空のJSONです。");
        }

        string rootPath = "$";
        JsonNode? node = ReadNode(ref reader, rootPath, duplicateLog);

        if (reader.Read())
        {
            throw new InvalidOperationException("JSONルートの後ろに余分なデータがあります。");
        }

        return node;
    }

    private static JsonNode? ReadNode(ref Utf8JsonReader reader, string currentPath, List<string>? duplicateLog)
    {
        return reader.TokenType switch
        {
            JsonTokenType.StartObject => ReadObject(ref reader, currentPath, duplicateLog),
            JsonTokenType.StartArray => ReadArray(ref reader, currentPath, duplicateLog),
            JsonTokenType.String => JsonValue.Create(reader.GetString()),
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.True => JsonValue.Create(true),
            JsonTokenType.False => JsonValue.Create(false),
            JsonTokenType.Null => null,
            _ => throw new InvalidOperationException($"Unexpected token: {reader.TokenType} at {currentPath}")
        };
    }

    private static JsonObject ReadObject(ref Utf8JsonReader reader, string currentPath, List<string>? duplicateLog)
    {
        JsonObject obj = new();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return obj;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new InvalidOperationException($"Expected property name, but got {reader.TokenType} at {currentPath}");
            }

            string key = reader.GetString()
                ?? throw new InvalidOperationException($"プロパティ名の取得に失敗しました: {currentPath}");

            string childPath = currentPath + "." + key;

            if (!reader.Read())
            {
                throw new InvalidOperationException($"Unexpected end of JSON after property name at {childPath}");
            }

            JsonNode? value = ReadNode(ref reader, childPath, duplicateLog);

            if (obj.ContainsKey(key))
            {
                duplicateLog?.Add($"重複キー: {childPath} -> 後の値で上書き");
            }

            obj[key] = value;
        }

        throw new InvalidOperationException($"Unclosed object at {currentPath}");
    }

    private static JsonArray ReadArray(ref Utf8JsonReader reader, string currentPath, List<string>? duplicateLog)
    {
        JsonArray array = new();
        int index = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return array;
            }

            string childPath = $"{currentPath}[{index}]";
            JsonNode? value = ReadNode(ref reader, childPath, duplicateLog);
            array.Add(value);
            index++;
        }

        throw new InvalidOperationException($"Unclosed array at {currentPath}");
    }

    private static JsonNode ReadNumber(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt64(out long l))
        {
            return JsonValue.Create(l)!;
        }

        if (reader.TryGetDouble(out double d))
        {
            return JsonValue.Create(d)!;
        }

        throw new InvalidOperationException("Invalid number.");
    }
}