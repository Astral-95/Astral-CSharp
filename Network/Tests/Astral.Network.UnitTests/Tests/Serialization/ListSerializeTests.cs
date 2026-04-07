using Astral.Serialization;

namespace Astral.Network.UnitTests.Tests.Serialization;

public class ListSerializeTests
{
    [Fact]
    public void ListOfStringsTest()
    {
        var Writer = new ByteWriter(8);
        const int Count = 10_000;
        var Data = new List<string>();
        for (int i = 0; i < Count; i++)
            Data.Add($"ListString {i}");

        Writer.Serialize(Data);

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);
        var List = new List<string>();
        Reader.Serialize(List);
        Assert.Equal(Count, List.Count);

        for (int i = 0; i < Count; i++)
            Assert.Equal(Data[i], List[i]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void ListOfIntTest()
    {
        var Writer = new ByteWriter(8);
        const int Count = 500_000;
        var Data = new List<int>();
        for (int i = 0; i < Count; i++) Data.Add(i);

        Writer.Serialize(Data);

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);
        var List = new List<int>();
        Reader.Serialize(List);
        Assert.Equal(Count, List.Count);

        for (int i = 0; i < Count; i++)
            Assert.Equal(Data[i], List[i]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void ListOfFloatTest()
    {
        var Writer = new ByteWriter(8);
        const int Count = 500_000;
        var Data = new List<float>();
        var Rand = new Random(42);

        for (int i = 0; i < Count; i++)
            Data.Add((float)Rand.NextDouble());

        Writer.Serialize(Data);

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);
        var List = new List<float>();
        Reader.Serialize(List);
        Assert.Equal(Count, List.Count);

        for (int i = 0; i < Count; i++)
            Assert.Equal(Data[i], List[i]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void ListOfDoubleTest()
    {
        var Writer = new ByteWriter(8);
        const int Count = 500_000;
        var Data = new List<double>();
        var Rand = new Random(42);

        for (int i = 0; i < Count; i++)
            Data.Add(Rand.NextDouble());

        Writer.Serialize(Data);

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);
        var List = new List<double>();
        Reader.Serialize(List);
        Assert.Equal(Count, List.Count);

        for (int i = 0; i < Count; i++)
            Assert.Equal(Data[i], List[i]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void ListOfShortTest()
    {
        var Writer = new ByteWriter(8);
        const int Count = 500_000;
        var Data = new List<short>();
        var Rand = new Random(42);

        for (int i = 0; i < Count; i++)
            Data.Add((short)Rand.Next(short.MinValue, short.MaxValue));

        Writer.Serialize(Data);

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);
        var List = new List<short>();
        Reader.Serialize(List);
        Assert.Equal(Count, List.Count);

        for (int i = 0; i < Count; i++)
            Assert.Equal(Data[i], List[i]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void ListOfUShortTest()
    {
        var Writer = new ByteWriter(8);
        const int Count = 500_000;
        var Data = new List<ushort>();
        var Rand = new Random(42);

        for (int i = 0; i < Count; i++)
            Data.Add((ushort)Rand.Next(ushort.MinValue, ushort.MaxValue));

        Writer.Serialize(Data);

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);
        var List = new List<ushort>();
        Reader.Serialize(List);
        Assert.Equal(Count, List.Count);

        for (int i = 0; i < Count; i++)
            Assert.Equal(Data[i], List[i]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void ListOfAdvancedStringsTest()
    {
        List<string> ValueList = new() { "", "a", "😀😃😄😁😆 Привет мир こんにちは Line1\\nLine2\\nLine 31234567890 !@#$%^&*()_+-=[]//{};:'\\\",.<>/?\\\\| \" \r\n\t\t\t\"Lorem ipsum dolor sit amet, consectetur adipiscing elit. \" +\r\n\t\t\t\"Sed do eiusmod //tempor incididunt ut labore et dolore magnaaliqua." };
        var Writer = new ByteWriter(64);
        Writer.Serialize(ValueList);

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);
        List<string> Result = [];
        Reader.Serialize(Result);
        Assert.Equal(ValueList, Result);
    }
}