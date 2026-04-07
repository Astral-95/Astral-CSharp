using Astral.Serialization;

namespace Astral.UnitTests.Serialization;

public class ArraySerializeTests
{
    [Fact]
    public void BitLevelTest()
    {
        var Writer = new ByteWriter(1);
        const int BitsToWrite = 1_000_000;
        bool[] Expected = new bool[BitsToWrite];
        var Rand = new Random(42);

        for (int i = 0; i < BitsToWrite; i++)
        {
            bool Bit = Rand.Next(2) == 1;
            Writer.Serialize<bool>(Bit);
            Expected[i] = Bit;
        }

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);

        for (int i = 0; i < BitsToWrite; i++)
        {
            bool Bit = Reader.Serialize<bool>();
            Assert.Equal(Expected[i], Bit);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void LargeArrayTest()
    {
        var Writer = new ByteWriter(8);
        const int Count = 1_000_000;
        int[] Data = new int[Count];
        for (int i = 0; i < Count; i++) Data[i] = i;

        Writer.Serialize(Data);

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);
        var RecArr = Reader.SerializeArray<int>();
        Assert.Equal(Count, RecArr.Count());

        for (int i = 0; i < Count; i++) Assert.Equal(Data[i], RecArr[i]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void MixedTypesTest()
    {
        var Writer = new ByteWriter(8);
        const int Iterations = 500_000;

        for (int i = 0; i < Iterations; i++)
        {
            Writer.Serialize(i);
            Writer.Serialize<bool>(i % 2 == 0);
            Writer.Serialize((short)(i % 100));
        }

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);

        for (int i = 0; i < Iterations; i++)
        {
            int IntVal = Reader.Serialize<int>();
            Assert.Equal(i, IntVal);

            bool BitVal = Reader.Serialize<bool>();
            Assert.Equal(i % 2 == 0, BitVal);

            short ShortVal = Reader.Serialize<short>();
            Assert.Equal((short)(i % 100), ShortVal);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void StringArrayTest()
    {
        var Writer = new ByteWriter(8);
        const int Count = 10_000;
        string[] Data = new string[Count];
        for (int i = 0; i < Count; i++)
            Data[i] = $"String {i}";

        Writer.Serialize(Data);

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);
        var RecArr = Reader.SerializeStringArray();
        Assert.Equal(Count, RecArr.Count());

        for (int i = 0; i < Count; i++)
            Assert.Equal(Data[i], RecArr[i]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void FloatArrayTest()
    {
        var Writer = new ByteWriter(8);
        const int Count = 500_000;
        float[] Data = new float[Count];
        var Rand = new Random(42);

        for (int i = 0; i < Count; i++) Data[i] = (float)Rand.NextDouble();

        Writer.Serialize(Data);

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);
        var RecArr = Reader.SerializeArray<float>();
        Assert.Equal(Count, RecArr.Count());

        for (int i = 0; i < Count; i++)
            Assert.Equal(Data[i], RecArr[i]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public void DoubleArrayTest()
    {
        var Writer = new ByteWriter(8);
        const int Count = 500_000;
        double[] Data = new double[Count];
        var Rand = new Random(42);

        for (int i = 0; i < Count; i++) Data[i] = Rand.NextDouble();

        Writer.Serialize(Data);

        var Reader = new ByteReader(Writer.GetBuffer(), Writer.Pos);
        var RecArr = Reader.SerializeArray<double>();
        Assert.Equal(Count, RecArr.Count());

        for (int i = 0; i < Count; i++)
            Assert.Equal(Data[i], RecArr[i]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}