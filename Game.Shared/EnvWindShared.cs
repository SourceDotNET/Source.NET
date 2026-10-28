using Source.Common;
#if GAME_DLL
using Game.Server;
#endif
using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Shared;

public partial class EnvWindShared
{
	[NetworkTable("DT_EnvWindShared")]
	public static readonly NetworkProp[] Props = [
		new(nameof(MinWind)) { Bits = 10, Flags = PropFlags.Unsigned },
		new(nameof(MaxWind)) { Bits = 10, Flags = PropFlags.Unsigned },
		new(nameof(MinGust)) { Bits = 10, Flags = PropFlags.Unsigned },
		new(nameof(MaxGust)) { Bits = 10, Flags = PropFlags.Unsigned },
		nameof(MinGustDelay),
		nameof(MaxGustDelay),
		new(nameof(GustDirChange)) { Bits = 9, Flags = PropFlags.Unsigned },
		new(nameof(WindSeed)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(InitialWindDir)) { Bits = 9, Flags = PropFlags.Unsigned },
		nameof(InitialWindSpeed),
		nameof(StartTime),
		nameof(GustDuration),
		new(nameof(WindRadius)) { Bits = 32, Flags = PropFlags.NoScale },
	];

#if GAME_DLL
	public BaseEntity? Outer;
	public void NetworkStateChanged() => Outer?.NetworkStateChanged();
	public void NetworkStateChanged(IFieldAccessor field) => Outer?.NetworkStateChanged();
#endif

	[NetworkVar("m_iMinWind")]
	public partial int MinWind { get; set; }
	[NetworkVar("m_iMaxWind")]
	public partial int MaxWind { get; set; }
	[NetworkVar("m_iMinGust")]
	public partial int MinGust { get; set; }
	[NetworkVar("m_iMaxGust")]
	public partial int MaxGust { get; set; }
	[NetworkVar("m_flMinGustDelay")]
	public partial float MinGustDelay { get; set; }
	[NetworkVar("m_flMaxGustDelay")]
	public partial float MaxGustDelay { get; set; }
	[NetworkVar("m_iGustDirChange")]
	public partial int GustDirChange { get; set; }
	[NetworkVar("m_iWindSeed")]
	public partial int WindSeed { get; set; }
	public int GustSound;
	public int WindDir;
	public float WindSpeed;
	[NetworkVar("m_iInitialWindDir")]
	public partial int InitialWindDir { get; set; }
	[NetworkVar("m_flInitialWindSpeed")]
	public partial float InitialWindSpeed { get; set; }
	[NetworkVar("m_flStartTime")]
	public partial float StartTime { get; set; }
	[NetworkVar("m_flGustDuration")]
	public partial float GustDuration { get; set; }
	[NetworkVar("m_windRadius")]
	public partial int WindRadius { get; set; }

	#if !CLIENT_DLL
		// todo: onguststart/ongustend
	#endif
}
