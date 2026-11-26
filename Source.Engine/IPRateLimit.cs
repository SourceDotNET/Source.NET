using Source.Common;
using Source.Common.Commands;
using Source.Common.Mathematics;
using Source.Common.Networking;

using System.Buffers.Binary;
using System.Net.Sockets;

namespace Source.Engine;

public class IPRateLimit
{
	[CvarIgnore] readonly ConVar maxSec;
	[CvarIgnore] readonly ConVar maxWindow;
	[CvarIgnore] readonly ConVar maxSecGlobal;

	public IPRateLimit(ConVar maxSec, ConVar maxWindow, ConVar maxSecGlobal) {
		this.maxSec = maxSec;
		this.maxWindow = maxWindow;
		this.maxSecGlobal = maxSecGlobal;
	}

	const int MAX_TREE_SIZE = 32768;
	const int FLUSH_TIMEOUT = 120;

	static readonly ConVar sv_logblocks = new("sv_logblocks", "0", 0, "If true when log when a query is blocked (can cause very large log files)");

	readonly SortedDictionary<int, IPRate> IPTree = [];
	int globalCount;
	long lastTime = -1;
	double flushTime;

	struct IPRate
	{
		public long LastTime;
		public int Count;
	}

	public bool CheckIP(NetAddress adr) {
		bool ret = CheckIPInternal(adr);
		if (!ret && sv_logblocks.GetBool())
			g_Log.Print($"Traffic from {adr.ToString(false)} was blocked for exceeding rate limits\n");

		return ret;
	}

	bool CheckIPInternal(NetAddress adr) {
		globalCount++;

		long curTime = (long)Platform.Time;

		if ((float)(curTime - lastTime) > maxWindow.GetFloat()) {
			lastTime = curTime;
			globalCount = 1;
		}

		float queryRate = globalCount / maxWindow.GetFloat();
		if (maxSecGlobal.GetFloat() > MathLibConsts.FLT_EPSILON && queryRate > maxSecGlobal.GetFloat())
			return false;

		int clientIP = 0;
		if (adr.Endpoint != null && adr.Endpoint.AddressFamily == AddressFamily.InterNetwork) {
			Span<byte> ip = stackalloc byte[4];
			adr.Endpoint.Address.TryWriteBytes(ip, out _);
			clientIP = BinaryPrimitives.ReadInt32LittleEndian(ip);
		}

		if (IPTree.Count > MAX_TREE_SIZE && curTime > flushTime + FLUSH_TIMEOUT / 4) {
			flushTime = curTime;

			int count = IPTree.Count;
			List<int> expired = [];
			foreach (KeyValuePair<int, IPRate> entry in IPTree.Reverse()) {
				if (count <= 2 * MAX_TREE_SIZE / 3)
					break;

				if (curTime - entry.Value.LastTime > FLUSH_TIMEOUT && entry.Key != clientIP) {
					expired.Add(entry.Key);
					count--;
				}
			}

			foreach (int key in expired)
				IPTree.Remove(key);
		}

		if (IPTree.TryGetValue(clientIP, out IPRate rate)) {
			rate.Count++;

			if ((float)(curTime - rate.LastTime) > maxWindow.GetFloat()) {
				rate.LastTime = curTime;
				rate.Count = 1;
				IPTree[clientIP] = rate;
			}
			else {
				IPTree[clientIP] = rate;

				float flQueryRate = rate.Count / maxWindow.GetFloat();
				if (flQueryRate > maxSec.GetFloat())
					return false;
			}
		}
		else
			IPTree.Add(clientIP, new() { Count = 1, LastTime = curTime });

		return true;
	}
}
