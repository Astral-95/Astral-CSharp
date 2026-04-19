using Astral.Serialization;

namespace Astral.UnitTests.Serialization;

public class OtherSerializationTests_Unmanaged
{
    [Fact]
    public void NestedWriterTest()
    {
        var InnerWriter = new ByteWriter(8);
        const int Count = 500_000;
        for (int i = 0; i < Count; i++)
            InnerWriter.Serialize<bool>(i % 2 == 0);

        var Writer = new ByteWriter(8);
        Writer.Serialize(InnerWriter);

        var Reader = new UnmanagedByteReader(Writer.GetBuffer(), Writer.Pos);
        for (int i = 0; i < Count; i++)
        {
            bool Bit = Reader.Serialize<bool>();
            Assert.Equal(i % 2 == 0, Bit);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void ResetAndReuseTest()
    {
        var Writer = new ByteWriter(8);
        const int Count = 500_000;

        for (int Iter = 0; Iter < 5; Iter++)
        {
            for (int i = 0; i < Count; i++)
                Writer.Serialize<bool>(i % 2 == 0);

            Writer.Reset(8);
            Assert.Equal(0, Writer.Pos);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void PaddingAndAlignmentTest()
    {
        var Writer = new ByteWriter(8);
        const int Count = 500_000;
        var Data = new List<ushort>();
        for (int i = 0; i < Count; i++)
            Data.Add((ushort)(i % 65536));

        Writer.Serialize(Data);

        var Reader = new UnmanagedByteReader(Writer.GetBuffer(), Writer.Pos);
        var RecList = new List<ushort>();
        Reader.Serialize(RecList);
        Assert.Equal(Count, RecList.Count);

        for (int i = 0; i < Count; i++)
            Assert.Equal(Data[i], RecList[i]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void RandomSerializeTest()
    {
        var Writer = new ByteWriter(8);
        var Rand = new Random(42);
        var RecordedItems = new List<(string Type, object Value)>();

        const int Iterations = 50_000; // reduce count for large types like string to avoid OOM

        for (int i = 0; i < Iterations; i++)
        {
            int Case = Rand.Next(11);
            switch (Case)
            {
                case 0:
                    bool Bit = Rand.Next(2) == 1;
                    Writer.Serialize<bool>(Bit);
                    RecordedItems.Add(("Bool", Bit));
                    break;
                case 1:
                    byte ByteVal = (byte)Rand.Next(byte.MinValue, byte.MaxValue + 1);
                    Writer.Serialize(ByteVal);
                    RecordedItems.Add(("Byte", ByteVal));
                    break;
                case 2:
                    sbyte SByteVal = (sbyte)Rand.Next(sbyte.MinValue, sbyte.MaxValue + 1);
                    Writer.Serialize(SByteVal);
                    RecordedItems.Add(("SByte", SByteVal));
                    break;
                case 3:
                    short ShortVal = (short)Rand.Next(short.MinValue, short.MaxValue + 1);
                    Writer.Serialize(ShortVal);
                    RecordedItems.Add(("Short", ShortVal));
                    break;
                case 4:
                    ushort UShortVal = (ushort)Rand.Next(ushort.MinValue, ushort.MaxValue + 1);
                    Writer.Serialize(UShortVal);
                    RecordedItems.Add(("UShort", UShortVal));
                    break;
                case 5:
                    int IntVal = Rand.Next();
                    Writer.Serialize(IntVal);
                    RecordedItems.Add(("Int", IntVal));
                    break;
                case 6:
                    uint UIntVal = (uint)Rand.Next() | ((uint)Rand.Next() << 16);
                    Writer.Serialize(UIntVal);
                    RecordedItems.Add(("UInt", UIntVal));
                    break;
                case 7:
                    long LongVal = ((long)Rand.Next() << 32) | (uint)Rand.Next();
                    Writer.Serialize(LongVal);
                    RecordedItems.Add(("Long", LongVal));
                    break;
                case 8:
                    ulong ULongVal = ((ulong)Rand.Next() << 32) | (uint)Rand.Next();
                    Writer.Serialize(ULongVal);
                    RecordedItems.Add(("ULong", ULongVal));
                    break;
                case 9:
                    float FloatVal = (float)Rand.NextDouble();
                    Writer.Serialize(FloatVal);
                    RecordedItems.Add(("Float", FloatVal));
                    break;
                case 10:
                    double DoubleVal = Rand.NextDouble();
                    Writer.Serialize(DoubleVal);
                    RecordedItems.Add(("Double", DoubleVal));
                    break;
                case 11:
                    // Random string of length 1–20 with all possible chars
                    string CharPool = " 😀😃😄😁😆 Привет мир こんにちは Line1\nLine2\nLine 31234567890 !@#$%^&*()_+-=[]{};:'\",.<>/?\\| " +
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
        "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";

                    int Len = Rand.Next(1, 21);
                    char[] Chars = new char[Len];
                    for (int j = 0; j < Len; j++)
                        Chars[j] = CharPool[Rand.Next(CharPool.Length)];
                    string StrVal = new string(Chars);
                    Writer.Serialize(StrVal);
                    RecordedItems.Add(("String", StrVal));
                    break;
            }
        }

        var Reader = new UnmanagedByteReader(Writer.GetBuffer(), Writer.Pos);

        foreach (var Rec in RecordedItems)
        {
            switch (Rec.Type)
            {
                case "Bool":
                    bool BitVal = Reader.Serialize<bool>();
                    Assert.Equal((bool)Rec.Value, BitVal);
                    break;
                case "Byte":
                    byte ByteRead = Reader.Serialize<byte>();
                    Assert.Equal((byte)Rec.Value, ByteRead);
                    break;
                case "SByte":
                    sbyte SByteRead = Reader.Serialize<sbyte>();
                    Assert.Equal((sbyte)Rec.Value, SByteRead);
                    break;
                case "Short":
                    short ShortRead = Reader.Serialize<short>();
                    Assert.Equal((short)Rec.Value, ShortRead);
                    break;
                case "UShort":
                    ushort UShortRead = Reader.Serialize<ushort>();
                    Assert.Equal((ushort)Rec.Value, UShortRead);
                    break;
                case "Int":
                    int IntRead = Reader.Serialize<int>();
                    Assert.Equal((int)Rec.Value, IntRead);
                    break;
                case "UInt":
                    uint UIntRead = Reader.Serialize<uint>();
                    Assert.Equal((uint)Rec.Value, UIntRead);
                    break;
                case "Long":
                    long LongRead = Reader.Serialize<long>();
                    Assert.Equal((long)Rec.Value, LongRead);
                    break;
                case "ULong":
                    ulong ULongRead = Reader.Serialize<ulong>();
                    Assert.Equal((ulong)Rec.Value, ULongRead);
                    break;
                case "Float":
                    float FloatRead = Reader.Serialize<float>();
                    Assert.Equal((float)Rec.Value, FloatRead);
                    break;
                case "Double":
                    double DoubleRead = Reader.Serialize<double>();
                    Assert.Equal((double)Rec.Value, DoubleRead);
                    break;
                case "String":
                    string StrRead = "";
                    Reader.Serialize(ref StrRead);
                    Assert.Equal((string)Rec.Value, StrRead);
                    break;
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }


    [Fact]
    public void GCPressureTest()
    {
        const int Iterations = 50;
        const int BitsPerIteration = 500_000;

        for (int Iter = 0; Iter < Iterations; Iter++)
        {
            var Writer = new ByteWriter(8);
            for (int i = 0; i < BitsPerIteration; i++)
                Writer.Serialize<bool>(i % 2 == 0);

            GC.Collect();
            GC.WaitForPendingFinalizers();

            var Reader = new UnmanagedByteReader(Writer.GetBuffer(), Writer.Pos);
            for (int i = 0; i < BitsPerIteration; i++)
            {
                bool Bit = Reader.Serialize<bool>();
                Assert.Equal(i % 2 == 0, Bit);
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}