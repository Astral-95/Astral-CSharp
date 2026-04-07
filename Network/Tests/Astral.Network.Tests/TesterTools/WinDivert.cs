using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using WinDivertSharp;

namespace Astral.Tests.TesterTools
{
	public unsafe class WinDivertReceiver : IDisposable
	{
		private byte* InternalBuffer;
		private nuint BufferCapacity;
		private readonly IntPtr Handle;

		// Use a small, fixed-size internal buffer. It needs to be large enough 
		// to hold the largest possible packet (65535), or at least the expected MTU.
		private const nuint DefaultInternalBufferSize = 65536;

		public WinDivertReceiver(IntPtr handle)
		{
			Handle = handle;
			BufferCapacity = DefaultInternalBufferSize;
			// Allocate the unmanaged buffer once
			InternalBuffer = (byte*)NativeMemory.Alloc(BufferCapacity, 16);
		}

		/// <summary>
		/// Receives a single packet and writes it into the caller-provided DestBuffer,
		/// using ref for ReadLength and Address to match the original pattern.
		/// </summary>
		/// <param name="DestBuffer">The managed byte array to write the packet data into.</param>
		/// <param name="ReadLength">Input/Output: Must be 0 on input, contains the actual length read on output.</param>
		/// <param name="Address">Input/Output: Contains the address structure populated by WinDivert.</param>
		/// <returns>True on success, False on failure or if DestBuffer is too small.</returns>
		public bool Receive(byte[] DestBuffer, ref uint ReadLength, ref WinDivertAddress Address)
		{
			Address = default;
			// Call WinDivert to fill the internal unmanaged buffer
			bool success = WinDivertRecv(
				Handle,
				InternalBuffer,
				(uint)BufferCapacity,
				ref Address,
				ref ReadLength
			);

			if (!success || ReadLength == 0)
			{
				ReadLength = 0;
				return false;
			}

			ArgumentOutOfRangeException.ThrowIfGreaterThan(ReadLength, (uint)DestBuffer.Length);

			// High-Performance Copy into the managed DestBuffer (Zero-Allocation on the heap)
			// Marshal.Copy is used for efficient unmanaged-to-managed copy.
			Marshal.Copy((IntPtr)InternalBuffer, DestBuffer, 0, (int)ReadLength);

			return true;
		}

		#region Unsafe P/Invoke
		//[DllImport("WinDivert.dll", EntryPoint = "WinDivertRecv", CallingConvention = CallingConvention.Cdecl)]
		//private static extern unsafe bool WinDivertRecv(
		//	IntPtr Handle,
		//	byte* Packet,
		//	uint PacketLength,
		//	uint* ReadLength,
		//	ref WinDivertAddress Address
		//);

		[DllImport("WinDivert.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool WinDivertRecv([In] IntPtr handle, byte* pPacket, uint packetLen, [In] ref WinDivertAddress pAddr, ref uint readLen);
		#endregion

		public void Dispose()
		{
			if (InternalBuffer != null)
			{
				// Free the unmanaged memory once on disposal
				NativeMemory.Free(InternalBuffer);
				InternalBuffer = null;
			}
		}
	}

	//public unsafe class WinDivertSender : IDisposable
	//{
	//	private byte* BatchBuffer;
	//	// We now manage the Address buffer using IntPtr/GCHandle for easier marshalling
	//	private IntPtr AddrArrayPtr;
	//
	//	private nuint BufferCapacity;    // in bytes
	//	private int AddrCapacity;      // number of addresses
	//
	//	private const nuint DefaultAddrCapacity = 256;
	//
	//	private readonly IntPtr Handle;
	//	private readonly int AddrStructSize;
	//
	//	public int Count { get; private set; }
	//	public uint TotalLength { get; private set; }
	//
	//	public WinDivertSender(IntPtr Handle, nuint DefaultBufferSize = 65536)
	//	{
	//		this.Handle = Handle;
	//		this.AddrStructSize = Marshal.SizeOf<WinDivertAddress>();
	//
	//		BufferCapacity = DefaultBufferSize;
	//		AddrCapacity = (int)DefaultAddrCapacity;
	//
	//		// Allocate unmanaged buffers once for reuse
	//		BatchBuffer = (byte*)NativeMemory.Alloc(BufferCapacity, 16);
	//		AddrArrayPtr = Marshal.AllocHGlobal(AddrCapacity * sizeof(WinDivertAddress));
	//
	//		Reset();
	//	}
	//
	//	/// <summary>
	//	/// Adds a packet to the batch buffer.
	//	/// </summary>
	//	public void AddPacket(byte[] pkt, uint length, WinDivertAddress addr)
	//	{
	//		if (length == 0 || length > pkt.Length) return;
	//		EnsureCapacity((nuint)length);
	//
	//		// Copy packet data into the unmanaged batch buffer
	//		fixed (byte* pktPtr = pkt)
	//		{
	//			Buffer.MemoryCopy(
	//				pktPtr,
	//				BatchBuffer + TotalLength,
	//				(long)(BufferCapacity - TotalLength),
	//				length
	//			);
	//		}
	//
	//		// Use Marshal.StructureToPtr to safely copy the WinDivertSharp struct instance 
	//		// to the correct offset in the unmanaged address array buffer.
	//		IntPtr targetOffsetPtr = IntPtr.Add(AddrArrayPtr, Count * AddrStructSize);
	//		Marshal.StructureToPtr(addr, targetOffsetPtr, false);
	//
	//		TotalLength += length;
	//		Count++;
	//	}
	//
	//	NativeOverlapped Op = new NativeOverlapped();
	//	public bool Send()
	//	{
	//		if (Count == 0) return true;
	//
	//		uint sent = 0;
	//		uint addrLenBytes = (uint)(Count * AddrStructSize);
	//
	//		bool ok = WinDivertSendEx(
	//			Handle,
	//			BatchBuffer,
	//			TotalLength,
	//			ref sent,
	//			0,
	//			AddrArrayPtr, // Pass the IntPtr here
	//			addrLenBytes,
	//			ref Op
	//		);
	//		int Error = Marshal.GetLastWin32Error();
	//		Reset();
	//		return ok;
	//	}
	//
	//	private void EnsureCapacity(nuint pktLength)
	//	{
	//		// 1. Resize batch buffer using Realloc if needed
	//		if ((nuint)TotalLength + pktLength > BufferCapacity)
	//		{
	//			nuint newSize = Math.Max(BufferCapacity * 2, (nuint)TotalLength + pktLength);
	//			BatchBuffer = (byte*)NativeMemory.Realloc(BatchBuffer, newSize);
	//			BufferCapacity = newSize;
	//		}
	//
	//		// 2. Resize address array using Marshal.ReAllocHGlobal if needed
	//		if (Count + 1 > AddrCapacity)
	//		{
	//			int newAddrCapacity = Math.Max(AddrCapacity * 2, Count + 1);
	//
	//			// Reallocate the address buffer via Marshal
	//			AddrArrayPtr = Marshal.ReAllocHGlobal(AddrArrayPtr, (IntPtr)((nuint)newAddrCapacity * (nuint)AddrStructSize));
	//			AddrCapacity = newAddrCapacity;
	//		}
	//	}
	//
	//	
	//	//[DllImport("WinDivert.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
	//	//[return: MarshalAs(UnmanagedType.Bool)]
	//	//public static extern bool WinDivertSendEx(
	//	//	IntPtr handle,
	//	//	byte* pPacket,       // We keep this as byte*
	//	//	UInt32 packetLen,
	//	//	ref UInt32 pSendLen,
	//	//	IntPtr pAddrArray,   // Changed this to IntPtr for compatibility with Marshal.AllocHGlobal
	//	//	UInt32 addrLen,
	//	//	IntPtr pOverlapped,
	//	//	IntPtr pSendCompletionRoutine
	//	//);
	//
	//	[DllImport("WinDivert.dll", EntryPoint = "WinDivertSendEx", CallingConvention = CallingConvention.Cdecl, SetLastError = //true)]
	//	[return: MarshalAs(UnmanagedType.Bool)]
	//	[SuppressUnmanagedCodeSecurity]
	//	public static extern bool WinDivertSendEx([In()] IntPtr handle, [In()] byte* pPacket, uint packetLen, ref uint sendLen, //ulong flags, [In()] IntPtr pAddr, uint addrLen, [In][Out] ref NativeOverlapped lpOverlapped);
	//
	//	public void Reset()
	//	{
	//		Count = 0;
	//		TotalLength = 0;
	//	}
	//
	//	public void Dispose()
	//	{
	//		if (BatchBuffer != null)
	//		{
	//			NativeMemory.Free(BatchBuffer);
	//			BatchBuffer = null;
	//		}
	//		if (AddrArrayPtr != IntPtr.Zero)
	//		{
	//			Marshal.FreeHGlobal(AddrArrayPtr);
	//			AddrArrayPtr = IntPtr.Zero;
	//		}
	//	}
	//}


	public unsafe class WinDivertSender : IDisposable
	{
		private byte* BatchBuffer;
		private WinDivertAddress* AddrArray; // Raw pointer
		private nuint BufferCapacity;        // in bytes
		private nuint AddrCapacity;          // number of addresses
		private const nuint DefaultAddrCapacity = 256;

		private readonly IntPtr Handle;
		private static readonly nuint AddrStructSize = (nuint)sizeof(WinDivertAddress);

		public int Count { get; private set; }
		public uint TotalLength { get; private set; }

		public WinDivertSender(IntPtr handle, nuint defaultBufferSize = 65536)
		{
			Handle = handle;
			BufferCapacity = defaultBufferSize;
			AddrCapacity = DefaultAddrCapacity;

			BatchBuffer = (byte*)NativeMemory.Alloc(BufferCapacity, 16);
			AddrArray = (WinDivertAddress*)NativeMemory.Alloc(AddrCapacity * AddrStructSize, 16);

			Reset();
		}

		public void AddPacket(byte[] pkt, uint length, WinDivertAddress addr)
		{
			if (length == 0 || length > pkt.Length) return;
			EnsureCapacity((nuint)length);

			fixed (byte* pktPtr = pkt)
			{
				Buffer.MemoryCopy(pktPtr, BatchBuffer + TotalLength, BufferCapacity - TotalLength, length);
			}

			AddrArray[Count] = addr;
			TotalLength += length;
			Count++;
		}

		public bool Send()
		{
			if (Count == 0) return true;

			uint sent = 0;
			uint addrLenBytes = (uint)(Count * (int)AddrStructSize);

			bool ok = WinDivertSendEx(
				Handle,
				BatchBuffer,
				TotalLength,
				&sent,
				0,               // Flags
				AddrArray,
				addrLenBytes,
				null,            // Overlapped
				IntPtr.Zero      // pRoutine
			);

			if (!ok)
			{
				int error = Marshal.GetLastWin32Error();
				Console.WriteLine($"WinDivertSendEx failed with Error: {error} (0x{error:X}). TotalLength: {TotalLength}, Count: {Count}, AddrLenBytes: {addrLenBytes}");
			}

			Reset();
			return ok;
		}

		[DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
		public static extern unsafe bool WinDivertSendEx(
			IntPtr handle,
			byte* pPacket,
			uint packetLen,
			uint* pSendLen,
			ulong Flags,
			WinDivertAddress* pAddr,
			uint addrLen,
			NativeOverlapped* pOverlapped,
			IntPtr pRoutine
		);

		private void EnsureCapacity(nuint pktLength)
		{
			// Resize batch buffer
			if (TotalLength + pktLength > BufferCapacity)
			{
				nuint newSize = Math.Max(BufferCapacity * 2, TotalLength + pktLength);
				BatchBuffer = (byte*)NativeMemory.Realloc(BatchBuffer, newSize);
				BufferCapacity = newSize;
			}

			// Resize address array
			if ((nuint)(Count + 1) > AddrCapacity)
			{
				nuint newAddrCapacity = Math.Max(AddrCapacity * 2, (nuint)(Count + 1));
				AddrArray = (WinDivertAddress*)NativeMemory.Realloc(
					AddrArray,
					newAddrCapacity * AddrStructSize
				);
				AddrCapacity = newAddrCapacity;
			}
		}

		public void Reset()
		{
			Count = 0;
			TotalLength = 0;
		}

		public void Dispose()
		{
			if (BatchBuffer != null)
			{
				NativeMemory.Free(BatchBuffer);
				BatchBuffer = null;
			}

			if (AddrArray != null)
			{
				NativeMemory.Free(AddrArray);
				AddrArray = null;
			}
		}
	}
}
