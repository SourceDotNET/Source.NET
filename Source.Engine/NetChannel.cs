using CommunityToolkit.HighPerformance;

using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Hashing;
using Source.Common.Networking;
using Source.Engine;

using System.Buffers;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;

using static Source.Common.Networking.Protocol;

namespace Source.Common.Networking;

public class NetChannel : INetChannelInfo, INetChannel
{
	public readonly Net Net;
	public NetChannel(Net Net) {
		this.Net = Net;

		SplitPacketSequence = 1;
		MaxRoutablePayloadSize = MAX_ROUTABLE_PAYLOAD;
		ProcessingMessages = false;
		ShouldDelete = false;
		ClearedDuringProcessing = false;
		streamContainsChallenge = false;
		Socket = NetSocketType.NotApplicable;
		RemoteAddress?.Clear();
		LastReceived = 0;
		ConnectTime = 0;
		ProtocolVersion = -1;

		StreamUnreliable.DebugName = "NetChan/UnreliableData";
		StreamReliable.DebugName = "NetChan/ReliableData";

		Rate = DEFAULT_RATE;
		Timeout = SIGNON_TIME_OUT;

		OutSequence = 1;
		InSequence = 0;
		OutSequenceAck = 0;
		OutReliableState = 0;
		InReliableState = 0;

		ChallengeNumber = 0;

		// Set up ReceiveList
		ReceiveList[(int)FragmentStream.Normal] = new();
		ReceiveList[(int)FragmentStream.File] = new();

		StreamSocket = null;
		StreamActive = false;

		ResetStreaming();

		MaxReliablePayloadSize = Protocol.MAX_PAYLOAD;
		FileRequestCounter = 0;
		FileBackgroundTransmission = true;
		UseCompression = false;
		QueuedPackets = 0;
		RemoteFrameTime = 0;
		RemoteFrameTimeStdDeviation = 0;

		FlowReset();
	}

	public const int MIN_RATE = 1000;
	public const int MAX_RATE = (1024 * 1024);
	public const int DEFAULT_RATE = 80_000;
	public const TimeUnit_t SIGNON_TIME_OUT = 300;
	/// <summary>
	/// Socket type
	/// </summary>
	public NetSocketType Socket { get; set; }
	public Socket? StreamSocket { get; set; }

	public string Name { get; set; } = "";
	public INetChannelHandler? MessageHandler;
	public int ProtocolVersion;

	public readonly bf_write StreamReliable = new();
	public byte[]? ReliableDataBuffer;

	public readonly bf_write StreamUnreliable = new();
	public byte[]? UnreliableDataBuffer;

	public readonly bf_write StreamVoice = new();
	public byte[]? VoiceDataBuffer;

	/// <summary>
	/// Address this netchannel is talking to
	/// </summary>
	public NetAddress? RemoteAddress { get; set; }


	public const int CONNECTION_PROBLEM_TIME = 4;

	public const int NET_FRAMES_BACKUP = 64;
	public const int NET_FRAMES_MASK = NET_FRAMES_BACKUP - 1;



	public void Clear() {
		int i;

		for (i = 0; i < (int)FragmentStream.Max; i++) {
			while (WaitingList[i].Count > 0)
				WaitingList[i].RemoveAt(WaitingList[i].Count - 1);
		}

		for (i = 0; i < SubChannel.MAX; i++) {
			if (SubChannels[i].State == SubChannelState.ToSend) {
				int bit = 1 << i;
				OutReliableState = FLIPBIT(OutReliableState, bit);

				SubChannels[i].Free();
			}
			else if (SubChannels[i].State == SubChannelState.Waiting) {
				SubChannels[i].State = SubChannelState.Dirty;
			}
		}

		if (ProcessingMessages)
			ClearedDuringProcessing = true;

		Reset();
	}

	public double LastReceived;
	public double ConnectTime;

	public int Rate { get; set; }
	double Timeout;

	public int OutSequence { get; private set; }
	public int InSequence { get; private set; }
	public int OutSequenceAck { get; private set; }
	public int OutReliableState { get; private set; }
	public int InReliableState { get; private set; }
	public int ChokedPackets { get; private set; }
	public double ClearTime { get; private set; }

	public uint ChallengeNumber { get; set; }

	public List<DataFragments>[] WaitingList = [new(), new()];
	public DataFragments[] ReceiveList = new DataFragments[(int)FragmentStream.Max];
	public SubChannel[] SubChannels = new SubChannel[SubChannel.MAX];

	public void Setup(NetSocketType socketType, NetAddress? address, string name, INetChannelHandler handler, int protocol) {
		Assert(name != null);
		Assert(handler != null);

		Socket = socketType;
		if (StreamSocket != null) {
			Net.CloseSocket(StreamSocket);
			StreamSocket = null;
		}

		if (address != null) {
			RemoteAddress = address.Copy(); // fixme?
		}
		else if (RemoteAddress != null) {
			RemoteAddress.Type = NetAddressType.Null;
		}

		LastReceived = Net.Time;
		ConnectTime = Net.Time;

		Name = name;
		MessageHandler = handler;
		ProtocolVersion = protocol;

		// Set up the unreliable buffer
		SetMaxBufferSize(
			reliable: false,
			bytes: MAX_DATAGRAM_PAYLOAD,
			voice: false);

		// Set up the voice buffer
		SetMaxBufferSize(
			reliable: false,
			bytes: MAX_DATAGRAM_PAYLOAD,
			voice: true);

		// Set up the reliable buffer
		SetMaxBufferSize(
			reliable: true,
			bytes: MAX_PAYLOAD,
			voice: false);

		Rate = DEFAULT_RATE;
		Timeout = SIGNON_TIME_OUT;

		OutSequenceAck = 1; // otherwise it looks like a connectionless header
		InSequence = 0;
		OutSequenceAck = 0;
		OutReliableState = 0;
		InReliableState = 0;

		ChallengeNumber = 0;

		StreamSocket = null;
		StreamActive = false;

		for (int i = 0; i < SubChannel.MAX; i++) {
			SubChannels[i] = new SubChannel();
			SubChannels[i].Index = i;
			SubChannels[i].Free();
		}

		ResetStreaming();

		MaxReliablePayloadSize = MAX_PAYLOAD;

		FileRequestCounter = 0;
		FileBackgroundTransmission = true;
		UseCompression = false;
		QueuedPackets = 0;

		RemoteFrameTime = 0;
		RemoteFrameTimeStdDeviation = 0;

		FlowReset();

		MessageHandler.ConnectionStart(this);
	}

	public int MaxReliablePayloadSize;
	public int MaxRoutablePayloadSize;
	public uint FileRequestCounter;
	public bool FileBackgroundTransmission;
	public bool UseCompression;
	public int QueuedPackets;
	TimeUnit_t InterpolationAmount;
	public TimeUnit_t RemoteFrameTime;
	public TimeUnit_t RemoteFrameTimeStdDeviation;

	// Packet history

	readonly NetFlow[] DataFlow = new NetFlow[NetFlow.MAX];
	readonly int[] MessageStats = new int[(int)NetChannelGroup.Total];

	// TCP stream state
	public bool StreamActive;
	public StreamCmd StreamType;
	public int StreamSeqNumber;
	public int StreamLength;
	public int StreamReceived;
	public string? StreamFile;
	readonly List<byte> StreamData = [];

	public void ResetStreaming() {
		StreamType = StreamCmd.None;
		StreamLength = 0;
		StreamReceived = 0;
		StreamSeqNumber = 0;
		StreamFile = null;
	}

	public bool StartStreaming(uint challenge) {
		// Reset streaming
		ResetStreaming();

		ChallengeNumber = challenge;

		// Going to pretend like listen servers don't exist because for now they don't!
		if (Net.IsMultiplayer()) {
			StreamSocket = null;
			return true;
		}

		//StreamSocket = Net.ConnectSocket(Socket, RemoteAddress);
		StreamData.EnsureCapacity(MAX_PAYLOAD);

		return StreamSocket != null;
	}

	public void FlowReset() {
		for (int i = 0; i < DataFlow.Length; i++)
			DataFlow[i] = new();

		for (int i = 0; i < MessageStats.Length; i++)
			MessageStats[i] = 0;
	}

	public void GetSequenceData(out int outSequence, out int inSequence, out int outSequenceAcknowledged) {
		outSequence = OutSequence;
		inSequence = InSequence;
		outSequenceAcknowledged = OutSequenceAck;
	}

	public void SetSequenceData(int outSequence, int inSequence, int outSequenceAcknowledged) {
		OutSequence = outSequence;
		InSequence = inSequence;
		OutSequenceAck = outSequenceAcknowledged;
	}

	public bool CanSendPacket() => ClearTime < Net.Time;

	public void SetChoked() {
		OutSequence++;
		ChokedPackets++;
	}

	public void Shutdown(ReadOnlySpan<char> reason) {
		if (Socket < 0)
			return;

		Clear();

		if (!reason.IsEmpty) {
			StreamUnreliable.WriteUBitLong(NET.Disconnect, NETMSG_TYPE_BITS);
			StreamUnreliable.WriteString(reason);
			Transmit();
		}

		if (StreamSocket != null) {
			Net.CloseSocket(StreamSocket);
			StreamSocket = null;
			StreamActive = false;
		}

		Socket = NetSocketType.NotApplicable;

		RemoteAddress?.Clear();

		if (MessageHandler != null) {
			MessageHandler.ConnectionClosing(reason);
			MessageHandler = null;
		}

		// GC can pick these up idrc
		NetMessages.Clear();

		if (ProcessingMessages) {
			Net.RemoveNetChannel(this, false);
			ShouldDelete = true;
		}
		else {
			Net.RemoveNetChannel(this, true);
		}
	}

	~NetChannel() {
		Shutdown("NetChannel removed.");
	}

	public void SetMaxBufferSize(bool reliable, int bytes, bool voice = false) {
		bytes = Math.Clamp(bytes, MAX_DATAGRAM_PAYLOAD, MAX_PAYLOAD);

		if (reliable)
			DoBufferThings(StreamReliable, ref ReliableDataBuffer, out ReliableDataBuffer, bytes);
		else if (voice)
			DoBufferThings(StreamVoice, ref VoiceDataBuffer, out VoiceDataBuffer, bytes);
		else
			DoBufferThings(StreamUnreliable, ref UnreliableDataBuffer, out UnreliableDataBuffer, bytes);
	}


	public int IncrementSplitPacketSequence() {
		return ++SplitPacketSequence;
	}

	private unsafe void DoBufferThings(bf_write stream, ref byte[]? bufferIn, out byte[] bufferOut, int bytes) {
		bufferOut = bufferIn!;

		if (bufferIn != null && bufferIn.Length == bytes)
			return;

		byte[] copybuf = new byte[MAX_DATAGRAM_PAYLOAD];
		int copybits = stream.BitsWritten;
		int copybytes = Bits2Bytes(copybits);

		if (copybytes >= bytes) {
			Warning($"NetChannel.SetMaxBufferSize: can't preserve existing data, because {copybytes} >= {bytes}.\n");
			return;
		}

		if (copybits > 0 && stream.BaseArray != null) {
			Array.Copy(stream.BaseArray, copybuf, copybytes);
		}

		var newBuffer = new byte[bytes];

		if (bufferIn != null && bufferIn.Length > 0) {
			fixed (byte* dstPtr = newBuffer)
			fixed (byte* srcPtr = copybuf) {
				for (int i = 0; i < copybytes; i++) {
					dstPtr[i] = srcPtr[i];
				}
			}
		}
		else {

		}

		bufferOut = newBuffer;

		stream.StartWriting(bufferOut, bytes, copybits);
	}

	public bool ShouldChecksumPackets() {
		return Net.IsMultiplayer();
	}

	public static int FLIPBIT(int v, int b) => (v & b) > 0 ? v & ~b : v |= b;

	private static unsafe uint CRC32_ProcessSingleBuffer(void* data, nint length) {
		return CRC32.ProcessSingleBuffer((byte*)data, (int)length);
	}

	public static unsafe ushort BufferToShortChecksum(void* data, nint length) {
		uint crc = CRC32_ProcessSingleBuffer(data, length);

		ushort lowpart = (ushort)(crc & 0xFFFF);
		ushort highpart = (ushort)(crc >> 16 & 0xFFFF);

		return (ushort)(lowpart ^ highpart);
	}

	private bool streamContainsChallenge = false;
	private int packetDrop = 0;


	public unsafe PacketFlag ProcessPacketHeader(NetPacket packet) {
		int sequence = packet.Message.ReadLong();
		int sequenceAck = packet.Message.ReadLong();

		PacketFlag flags = (PacketFlag)packet.Message.ReadByte();

		// Assert((flags & PacketFlag.Compressed) == 0);

		if (ShouldChecksumPackets()) {
			ushort checksum = (ushort)packet.Message.ReadUBitLong(16);
			Assert(packet.Message.BitsRead % 8 == 0);

			int offset = packet.Message.BitsRead >> 3;
			int checksumBytes = packet.Message.BytesAvailable - offset;

			fixed (byte* ptr = packet.Message.BaseArray) {
				ushort dataChecksum = BufferToShortChecksum(ptr + offset, checksumBytes);

				if (dataChecksum != checksum) {
					Warning($"{RemoteAddress}:corrupted packet {sequence} at {InSequence}\n");
					Assert(false);
					return PacketFlag.Invalid;
				}
			}
		}

		int relState = (int)packet.Message.ReadUBitLong(IN_RELIABLE_STATE_BITS);
		int choked = 0;
		int i, j;

		if ((flags & PacketFlag.Choked) != 0)
			choked = packet.Message.ReadByte();

		if ((flags & PacketFlag.Challenge) != 0) {
			uint challenge = (uint)packet.Message.ReadLong();
			if (challenge != ChallengeNumber)
				return PacketFlag.Invalid;
			streamContainsChallenge = true;
		}
		else if (streamContainsChallenge)
			return PacketFlag.Invalid;

		// Stale/duplicated packets
		if (sequence <= InSequence) {
			if (Net.net_showdrop.GetBool()) {
				if (sequence == InSequence)
					Warning($"{RemoteAddress}: duplicate packet {sequence} at {InSequence}\n");
				else
					Warning($"{RemoteAddress}: out-of-order packet {sequence} at {InSequence}\n");
			}

			return PacketFlag.Invalid;
		}

		packetDrop = sequence - (InSequence + choked + 1);
		if (packetDrop > 0)
			if (Net.net_showdrop.GetBool())
				Warning($"{RemoteAddress}: Dropped {packetDrop} packets at {sequence}\n");

		if (Net.net_maxpacketdrop.GetInt() > 0 && packetDrop > Net.net_maxpacketdrop.GetInt())
			if (Net.net_showdrop.GetBool())
				Warning($"{RemoteAddress}: Too many dropped packets ({packetDrop}) at {sequence}\n");
		// todo: net_maxpacketdrop

		for (i = 0; i < SubChannel.MAX; i++) {
			int bitmask = 1 << i;
			SubChannel subchan = SubChannels[i];
			Assert(subchan.Index == i);

			if ((OutReliableState & bitmask) == (relState & bitmask)) {
				if (subchan.State == SubChannelState.Dirty)
					subchan.Free();
				else if (subchan.SendSeqNumber > sequenceAck) {
					Warning($"{RemoteAddress}: reliable state invalid ({i}).\n");
					Assert(false);
				}
				else if (subchan.State == SubChannelState.Waiting) {
					for (j = 0; j < (int)FragmentStream.Max; j++) {
						if (subchan.NumFragments[j] == 0)
							continue;

						Assert(WaitingList[j].Count > 0);

						DataFragments data = WaitingList[j][0];

						data.AckedFragments += subchan.NumFragments[j];
						data.PendingFragments -= subchan.NumFragments[j];
					}

					subchan.Free();
				}
			}
			else {
				if (subchan.SendSeqNumber <= sequenceAck) {
					Assert(subchan.State != SubChannelState.Free);

					if (subchan.State == SubChannelState.Waiting) {
						if (Net.net_showfragments.GetBool())
							Msg($"Resending subchan {subchan.Index}: start {subchan.StartFragment[0]}, num {subchan.NumFragments[0]}");

						subchan.State = SubChannelState.ToSend;
					}
					else if (subchan.State == SubChannelState.Dirty) {
						int bit = 1 << subchan.Index;
						OutReliableState = FLIPBIT(OutReliableState, bit);
						subchan.Free();
					}
				}
			}
		}

		InSequence = sequence;
		OutSequenceAck = sequenceAck;

		for (i = 0; i < (int)FragmentStream.Max; i++)
			CheckWaitingList(i);

		FlowNewPacket(NetFlow.FLOW_INCOMING, InSequence, OutSequenceAck, choked, packetDrop, packet.WireSize + UDP_HEADER_SIZE);

		return flags;
	}

	public SubChannel? GetFreeSubchannel() {
		for (int i = 0; i < SubChannel.MAX; i++) {
			if (SubChannels[i].State == SubChannelState.Free)
				return SubChannels[i];
		}

		return null;
	}

	public static unsafe void WritePacketToConsole(byte* ptr, int length) {
		int x = 0;
		Console.WriteLine("Packet at " + new nint(ptr) + " (length " + length + ")");
		for (int i = 0; i < length; i++) {
			if (x == 0)
				Console.Write($"{i:X}".PadLeft(4, '0') + "    ");

			Console.Write($"{ptr[i]:X}".PadLeft(2, '0') + " ");

			x++;
			if (x == 8)
				Console.Write("  ");

			if (x >= 16) {
				Console.WriteLine();
				x = 0;
			}
		}
		if (x > 0)
			Console.WriteLine();
	}

	public unsafe void RemoveHeadInWaitingList(int list) {
		DataFragments data = WaitingList[list][0];
		data.Return();

		// File freeing later...

		WaitingList[list].Remove(data);
	}

	public unsafe void CheckWaitingList(int list) {
		if (WaitingList[list].Count == 0 || OutSequenceAck <= 0)
			return;

		DataFragments data = WaitingList[list][0];

		if (data.AckedFragments == data.NumFragments) {
			if (Net.net_showfragments.GetBool())
				Msg($"Sending complete: {data.NumFragments} fragments, {data.Bytes} bytes\n");
			RemoveHeadInWaitingList(list);
			return;
		}
	}

	public void FlowNewPacket(int flowIdx, int seq, int ack, int choked, int dropped, int size) {
		NetFlow flow = DataFlow[flowIdx];
		NetFrame? frame = null;

		if (seq > flow.CurrentIndex) {
			for (int i = flow.CurrentIndex + 1, numPacketFramesOverflow = 0;
				(i <= seq) && (numPacketFramesOverflow < NET_FRAMES_BACKUP);
				++i, ++numPacketFramesOverflow) {
				int backTrack = seq - i;

				frame = flow.Frames[i & NET_FRAMES_MASK];

				frame.Time = Net.Time;
				frame.IsValid = false;
				frame.Size = 0;
				frame.Latency = -1.0f;
				frame.AverageLatency = GetAverageLatency(NetFlow.FLOW_OUTGOING);
				frame.ChokedPackets = 0;
				frame.DroppedPackets = 0;
				frame.InterpolationAmount = 0;
				frame.MessageGroups.ZeroOut();

				if (backTrack < (choked + dropped)) {
					if (backTrack < choked)
						frame.ChokedPackets = 1;
					else
						frame.DroppedPackets = 1;
				}
			}

			if (frame != null) {
				frame.DroppedPackets = dropped;
				frame.ChokedPackets = choked;
				frame.Size = size;
				frame.IsValid = true;
				frame.AverageLatency = GetAverageLatency(NetFlow.FLOW_OUTGOING);
				frame.InterpolationAmount = InterpolationAmount;
			}
		}

		flow.TotalPackets++;
		flow.CurrentIndex = seq;
		flow.CurrentFrame = frame;

		int aflow = (flowIdx == NetFlow.FLOW_OUTGOING) ? NetFlow.FLOW_INCOMING : NetFlow.FLOW_OUTGOING;

		if (ack <= (DataFlow[aflow].CurrentIndex - NET_FRAMES_BACKUP))
			return;

		NetFrame aframe = DataFlow[aflow].Frames[ack & NET_FRAMES_MASK];

		if (aframe.IsValid && aframe.Latency == -1) {
			aframe.Latency = Net.Time - aframe.Time;

			if (aframe.Latency < 0)
				aframe.Latency = 0;
		}
	}

	public void ProcessPacket(NetPacket packet, bool hasHeader) {
		Assert(packet != null);
		Assert(MessageHandler != null);

		bf_read msg = packet.Message;

		if (RemoteAddress != null && !packet.From.CompareAddress(RemoteAddress))
			return;

		FlowUpdate(NetFlow.FLOW_INCOMING, packet.WireSize + UDP_HEADER_SIZE);

		PacketFlag flags = PacketFlag.None;
		if (hasHeader)
			flags = ProcessPacketHeader(packet);

		if (flags == PacketFlag.Invalid) {
			Assert(false);
			return;
		}

		Assert((flags & PacketFlag.Compressed) == 0);

		LastReceived = Net.Time;

		MessageHandler.PacketStart(InSequence, OutSequenceAck);

		if ((flags & PacketFlag.Reliable) != 0) {
			FragmentStream i = 0;
			int bit = 1 << (int)msg.ReadUBitLong(SubChannel.MAX_BITS);

			for (i = 0; i < FragmentStream.Max; i++) {
				if (msg.ReadOneBit() != 0) {
					if (!ReadSubChannelData(msg, i))
						return; // Error reading fragments; drop whole packet
				}
			}

			InReliableState = FLIPBIT(InReliableState, bit);

			for (i = 0; i < FragmentStream.Max; i++) {
				if (!CheckReceivingList(i))
					return;
			}
		}

		if (msg.BitsLeft > 0) {
			if (!ProcessMessages(msg))
				return;
		}

		MessageHandler.PacketEnd();
	}

	public unsafe bool CheckReceivingList(FragmentStream list) {
		DataFragments data = ReceiveList[(int)list];

		if (data.Buffer == null)
			return true;

		if (data.AckedFragments < data.NumFragments)
			return true;

		if (data.AckedFragments > data.NumFragments) {
			Warning($"receiving failed: too many fragments {data.AckedFragments}/{data.NumFragments} from {RemoteAddress}\n");
			return false;
		}

		// Got all fragments
		if (Net.net_showfragments.GetBool())
			Msg($"Receiving complete: {data.NumFragments} fragments, {data.Bytes} bytes\n");

		if (data.Compressed)
			UncompressFragments(data);

		if (data.Filename == null) {
			bf_read buffer = new bf_read(data.Buffer, data.Bytes);
			if (!ProcessMessages(buffer))
				return false;
		}
		else if (MessageHandler != null) {
			HandleUpload(data, MessageHandler);
		}

		if (data.Buffer != null) {
			data.Return();
		}

		return true;
	}

	public void HandleUpload(DataFragments data, INetChannelHandler handler) {
		string? errorStr = null;

		ConVar? allowUpload = cvar.FindVar("sv_allowupload");
		if (allowUpload != null && !allowUpload.GetBool()) {
			errorStr = "ignored. File uploads are disabled!";
		}
		else if (data.Filename == null || !IsValidFileForTransfer(data.Filename)) {
			errorStr = "has invalid path or extension!";
		}
		else {
			const string pathID = "download";

			if (g_pFileSystem.FileExists(data.Filename, pathID)) {
				errorStr = "already exists!";
			}
			else {
				ReadOnlySpan<char> filename = data.Filename;
				int lastSlash = filename.LastIndexOfAny('/', '\\');
				if (lastSlash > 0)
					g_pFileSystem.CreateDirHierarchy(filename[..lastSlash], pathID);

				data.File = g_pFileSystem.Open(data.Filename, Filesystem.FileOpenOptions.Write | Filesystem.FileOpenOptions.Binary, pathID);

				if (data.File == null) {
					errorStr = "failed to write!";
				}
				else {
					if (data.Buffer != null)
						data.File.Stream.Write(data.Buffer, 0, (int)data.Bytes);
					data.File.Dispose();
					data.File = null;

					if (Net.net_showfragments.GetInt() == 2)
						DevMsg($"FileReceived: {data.Filename}, {data.Bytes} bytes (ID {data.TransferID})\n");

					handler.FileReceived(data.Filename, data.TransferID);
				}
			}
		}

		if (errorStr != null)
			Msg($"Download file '{data.Filename}' {errorStr}\n");
	}

	public void UncompressFragments(DataFragments data) {
		if (!data.Compressed || data.Buffer == null)
			return;

		uint uncompressedSize = data.UncompressedSize;

		if (uncompressedSize == 0)
			return;

		if (data.Bytes > 100_000_000)
			return;

		byte[] newBuffer = ArrayPool<byte>.Shared.Rent((int)(uncompressedSize * 3u));

		Net.BufferToBufferDecompress(newBuffer.AsSpan(), ref uncompressedSize, data.Buffer.AsSpan(), data.Bytes);

		data.Return();
		data.Buffer = newBuffer;
		data.Bytes = uncompressedSize;
		data.Compressed = false;
	}

	public bool ProcessControlMessage(uint cmd, bf_read buf) {
		string? str = null;
		if (cmd == NET.NOP)
			return true;

		if (cmd == NET.Disconnect) {
			buf.ReadString(out str, 1024);
			MessageHandler?.ConnectionClosing(str ?? "Forced disconnect");
			return false;
		}

		if (cmd == NET.File) {
			uint transferID = buf.ReadUBitLong(32);
			if (buf.ReadOneBit() == 0) {
				MessageHandler?.FileDenied(transferID);
			}
			else {
				RequestFile type = (RequestFile)buf.ReadOneBit();
				uint value = buf.ReadUBitLong(32);
				MessageHandler?.FileRequested(type, value, transferID);
			}

			return true;
		}

		Warning($"NetChannel: received bad control cmd {cmd} from {RemoteAddress}.\n");
		return false;
	}

	public bool ProcessMessages(bf_read buf) {
		int startbit = buf.BitsRead;
		while (true) {
			if (buf.Overflowed) {
				MessageHandler?.ConnectionCrashed("Buffer overflow in net message");
				return false;
			}

			if (buf.BitsLeft < NETMSG_TYPE_BITS)
				break;

			uint cmd = buf.ReadUBitLong(NETMSG_TYPE_BITS);

			if (cmd <= NET.File) {
				if (!ProcessControlMessage(cmd, buf))
					return false;

				continue;
			}

			// Find net message handler
			INetMessage? netMsg = FindMessage((int)cmd);
			if (netMsg != null) {
				string msgName = netMsg.GetName();

				int msgStartBit = buf.BitsRead;
				if (!netMsg.ReadFromBuffer(buf)) {
					Warning($"NetChannel: failed reading message {msgName} from {RemoteAddress}\n");
					return false;
				}

				UpdateMessageStats(netMsg.GetGroup(), buf.BitsRead - msgStartBit);

				string showmsgname = Net.net_showmsg.GetString();
				if (showmsgname != "0") {
					bool invert = showmsgname.StartsWith('!');
					if (invert) showmsgname = showmsgname[1..];
					if (showmsgname == "1" || (showmsgname.Equals(netMsg.GetName(), StringComparison.OrdinalIgnoreCase) ^ invert))
#if DEBUG
						_ColorSpewMessage(SpewType.Message, new Color(255, 128, 0), $"Msg from {RemoteAddress}: {netMsg.ToString()?.Trim('\n')}\n");
#else
						Msg($"Msg from {RemoteAddress}: {netMsg.GetName()}\n");
#endif
				}

				string blockmsgname = Net.net_blockmsg.GetString();
				if (blockmsgname != "0" && (blockmsgname == "1" || blockmsgname.Equals(netMsg.GetName(), StringComparison.OrdinalIgnoreCase))) {
					Msg($"Blocking message {netMsg.ToString()?.Trim('\n')}\n");
					continue;
				}

				// todo: block

				ProcessingMessages = true;
				bool ret = netMsg.Process();
				ProcessingMessages = false;

				if (ShouldDelete) {
					return false;
				}

				if (ClearedDuringProcessing) {
					ClearedDuringProcessing = false;
					return false;
				}

				if (!ret) {
					Warning($"NetChannel: no handler processed message '{msgName}'\n");
					return false;
				}

				if (IsOverflowed())
					return false;
			}
			else {
				Warning($"NetChannel: unknown net message ({cmd}) from {RemoteAddress}.\n");
				return false;
			}
		}

		return true;
	}

	public bool ShouldDelete { get; private set; }
	public bool ProcessingMessages { get; private set; }
	public bool ClearedDuringProcessing { get; private set; }

	readonly int[] MsgStats = new int[(int)NetChannelGroup.Total];


	public List<INetMessage?> NetMessages = [];

	public INetMessage? FindMessage(int type) {
		if (type < 0) return null;
		if (type >= NetMessages.Count) return null;
		return NetMessages[type];
	}

	public bool RegisterMessage<T>() where T : INetMessage, new() {
		INetMessage msg = new T();
		int type = msg.GetMessageType();
		if (FindMessage(type) != null)
			return false;

		NetMessages.EnsureCountDefault(type + 1);
		NetMessages[type] = msg;
		msg.SetNetChannel(this);

		return true;
	}

	public bool SendNetMsg(INetMessage msg, bool forceReliable = false, bool voice = false) {
		if (RemoteAddress == null || RemoteAddress.Type == NetAddressType.Null)
			return true;

		bf_write stream = StreamUnreliable;
		if (msg.IsReliable() || forceReliable)
			stream = StreamReliable;

		if (voice)
			stream = StreamVoice;

		//if (msg.IsReliable)
		//Msg("writing " + msg + "\n");
		return msg.WriteToBuffer(stream);
	}

	public bool SendData(bf_write msg, bool reliable) {
		// No remote address on the NetChannel
		if (RemoteAddress == null || RemoteAddress.Type == NetAddressType.Null)
			return true;

		// Empty (or somehow, negative-length) packet
		if (msg.BitsWritten <= 0)
			return true;

		// The write-from overflowed, unreliable message, drop packet
		if (msg.Overflowed && !reliable)
			return true;

		bf_write buf = reliable ? StreamReliable : StreamUnreliable;

		// Writing the write-from (msg) to the write-to (buf) would result in the write-to overflowing
		if (msg.BitsWritten > buf.BitsLeft) {
			if (reliable)
				Warning($"Error: SendData reliable data too big ({msg.BytesWritten} bytes)\n");

			return false;
		}

		// Copy write-from -> write-to buffers
		unsafe {
			return buf.WriteBits(msg.BaseArray, msg.BitsWritten);
		}
	}

	public static int ENCODE_PAD_BITS(int x) => x << 5 & 0xff;
	public static byte ENCODE_PAD_BITS(byte x) => (byte)(x << 5 & 0xff);
	public static int DECODE_PAD_BITS(int x) => x >> 5 & 0xff;
	public static byte DECODE_PAD_BITS(byte x) => (byte)(x >> 5 & 0xff);

	public SubChannel? GetFreeSubChannel() {
		for (int i = 0; i < SubChannel.MAX; i++) {
			if (SubChannels[i].State == SubChannelState.Free)
				return SubChannels[i];
		}

		return null;
	}

	public unsafe void UpdateSubchannels() {
		// first check if there is a free subchannel
		SubChannel? freeSubChan = GetFreeSubChannel();

		if (freeSubChan == null)
			return; //all subchannels in use right now

		int i, nSendMaxFragments = MaxReliablePayloadSize / FRAGMENT_SIZE;

		bool bSendData = false;

		for (i = 0; i < (int)FragmentStream.Max; i++) {
			if (WaitingList[i].Count <= 0)
				continue;

			DataFragments data = WaitingList[i][0]; // get head

			if (data.AsTCP)
				continue;

			int nSentFragments = data.AckedFragments + data.PendingFragments;

			Assert(nSentFragments <= data.NumFragments);

			if (nSentFragments == data.NumFragments)
				continue; // all fragments already send

			// how many fragments can we send ?

			int numFragments = Math.Min(nSendMaxFragments, data.NumFragments - nSentFragments);

			// if we are in file background transmission mode, just send one fragment per packet
			//if (i == FRAG_FILE_STREAM && FileBackgroundTranmission)
			//numFragments = min(1, numFragments);

			// copy fragment data into subchannel

			freeSubChan.StartFragment[i] = nSentFragments;
			freeSubChan.NumFragments[i] = numFragments;

			data.PendingFragments += numFragments;

			bSendData = true;

			nSendMaxFragments -= numFragments;

			if (nSendMaxFragments <= 0)
				break;
		}

		if (bSendData) {
			// flip channel bit 
			int bit = 1 << freeSubChan.Index;

			OutReliableState = FLIPBIT(OutReliableState, bit);

			freeSubChan.State = SubChannelState.ToSend;
			freeSubChan.SendSeqNumber = 0;
		}
	}

	public const int IN_RELIABLE_STATE_BITS = 16;

	void CompressFragments() {
		if (!UseCompression)
			return;

		// write fragemnts for both streams
		Span<char> compressedfilenameBuffer = stackalloc char[MAX_OSPATH];

		for (int i = 0; i < (int)FragmentStream.Max; i++) {
			if (WaitingList[i].Count == 0)
				continue;

			// get the first fragments block which is send next
			DataFragments data = WaitingList[i][0];

			// if data is already compressed or too small, skip it
			if (data.Compressed || (int)data.Bytes < Net.net_compresspackets_minsize.GetInt())
				continue;

			// if we already started sending this block, we can't compress it anymore
			if (data.AckedFragments > 0 || data.PendingFragments > 0)
				continue;

			//ok, compress it.

			if (data.Buffer != null) {
				var now = DateTime.UtcNow;

				// fragments data is in memory
				uint compressedSize = Source.Engine.Common.GetIdealDestinationCompressionBufferSize_Snappy(data.Bytes);
				byte[] compressedDataArray = ArrayPool<byte>.Shared.Rent((int)compressedSize);
				Span<byte> compressedData = compressedDataArray.AsSpan();

				if (Source.Engine.Common.BufferToBufferCompress_Snappy(ref compressedData, data.Buffer.AsSpan()[..(int)data.Bytes]) && (compressedSize < data.Bytes)) {
					TimeSpan timer = DateTime.UtcNow - now;
					DevMsg($"Compressing fragments ({data.Bytes} -> {compressedData.Length} bytes): {Math.Round(timer.TotalSeconds, 2)}ms\n");

					// copy compressed data but dont reallocate memory
					memcpy(data.Buffer.AsSpan()[..(int)compressedSize], compressedData[..(int)compressedSize]);

					data.UncompressedSize = data.Bytes;
					data.Bytes = (uint)compressedData.Length;
					data.NumFragments = (int)BYTES2FRAGMENTS(data.Bytes);
					data.Compressed = true;
				}

				ArrayPool<byte>.Shared.Return(compressedDataArray, true);
			}
			else // it's a file
			{
				Assert(data.File != null);

				long compressedFileSize = -1;
				IFileHandle? hZipFile = null;

				// check to see if there is a compressed version of the file
				ReadOnlySpan<char> compressedfilename = sprintf(compressedfilenameBuffer, "%s.ztmp").S(data.Filename).ToSpan();

				// check the timestamps 
				DateTime compressedFileTime = g_pFileSystem.GetFileTime(compressedfilename);
				DateTime fileTime = g_pFileSystem.GetFileTime(data.Filename);

				if (compressedFileTime >= fileTime) {
					// compressed file is newer than uncompressed file, use this one
					hZipFile = g_pFileSystem.Open(compressedfilename, FileOpenOptions.Read | FileOpenOptions.Binary, null);
				}

				if (hZipFile != null) {
					// use the existing compressed file
					compressedFileSize = hZipFile.Stream.Length;
				}
				else {
					// create compressed version of source file
					uint uncompressedSize = data.Bytes;
					uint compressedSize = Source.Engine.Common.GetIdealDestinationCompressionBufferSize_Snappy(uncompressedSize);

					byte[] uncompressedArray = ArrayPool<byte>.Shared.Rent((int)uncompressedSize);
					byte[] compressedArray = ArrayPool<byte>.Shared.Rent((int)compressedSize);
					Span<byte> uncompressed = uncompressedArray.AsSpan();
					Span<byte> compressed = compressedArray.AsSpan();

					// read in source file
					data.File!.Stream.ReadExactly(uncompressed);

					// compress into buffer
					if (Source.Engine.Common.BufferToBufferCompress_Snappy(ref compressed, uncompressed)) {
						// write out to disk compressed version
						hZipFile = g_pFileSystem.Open(compressedfilename, FileOpenOptions.Write | FileOpenOptions.Binary, null);

						if (hZipFile != null) {
							DevMsg("Creating compressed version of file %s (%d -> %d)\n", data.Filename, data.Bytes, compressedSize);
							hZipFile.Stream.Write(compressed);
							hZipFile.Stream.Dispose();

							// and open zip file it again for reading
							hZipFile = g_pFileSystem.Open(compressedfilename, FileOpenOptions.Read | FileOpenOptions.Binary, null);

							if (hZipFile != null) {
								// ok, now everything if fine
								compressedFileSize = compressedSize;
							}
						}
					}

					ArrayPool<byte>.Shared.Return(uncompressedArray, true);
					ArrayPool<byte>.Shared.Return(compressedArray, true);
				}

				if (compressedFileSize > 0) {
					// use compressed file handle instead of original file
					data.File.Stream.Dispose();
					data.File = hZipFile;
					data.UncompressedSize = data.Bytes;
					data.Bytes = (uint)compressedFileSize;
					data.NumFragments = (int)BYTES2FRAGMENTS(data.Bytes);
					data.Compressed = true;
				}
			}
		}
	}

	public unsafe bool SendSubChannelData(bf_write buf) {
		SubChannel? subChan = null;
		int i;
		CompressFragments();
		// send tcp data
		UpdateSubchannels();

		for (i = 0; i < SubChannel.MAX; i++) {
			subChan = SubChannels[i];

			if (subChan.State == SubChannelState.ToSend)
				break;
		}

		if (i == SubChannel.MAX || subChan == null)
			return false; // no data to send in any subchannel

		buf.WriteUBitLong((uint)i, SubChannel.MAX_BITS);

		for (i = 0; i < (int)FragmentStream.Max; i++) {
			if (subChan.NumFragments[i] == 0) {
				buf.WriteOneBit(0); // no data for this stream
				continue;
			}

			DataFragments data = WaitingList[i][0];

			buf.WriteOneBit(1); // data follows:

			uint offset = (uint)(subChan.StartFragment[i] * FRAGMENT_SIZE);
			uint length = (uint)(subChan.NumFragments[i] * FRAGMENT_SIZE);

			if (subChan.StartFragment[i] + subChan.NumFragments[i] == data.NumFragments) {
				// we are sending the last fragment, adjust length
				int rest = (int)(FRAGMENT_SIZE - data.Bytes % FRAGMENT_SIZE);
				if (rest < FRAGMENT_SIZE)
					length -= (uint)rest;
			}

			// if all fragments can be send within a single packet, avoid overhead (if not a file)
			bool bSingleBlock = subChan.NumFragments[i] == data.NumFragments &&
								 data.Filename == null;

			if (bSingleBlock) {
				Assert(length == data.Bytes);
				Assert(length < MAX_PAYLOAD);
				Assert(offset == 0);

				buf.WriteOneBit(0); // single block bit

				// data compressed ?
				if (data.Compressed) {
					buf.WriteOneBit(1);
					buf.WriteUBitLong(data.UncompressedSize, MAX_FILE_SIZE_BITS);
				}
				else {
					buf.WriteOneBit(0);
				}

				buf.WriteVarInt32(data.Bytes);
			}
			else {
				buf.WriteOneBit(1); // uses fragments with start fragment offset byte
				buf.WriteUBitLong((uint)subChan.StartFragment[i], MAX_FILE_SIZE_BITS - FRAGMENT_BITS);
				buf.WriteUBitLong((uint)subChan.NumFragments[i], MAX_FRAGMENTS_PER_PACKET_BITS);

				if (offset == 0) {
					// this is the first fragment, write header info

					if (data.Filename != null) {
						buf.WriteOneBit(1); // file transmission net message stream
						buf.WriteUBitLong(data.TransferID, 32);
						buf.WriteString(data.Filename);
					}
					else {
						buf.WriteOneBit(0); // normal net message stream
					}

					// data compressed ?
					if (data.Compressed) {
						buf.WriteOneBit(1);
						buf.WriteUBitLong(data.UncompressedSize, MAX_FILE_SIZE_BITS);
					}
					else {
						buf.WriteOneBit(0);
					}

					buf.WriteUBitLong(data.Bytes, MAX_FILE_SIZE_BITS); // 4MB max for files
				}
			}

			// write fragments to buffer
			if (data.Buffer != null) {
				Assert(data.Filename == null);
				// send from memory block
				fixed (byte* ptr = data.Buffer)
					buf.WriteBytes(ptr + offset, (int)length);
			}
			else // if ( data->file != FILESYSTEM_INVALID_HANDLE )
			{
				// send from file
				Assert(data.File != null);
				Span<byte> tmpbuf = stackalloc byte[(int)length];
				data.File.Stream.Seek(offset, SeekOrigin.Begin);
				data.File.Stream.ReadExactly(tmpbuf[..(int)length]);
				buf.WriteBytes(tmpbuf);
			}

			if (Net.net_showfragments.GetBool())
				ConMsg($"Sending subchan {subChan.Index}: start {subChan.StartFragment[i]}, num {subChan.NumFragments[i]}");

			subChan.SendSeqNumber = OutSequence;
			subChan.State = SubChannelState.Waiting;
		}

		return true;
	}

	static ConVar net_minroutable = new("16", 0, "Forces larger payloads.");
	static ConVar net_maxroutable = new("1260", FCvar.Archive | FCvar.UserInfo, "Requested max packet size before packets are 'split'.", MIN_USER_MAXROUTABLE_SIZE, MAX_USER_MAXROUTABLE_SIZE);
	private unsafe byte[] sendbuf = new byte[MAX_MESSAGE];
	private bf_write send = new();
	public unsafe int SendDatagram(bf_write? datagram) {
		if (Socket == NetSocketType.Client) {
			if (net_maxroutable.GetInt() != GetMaxRoutablePayloadSize()) {
				SetMaxRoutablePayloadSize(net_maxroutable.GetInt());
			}
		}

		if (RemoteAddress == null || RemoteAddress.Type == NetAddressType.Null) {
			// demo channels, ignoring
			OutSequence++;
			return OutSequence - 1;
		}

		if (StreamReliable.Overflowed) {
			Warning($"{RemoteAddress}: send reliable stream overflow\n");
			return 0;
		}
		else if (StreamReliable.BitsWritten > 0) {
			CreateFragmentsFromBuffer(StreamReliable, FragmentStream.Normal);
			StreamReliable.Reset();
		}

		send.StartWriting(sendbuf, MAX_MESSAGE, 0);

		byte flags = (byte)PacketFlag.None;

		send.WriteLong(OutSequence);
		send.WriteLong(InSequence);

		bf_write flagsPos = send.Copy();

		send.WriteByte(0);

		if (ShouldChecksumPackets()) {
			send.WriteShort(0);
			Assert(send.BitsWritten % 8 == 0);
		}

		int checksumStart = send.BytesWritten;
		send.WriteUBitLong((uint)InReliableState, IN_RELIABLE_STATE_BITS);
		if (ChokedPackets > 0) {
			flags |= (byte)PacketFlag.Choked;
			send.WriteByte(ChokedPackets & 0xFF);
		}

		flags |= (byte)PacketFlag.Challenge;
		send.WriteLong((int)ChallengeNumber);

		if (SendSubChannelData(send))
			flags |= (byte)PacketFlag.Reliable;

		if (datagram != null) {
			if (datagram.BitsWritten < send.BitsLeft)
				send.WriteBits(datagram.BaseArray, datagram.BitsWritten);
			else
				Warning("NetChannel.SendDatagram: writing datagram would overflow buffer, ignoring\n");
		}

		if (StreamUnreliable.BitsWritten < send.BitsLeft)
			send.WriteBits(StreamUnreliable.BaseArray, StreamUnreliable.BitsWritten);
		else
			Warning("NetChannel.SendDatagram: writing unreliable would overflow buffer, ignoring\n");

		StreamUnreliable.Reset();

		if (StreamVoice.BitsWritten > 0 && StreamVoice.BitsWritten < send.BitsLeft) {
			send.WriteBits(StreamVoice.BaseArray, StreamVoice.BitsWritten);
			StreamVoice.Reset();
		}

		int minRoutable = MIN_ROUTABLE_PAYLOAD;

		if (Socket == NetSocketType.Server)
			minRoutable = net_minroutable.GetInt();

		while (send.BytesWritten < minRoutable)
			send.WriteUBitLong(NET.NOP, NETMSG_TYPE_BITS);

		int remainingBits = send.BitsWritten % 8;
		if (remainingBits > 0 && remainingBits <= 8 - NETMSG_TYPE_BITS)
			send.WriteUBitLong(NET.NOP, NETMSG_TYPE_BITS);

		{
			remainingBits = send.BitsWritten % 8;
			if (remainingBits > 0) {
				int padBits = 8 - remainingBits;
				flags |= ENCODE_PAD_BITS((byte)padBits);

				if (padBits > 0) {
					uint unOnes = BitHelpers.GetBitForBitnum(padBits) - 1;
					send.WriteUBitLong(unOnes, padBits);
				}
			}
		}

		bool sendVoice = false;

		bool compress = false;
		// net compress?

		flagsPos.WriteByte(flags);

		if (ShouldChecksumPackets()) {
			fixed (byte* ptr = send.BaseArray) {
				void* pvData = ptr + checksumStart;
				Assert(send.BitsWritten % 8 == 0);
				int nCheckSumBytes = send.BytesWritten - checksumStart;
				ushort usCheckSum = BufferToShortChecksum(pvData, nCheckSumBytes);
				flagsPos.WriteUBitLong(usCheckSum, 16);
			}
		}

		int bytesSent = Net.SendPacket(this, Socket, RemoteAddress, send.BaseArray, send.BytesWritten, sendVoice ? StreamVoice : null, compress);

		if (sendVoice)
			StreamVoice.Reset();

		int totalSize = bytesSent + UDP_HEADER_SIZE;

		FlowNewPacket(NetFlow.FLOW_OUTGOING, OutSequence, InSequence, ChokedPackets, 0, totalSize);
		FlowUpdate(NetFlow.FLOW_OUTGOING, totalSize);

		if (ClearTime < Net.Time)
			ClearTime = Net.Time;

		double addTime = totalSize / (double)Rate;
		ClearTime += addTime;
		if (Net.net_maxcleartime.GetDouble() > 0) {
			double latestClearTime = Net.Time + Net.net_maxcleartime.GetDouble();
			if (ClearTime > latestClearTime)
				ClearTime = latestClearTime;
		}

		// convar...
		ChokedPackets = 0;
		OutSequence++;

		return OutSequence - 1;
	}

	public void SetMaxRoutablePayloadSize(int splitSize) {
		if (MaxRoutablePayloadSize != splitSize) {
			DevMsg($"Setting max routable payload size from {MaxRoutablePayloadSize} to {splitSize} for {GetName()}\n");
		}
		MaxRoutablePayloadSize = splitSize;
	}

	public ReadOnlySpan<char> GetName() => Name;

	public int GetMaxRoutablePayloadSize() => MaxRoutablePayloadSize;

	public unsafe bool CreateFragmentsFromBuffer(bf_write buffer, FragmentStream stream) {
		bf_write bfwrite = new();
		DataFragments? data = null;

		// if we have more than one item in the waiting list, try to add the 
		// reliable data to the last item. that doesn't work with the first item
		// since it may have been already send and is waiting for acknowledge

		int count = WaitingList[(int)stream].Count;

		if (count > 1) {
			// get last item in waiting list
			data = WaitingList[(int)stream][count - 1];

			int totalBytes = Bits2Bytes((int)(data.Bits + (uint)buffer.BitsWritten));

			totalBytes = PAD_NUMBER(totalBytes, 4); // align to 4 bytes boundary

			if (totalBytes < MAX_PAYLOAD && data.Buffer != null) {
				// we have enough space for it, create new larger mem buffer
				byte[] newBuf = ArrayPool<byte>.Shared.Rent(totalBytes);

				Array.Copy(data.Buffer, newBuf, data.Bytes);
				ArrayPool<byte>.Shared.Return(data.Buffer, true);

				data.Buffer = newBuf; // set new buffer

				bfwrite.StartWriting(newBuf, totalBytes, (int)data.Bits);
			}
			else {
				data = null; // reset to NULL
			}
		}

		// if not added to existing item, create a new reliable data waiting buffer
		if (data == null) {
			int totalBytes = Bits2Bytes(buffer.BitsWritten);

			totalBytes = PAD_NUMBER(totalBytes, 4); // align to 4 bytes boundary

			data = new DataFragments();
			data.Bytes = 0;    // not filled yet
			data.Bits = 0;
			data.Buffer = ArrayPool<byte>.Shared.Rent(totalBytes);
			data.Compressed = false;
			data.UncompressedSize = 0;
			data.Filename = null;

			bfwrite.StartWriting(data.Buffer, totalBytes, 0);
			WaitingList[(int)stream].Add(data);  // that's it for now
		}

		// update bit length
		data.Bits += (uint)buffer.BitsWritten;
		data.Bytes = (uint)Bits2Bytes((int)data.Bits);

		// write new reliable data to buffer
		bfwrite.WriteBits(buffer.BaseArray, buffer.BitsWritten);

		// fill last bits in last byte with NOP if necessary
		int nRemainingBits = bfwrite.BitsWritten % 8;
		if (nRemainingBits > 0 && nRemainingBits <= 8 - NETMSG_TYPE_BITS) {
			bfwrite.WriteUBitLong(NET.NOP, NETMSG_TYPE_BITS);
		}

		// check if send as stream or with snapshot
		data.AsTCP = StreamActive && data.Bytes > MaxReliablePayloadSize;

		// calc number of fragments needed
		data.NumFragments = BYTES2FRAGMENTS((int)data.Bytes);
		data.AckedFragments = 0;
		data.PendingFragments = 0;

		return true;
	}

	public bool Transmit(bool onlyReliable = false) {
		if (onlyReliable)
			StreamUnreliable.Reset();

		return SendDatagram(null) != 0;
	}

	public int SplitPacketSequence;

	public double TimeConnected() => Math.Max(0, Net.Time - ConnectTime);
	public bool IsTimedOut() => Timeout == -1 ? false : LastReceived + Timeout < Net.Time;
	public bool IsTimingOut() => Timeout == -1 ? false : LastReceived + CONNECTION_PROBLEM_TIME < Net.Time;
	public double TimeSinceLastReceived() => Math.Max(Net.Time - LastReceived, 0);
	public bool IsOverflowed() => StreamReliable.Overflowed;

	public void Reset() {
		StreamUnreliable.Reset();
		StreamReliable.Reset();
		ClearTime = 0;
		ChokedPackets = 0;
		SplitPacketSequence = 1;
	}

	public int GetTotalData(int flow) => DataFlow[flow].TotalBytes;
	public int GetSequenceNr(int flow) {
		if (flow == NetFlow.FLOW_OUTGOING)
			return OutSequence;

		else if (flow == NetFlow.FLOW_INCOMING)
			return InSequence;

		return 0;
	}

	public TimeUnit_t GetCommandInterpolationAmount(int flow, int frame) => DataFlow[flow].Frames[frame & NET_FRAMES_MASK].InterpolationAmount;
	public bool IsValidPacket(int flow, int frame) => DataFlow[flow].Frames[frame & NET_FRAMES_MASK].IsValid;
	public double GetPacketTime(int flow, int frame) => DataFlow[flow].Frames[frame & NET_FRAMES_MASK].Time;
	public double GetLatency(int flow) => DataFlow[flow].Latency;

	public int GetSequenceNumber(int flow) {
		if (flow == NetFlow.FLOW_OUTGOING)
			return OutSequence;
		else if (flow == NetFlow.FLOW_INCOMING)
			return InSequence;
		return 0;
	}

	public int GetPacketBytes(int flow, int frame, NetChannelGroup group) {
		if (group >= NetChannelGroup.Total)
			return DataFlow[flow].Frames[frame & NET_FRAMES_MASK].Size;
		else
			return Bits2Bytes((int)DataFlow[flow].Frames[frame & NET_FRAMES_MASK].MessageGroups[(int)group]);
	}

	public void IncrementQueuedPackets() {
		QueuedPackets++;
	}
	public void DecrementQueuedPackets() {
		QueuedPackets--;
		Assert(QueuedPackets >= 0);
		if (QueuedPackets < 0)
			QueuedPackets = 0;
	}
	public bool HasQueuedPackets() {
		// todo: queued packet sender
		return QueuedPackets > 0;
	}


	public void SetInterpolationAmount(TimeUnit_t interpolationAmount) {
		InterpolationAmount = interpolationAmount;
	}

	public const int MAX_FRAGMENTS_PER_PACKET_BITS = 5;

	public unsafe bool ReadSubChannelData(bf_read buf, FragmentStream stream) {
		DataFragments data = ReceiveList[(int)stream]; // get list
		int startFragment = 0;
		int numFragments = 0;
		uint offset = 0;
		uint length = 0;

		bool singleBlock = buf.ReadOneBit() == 0; // is single block ?

		if (!singleBlock) {
			startFragment = (int)buf.ReadUBitLong(MAX_FILE_SIZE_BITS - FRAGMENT_BITS); // 16 MiB max
			numFragments = (int)buf.ReadUBitLong(MAX_FRAGMENTS_PER_PACKET_BITS);  // 8 fragments per packet max
			offset = (uint)(startFragment * FRAGMENT_SIZE);
			length = (uint)(numFragments * FRAGMENT_SIZE);
		}

		// first fragment, read header info
		if (offset == 0) {
			data.Filename = null;
			data.Compressed = false;
			data.TransferID = 0;

			if (singleBlock) {
				// data compressed ?
				if (buf.ReadOneBit() == 1) {
					data.Compressed = true;
					data.UncompressedSize = buf.ReadUBitLong(MAX_FILE_SIZE_BITS);
				}
				else
					data.Compressed = false;

				data.Bytes = buf.ReadVarInt32();
			}
			else {
				// is it a file ?
				if (buf.ReadOneBit() == 1) {
					data.TransferID = buf.ReadUBitLong(32);
					data.Filename = buf.ReadString(MAX_PATH);
				}

				// data compressed ?
				if (buf.ReadOneBit() == 1) {
					data.Compressed = true;
					data.UncompressedSize = buf.ReadUBitLong(MAX_FILE_SIZE_BITS);
				}
				else 
					data.Compressed = false;

				data.Bytes = buf.ReadUBitLong(MAX_FILE_SIZE_BITS);
			}

			if (data.Buffer != null) {
				// last transmission was aborted, free data
				ArrayPool<byte>.Shared.Return(data.Buffer, true);
				data.Buffer = null;
				Warning($"Fragment transmission aborted at {data.AckedFragments}/{data.NumFragments} from {RemoteAddress}.\n");
			}

			data.Bits = data.Bytes * 8;
			data.Rent(PAD_NUMBER((int)data.Bytes, 4));
			data.AsTCP = false;
			data.NumFragments = BYTES2FRAGMENTS((int)data.Bytes);
			data.AckedFragments = 0;
			data.File = null;

			if (singleBlock) {
				numFragments = data.NumFragments;
				length = (uint)(numFragments * FRAGMENT_SIZE);
			}

			if (data.Bytes > MAX_FILE_SIZE) {
				// This can happen with the compressed path above, which uses VarInt32 rather than MAX_FILE_SIZE_BITS
				Warning($"Net message exceeds max size ({MAX_FILE_SIZE} / {data.Bytes})\n");
				// Subsequent packets for this transfer will treated as invalid since we never setup a buffer.
				return false;
			}

		}
		else {
			if (data.Buffer == null) {
				// This can occur if the packet containing the "header" (offset == 0) is dropped.  Since we need the header to arrive we'll just wait
				//  for a retry
				// ConDMsg("Received fragment out of order: %i/%i\n", startFragment, numFragments );
				return false;
			}
		}

		if (startFragment + numFragments == data.NumFragments) {
			// we are receiving the last fragment, adjust length
			int rest = FRAGMENT_SIZE - (int)(data.Bytes % FRAGMENT_SIZE);
			if (rest < FRAGMENT_SIZE)
				length -= (uint)rest;
		}
		else if (startFragment + numFragments > data.NumFragments) {
			// a malicious client can send a fragment beyond what was arranged in fragment#0 header
			// old code will overrun the allocated buffer and likely cause a server crash
			// it could also cause a client memory overrun because the offset can be anywhere from 0 to 16MB range
			// drop the packet and wait for client to retry
			Warning($"Received fragment chunk out of bounds: {startFragment}+{numFragments}>{data.NumFragments} from {RemoteAddress}\n");
			return false;
		}

		Assert(offset + length <= data.Bytes);
		if (length == 0 || offset + length > data.Bytes) {
			data.Return();
			Warning($"Malformed fragment offset {offset} len {length} buffer size {PAD_NUMBER((int)data.Bytes, 4)} from {RemoteAddress}\n");
			return false;
		}

		fixed (byte* ptr = data.Buffer)
			buf.ReadBytes(new Span<byte>(ptr + offset, (int)length)); // read data

		data.AckedFragments += numFragments;

		if (Net.net_showfragments.GetBool())
			Msg($"Received fragments: start {startFragment}, num {numFragments}, end {data.NumFragments}\n");

		return true;
	}

	private void FlowUpdate(int flowIdx, int size) {
		NetFlow flow = DataFlow[flowIdx];
		flow.TotalBytes += size;

		if (flow.NextCompute > Net.Time)
			return;

		flow.NextCompute = Net.Time + NetFlow.FLOW_INTERVAL;

		int totalvalid = 0;
		int totalinvalid = 0;
		int totalbytes = 0;
		TimeUnit_t totallatency = 0.0;
		int totallatencycount = 0;
		int totalchoked = 0;

		TimeUnit_t starttime = double.MaxValue;
		TimeUnit_t endtime = 0.0;

		NetFrame prev = flow.Frames[NET_FRAMES_BACKUP - 1];

		for (int i = 0; i < NET_FRAMES_BACKUP; i++) {
			NetFrame curr = flow.Frames[i];

			if (curr.IsValid) {
				if (curr.Time < starttime)
					starttime = curr.Time;

				if (curr.Time > endtime)
					endtime = curr.Time;

				totalvalid++;
				totalchoked += curr.ChokedPackets;
				totalbytes += curr.Size;

				if (curr.Latency > 0) {
					totallatency += curr.Latency;
					totallatencycount++;
				}
			}
			else
				totalinvalid++;

			prev = curr;
		}

		TimeUnit_t totaltime = endtime - starttime;

		if (totaltime > 0) {
			flow.AverageBytesPerSec *= NetFlow.FLOW_AVG;
			flow.AverageBytesPerSec += (1.0f - NetFlow.FLOW_AVG) * (totalbytes / totaltime);

			flow.AveragePacketsPerSec *= NetFlow.FLOW_AVG;
			flow.AveragePacketsPerSec += (1.0f - NetFlow.FLOW_AVG) * (totalvalid / totaltime);
		}

		int totalPackets = totalvalid + totalinvalid;

		if (totalPackets > 0) {
			flow.AverageLoss *= NetFlow.FLOW_AVG;
			flow.AverageLoss += (1.0f - NetFlow.FLOW_AVG) * ((float)(totalinvalid - totalchoked) / totalPackets);

			if (flow.AverageLoss < 0)
				flow.AverageLoss = 0;

			flow.AverageChoke *= NetFlow.FLOW_AVG;
			flow.AverageChoke += (1.0f - NetFlow.FLOW_AVG) * ((float)totalchoked / totalPackets);
		}

		if (totallatencycount > 0) {
			TimeUnit_t newping = totallatency / totallatencycount;
			flow.Latency = newping;
			flow.AverageLatency *= NetFlow.FLOW_AVG;
			flow.AverageLatency += (1.0 - NetFlow.FLOW_AVG) * newping;
		}
	}

	public void SetRemoteFramerate(TimeUnit_t hostFrameTime, TimeUnit_t hostFrameDeviation) {
		RemoteFrameTime = hostFrameTime;
		RemoteFrameTimeStdDeviation = hostFrameDeviation;
	}

	public void SetTimeout(TimeUnit_t time) {
		Timeout = time;
	}

	public bool IsLoopback() => RemoteAddress.IsLoopback();
	public TimeUnit_t GetAverageLatency(int flow) => DataFlow[flow].AverageLatency;
	public TimeUnit_t GetAverageLoss(int flow) => DataFlow[flow].AverageLoss;
	public TimeUnit_t GetAverageChoke(int flow) => DataFlow[flow].AverageChoke;
	public TimeUnit_t GetAverageData(int flow) => DataFlow[flow].AverageBytesPerSec;
	public TimeUnit_t GetAveragePackets(int flow) => DataFlow[flow].AveragePacketsPerSec;

	public ReadOnlySpan<char> GetAddress() => RemoteAddress?.ToString();

	public double GetTime() => Net.Time;

	public double GetTimeConnected() {
		TimeUnit_t t = Net.Time - ConnectTime;
		return (t > 0) ? t : 0;
	}

	public int GetBufferSize() => NET_FRAMES_BACKUP;
	public int GetDataRate() => Rate;

	public double GetTimeSinceLastReceived() {
		TimeUnit_t t = Net.Time - LastReceived;
		return (t > 0) ? t : 0;
	}

	public double GetTimeoutSeconds() => Timeout;

	public bool GetStreamProgress(int flow, out int received, out int total) {
		total = 0;
		received = 0;

		if (flow == NetFlow.FLOW_INCOMING) {
			for (int i = 0; i < (int)FragmentStream.Max; i++) {
				if (ReceiveList[i].Buffer != null) {
					total += ReceiveList[i].NumFragments * FRAGMENT_SIZE;
					received += ReceiveList[i].AckedFragments * FRAGMENT_SIZE;
				}
			}

			return total > 0;
		}

		if (flow == NetFlow.FLOW_OUTGOING) {
			for (int i = 0; i < (int)FragmentStream.Max; i++) {
				if (WaitingList[i].Count > 0) {
					total += WaitingList[i][0].NumFragments * FRAGMENT_SIZE;
					received += WaitingList[i][0].AckedFragments * FRAGMENT_SIZE;
				}
			}

			return total > 0;
		}

		return false;
	}

	public void GetPacketResponseLatency(int flow, int frameNumber, out int latencyMsecs, out int choke) {
		NetFrame frame = DataFlow[flow].Frames[frameNumber & NET_FRAMES_MASK];

		if (frame.DroppedPackets != 0)
			latencyMsecs = 9999;
		else
			latencyMsecs = (int)(float)(1000.0 * frame.AverageLatency);

		choke = frame.ChokedPackets;
	}

	public void GetRemoteFramerate(out double frameTime, out double frameTimeStdDeviation) {
		frameTime = RemoteFrameTime;
		frameTimeStdDeviation = RemoteFrameTimeStdDeviation;
	}

	public double GetAvgData(int flow) {
		return DataFlow[flow].AverageBytesPerSec;
	}

	public void SetDataRate(float rate) {
		Rate = (int)Math.Clamp(rate, MIN_RATE, MAX_RATE);
	}

	public void SetChallengeNr(uint chnr) {
		ChallengeNumber = chnr;
	}

	public void ProcessPlayback() {
		// TODO: Demoplayer subsystem/interface/etc...
		throw new NotImplementedException();
	}

	public bool ProcessStream() {
		// If I remember correctly, Garry's Mod removed the TCP	socket entirely, and we currently dont support it in SDN anyway.
		// I'm not sure if other Source games even really use it...
		// Might be worth tearing out of the engine entirely later
		throw new NotImplementedException();
	}

	public bool IsValidFileForTransfer(ReadOnlySpan<char> filename) {
		if (filename.IsEmpty || filename[0] == '\0')
			return false;

		if (!Source.Engine.Common.IsValidPath(filename) || Path.IsPathFullyQualified(filename))
			return false;

		int len = (int)strlen(filename);
		if (len >= MAX_PATH)
			return false;

		Span<char> szTemp = stackalloc char[MAX_PATH];
		strcpy(szTemp, filename);

		// Convert so we've got all forward slashes in the path.
		StrTools.FixSlashes(szTemp);
		StrTools.FixDoubleSlashes(szTemp);
		if (szTemp[len - 1] == '/')
			return false;

		int slash_count = 0;
		for (ReadOnlySpan<char> psz = szTemp; !psz.IsEmpty && psz[0] != '\0'; psz = psz[1..]) {
			if (psz[0] == '/')
				slash_count++;
		}

		// Really no reason to have deeper directory than this?
		if (slash_count >= 32)
			return false;

		// Don't allow filenames with unicode whitespace in them.
		if (StrTools.RemoveAllEvilCharacters(szTemp))
			return false;

		if (!stristr(szTemp, "lua/").IsStringEmpty ||
			 !stristr(szTemp, "gamemodes/").IsStringEmpty ||
			 !stristr(szTemp, "addons/").IsStringEmpty ||
			 !stristr(szTemp, "~/").IsStringEmpty ||
			 !stristr(szTemp, "./././").IsStringEmpty || // Don't allow folks to make crazy long paths with ././././ stuff.
			 !stristr(szTemp, "   ").IsStringEmpty ||        // Don't allow multiple spaces or tab (was being used for an exploit).
			!stristr(szTemp, "\t").IsStringEmpty) {
			return false;
		}

		// If .exe or .EXE or these other strings exist _anywhere_ in the filename, reject it.
		if (!stristr(szTemp, ".cfg").IsStringEmpty ||
			 !stristr(szTemp, ".lst").IsStringEmpty ||
			 !stristr(szTemp, ".exe").IsStringEmpty ||
			 !stristr(szTemp, ".vbs").IsStringEmpty ||
			 !stristr(szTemp, ".com").IsStringEmpty ||
			 !stristr(szTemp, ".bat").IsStringEmpty ||
			 !stristr(szTemp, ".cmd").IsStringEmpty ||
			 !stristr(szTemp, ".dll").IsStringEmpty ||
			 !stristr(szTemp, ".so").IsStringEmpty ||
			 !stristr(szTemp, ".dylib").IsStringEmpty ||
			 !stristr(szTemp, ".ini").IsStringEmpty ||
			 !stristr(szTemp, ".log").IsStringEmpty ||
			 !stristr(szTemp, ".lua").IsStringEmpty ||
			 !stristr(szTemp, ".vdf").IsStringEmpty ||
			 !stristr(szTemp, ".smx").IsStringEmpty ||
			 !stristr(szTemp, ".gcf").IsStringEmpty ||
			 !stristr(szTemp, ".lmp").IsStringEmpty ||
			 !stristr(szTemp, ".sys").IsStringEmpty) {
			return false;
		}

		// Search for the first . in the base filename, and bail if not found.
		// We don't want people passing in things like 'cfg/.wp.so'...
		ReadOnlySpan<char> basename = strrchr(szTemp, '/');
		if (basename.IsEmpty)
			basename = szTemp;
		ReadOnlySpan<char> extension = strchr(basename, '.');
		if (extension.IsEmpty)
			return false;

		// If the extension is not exactly 3 or 4 characters, bail.
		int extension_len = (int)strlen(extension);
		if ((extension_len != 3) &&
			 (extension_len != 4) &&
			 stricmp(extension, ".bsp.bz2") != 0 &&
			 stricmp(extension, ".xbox.vtx") != 0 &&
			 stricmp(extension, ".dx80.vtx") != 0 &&
			 stricmp(extension, ".dx90.vtx") != 0 &&
			 stricmp(extension, ".sw.vtx") != 0) {
			return false;
		}

		// If there are any spaces in the extension, bail. (Windows exploit).
		if (!strchr(extension, ' ').IsStringEmpty)
			return false;

		return true;
	}

	public bool SendFile(ReadOnlySpan<char> filename, uint transferID) {
		// add file to waiting list
		if (RemoteAddress!.Type == NetAddressType.Null)
			return true;

		if (filename.IsEmpty || filename[0] == '\0')
			return false;

		ReadOnlySpan<char> sendfile = filename;
		while (sendfile[0] != '\0' && sendfile[0].IsPathSeparator())
			sendfile = sendfile[1..];

		// Don't transfer exe, vbs, com, bat-type files.
		if (!IsValidFileForTransfer(sendfile))
			return false;

		if (!CreateFragmentsFromFile(sendfile, FragmentStream.File, transferID)) {
			DenyFile(transferID); // send host a deny message
			return false;
		}

		if (Net.net_showfragments.GetInt() == 2)
			DevMsg($"SendFile: {sendfile} (ID {transferID})\n");

		return true;
	}

	public bool IsFileInWaitingList(ReadOnlySpan<char> filename) {
		if (filename.IsStringEmpty)
			return true;

		for (FragmentStream stream = 0; stream < FragmentStream.Max; stream++) {
			for (int i = 0; i < WaitingList[(int)stream].Count; i++) {
				DataFragments data = WaitingList[(int)stream][i];

				if (0 == strcmp(data.Filename, filename))
					return true; // alread in list
			}
		}

		return false; // file not found
	}

	public bool CreateFragmentsFromFile(ReadOnlySpan<char> filename, FragmentStream stream, uint transferID) {
		filename = filename.SliceNullTerminatedString();
		if (IsFileInWaitingList(filename))
			return true; // already scheduled for upload

		ReadOnlySpan<char> pathID = "GAME";

		if (!g_pFileSystem.FileExists(filename, pathID)) {
			ConMsg($"CreateFragmentsFromFile: '{filename}' doesn't exist.\n");
			return false;
		}

		long totalBytes = g_pFileSystem.Size(filename, pathID);

		if (totalBytes >= (Net.net_maxfilesize.GetInt() * 1024 * 1024)) {
			ConMsg($"CreateFragmentsFromFile: '{filename}' size exceeds net_maxfilesize limit ({Net.net_maxfilesize.GetInt()} MiB).\n");
			return false;
		}

		if (totalBytes >= MAX_FILE_SIZE) {
			ConMsg($"CreateFragmentsFromFile: '{filename}' too big (max {MAX_FILE_SIZE} bytes).\n");
			return false;
		}

		DataFragments data = new();
		data.Bytes = (uint)totalBytes;
		data.Bits = data.Bytes * 8;
		data.Buffer = null;
		data.Compressed = false;
		data.UncompressedSize = 0;
		data.File = g_pFileSystem.Open(filename, Filesystem.FileOpenOptions.Read | Filesystem.FileOpenOptions.Binary, pathID);

		if (data.File == null) {
			ConMsg($"CreateFragmentsFromFile: couldn't open '{filename}'.\n");
			return false;
		}

		data.TransferID = transferID;
		data.Filename = new(filename.SliceNullTerminatedString());

		WaitingList[(int)stream].Add(data);  // that's it for now

		// check if send as stream or with snapshot
		data.AsTCP = false; // m_StreamActive && ( Bits2Bytes(data->length) > m_MaxReliablePayloadSize );

		// calc number of fragments needed
		data.NumFragments = (int)BYTES2FRAGMENTS(data.Bytes);
		data.AckedFragments = 0;
		data.PendingFragments = 0;

		return true;
	}

	public void DenyFile(uint transferID) {
		if (Net.net_showfragments.GetInt() == 2)
			DevMsg($"DenyFile: (ID {transferID})\n");

		StreamReliable.WriteUBitLong(NET.File, NETMSG_TYPE_BITS);
		StreamReliable.WriteUBitLong(transferID, 32);
		StreamReliable.WriteOneBit(0); // deny this file
	}

	public NetAddress? GetRemoteAddress() {
		return RemoteAddress;
	}

	public INetChannelHandler? GetMsgHandler() {
		return MessageHandler;
	}

	public int GetDropNumber() {
		return packetDrop;
	}

	public NetSocketType GetSocket() {
		return Socket;
	}

	public uint GetChallengeNr() {
		return ChallengeNumber;
	}

	public void UpdateMessageStats(NetChannelGroup msggroup, int bits) {
		NetFlow flow = DataFlow[NetFlow.FLOW_INCOMING];
		NetFrame? frame = flow.CurrentFrame;

		Assert((msggroup >= NetChannelGroup.Generic) && (msggroup < NetChannelGroup.Total));

		MessageStats[(int)msggroup] += bits;

		if (frame != null)
			frame.MessageGroups[(int)msggroup] += unchecked((ushort)bits);
	}

	public bool CanPacket() {
		if (Net.net_chokeloopback.GetInt() == 0 && RemoteAddress!.IsLoopback())
			return true;

		if (HasQueuedPackets())
			return false;

		return ClearTime < Net.Time;
	}

	public bool HasPendingReliableData() {
		return StreamReliable.BitsWritten > 0 || WaitingList[(int)FragmentStream.Normal].Count > 0 || WaitingList[(int)FragmentStream.File].Count > 0;
	}

	public void SetFileTransmissionMode(bool backgroundMode) => FileBackgroundTransmission = backgroundMode;

	public void SetCompressionMode(bool useCompression) => UseCompression = useCompression;

	public uint RequestFile(RequestFile type, uint value) {
		uint transferID = FileRequestCounter++;

		if (Net.net_showfragments.GetInt() == 2)
			DevMsg($"RequestFile: type {type} value {value} (ID {transferID})\n");

		StreamReliable.WriteUBitLong(NET.File, NETMSG_TYPE_BITS);
		StreamReliable.WriteUBitLong(transferID, 32);
		StreamReliable.WriteOneBit(1);              // request (vs. deny)
		StreamReliable.WriteOneBit((int)type);      // RequestFile_t
		StreamReliable.WriteUBitLong(value, 32);

		return transferID;
	}

	public bool IsNull() => RemoteAddress?.Type == NetAddressType.Null;

	public int GetNumBitsWritten(bool reliable) {
		bf_write stream = reliable ? StreamReliable : StreamUnreliable;
		return stream.BitsWritten;
	}

	public int GetProtocolVersion() {
		return ProtocolVersion;
	}
}
