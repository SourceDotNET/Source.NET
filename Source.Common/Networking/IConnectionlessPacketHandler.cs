using CommunityToolkit.HighPerformance;

using Source.Common.Bitbuffers;

using System.Buffers;
using System.Net;
using System.Runtime.InteropServices;

namespace Source.Common.Networking;

using static Protocol;

public class SubChannel
{
	public const int MAX_BITS = 4;
	public const int MAX = 1 << MAX_BITS;

	public int[] StartFragment = new int[(int)FragmentStream.Max];
	public int[] NumFragments = new int[(int)FragmentStream.Max];
	public int SendSeqNumber;
	public SubChannelState State;
	public int Index;

	public void Free() {
		State = SubChannelState.Free;
		SendSeqNumber = -1;
		for (int i = 0; i < (int)FragmentStream.Max; i++) {
			StartFragment[i] = 0;
			NumFragments[i] = 0;
		}
	}
}

public class NetFrame
{
	/// <summary>
	/// Must be power of 2
	/// </summary>
	public const int BACKUP = 64;
	public const int MASK = BACKUP - 1;

	public TimeUnit_t Time;
	public int Size;
	public TimeUnit_t Latency;
	public TimeUnit_t AverageLatency;
	public bool IsValid;
	public int ChokedPackets;
	public int DroppedPackets;
	public TimeUnit_t InterpolationAmount;
	public ushort[] MessageGroups = new ushort[(uint)NetChannelGroup.Total];
}

public class NetFlow
{
	public const int MAX = 2;
	/// <summary>
	/// How fast to converge flow estimates
	/// </summary>
	public const double FLOW_AVG = 3d / 4d;
	/// <summary>
	/// Don't compute more often than this
	/// </summary>
	public const double FLOW_INTERVAL = .25d;

	public const int FLOW_OUTGOING = 0;
	public const int FLOW_INCOMING = 1;
	public const int MAX_FLOWS = 2;

	public TimeUnit_t NextCompute;
	public TimeUnit_t AverageBytesPerSec;
	public TimeUnit_t AveragePacketsPerSec;
	public TimeUnit_t AverageLoss;
	public TimeUnit_t AverageChoke;
	public TimeUnit_t AverageLatency;
	public TimeUnit_t Latency;
	public int TotalPackets;
	public int TotalBytes;
	public int CurrentIndex;
	public readonly NetFrame[] Frames = new NetFrame[NetFrame.BACKUP];
	public NetFrame? CurrentFrame;

	public NetFlow() {
		for (int i = 0; i < Frames.Length; i++)
			Frames[i] = new();
	}
}

[Flags]
public enum PacketFlag
{
	Invalid = -1,
	None = 0,
	Reliable = 1 << 0,     // 1 0 0 0 0 0 0 0
	Compressed = 1 << 1,   // 0 1 0 0 0 0 0 0
	Encrypted = 1 << 2,    // 0 0 1 0 0 0 0 0
	Split = 1 << 3,        // 0 0 0 1 0 0 0 0
	Choked = 1 << 4,       // 0 0 0 0 1 0 0 0
	Challenge = 1 << 5,    // 0 0 0 0 0 1 0 0
}
public class NetAddress
{
	public NetAddressType Type;
	public IPEndPoint? Endpoint = new IPEndPoint(IPAddress.Any, 0);


	public NetAddress Copy() => new() {
		Type = Type,
		Endpoint = Endpoint == null ? null : new IPEndPoint(Endpoint.Address, Endpoint.Port)
	};
	static char[][] renderBuffers;
	static int curRenderBuffer;
	static NetAddress() {
		renderBuffers = new char[4][];
		for (int i = 0; i < renderBuffers.Length; i++)
			renderBuffers[i] = new char[64];
	}

	public ReadOnlySpan<char> ToString(bool onlyBase) {
		Span<char> renderBuffer = renderBuffers[curRenderBuffer = (curRenderBuffer + 1) % renderBuffers.Length];
		memreset(renderBuffer);

		if (Type == NetAddressType.Loopback)
			"loopback".CopyTo(renderBuffer);
		else if (Type == NetAddressType.Broadcast)
			"broadcast".CopyTo(renderBuffer);
		else if (Type == NetAddressType.IP)
			if (onlyBase)
				PrintAddress(renderBuffer);
			else
				PrintAddressWithPort(renderBuffer);
		else
			"unknown".CopyTo(renderBuffer);

		return renderBuffer.SliceNullTerminatedString();
	}

	private void PrintAddressWithPort(Span<char> renderBuffer) {
		const string formatIPV4 = "%i.%i.%i.%i:%i";
		byte[] addr = Endpoint!.Address.GetAddressBytes();
		sprintf(renderBuffer, formatIPV4).I(addr[0]).I(addr[1]).I(addr[2]).I(addr[3]).I(Endpoint.Port);
	}

	private void PrintAddress(Span<char> renderBuffer) {
		const string formatIPV4 = "%i.%i.%i.%i";
		byte[] addr = Endpoint!.Address.GetAddressBytes();
		sprintf(renderBuffer, formatIPV4).I(addr[0]).I(addr[1]).I(addr[2]).I(addr[3]);
	}

	public override string ToString() {
		if (Endpoint == null) return "<NULL NETADDR>";

		return $"<{Type} {Endpoint.ToString()}>";
	}

	public IPAddress IP {
		get => Endpoint?.Address ?? IPAddress.None;
		set => (Endpoint ?? throw new Exception()).Address = value;
	}

	public ushort Port {
		get => (ushort)(Endpoint?.Port ?? 0);
		set => (Endpoint ?? throw new Exception()).Port = value;
	}

	// TODO: This sucks
	public uint GetIPNetworkByteOrder() {
		byte[] data = Endpoint!.Address.GetAddressBytes();
		return data.AsSpan().Cast<byte, uint>()[0];
	}

	// TODO: This sucks
	public uint GetIPHostByteOrder() {
		byte[] data = Endpoint!.Address.GetAddressBytes();
		data.AsSpan().Reverse();
		return data.AsSpan().Cast<byte, uint>()[0];
	}

	public short GetPort() => (short)(Endpoint?.Port ?? 0);

	public NetAddress() {

	}

	public NetAddress(string hostname) {
		Endpoint = IPEndPoint.Parse(hostname);
	}

	public NetAddress(string hostname, ushort port) {
		Endpoint = IPEndPoint.Parse($"{hostname}:{port}");
	}

	public void SetFromEndPoint(EndPoint ep) {
		Endpoint = (IPEndPoint)ep; // please work please work please work please work please work please work please work please work please work please work please work 
	}

	public static implicit operator IPEndPoint(NetAddress addr) => addr.Endpoint ?? throw new Exception("Unset endpoint");

	public bool CompareAddress(NetAddress? other) => other != null && IP.Equals(other.IP) && Port == other.Port;

	public void Clear() {
		Endpoint = null;
		Type = NetAddressType.Null;
	}

	public bool IsLoopback() {
		return Type == NetAddressType.Loopback;
	}

	public bool IsLocalhost() {
		byte[] ip = Endpoint!.Address.GetAddressBytes();
		return (ip[0] == 127) && (ip[1] == 0) && (ip[2] == 0) && (ip[3] == 1);
	}

	public bool CompareAdr(NetAddress? a, bool onlyBase = false) {
		if (a.Type != Type)
			return false;

		if (Type == NetAddressType.Loopback)
			return true;

		if (Type == NetAddressType.Broadcast)
			return true;

		if (Type == NetAddressType.IP) {
			if (!onlyBase && (Port != a.Port))
				return false;

			byte[] ip = Endpoint!.Address.GetAddressBytes();
			byte[] a_ip = a.Endpoint!.Address.GetAddressBytes();

			if (a_ip[0] == ip[0] && a_ip[1] == ip[1] && a_ip[2] == ip[2] && a_ip[3] == ip[3])
				return true;
		}

		return false;
	}

	public void SetIP(uint v) {
		IP = new(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v));
	}
	public void SetIP(uint v, ushort port) {
		SetIP(v);
		Port = port;
	}
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SPLITPACKET
{
	public int NetID;
	public int SequenceNumber;
	public short PacketID;
	public short SplitSize;
}

public class LONGPACKET
{
	public int CurrentSequence;
	public int SplitCount;
	public int TotalSize;
	public int ExpectedSplitSize;
	public byte[] Buffer;
	public bool RentedBuffer;
	public LONGPACKET() {
		Buffer = ArrayPool<byte>.Shared.Rent(MAX_MESSAGE);
		RentedBuffer = true;
	}
	~LONGPACKET() {
		if (RentedBuffer) {
			ArrayPool<byte>.Shared.Return(Buffer, true);
		}
	}
}

public class SplitPacketEntry
{
	public NetAddress? From;
	public readonly int[] SplitFlags = new int[MAX_SPLITPACKET_SPLITS];
	public LONGPACKET NetSplit = new();
	public double LastActiveTime;
}

public unsafe class NetScratchBuffer
{
	public readonly byte[] Data = ArrayPool<byte>.Shared.Rent(MAX_MESSAGE);
	public int Length => Data.Length;
}

public abstract class NetMessage : INetMessage
{
	private int type;
	protected bool reliable;
	private INetChannel? netchan;

	public NetMessage(int type) {
		this.type = type;
		reliable = true;
	}

	// Implement interface

	public INetChannel? GetNetChannel() => netchan;
	public void SetNetChannel(INetChannel? netchan) => this.netchan = netchan;

	public bool IsReliable() => reliable;
	public void SetReliable(bool state) => reliable = state;

	public int GetMessageType() => type;
	public string GetName() => GetType().Name;

	public virtual bool Process() {
		return netchan?.GetMsgHandler()?.ProcessMessage(this) ?? false;
	}

	public virtual bool ReadFromBuffer(bf_read buffer) => false;
	public virtual bool WriteToBuffer(bf_write buffer) => false;

	public virtual NetChannelGroup GetGroup() => NetChannelGroup.Generic;

	public override string ToString() {
		return $"{GetType().Name} <no ToString() avail>";
	}
}

public unsafe class NetPacket
{
	public NetAddress From;
	public byte[]? Data;
	public int Size;
	public double Received;
	public NetSocketType Source;
	public bf_read Message;
	public int WireSize;
	public bool Stream;

	public NetPacket() {
		From = new();
		Size = 0;
		Received = 0;
		Source = NetSocketType.NotApplicable;
		Message = new();
		WireSize = 0;
		Stream = false;
	}
}

public interface IConnectionlessPacketHandler
{
	public bool ProcessConnectionlessPacket(NetPacket packet);
}
