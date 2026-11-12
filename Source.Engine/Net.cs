using Snappier;

using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Compression;
using Source.Common.Networking;

using Steamworks;

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

using static Source.Common.Networking.Protocol;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Source.Common.Networking;

public class Net
{
	const int LOOPBACK_SOCKETS = 2;
	InlineArray2<Queue<Loopback>> Loopbacks = new();
	public Net() {
		Loopbacks[0] = [];
		Loopbacks[1] = [];
	}
	static bool net_noip = false;   // Disable IP support, can't switch to MP mode
	static bool net_nodns = false;  // Disable DNS request to avoid long timeouts
	static bool net_notcp = true;   // Disable TCP support
	static bool net_nohltv = false; // disable HLTV support
	static bool net_dedicated = false;  // true is dedicated system

	internal static readonly ConVar net_showsplits = new("net_showsplits", "0", 0, "Show info about packet splits");
	internal static readonly ConVar net_showmsg = new("net_showmsg", "0", 0, "Show incoming message: <0|1|name>");
	internal static readonly ConVar net_showfragments = new("net_showfragments", "0", 0, "Show netchannel fragments");
	internal static readonly ConVar net_showpeaks = new("net_showpeaks", "0", 0, "Show messages for large packets only: <size>");
	internal static readonly ConVar net_blockmsg = new("net_blockmsg", "none", FCvar.Cheat, "Discards incoming message: <0|1|name>");
	internal static readonly ConVar net_showdrop = new("net_showdrop", "0", 0, "Show dropped packets in console");
	internal static readonly ConVar net_drawslider = new("net_drawslider", "0", 0, "Draw completion slider during signon");
	internal static readonly ConVar net_chokeloopback = new("net_chokeloop", "0", 0, "Apply bandwidth choke to loopback packets");
	internal static readonly ConVar net_maxfilesize = new("net_maxfilesize", "16", 0, "Maximum allowed file size for uploading in MiB", 0, 64);
	internal static readonly ConVar net_compresspackets = new("net_compresspackets", "1", 0, "Use compression on game packets.");
	internal static readonly ConVar net_compresspackets_minsize = new("net_compresspackets_minsize", "1024", 0, "Don't bother compressing packets below this size.");
	internal static readonly ConVar net_maxcleartime = new("net_maxcleartime", "4.0", 0, "Max # of seconds we can wait for next packets to be sent based on rate setting (0 == no limit).");
	internal static readonly ConVar net_maxpacketdrop = new("net_maxpacketdrop", "5000", 0, "Ignore any packets with the sequence number more than this ahead (0 == no limit)");


	public readonly NetAddress LocalAdr = new();



	public List<VecSplitPacketEntries> SplitPackets = [];

	public void DiscardStaleSplitPackets(NetSocketType sock) {
		VecSplitPacketEntries splitPacketEntries = SplitPackets[(int)sock];
		int i;
		for (i = splitPacketEntries.Count - 1; i >= 0; i--) {
			SplitPacketEntry entry = splitPacketEntries[i];
			Debug.Assert(entry != null);

			if (Time < entry.LastActiveTime + SPLIT_PACKET_STALE_TIME)
				continue;

			splitPacketEntries.RemoveAt(i);
		}

		if (splitPacketEntries.Count > SPLIT_PACKET_TRACKING_MAX) {
			while (splitPacketEntries.Count > SPLIT_PACKET_TRACKING_MAX) {
				SplitPacketEntry entry = splitPacketEntries[i];
				if (Time != entry.LastActiveTime) {
					splitPacketEntries.RemoveAt(0);
				}
			}
		}
	}

	public SplitPacketEntry? FindOrCreateSplitPacketEntry(NetSocketType sock, NetAddress from) {
		VecSplitPacketEntries splitPacketEntries = SplitPackets[(int)sock];
		int i = 0;
		int count = splitPacketEntries.Count;
		SplitPacketEntry? entry = null;

		for (i = 0; i < count; i++) {
			entry = splitPacketEntries[i];
			Debug.Assert(entry != null);

			if (from.CompareAddress(entry.From))
				break;
		}

		if (i >= count) {
			SplitPacketEntry newEntry = new();
			newEntry.From = from;

			splitPacketEntries.Add(newEntry);
			// C++ does it like this for pointer reasons; we could just throw newEntry into here instead though
			entry = splitPacketEntries[splitPacketEntries.Count - 1];
		}

		return entry;
	}

	public string DescribeSocket(int sock) {
		switch (sock) {
			case 0: return "cl ";
			case 1: return "sv ";
			default: return "?? ";
		}
	}
	public string DescribeSocket(NetSocketType sock) => DescribeSocket((int)sock);

	public static int Bits2Bytes(int b) {
		return b + 7 >> 3;
	}
	public static TimeUnit_t Time { get; private set; }

	public bool IsMultiplayer() => Multiplayer; // its always going to be true; listen servers don't exist yet (if ever)


	public readonly List<NetSocket> NetSockets = [];
	public readonly List<NetPacket> NetPackets = [];
	public readonly List<NetChannel> NetChannels = [];
	public readonly Stack<NetScratchBuffer> NetScratchBuffers = [];

	public NetScratchBuffer ObtainScratchBuffer() {
		if (NetScratchBuffers.TryPop(out var res))
			return res;

		res = new NetScratchBuffer();
		NetScratchBuffers.Push(res);
		return res;
	}

	public static void OutOfBandPrint(NetSocketType sock, NetAddress adr, ReadOnlySpan<char> text){
		// todo
	}

	public bool StringToAdr(ReadOnlySpan<char> host, int port, [NotNullWhen(true)] out IPEndPoint? ep) {
		ep = null;
		if (host.IsEmpty) return false;

		if (host == "localhost") {
			ep = new IPEndPoint(IPAddress.Loopback, port);
			return true;
		}
		if (IPAddress.TryParse(host, out IPAddress? ip)) {
			ep = new IPEndPoint(ip, port < 0 ? 0 : port);
			return true;
		}

		return false;
	}

	public bool StringToAdr(ReadOnlySpan<char> host, [NotNullWhen(true)] out IPEndPoint? ep) {
		host = host.SliceNullTerminatedString();
		ep = null;
		if (host.IsEmpty) return false;

		Span<char> address = stackalloc char[128];
		strcpy(address, host);
		if (strncmp(address, "localhost", 10) == 0 || strncmp(address, "localhost:", 10) == 0)
			memcpy(address, "127.0.0.1");

		if (!IPEndPoint.TryParse(address.SliceNullTerminatedString(), out ep))
			return false;

		if (ep.Port == 0)
			ep.Port = Net.PORT_SERVER;

		return true;
	}

	public NetChannel? CreateNetChannel(
		NetSocketType socket, NetAddress address, string name, INetChannelHandler handler,
		bool forceNewChannel = false, int protocolVersion = VERSION
	) {

		NetChannel? channel = null;

		if (!forceNewChannel && address != null && TryFindNetChannel(socket, address, out channel))
			channel.Clear();

		if (channel == null) {
			channel = new NetChannel(this);
			lock (NetChannels) {
				NetChannels.Add(channel);
			}
		}

		// ClearLagData?

		channel.Setup(socket, address, name, handler, protocolVersion);

		return channel;
	}

	public void RemoveNetChannel(NetChannel netchan, bool delete) {
		if (netchan == null) return;

		lock (NetChannels) {
			if (NetChannels.FirstOrDefault(x => x == netchan) == null) {
				Msg($"Net.CloseNetChannel: unknown net channel.\n");
				return;
			}

			// todo: should use ConcurrentDictionary instead to avoid this mess (probably?)
			NetChannel[] copychannels = new NetChannel[NetChannels.Count - 1];
			int i = 0;
			foreach (var item in NetChannels) {
				if (item == netchan)
					continue;

				copychannels[i] = item;
				i++;
			}

			NetChannels.Clear();

			foreach (var channel in copychannels) {
				NetChannels.Add(channel);
			}

			// todo; multithreading?
		}
	}

	public NetChannel? FindNetChannel(NetSocketType socket, NetAddress address) {
		lock (NetChannels) {
			foreach (var chan in NetChannels) {
				if (socket != chan.Socket)
					continue;

				if (address.CompareAddress(chan.RemoteAddress)) {
					return chan;
				}
			}

			return null;
		}
	}

	public bool TryFindNetChannel(NetSocketType socket, NetAddress address, [NotNullWhen(true)] out NetChannel? netchan) {
		netchan = FindNetChannel(socket, address);
		return netchan != null;
	}

	public unsafe void ProcessSocket(NetSocketType sock, IConnectionlessPacketHandler handler) {
		NetPacket? packet;
		lock (NetChannels) {
			int numChannels = NetChannels.Count;

			foreach (var netchan in NetChannels) {
				if (netchan.Socket != sock)
					continue;

				// process TCP stream later
			}
		}

		// datagrams from socket

		NetScratchBuffer scratch = ObtainScratchBuffer();
		while (true) {
			packet = GetPacket(sock, scratch.Data);
			if (packet == null) {
				break;
			}

			unsafe {
				int header = packet.Message.ReadLong();

				switch (header) {
					case CONNECTIONLESS_HEADER:
						// Hand off out-of-band packet
						handler.ProcessConnectionlessPacket(packet);
						continue;
				}

				packet.Message.Reset();

				NetChannel? channel = FindNetChannel(sock, packet.From);
				if (channel != null) {
					channel.ProcessPacket(packet, true);
				}
			}
		}
		NetScratchBuffers.Push(scratch);
	}

	public bool GetLoopPacket(NetPacket packet) {
		if (packet.Source > NetSocketType.Server)
			return false;

		if (!Loopbacks[(int)packet.Source].TryDequeue(out Loopback? loop))
			return false;

		if (loop.Length == 0) {
			ObjectPool<Loopback>.Shared.Free(loop);
			return LagPacket(false, packet);
		}

		packet.From.Type = NetAddressType.Loopback;
		packet.Size = loop.Length;
		packet.WireSize = loop.Length;
		memcpy(packet.Data, loop.Data.AsSpan()[..packet.Size]);
		loop.Length = 0;

		if (loop.Data != loop.DefBuffer) {
			ArrayPool<byte>.Shared.Return(loop.Data!, true);
			loop.Data = loop.DefBuffer;
		}

		ObjectPool<Loopback>.Shared.Free(loop);
		return LagPacket(true, packet);
	}

	private int net_error;
	private void ClearLastError() => net_error = 0;
	private int GetLastError() => net_error;
	private bool Errored => net_error > 0;

	public unsafe bool GetLong(NetSocketType sock, NetPacket packet) {
		int packetNumber, packetCount, sequenceNumber, offset;
		short packetID;

		SPLITPACKET* pHeader;

		if (packet.Size < sizeof(SPLITPACKET)) {
			Warning($"Invalid split packet length {packet.Size}\n");
			return false;
		}

		fixed (byte* ptr = packet.Data) {
			pHeader = (SPLITPACKET*)ptr;
			sequenceNumber = pHeader->SequenceNumber;
			packetID = pHeader->PacketID;
			packetNumber = packetID >> 8;
			packetCount = packetID & 0xff;

			int splitSizeMinusHeader = pHeader->SplitSize;
			if (splitSizeMinusHeader < MIN_SPLIT_SIZE || splitSizeMinusHeader > MAX_SPLIT_SIZE) {
				Warning($"Net.GetLong: Split packet from {packet.From} with invalid split size (number {packetNumber}/ count {packetCount}) where size {splitSizeMinusHeader} is out of valid range [{MIN_SPLIT_SIZE} - {MAX_SPLIT_SIZE}]\n");
				return false;
			}

			if (packetNumber >= MAX_SPLITPACKET_SPLITS || packetCount > MAX_SPLITPACKET_SPLITS) {
				Warning($"Net.GetLong: Split packet from {packet.From} with too many split parts (number {packetNumber}/ count {packetCount}), where {MAX_SPLITPACKET_SPLITS} is max count allowed\n");
				return false;
			}

			SplitPacketEntry? entry = FindOrCreateSplitPacketEntry(sock, packet.From);
			Debug.Assert(entry != null);
			if (entry == null)
				return false;

			entry.LastActiveTime = Time;
			Debug.Assert(packet.From.CompareAddress(entry.From));

			if (entry.NetSplit.CurrentSequence == -1 || sequenceNumber != entry.NetSplit.CurrentSequence) {
				entry.NetSplit.CurrentSequence = sequenceNumber;
				entry.NetSplit.SplitCount = packetCount;
				entry.NetSplit.ExpectedSplitSize = splitSizeMinusHeader;
			}

			if (entry.NetSplit.ExpectedSplitSize != splitSizeMinusHeader) {
				Warning($"Net.GetLong: Split packet from {packet.From} with inconsistent split size (number {packetNumber}/ count {packetCount}) where size {splitSizeMinusHeader} not equal to initial size of {entry.NetSplit.ExpectedSplitSize}\n");
				entry.LastActiveTime = Time + SPLIT_PACKET_STALE_TIME;
				return false;
			}

			int size = packet.Size - sizeof(SPLITPACKET);
			if (entry.SplitFlags[packetNumber] != sequenceNumber) {
				if (packetNumber == packetCount - 1)
					entry.NetSplit.TotalSize = (packetCount - 1) * splitSizeMinusHeader + size;

				entry.NetSplit.SplitCount--;
				entry.SplitFlags[packetNumber] = sequenceNumber;

				if (net_showsplits.GetInt() != 0 && net_showsplits.GetInt() != 3)
					Msg($"<-- [{DescribeSocket(sock)}] Split packet {packetNumber + 1}/{packetCount} seq {sequenceNumber} size {size} mtu {splitSizeMinusHeader + sizeof(SPLITPACKET)} from {packet.From}\n");
			}
			else {
				Warning($"Net.GetLong: Ignoring duplicated split packet {packetNumber + 1} of {packetCount} ({size} bytes) from {packet.From}\n");
			}

			// Copy incoming data
			offset = packetNumber * splitSizeMinusHeader;
			fixed (byte* src = entry.NetSplit.Buffer) {
				byte* entryNSBuf = src + offset;
				NativeMemory.Copy(ptr + sizeof(SPLITPACKET), entryNSBuf, (nuint)size);
			}

			if (entry.NetSplit.SplitCount <= 0) {
				entry.NetSplit.CurrentSequence = -1;
				if (entry.NetSplit.TotalSize > MAX_MESSAGE) {
					Warning($"Split packet too large! {entry.NetSplit.TotalSize} bytes from {packet.From}\n");
					return false;
				}


				entry.NetSplit.Buffer.AsSpan()[..entry.NetSplit.TotalSize].CopyTo(packet.Data);

				packet.Size = entry.NetSplit.TotalSize;
				packet.WireSize = entry.NetSplit.TotalSize;

				return true;
			}
		}

		return false;
	}
	public static uint LZSS_ID = 'S' << 24 | 'S' << 16 | 'Z' << 8 | 'L';
	public static uint SNAPPY_ID = 'P' << 24 | 'A' << 16 | 'N' << 8 | 'S';
	public static unsafe int GetUncompressedSize(byte* compressedData, uint compressedLen) {
		LZSSHeader* pHeader = (LZSSHeader*)compressedData;

		if (compressedLen >= sizeof(LZSSHeader) && pHeader->ID == LZSS_ID) {
			return (int)pHeader->ActualSize; // LZSS size
		}

		if (compressedLen > 4 && pHeader->ID == SNAPPY_ID) {
			Span<byte> d = new Span<byte>(compressedData, (int)(compressedLen - 4));
			int snappySize = Snappy.GetUncompressedLength(d);
			if (snappySize > 0)
				return snappySize;
		}

		return -1;
	}
	public const int LZSS_LOOKSHIFT = 4;
	public const int LZSS_LOOKAHEAD = 1 << LZSS_LOOKSHIFT;
	public unsafe uint GetActualSize(byte* pInput) {
		LZSSHeader* asU = (LZSSHeader*)pInput;

		if (asU != null && asU->ID == LZSS_ID)
			return asU->ActualSize;

		return 0;
	}
	public unsafe uint SafeUncompress(byte* pInput, byte* pOutput) {
		uint totalBytes = 0;
		int cmdByte = 0;
		int getCmdByte = 0;

		uint actualSize = GetActualSize(pInput);
		if (actualSize == 0) {
			// unrecognized
			return 0;
		}

		pInput += sizeof(lzss_header_t);

		for (; ; )
		{
			if (getCmdByte <= 0) {
				cmdByte = *pInput++;
			}
			getCmdByte = getCmdByte + 1 & 0x07;

			if ((cmdByte & 0x01) == 0x01) {
				int position = *pInput++ << LZSS_LOOKSHIFT;
				position |= *pInput >> LZSS_LOOKSHIFT;
				int count = (*pInput++ & 0x0F) + 1;
				if (count == 1) {
					break;
				}
				byte* pSource = pOutput - position - 1;
				for (int i = 0; i < count; i++) {
					*pOutput++ = *pSource++;
				}
				totalBytes += (uint)count;
			}
			else {
				*pOutput++ = *pInput++;
				totalBytes++;
			}
			cmdByte = cmdByte >> 1;
		}

		if (totalBytes != actualSize) {
			return 0;
		}

		return totalBytes;
	}
	public unsafe uint LZSS_GetActualSize(byte* pInput) {
		LZSSHeader* pHeader = (LZSSHeader*)pInput;
		if (pHeader != null && pHeader->ID == LZSS_ID)
			return pHeader->ActualSize;

		return 0;
	}

	public bool BufferToBufferDecompress(Span<byte> dst, ref uint dstLen, Span<byte> src, uint srcLen) {
		if (CLZSS.IsCompressed(src)) {
			uint uDecompressedLen = CLZSS.GetActualSize(src);
			if (uDecompressedLen > dstLen) {
				Warning($"NET_BufferToBufferDecompress with improperly sized dest buffer ({dstLen} in, {uDecompressedLen} needed)\n");
				return false;
			}
			else {
				dstLen = CLZSS.Uncompress(src, dst);
			}
		}
		else if (srcLen > sizeof(uint) && MemoryMarshal.Read<uint>(src) == SNAPPY_ID) {
			// gmod tags Snappy-compressed blobs (e.g. string table baselines) with a leading 4-byte 'SNAP' id.
			Span<byte> snappy = src.Slice(sizeof(uint), (int)(srcLen - sizeof(uint)));
			int uDecompressedLen = Snappy.GetUncompressedLength(snappy);
			if (uDecompressedLen < 0 || uDecompressedLen > dstLen) {
				Warning($"NET_BufferToBufferDecompress with improperly sized dest buffer ({dstLen} in, {uDecompressedLen} needed)\n");
				return false;
			}
			dstLen = (uint)Snappy.Decompress(snappy, dst);
		}
		else {
			src.CopyTo(dst);
			dstLen = srcLen;
		}

		return true;
	}

	private ArrayPool<byte> DecompressionPool = ArrayPool<byte>.Create();
	public unsafe uint GetDecompressedBufferSize(Span<byte> compressedbuf) {
		if (compressedbuf.IsEmpty)
			return 0;

		if (!CLZSS.IsCompressed(compressedbuf))
			return 0;

		return CLZSS.GetActualSize(compressedbuf);
	}

	public bool ReceiveDatagram(NetSocketType sock, NetPacket packet) {
		if (packet.Data == null)
			return false;

		NetSocket net_socket = NetSockets[(int)packet.Source];
		Socket socket = net_socket.UDP ?? throw new Exception("No UDP socket.");

		EndPoint recv = new IPEndPoint(IPAddress.Any, 0);
		int ret = socket.Available > 0 ? socket.ReceiveFrom(packet.Data, SocketFlags.None, ref recv) : -1;
		/*if (socket.Poll(10, SelectMode.SelectRead)) {
			ret = socket.ReceiveFrom(packet.Data, SocketFlags.None, ref recv);
		}*/

		if (ret >= MIN_MESSAGE) {
			packet.WireSize = ret;
			packet.From.SetFromEndPoint(recv);
			packet.Size = ret;

			if (ret < MAX_MESSAGE) {
				// Check for split messages

				// Check for split packet
				//Console.WriteLine($"Header: {netHeader}");
				if (MemoryMarshal.Cast<byte, int>(packet.Data.AsSpan())[0] == (int)NetHeaderFlag.SplitPacket) {
					if (!GetLong(sock, packet))
						return false;
				}

				// Check for compressed packet
				if (MemoryMarshal.Cast<byte, int>(packet.Data.AsSpan())[0] == (int)NetHeaderFlag.CompressedPacket) {
					Span<byte> packetData = packet.Data.AsSpan();
					Span<byte> lzssStart = packetData[sizeof(uint)..];
					uint uncompressedSize = GetDecompressedBufferSize(lzssStart);

					if (uncompressedSize <= 0 || uncompressedSize > MAX_PAYLOAD)
						return false;

					byte[] uncompressedArray = DecompressionPool.Rent((int)(uncompressedSize * 2 + 1024));

					BufferToBufferDecompress(uncompressedArray.AsSpan(), ref uncompressedSize, lzssStart, (uint)packet.WireSize);

					if (uncompressedSize == 0) {
						DecompressionPool.Return(uncompressedArray);
						Warning($"UDP: discarding {ret} bytes due to decompression error.\n");
						return false;
					}

					uncompressedArray[..(int)uncompressedSize].CopyTo(packetData);
					DecompressionPool.Return(uncompressedArray, true);
					packet.Size = (int)uncompressedSize;
				}

				return LagPacket(true, packet);
				// Check for compressed messages
			}
			else {
				Warning("Net.ReceiveDatagram: Oversize packet\n");
			}
		}
		else if (ret == -1) {
			net_error = 10035;
		}

		return false;
	}

	public bool ReceiveValidDatagram(NetSocketType sock, NetPacket packet) {
		for (int i = 0; i < 1000; i++) {
			ClearLastError();
			if (ReceiveDatagram(sock, packet))
				return true;

			if (Errored)
				break;
		}

		return false;
	}
	public bool LagPacket(bool newdata, NetPacket packet) {
		if ((int)packet.Source >= NetSocket.MAX)
			return newdata; // Fake lag not supported for extra sockets

		// ignore droppackets for now

		// ignore fakeloss for now...

		// ignore fakelag for now.......

		// man

		return newdata;
	}

	public unsafe NetPacket? GetPacket(NetSocketType sock, byte[] scratch) {
		DiscardStaleSplitPackets(sock);
		NetPacket packet = NetPackets[(int)sock];

		packet.From.Type = NetAddressType.IP;
		//packet.From.Clear();
		packet.Received = Time;
		packet.Source = sock;
		packet.Data = scratch;
		packet.Size = 0;
		packet.WireSize = 0;

		// Check loopback 
		if (!GetLoopPacket(packet)) {
			if (!IsMultiplayer())
				return null;

			// Then check UDP
			if (!ReceiveValidDatagram(sock, packet)) {
				// Check lag packet
				if (!LagPacket(false, packet))
					return null;
			}
		}

		// Prepare the packet message-buffer-reader for reading from the network packet
		packet.Message.StartReading(packet.Data, packet.Size);
		return packet;
	}

	public void SetMultiplayer(bool multiplayer) {
		if (net_noip && multiplayer) {
			Warning("Warning: Cannot use multiplayer networking with no Internet Protocol\n");
			return;
		}

		if (Dedicated && !multiplayer) {
			Warning("Warning: Cannot use singleplayer mode on a dedicated server\n");
			return;
		}

		if (Multiplayer != multiplayer) {
			Multiplayer = multiplayer;
			Config();
		}

		if (!multiplayer)
			ClearLoopbackBuffers();
	}

	public void Config() {
		CloseAllSockets();
		Time = 0;
		if (Multiplayer) {
			ConfigLoopbackBuffers(false);
			GetLocalAddress();
			OpenSockets();
		}
		else {
			ConfigLoopbackBuffers(true);
		}

		Msg("Net.Config: ready!\n");
	}

	public NetSocket GetSocket(NetSocketType type) {
		return NetSockets[(int)type];
	}

	public void CloseSocket(Socket socket, NetSocketType socketType = NetSocketType.NotApplicable) {
		socket.Close();

		if (socketType != NetSocketType.NotApplicable) {
			var netsock = NetSockets[(int)socketType];
			if (netsock.TCP == socket) {
				netsock.TCP = null;
				netsock.Listening = false;
			}
		}
	}

	public Socket? OpenSocket(ReadOnlySpan<char> netInterface, ref int port, ProtocolType protocol) {
		AddressFamily addressFamily = AddressFamily.InterNetwork;
		SocketType socketType = (protocol == ProtocolType.Tcp) ? SocketType.Stream : SocketType.Dgram;

		Socket? socket;
		try {
			socket = new Socket(addressFamily, socketType, protocol);
		}
		catch (SocketException ex) {
			Warning($"WARNING: OpenSocket: socket failed: {ex.Message}\n");
			return null;
		}

		try {
			socket.Blocking = false;

			if (protocol == ProtocolType.Tcp) {
				socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
				socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(false, 0));
				socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
				socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, Protocol.MAX_MESSAGE);
				socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, Protocol.MAX_MESSAGE);

				// TCP sockets do not bind here
				return socket;
			}

			// UDP only
			socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, Protocol.MAX_MESSAGE);
			socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, Protocol.MAX_MESSAGE);
			socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
			socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

			// This disables the exception when the UDP socket is forcibly closed
			const int SIO_UDP_CONNRESET = -1744830452;
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				socket.IOControl((IOControlCode)SIO_UDP_CONNRESET, [0, 0, 0, 0], null);

			// Determine IP address to bind to
			IPAddress? ipAddress = null;
			if (!netInterface.IsEmpty && netInterface != "localhost") {
				if (!IPAddress.TryParse(netInterface, out ipAddress)) {
					Warning($"WARNING: OpenSocket: failed to parse address: {netInterface}\n");
					socket.Close();
					return null;
				}
			}

			ipAddress ??= IPAddress.Any;

			IPEndPoint endPoint = new IPEndPoint(ipAddress, 0);
			const int PORT_TRY_MAX = 32;
			for (int portOffset = 0; portOffset < PORT_TRY_MAX; portOffset++) {
				int tryPort = port == PORT_ANY ? 0 : port + portOffset;
				endPoint.Port = tryPort;

				try {
					socket.Bind(endPoint);

					if (port != PORT_ANY && portOffset != 0) {
						port = tryPort;
						//Warning($"Socket bound to non-default port {port} because original port was already in use.\n");
					}

					return socket;
				}
				catch (SocketException bindEx) {
					if (port == PORT_ANY) {
						Warning($"WARNING: OpenSocket: bind failed: {bindEx.Message} (code {bindEx.SocketErrorCode})\n");
						socket.Close();
						return null;
					}

					// Try next port
				}
			}

			Warning("WARNING: OpenSocket: unable to bind socket after multiple attempts\n");
			socket.Close();
			return null;
		}
		catch (SocketException ex) {
			Warning($"Socket error: {ex.Message}\n");
			socket?.Close();
			return null;
		}
		catch (Exception ex) {
			Warning($"Unhandled exception: {ex}\n");
			socket?.Close();
			return null;
		}
	}

	public bool Multiplayer { get; private set; }
	public bool Dedicated { get; private set; }

	public void SetDedicated() {
		Dedicated = true;
	}

	public void ClearLoopbackBuffers() {
		// unimplemented
	}
	public void CloseAllSockets() {
		for (int i = 0; i < NetSockets.Count; i++) {
			if (NetSockets[i] != null && NetSockets[i].Port > 0) {
				if (NetSockets[i].UDP != null) CloseSocket(NetSockets[i].UDP!);
				if (NetSockets[i].TCP != null) CloseSocket(NetSockets[i].TCP!);

				NetSockets[i].Port = 0;
				NetSockets[i].Listening = false;
				NetSockets[i].UDP = null;
				NetSockets[i].TCP = null;
			}
		}

		Msg("Closed all sockets.\n");
	}
	public void ConfigLoopbackBuffers(bool alloc) {
		ClearLoopbackBuffers();
	}

	// todo
	public void GetLocalAddress() {
		LocalAdr.Clear();
		if (net_noip)
			Msg("TCP/UDP Disabled.\n");
		else {
			Span<char> buff = stackalloc char[512];
			if (strcmp(ipname.GetString(), "localhost") != 0)
				strcpy(buff, ipname.GetString());
			else {
				gethostname(buff);
				buff[^1] = '\0';
			}

			StringToAdr(buff, out LocalAdr.Endpoint);
			byte[] ip = LocalAdr.Endpoint!.Address.GetAddressBytes();
			int ipaddr = (ip[0] << 24) + (ip[1] << 16) + (ip[2] << 8) + ip[3];
			hostip.SetValue(ipaddr);
		}
	}

	private void gethostname(Span<char> buff) {
		Dns.GetHostEntry(Dns.GetHostName())!.AddressList.First().MapToIPv4().ToString().CopyTo(buff);
	}

	public const int PORT_ANY = -1;
	public const int PORT_RCON = 27015;
	public const int PORT_MASTER = 2701!;
	public const int PORT_CLIENT = 27005;
	public const int PORT_SERVER = 27015;
	public const int PORT_HLTV = 27020;

	static string NETSTRING(int i) => $"{i}";
	public readonly ConVar ipname = new("ip", "localhost", 0, "Overrides IP for multihomed hosts");
	public readonly ConVar hostport = new("hostport", NETSTRING(PORT_SERVER), 0, "Host game server port");
	public readonly ConVar hostip = new("hostip", "", 0, "Host game server ip");
	public readonly ConVar clientport = new("clientport", NETSTRING(PORT_CLIENT), 0, "Host game client port");
	public readonly ConVar hltvport = new("tv_port", NETSTRING(PORT_HLTV), 0, "Host SourceTV port");

	public void OpenSockets() {
		OpenSocketInternal(NetSocketType.Server, hostport.GetInt(), PORT_SERVER, "server", ProtocolType.Udp, false);
		OpenSocketInternal(NetSocketType.Client, clientport.GetInt(), PORT_SERVER, "client", ProtocolType.Udp, true);
	}

	bool OpenSocketInternal(NetSocketType module, int setPort, int defaultPort, string name, ProtocolType protocol, bool tryAny) {
		int port = setPort > 0 ? setPort : defaultPort;
		var netSock = NetSockets[(int)module];
		Socket? socket;

		if (netSock.Port <= 0) {
			socket = OpenSocket(ipname.GetString(), ref port, protocol);
			if (socket == null && tryAny) {
				port = PORT_ANY;
				socket = OpenSocket(ipname.GetString(), ref port, protocol);
			}

			if (socket == null) {
				throw new Exception();
			}

			netSock.Port = port;
			Msg($"Net.OpenSocketInternal: {name} successfully opened, local ep {socket.LocalEndPoint}\n");
		}
		else {
			Warning($"Warning: Net.OpenSockets: {name} port {netSock.Port} already open.\n");
			return false;
		}


		if (protocol == ProtocolType.Tcp) {
			netSock.TCP = socket;
		}
		else if (protocol == ProtocolType.Udp) {
			netSock.UDP = socket;
		}
		else {
			throw new Exception($"Unrecognized protocol '{protocol}'");
		}

		return netSock.Port != 0;
	}

	public void Init(bool isDedicated) {
		Time = 0;

		ICommandLine commandLine = Singleton<ICommandLine>();
		int port = commandLine.ParmValue("-port", -1);
		if (port == -1)
			port = commandLine.ParmValue("+port", -1);

		if (port != -1)
			hostport.SetValue(port);

		NetSockets.EnsureCount(NetSocket.MAX);
		NetPackets.EnsureCount(NetSocket.MAX);
		SplitPackets.EnsureCount(NetSocket.MAX);
		//SplitPackets.EnsureCount(NetSocket.MAX); // wip

		// todo: setup lagdata
		if (isDedicated)
			SetDedicated();
		else
			ConfigLoopbackBuffers(true);
	}

	private double lastRealTime = 0;
	public void SetTime(double realTime) {
		double frametime = realTime - lastRealTime;
		lastRealTime = realTime;

		if (frametime > 1.0) {
			frametime = 1.0;
		}
		else if (frametime < 0)
			frametime = 0;

		Time += frametime * 1.0; // host timescale... later
	}

	public bool NoTCP { get; set; }

	public void RunFrame(double realTime) {
		SetTime(realTime);

		if (!IsMultiplayer() || NoTCP)
			return;

		// process TCP sockets? todo
	}

	public void Shutdown() {
		Multiplayer = false;
		Dedicated = false;
		CloseAllSockets();
		ConfigLoopbackBuffers(false);
	}

	public unsafe int SendPacket(NetChannel chan, NetSocketType sock, NetAddress? to, byte[]? data, int length, bf_write? voicePayload = null, bool useCompression = false) {
		int ret = -1;

		if (data == null)
			return 0;

		if (to == null)
			goto end;

		Socket? netSocket;

		if (!IsMultiplayer() || to.Type == NetAddressType.Loopback) {
			Debug.Assert(voicePayload == null);
			SendLoopPacket(sock, length, data, to);
			return length;
		}

		if (to.Type == NetAddressType.Broadcast) {
			netSocket = NetSockets[(int)sock].UDP;
			if (netSocket == null)
				return length;
		}
		else if (to.Type == NetAddressType.IP) {
			netSocket = NetSockets[(int)sock].UDP;
			if (netSocket == null)
				return length;
		}
		else {
			Warning($"Net.SendPacket: bad address type {to.Type}\n");
			return length;
		}

		// Drop packets...
		// Fake loss...

		int gameDataLength = length;

		// Payloads....
		// Voice stuff...
		// Compression...
		// Write voice...


		int maxRoutable = MAX_ROUTABLE_PAYLOAD;
		if (chan != null) {
			maxRoutable = Math.Clamp(chan.MaxRoutablePayloadSize, MIN_USER_MAXROUTABLE_SIZE, MAX_USER_MAXROUTABLE_SIZE); // todo: sv_maxroutable?
		}

		var addr = to;
		//Console.WriteLine($"writing packet len {length}");

		//NetChannel.WritePacketToConsole((byte*)data, length);


		if (length <= maxRoutable && !(false && chan != null)) {
			// simple case, small packet, just send it
			ret = SendTo(true, netSocket, data, length, addr, gameDataLength);
		}
		else if (chan != null) {
			// split packet into smaller pieces
			ret = SendLong(chan, sock, netSocket, data, length, addr, maxRoutable);
		}
		else
			ret = -1;

	end:
		if (ret == -1) {
			Warning("Net.SendPacket went wrong!!!\n");
			ret = length;
		}

		return ret;
	}

	public unsafe int SendTo(bool verbose, Socket s, byte[] buf, int len, NetAddress to, int gameDataLength) {
		int send = SendToImpl(s, buf, len, to, gameDataLength);
		return send;
	}

	private unsafe int SendToImpl(Socket s, Span<byte> buf, int len, NetAddress to, int gameDataLength) {
		return s.SendTo(buf.Slice(0, len), 0, to);
	}

	private readonly int net_splitrate = 1;

	public unsafe int SendLong(NetChannel netchan, NetSocketType sock, Socket s, byte[] buf, int len, NetAddress to, int maxRoutable) {
		short splitSizeMinusHeader = (short)(maxRoutable - sizeof(SPLITPACKET));

		int sequenceNumber = -1;
		if (netchan != null) {
			sequenceNumber = netchan.IncrementSplitPacketSequence();
		}
		else {
			Debug.Assert(false, "FINISH THIS");
		}

		int packetNumber = 0;
		int totalBytesSent = 0;
		int fragmentsSent = 0;

		fixed (byte* sendbuf_u = buf) {
			sbyte* sendbuf = (sbyte*)sendbuf_u;
			int sendlen = len;

			sbyte* packetptr = stackalloc sbyte[MAX_ROUTABLE_PAYLOAD];
			sbyte* packet = packetptr;

			SPLITPACKET* pPacket = (SPLITPACKET*)packetptr;
			pPacket->NetID = NET_HEADER_FLAG_SPLITPACKET;
			pPacket->SequenceNumber = sequenceNumber;
			pPacket->SplitSize = splitSizeMinusHeader;

			int packetCount = (sendlen + splitSizeMinusHeader - 1) / splitSizeMinusHeader;

			int bytesLeft = sendlen;

			while (bytesLeft > 0) {
				int size = Math.Min(splitSizeMinusHeader, bytesLeft);
				pPacket->PacketID = (short)((packetNumber << 8) + packetCount);
				NativeMemory.Copy(sendbuf + packetNumber * splitSizeMinusHeader, packet + sizeof(SPLITPACKET), (nuint)size);
				int ret = 0;

				int len2 = size + sizeof(SPLITPACKET);
				ret = SendToImpl(s, new(packet, len2), len2, to, -1);

				++fragmentsSent;
				if (ret < 0)
					return ret;

				if (ret >= size)
					totalBytesSent += size;

				bytesLeft -= size;
				++packetNumber;

				if (net_showsplits.GetInt() != 0 && net_showsplits.GetInt() != 2)
					Msg($"--> [{DescribeSocket(sock)}] Split packet {packetNumber}/{packetCount} seq {sequenceNumber} size {size} mtu {maxRoutable} to {to} [ total {sendlen} ]\n");
			}
		}

		return totalBytesSent;
	}

	const int DEF_LOOPBACK_SIZE = 2048;

	class Loopback : IPoolableObject
	{
		public byte[]? Data;
		public readonly byte[] DefBuffer = new byte[DEF_LOOPBACK_SIZE];
		public int Length;

		public void Init() {
			Data = null;
			Length = 0;
		}

		public void Reset() {
			Data = null;
			Length = 0;
		}
	}


	public unsafe void SendLoopPacket(NetSocketType sock, int length, byte[] data, NetAddress? to) {
		if (length > MAX_PAYLOAD) {
			DevMsg("NET_SendLoopPacket:  packet too big (%i).\n", length);
			return;
		}

		Loopback loop = ObjectPool<Loopback>.Shared.Alloc();

		if (length <= DEF_LOOPBACK_SIZE)
			loop.Data = loop.DefBuffer;
		else
			loop.Data = ArrayPool<byte>.Shared.Rent(length);

		memcpy(loop.Data, data.AsSpan()[..length]);
		loop.Length = length;

		if (sock == NetSocketType.Server)
			Loopbacks[(int)NetSocketType.Client].Enqueue(loop);
		else if (sock == NetSocketType.Client)
			Loopbacks[(int)NetSocketType.Server].Enqueue(loop);
		else {
			DevMsg("NET_SendLoopPacket:  invalid socket (%i).\n", sock);
			return;
		}
	}

	public void SendQueuedPackets() {

	}

	float FakeLag = 0;

	public float GetFakeLag() {
		return FakeLag;
	}

	public int GetUDPPort(NetSocketType socket) {
		if (socket < 0 || (int)socket >= NetSockets.Count)
			return 0;

		return NetSockets[(int)socket].Port;
	}

	public void ListenSocket(NetSocketType sock, bool listen) {
		Assert((sock >= 0) && ((int)sock < NetSockets.Count));

		NetSocket netsock = NetSockets[(int)sock];

		if (netsock.TCP != null)
			CloseSocket(netsock.TCP, sock);

		if (!IsMultiplayer() || net_notcp)
			return;

		if (listen) {
			// todo. TCP isn't even used by Garry's Mod anymore.
		}
	}
}
