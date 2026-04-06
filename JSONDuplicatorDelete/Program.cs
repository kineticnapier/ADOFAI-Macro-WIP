using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonDuplicateKeyCleaner;

internal static class Program
{
    static int Main(string[] args)
    {
        try
        {
            string path = GetTargetPath(args);

            if (!File.Exists(path))
            {
                Console.WriteLine($"ファイルが見つかりません: {path}");
                return 1;
            }

            string originalText = File.ReadAllText(path, Encoding.UTF8);

            var duplicateLog = new List<string>();
            JsonNode? root = LenientJsonParser.Parse(originalText, duplicateLog);

            if (root is null)
            {
                Console.WriteLine("JSONの解析に失敗しました。");
                return 1;
            }

            string backupPath = path + ".bak";
            File.Copy(path, backupPath, overwrite: true);

            string normalized = JsonWriterHelper.ToIndentedJson(root);
            File.WriteAllText(path, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Console.WriteLine("保存しました。");
            Console.WriteLine($"対象: {path}");
            Console.WriteLine($"バックアップ: {backupPath}");
            Console.WriteLine();

            if (duplicateLog.Count == 0)
            {
                Console.WriteLine("重複キーは見つかりませんでした。");
            }
            else
            {
                Console.WriteLine($"重複キーを {duplicateLog.Count} 件検出しました。");
                foreach (string line in duplicateLog)
                {
                    Console.WriteLine(line);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("エラーが発生しました:");
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static string GetTargetPath(string[] args)
    {
        if (args.Length >= 1 && !string.IsNullOrWhiteSpace(args[0]))
        {
            return args[0];
        }

        Console.Write("ファイルパスを入力してください: ");
        string? input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new InvalidOperationException("ファイルパスが空です。");
        }

        return input.Trim().Trim('"');
    }
}

internal static class JsonWriterHelper
{
    public static string ToIndentedJson(JsonNode node)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        return node.ToJsonString(options);
    }
}