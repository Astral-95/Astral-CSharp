using System.Collections.Concurrent;
using Astral.Network.Connections;
using Astral.Network.Enums;
using Astral.Network.Tools;
using Astral.Network.Transport;
using Xunit.Abstractions;

namespace Astral.Network.UnitTests.Tools;

public class GlobalData
{
	public int NumSends = 0;
	public int NumReceives = 0;
	public int NumGroupSends = 0;
	public int NumGroupReceives = 0;
	public void IncrementSends() => Interlocked.Increment(ref NumSends);
	public void IncrementReceives() => Interlocked.Increment(ref NumReceives);
	public void IncrementGroupSends() => Interlocked.Increment(ref NumGroupSends);
	public void IncrementGroupReceives() => Interlocked.Increment(ref NumGroupReceives);
}
public class SenderOptions
{
	public NetaConnection Connection;
	public PacketFragmentationHandler Handler;
	public ConcurrentQueue<PooledInPacket> TargetQueue;
	public GlobalData Global;

	public int NumSenders = 512;
	public int MaxGroupsAtOnce = 2048;
	public int MinPacketSize = 128;
	public int MaxPacketSize = 128;
}

public class ReceiverOptions
{
	public PacketFragmentationHandler Handler;
	public ConcurrentQueue<PooledInPacket> TargetQueue;
	public GlobalData Global;

	public int NumReceivers = 512;
}

[Collection("DisableParallelizationCollection")]
public class PacketFragmentationHandlerLongTest
{
	private readonly ITestOutputHelper Output;

	public PacketFragmentationHandlerLongTest(ITestOutputHelper Output)
	{
		//AutoParallelTickManager.Initialize();
		this.Output = Output;
	}

	void CheckPoolLeaks(bool LeaksExpected = false)
	{
		var LeaksStrList = PooledObjectsTracker.ReportLeaks();
		if (LeaksStrList != null && !LeaksExpected)
		{
			Output.WriteLine(string.Join("\n", LeaksStrList!));
			//Assert.Fail(string.Join("\n", LeaksStrList!));
			Assert.Fail($"Pool leak.");
		}
	}


	class PktFragHandlerLongTest_Senders_1 { }
	class PktFragHandlerLongTest_Senders_2 { }

	async Task Senders(SenderOptions Opts, CancellationTokenSource Cts)
	{
		using SemaphoreSlim Semaphore = new SemaphoreSlim(Opts.MaxGroupsAtOnce);

		var BodySize = Random.Shared.Next(Opts.MinPacketSize, Opts.MaxPacketSize);

		byte[] SendBuffer = new byte[BodySize];
		for (int I = 0; I < BodySize; I++)
			SendBuffer[I] = (byte)((I & 1) == 0 ? 1 : 0);

		List<Task> Tasks = new List<Task>();

		var Options = new ParallelOptions
		{
			CancellationToken = Cts.Token,
			MaxDegreeOfParallelism = Environment.ProcessorCount
		};

		await Parallel.ForEachAsync(Enumerable.Range(0, Opts.NumSenders), Options, async (I, Ct) => 
		{
			while (!Cts.IsCancellationRequested)
			{
				try
				{
					var Fragments = new List<PooledOutPacket>();
					try
					{
						await Semaphore.WaitAsync();

						var PktOut = PooledOutPacket.RentReliable<PktFragHandlerLongTest_Senders_1>(
							Opts.Connection.NextPacketId, EProtocolMessage.Reliable);

						PktOut.Serialize(SendBuffer);

						Opts.Handler.ProcessOutgoingPacket(PktOut, Fragments, Context.Ticks);
						Shuffle(Fragments, Random.Shared);

						for (int Index = Fragments.Count - 1; Index >= 0; Index--)
						{
							var Pkt = Fragments[Index];
							Fragments.RemoveAt(Index);
							var PacketIn = PooledInPacket.Rent<PktFragHandlerLongTest_Senders_2>(Pkt);
							Pkt.Return();
							Opts.TargetQueue.Enqueue(PacketIn);
							Opts.Global.IncrementSends();
							//await Task.Delay(Random.Shared.Next(5), Cts.Token);
						}
						Opts.Global.IncrementGroupSends();

						//Thread.Yield();
						await Task.Delay(0);
						//var Sw = new SpinWait();
						//for (int i = 0; i < 14; i++)
						//	Sw.SpinOnce();
					}
					finally { foreach (var Pkt in Fragments) Pkt.Return(); }
				}
				catch (OperationCanceledException) { break; }
				finally { Semaphore.Release(); }
			}
		});
	}


	async Task Receivers(ReceiverOptions Opts, CancellationTokenSource Cts)
	{
		List<Task> Tasks = new List<Task>();

		var Options = new ParallelOptions
		{
			CancellationToken = Cts.Token,
			MaxDegreeOfParallelism = Environment.ProcessorCount
		};

		await Parallel.ForEachAsync(Enumerable.Range(0, Opts.NumReceivers), Options, async (PrallelIndex, Ct) => 
		{
			while (!Cts.IsCancellationRequested)
			{
				while (Opts.TargetQueue.TryDequeue(out var Pkt))
				{
					Opts.Global.IncrementReceives();
					Pkt.Init();
					Pkt.Serialize<long>(); // Consume timestamp
					PooledInPacket? Combined = Opts.Handler.ProcessIncomingPacket(Pkt);
					if (Combined == null) continue;
					var RecvBuffer = Combined.SerializeArray<byte>();
					for (int I = 0; I < RecvBuffer.Length; I++)
					{
						byte Expected = (byte)((I & 1) == 0 ? 1 : 0);
						var Byte = RecvBuffer[I];
						if (Byte != Expected) throw new InvalidOperationException($"Buffer mismatch");
					}
		
					Combined.Return();
					Opts.Global.IncrementGroupReceives();
				}
				//Thread.Yield();
				await Task.Delay(1);
			}
		});

		//for (int Index = 0; Index < Opts.NumReceivers; Index++)
		//{
		//	Tasks.Add(Task.Run(async () => 
		//	{
		//		while (!Cts.IsCancellationRequested)
		//		{
		//			while (Opts.TargetQueue.TryDequeue(out var Pkt))
		//			{
		//				Opts.Global.IncrementReceives();
		//				Pkt.Init();
		//				InPacket? Combined = Opts.Handler.ProcessIncomingPacket(Pkt);
		//				if (Combined == null) continue;
		//				var RecvBuffer = Combined.SerializeArray<byte>();
		//				for (int I = 0; I < RecvBuffer.Length; I++)
		//				{
		//					byte Expected = (byte)((I & 1) == 0 ? 1 : 0);
		//					var Byte = RecvBuffer[I];
		//					if (Byte != Expected) throw new InvalidOperationException($"Buffer mismatch");
		//				}
		//
		//				Combined.Return();
		//				Opts.Global.IncrementGroupReceives();
		//				
		//			}
		//			await Task.Delay(1);
		//		}
		//	}));
		//}
		//
		//while (Tasks.Count > 0)
		//{
		//	Task Finished = await Task.WhenAny(Tasks);
		//	Tasks.Remove(Finished);
		//
		//	if (Finished.IsFaulted)
		//	{
		//		Cts.Cancel();
		//		await Finished;
		//		return;
		//	}
		//}
	}

	[Fact]
	public async Task InterleavedExtremeStressAsync()
	{
		await using var _ = await AsyncScopeLock.LockAsync();
		PooledObjectsTracker.ClearForTests();

		const int MaxRunTimeMs = 10_000;

		CancellationTokenSource Cts = new CancellationTokenSource();
		var Shared = new GlobalData();

		int NumParallelRuns = 2;

		List<Task> Tasks = new List<Task>();
		Task TImeoutTask = Task.Delay(MaxRunTimeMs);
		Tasks.Add(TImeoutTask);

		for (int i = 0; i < NumParallelRuns; i++)
		{
			Tasks.Add(Task.Run(async () =>
			{
				var Sock = new NetaConnection();
				var Handler = new PacketFragmentationHandler(Sock);
				ConcurrentQueue<PooledInPacket> InFlightFrags = new ConcurrentQueue<PooledInPacket>();

				var SenderOpts = new SenderOptions();
				SenderOpts.Connection = Sock;
				SenderOpts.Handler = Handler;
				SenderOpts.NumSenders = 8;
				SenderOpts.MinPacketSize = 1024;
				SenderOpts.MaxPacketSize = 4024;
				SenderOpts.MaxGroupsAtOnce = 16048;
				SenderOpts.TargetQueue = InFlightFrags;
				SenderOpts.Global = Shared;

				var RecversOpts = new ReceiverOptions();
				RecversOpts.Handler = Handler;
				RecversOpts.TargetQueue = InFlightFrags;
				RecversOpts.Global = Shared;
				RecversOpts.NumReceivers = 4;

				try { await Task.WhenAll(Senders(SenderOpts, Cts), Receivers(RecversOpts, Cts)); }
				finally
				{
					Handler.Reset();
					while (InFlightFrags.TryDequeue(out var Pkt)) Pkt.Return();
				}
			}));
		}

		while (Tasks.Count > 0)
		{
			Task Finished = await Task.WhenAny(Tasks);
			Tasks.Remove(Finished);

			if (Finished == TImeoutTask)
			{
				await Cts.CancelAsync(); continue;
			}

			if (Finished.IsFaulted)
			{
				Cts.Cancel();
				await Finished;
				return;
			}
		}

		Output.WriteLine($"Sends: [{Shared.NumSends}] - Receives: [{Shared.NumReceives}]");
		Output.WriteLine($"GroupSends: [{Shared.NumGroupSends}] - GroupReceives: [{Shared.NumGroupReceives}]");

		CheckPoolLeaks(false);
	}

	static void Shuffle<T>(IList<T> List, Random Rng)
	{
		for (int I = List.Count - 1; I > 0; I--)
		{
			int J = Rng.Next(0, I + 1);
			(List[I], List[J]) = (List[J], List[I]);
		}
	}
}