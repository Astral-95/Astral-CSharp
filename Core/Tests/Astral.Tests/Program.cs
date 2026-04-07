using System.Diagnostics;

namespace Astral.Tests;
public class Program
{
    
    public static async Task Main(string[] args)
    {
        Stopwatch Sw = new Stopwatch();

        for (int i = 0; i < 10; i++)
        {
            Sw.Start();
            await Task.Delay(1000);
            Sw.Stop();
            Console.WriteLine($"Ticks: {Sw.ElapsedTicks}");
        }
        
        Console.ReadLine();
    }
}
