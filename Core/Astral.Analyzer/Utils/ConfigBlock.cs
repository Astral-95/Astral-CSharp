using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

#nullable enable
namespace Astral.Analyzer;

//public class Config
//{
//    Dictionary<string, string> Values;
//    Dictionary<string, Config> InnerBlocks;
//}

public class ConfigBlock
{
    public string Name { get; set; } = "";
    public Dictionary<string, object> Values { get; set; } = new();
    public Dictionary<string, ConfigBlock> SubBlocks { get; set; } = new();


    public static ConfigBlock? ParseFile(string? Text)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return null;
        }

        var Lines = Text?.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        if (Lines == null || Lines.Length == 0)
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
            string Line = Lines[Index].Split(new[] { "//" }, StringSplitOptions.None)[0].Trim();
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
                        .Split(new[] { "//" }, StringSplitOptions.RemoveEmptyEntries)
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
}