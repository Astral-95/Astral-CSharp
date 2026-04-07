using System.Diagnostics;
using System.Runtime.InteropServices;
using Astral.Logging;
using Astral.Network;
using Astral.Network.Tools;
using WinDivertSharp;

namespace Astral.Tests.TesterTools
{
	public class PacketInterceptorOptions
	{
		public int BaseLatency = 50; 
		public float BasePktLoss = 50; 
	}
	public static class WinDivertInterceptor
	{
		private struct Packet
		{
			public WinDivertBuffer Buffer;
			public WinDivertAddress Addr;
			public uint Length;
		}

#pragma warning disable CS8618
		private static readonly double TickFrequency = (double)Context.ClockFrequency; // ticks per second

		// --- Baseline ---
		public static readonly long BaselineDelayTicks = (long)(0.030 * TickFrequency);
		public static readonly long BaselineJitterTicks = (long)(0.001 * TickFrequency);
		public static readonly float BaselineLossChance = 0.00f;

		// --- Block configuration ---
		public static int BlockDurationSeconds = 30;

		public static int BlockDurationSecondsMin = 50;
		public static int BlockDurationSecondsMax = 70;

		// --- Event probabilities per block (percent) ---
		//public static int MinorChance = 70;
		public static int MinorChance = 65;
		public static int MajorChance = 28;
		public static int CatastrophicChance = 2;

		// --- Spike ranges ---
		public static (float Min, float Max) MinorDelay = (40, 60);
		public static (float Min, float Max) MinorLoss = (2, 5);
		public static (int Min, int Max) MinorDuration = (4, 5);

		public static (float Min, float Max) MajorDelay = (75, 125);
		public static (float Min, float Max) MajorLoss = (5, 15);
		public static (int Min, int Max) MajorDuration = (6, 12);

		public static (float Min, float Max) CatastrophicDelay = (150, 250);
		public static float CatastrophicLoss = 50;
		public static (int Min, int Max) CatastrophicDuration = (5, 10);

		private static long CurrentDelayTicks = BaselineDelayTicks;
		private static float CurrentLossChance = BaselineLossChance;
		private static long CurrentJitterTicks = BaselineJitterTicks;


		private static readonly PriorityQueue<Packet, long> PacketsQueue = new();
		//private static readonly List<Packet> DelayedPackets = new(32768);
		static SpinLock DelayedPacketsSpinLock = new();

		static nint Handle = 0;

		private static AutoResetEvent NewPacketEvent = new(false);
		static Thread? RecvThread;
		static Thread? SendThread;
		//static byte[] RecvBuffer = new byte[NetaConsts.BufferMaxSizeBytes];
        //static WinDivertBuffer DivertBuffer = new WinDivertBuffer(RecvBuffer);

		static CancellationTokenSource? Cts;
		static AstralLogger Logger = new AstralLogger("WinDivertInterceptor");

		public static void Start(string Filter = "udp")
		{
			return;
			if (Handle != IntPtr.Zero) return;
			Cts = new CancellationTokenSource();

			Handle = WinDivert.WinDivertOpen(Filter, WinDivertLayer.Network, 0, WinDivertOpenFlags.Drop);

			if (Handle == IntPtr.Zero)
			{
				throw new Exception("Failed to open WinDivert handle");
			}

			RecvThread = new Thread(RecvLoop) { IsBackground = true, Name = "WD_Recv" };
			SendThread = new Thread(SendLoop) { IsBackground = true, Name = "WD_Send", Priority = ThreadPriority.AboveNormal };

			RecvThread.Start();
			SendThread.Start();

			Task.Run(async () =>
			{
				await Task.Delay(2500);
				while (!Cts.IsCancellationRequested)
				{
					int RandomDelay = Random.Shared.Next(BlockDurationSeconds) * 1000;
					Logger.LogInfo($"Packets event in {RandomDelay / 1000} sec...");
					await Task.Delay(RandomDelay, Cts.Token);

					if (Cts.IsCancellationRequested) return;

					int EventRoll = Random.Shared.Next(100);
					if (EventRoll < MinorChance)
						await ApplySpikeAsync(MinorDelay, MinorLoss, MinorDuration, "Minor");
					else if (EventRoll < MinorChance + MajorChance)
						await ApplySpikeAsync(MajorDelay, MajorLoss, MajorDuration, "Major"); 
					else
						await ApplySpikeAsync(CatastrophicDelay, (CatastrophicLoss, CatastrophicLoss), CatastrophicDuration, "Catastrophic");

					// Wait remaining block
					//int Remaining = BlockDurationSeconds * 1000 - RandomDelay;
					//if (Remaining > 0)
					//	await Task.Delay(Remaining, Cts.Token);
				}
			}, Cts.Token);
		}

		class WinDivertInterceptor_RecvLoop { }
		private static void RecvLoop()
		{
			while (true)
			{
				WinDivertAddress Addr = new WinDivertAddress();
				uint ReadLen = 0;
				var DivertBuffer = new WinDivertBuffer(new byte[NetaConsts.BufferMaxSizeBytes]);
				//bool Received = Receiver!.Receive(Buffer, ref ReadLen, ref Addr);
				bool Received = WinDivert.WinDivertRecv(Handle, DivertBuffer, ref Addr, ref ReadLen);

				if (!Received)
				{
					int Error = Marshal.GetLastWin32Error();
					if (Error == 0 || Error == 259 /*NO_MORE_ITEMS*/)
						continue; // harmless non-packet
					break;        // real error → break out
				}

				if (ReadLen <= 0)
					continue;

				// if cancellation requested, re-inject immediately and skip renting
				if (Cts!.IsCancellationRequested)
				{
					WinDivert.WinDivertSend(Handle, DivertBuffer, ReadLen, ref Addr);
					continue;
				}

				if (Random.Shared.NextDouble() < CurrentLossChance) continue;

				long JitterTicks = (long)((Random.Shared.NextDouble() * 2 - 1) * Interlocked.Read(ref CurrentJitterTicks));
				long ReleaseTicks = Context.Ticks + Interlocked.Read(ref CurrentDelayTicks) + JitterTicks;

				var DuePacket = new Packet();
				DuePacket.Buffer = DivertBuffer;
				DuePacket.Addr = Addr;
				DuePacket.Length = ReadLen;

				bool LockTaken = false;
				try
				{
					DelayedPacketsSpinLock.Enter(ref LockTaken);
					PacketsQueue.Enqueue(DuePacket, ReleaseTicks);
				}
				finally { if (LockTaken) DelayedPacketsSpinLock.Exit(); }
					
				NewPacketEvent.Set();
			}
		}

		class WinDivertInterceptor_SendLoop { }
		public static void SendLoop()
		{
			long MinReleaseTicks;
			while (!Cts!.IsCancellationRequested)
			{
				long CurrentTicks = Context.Ticks;

				List<Packet> DuePackets = new List<Packet>();

				var Count = 0;
				bool LockTaken = false;
				try
				{
					DelayedPacketsSpinLock.Enter(ref LockTaken);
					MinReleaseTicks = long.MaxValue;
					Count = PacketsQueue.Count;

					while (PacketsQueue.TryPeek(out var Pkt, out long ReleaseTicks))
					{
						if (ReleaseTicks > Context.Ticks)
						{
							MinReleaseTicks = ReleaseTicks;
							break;
						}

						PacketsQueue.Dequeue();
						DuePackets.Add(Pkt);
					}
				}
				finally { if (LockTaken) DelayedPacketsSpinLock.Exit(); }

				if (DuePackets.Count > 0)
				{
					if (Random.Shared.NextDouble() < 0.005f)
					{
						Shuffle(DuePackets, Random.Shared);
					}

					foreach (var DuePacket in DuePackets)
					{
						var Addr = DuePacket.Addr;
						try
						{
							var Tks = Context.Ticks;
							WinDivert.WinDivertSend(Handle, DuePacket.Buffer, DuePacket.Length, ref Addr);
							var Elpsed = Context.Ticks - Tks;
						}
						catch (Exception Ex)
						{
							NetGuard.Fail(Ex.ToString());
						}
						finally
						{
						}
					}
				}

				if (Count - DuePackets.Count < 1)
				{
					NewPacketEvent.WaitOne();
					continue;
				}

				var WaitTicks = MinReleaseTicks - CurrentTicks;

                if (WaitTicks <= 0) continue;

				int WaitMs = (int)Math.Ceiling(WaitTicks / TickFrequency * 1000.0);

				if (WaitMs > 16)
				{
					NewPacketEvent.WaitOne(WaitMs);
					continue;
				}

				while (true)
				{
					if (NewPacketEvent.WaitOne(0)) break;

					long ElapsedTicks = Context.Ticks - CurrentTicks;
					if (ElapsedTicks >= WaitTicks)
						break;

					Thread.SpinWait(50);
					Thread.Yield();
				}
			}
		}


		private static long MsToTicks(double ms)
		{
			return (long)Math.Round(ms / 1000.0 * Context.ClockFrequency);
		}

		static async Task ApplySpikeAsync((float Min, float Max) DelayRange, (float Min, float Max) LossRange, (int Min, int Max) DurationRange, string Name)
		{
			double DelayMs = Random.Shared.NextDouble() * (DelayRange.Max - DelayRange.Min) + DelayRange.Min;
			Interlocked.Exchange(ref CurrentDelayTicks, MsToTicks(DelayMs));
			var NewLose = (float)(Random.Shared.NextDouble() * (LossRange.Max - LossRange.Min) + LossRange.Min) / 100f;
			Interlocked.Exchange(ref CurrentLossChance, NewLose);

			int DurationSec = Random.Shared.Next(DurationRange.Min, DurationRange.Max + 1);
			Logger.LogInfo($"{Name} spike -> Delay={DelayMs:F2}ms, Loss={NewLose * 100:F2}%, Duration={DurationSec}s");

			await Task.Delay(DurationSec * 1000).ContinueWith(_ =>
			{
				Logger.LogInfo($"{Name} spike -> Delay={DelayMs:F2}ms, Loss={NewLose * 100:F2}%, Duration={DurationSec}s Ended.");
				Interlocked.Exchange(ref CurrentDelayTicks, BaselineDelayTicks);
				Interlocked.Exchange(ref CurrentLossChance, BaselineLossChance);
				Interlocked.Exchange(ref CurrentJitterTicks, BaselineJitterTicks);
			});
		}

		class WinDivertInterceptor_Stop { }
		public static async Task Stop()
		{
			if (Cts == null || Cts.IsCancellationRequested) return;
			Cts.Cancel();

			// 3. Wait for threads to finish gracefully
			await Task.Run(() =>
			{
				RecvThread?.Join(1000); // Wait max 1 sec
				SendThread?.Join(2000); // Give SendThread time to flush
			});

			while (PacketsQueue.TryDequeue(out var Pkt, out var ReleaseTicks))
			{
				throw new NotImplementedException();
				//ArrayPool<byte>.Shared.Return(Pkt.Buffer);
			}

			if (Handle != 0)
			{
				WinDivert.WinDivertClose(Handle);
				Handle = 0;
			}
		}


		static void Shuffle<T>(IList<T> List, Random Rng)
		{
			for (Int32 I = List.Count - 1; I > 0; I--)
			{
				Int32 J = Rng.Next(0, I + 1);
				(List[I], List[J]) = (List[J], List[I]);
			}
		}
	}
}
