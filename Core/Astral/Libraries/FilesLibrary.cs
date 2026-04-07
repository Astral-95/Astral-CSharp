using System.Text.RegularExpressions;

namespace Astral.Libraries;

public class ConfigBlock
{
    public string Name { get; set; } = "";
    public Dictionary<string, object> Values { get; set; } = new();
    public Dictionary<string, ConfigBlock> SubBlocks { get; set; } = new();
}

public static class FilesLibrary
{
    public static string? FindFileUpwards(string FileName, string? StartDirectory = null)
    {
        string CurrentDirectory = Path.GetFullPath(StartDirectory ?? Directory.GetCurrentDirectory());

        while (!string.IsNullOrEmpty(CurrentDirectory))
        {
            string FilePath = Path.Combine(CurrentDirectory, FileName);
            if (File.Exists(FilePath))
            {
                return FilePath;
            }

            // Move one directory up
            DirectoryInfo? ParentDir = Directory.GetParent(CurrentDirectory);

            if (ParentDir == null)
            {
                break;
            }

            CurrentDirectory = ParentDir.FullName;
        }

        return null; // file not found
    }

    public static ConfigBlock? ParseFile(string? Path)
    {
        if (string.IsNullOrEmpty(Path))
        {
            return null;
        }

        var Lines = File.ReadAllLines(Path);
        if (Lines.Length == 0)
        {
            return null;
        }

        int Index = 0;
        return ParseBlock(ref Lines, ref Index, null);

    }

    private static ConfigBlock? ParseBlock(ref string[] Lines, ref int Index, string? BlockName)
    {
        if (Index >= Lines.Length)
        {
            return null;
        }

        var Block = new ConfigBlock { Name = BlockName ?? "Root" };

        while (Index < Lines.Length)
        {
            string Line = Lines[Index].Split("//")[0].Trim();
            Index++;

            if (string.IsNullOrEmpty(Line))
                continue;

            if (Line.EndsWith("{")) // Start of sub-block
            {
                string Name = Line.Substring(0, Line.Length - 1).Trim();
                var SubBlock = ParseBlock(ref Lines, ref Index, Name);

                if (SubBlock != null)
                {
                    Block.SubBlocks[Name] = SubBlock;
                }
            }
            else if (Line == "}") // End of current block
            {
                return Block;
            }
            else // Key-value line
            {
                var KvMatch = Regex.Match(Line, @"(\w+)\s*=\s*(.+)");
                if (!KvMatch.Success)
                {
                    continue;
                }

                string Key = KvMatch.Groups[1].Value;
                string ValueStr = KvMatch.Groups[2].Value.Trim();

                if (ValueStr.StartsWith("[") && ValueStr.EndsWith("]")) // List
                {
                    Block.Values[Key] = ValueStr
                        .Substring(1, ValueStr.Length - 2)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .ToList();
                }
                else // String
                {
                    Block.Values[Key] = ValueStr.Trim('"');
                }
            }
        }

        return Block;
    }

    public static ConfigBlock? GetSingleBlockFromConfig(string? Path, string BlockName)
    {
        if (string.IsNullOrWhiteSpace(Path))
        {
            return null;
        }

        var Lines = File.ReadAllLines(Path);
        int Index = 0;

        while (Index < Lines.Length)
        {
            string Line = Lines[Index].Split("//")[0].Trim();
            Index++;

            if (string.IsNullOrEmpty(Line))
            {
                continue;
            }

            if (Line.StartsWith(BlockName) && Line.EndsWith("{"))
            {
                return ParseBlock(ref Lines, ref Index, BlockName);
            }
        }

        return null; // Not found
    }
}
