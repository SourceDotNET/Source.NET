#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source.Common.Commands;
using Source.Common.GarrysMod.Lua;
using Source.Common.Input;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaConCommands
{

	static readonly string[] s_BannedConvars = [
		"crosshair_setup",
		"hud_fastswitch",
		"hud_quickinfo",
		"cl_crosshairstyle",
		"cl_crosshairusealpha",
		"cl_crosshairgap",
		"cl_crosshairsize",
		"cl_crosshairthickness",
		"cl_crosshairdot",
		"cl_crosshair_t",
		"cl_crosshaircolor_r",
		"cl_crosshaircolor_g",
		"cl_crosshaircolor_b",
		"cl_crosshairalpha",
		"cl_crosshair_drawoutline",
		"cl_crosshair_outlinethickness",
		"sensitivity",
		"cl_enable_loadingurl",
		"cl_defaultweapon",
		"cl_autowepswitch",
		"fps_max",
		"fps_max_menu",
		"fps_max_nofocus",
		"engine_no_focus_sleep",
		"cl_downloadfilter",
		"lookspring",
		"lookstrafe",
		"m_customaccel",
		"m_customaccel_exponent",
		"m_customaccel_scale",
		"m_customaccel_max",
		"m_mousespeed",
		"m_mouseaccel1",
		"m_mouseaccel2",
		"m_yaw",
		"m_pitch",
		"m_forward",
		"m_side",
		"cl_software_cursor",
		"cl_allowdownload",
		"cl_allowupload",
		"cl_logofile",
		"mat_color_projection",
		"mat_disable_d3d9ex",
		"voice_enable",
		"voice_scale",
		"voice_avggain",
		"voice_maxgain",
		"voice_gain_rate",
		"voice_gain_downward_multiplier",
		"voice_gain_target",
		"voice_gain_max",
		"voice_fadeouttime",
		"voice_forcemicrecord",
		"voice_inputfromfile",
		"voice_loopback",
		"voice_modenable",
		"voice_overdrive",
		"voice_overdrivefadetime",
		"voice_recordtofile",
		"volume",
		"volume_sfx",
		"suitvolume",
		"snd_musicvolume",
		"snd_gain",
		"snd_gain_max",
		"snd_gain_min",
		"snd_mixahead",
		"snd_pitchquality",
		"snd_ducktovolume",
		"snd_duckerattacktime",
		"snd_duckerreleasetime",
		"snd_duckerthreshold",
		"snd_mute_losefocus",
		"snd_surround_speakers",
		"dsp_slow_cpu",
		"dsp_enhance_stereo",
		"fov_desired",
		"crosshair",
		"cl_chatfilters",
		"con_enable",
		"con_bgalpha",
		"con_border",
		"con_filter_enable",
		"con_filter_dupe",
		"con_filter_text",
		"con_filter_text_out",
		"gmod_delete_temp_files",
		"host_writeconfig",
		"host_writeconfig_lua",
		"mat_savechanges",
		"cl_mouseenable",
		"skill",
		"cl_playerspraydisable",
		"mp_flashlight",
		"lua_strict",
		"closecaption",
		"cc_lang",
		"cc_subtitles",
		"cc_linger_time",
		"cc_predisplay_time",
		"mp_decals",
		"muzzleflash_light",
		"r_ambientboost",
		"r_ambientmin",
		"r_ambientfraction",
		"r_ambientfactor",
		"r_lightcachemodel",
		"r_drawlightcache",
		"zoom_sensitivity_ratio",
		"cl_rumblescale",
		"m_filter",
		"cl_chatfilter_version",
		"net_maxroutable",
		"tv_nochat",
		"windows_speaker_config",
		"xbox_autothrottle",
		"xbox_steering_deadzone",
		"xbox_throttlespoof",
		"xbox_throttlebias",
		"func_break_max_pieces",
		"cl_savescreenshotstosteam",
		"texture_budget_background_alpha",
		"texture_budget_panel_bottom_of_history_fraction",
		"texture_budget_panel_height",
		"texture_budget_panel_width",
		"texture_budget_panel_x",
		"texture_budget_panel_y",
		"budget_averages_window",
		"budget_background_alpha",
		"budget_bargraph_background_alpha",
		"budget_bargraph_range_ms",
		"budget_history_numsamplesvisible",
		"budget_history_range_ms",
		"budget_panel_bottom_of_history_fraction",
		"budget_panel_height",
		"budget_panel_width",
		"budget_panel_x",
		"budget_panel_y",
		"budget_peaks_window",
		"budget_show_averages",
		"budget_show_history",
		"budget_show_peaks",
		"net_graph",
		"net_scale",
		"net_graphheight",
		"net_graphmsecs",
		"net_graphpos",
		"net_graphproportionalfont",
		"net_graphshowinterp",
		"net_graphshowlatency",
		"net_graphsolid",
		"net_graphtext",
		"mat_forcehardwaresync",
		"mat_forceaniso",
		"mat_picmip",
		"mat_trilinear",
		"mat_antialias",
		"mat_aaquality",
		"mat_colorcorrection",
		"mat_hdr_level",
		"mat_vsync",
		"mat_reducefillrate",
		"mat_reduceparticles",
		"r_shadowrendertotexture",
		"r_rootlod",
		"r_waterforceexpensive",
		"r_waterforcereflectentities",
		"mat_forcemanagedtextureintohardware",
		"mat_managedtextures",
		"mat_monitorgamma",
		"mat_monitorgamma_tv_enabled",
		"mat_powersavingsmode",
		"mat_requires_rt_alloc_first",
		"mat_supportflashlight",
		"r_worldlights",
		"r_teeth",
		"r_shader_srgb",
		"mat_dump_rts",
		"sv_cheats",
		"mat_setvideomode",
		"mat_viewportscale",
		"mat_viewportupscale",
		"connect",
		"gm_video",
		"cancelselect",
		"gmod_language",
		"cef_credits",
		"gmod_tos",
		"gmod_privacy",
		"gmod_modding",
		"gmod_servers",
		"whereis",
		"path",
		"fs_tellmeyoursecrets",
		"fs_warning_level",
		"fs_printopenfiles ",
		"fs_report_sync_opens",
		"fs_monitor_read_from_pack",
		"gm_demo_to_video",
		"mem_force_flush",
		"scene_flush",
		"startdemos",
		"nextdemo",
		"startmovie",
		"playdemo",
		"editdemo",
		"timedemo",
		"spike",
		"timedemoquit",
		"demo_quitafterplayback",
		"contimes",
		"con_drawnotify",
		"developer",
#if CLIENT_DLL
		"_restart",
		"map",
		"map_background",
		"map_edit",
		"maxplayers",
		"tv_record",
#endif
		"exec",
		"bind",
		"bind_mac",
		"bindtoggle",
		"unbind",
		"unbind_mac",
		"unbindall",
		"alias",
		"ent_fire",
		"ent_setname",
		"name",
		"quit",
		"quti",
		"exit",
		"killserver",
		"lua_run",
		"lua_run_cl",
		"lua_open",
		"lua_cookieclear",
		"lua_showerrors_cl",
		"lua_showerrors_sv",
		"lua_openscript",
		"lua_openscript_cl",
		"lua_redownload",
		"lua_reloadents",
		"lua_reloadents_cl",
		"lua_error_url",
		"gamemode_reload",
		"gamemode_reload_cl",
		"clear",
		"rcon_password",
		"rcon_address",
		"echo",
		"test_randomchance",
		"test_startscript",
		"toggle",
		"incrementvar",
		"multvar",
		"gameui_hide",
		"gameui_preventescapetoshow",
		"mat_texture_limit",
		"mat_crosshair_explorer",
		"mat_dxlevel",
		"toggleconsole",
		"showconsole",
		"hideconsole",
		"plugin_load",
		"sv_logsdir",
		"sv_logfilename_format",
		"sv_log_storetime",
		"logaddress_add",
		"log",
		"askconnect_accept",
		"lightprobe",
		"buildcubemaps",
		"gameui_show_dialog",
		"menu_reload",
		"debug_dump",
		"servercfgfile",
		"lservercfgfile",
		"+voicerecord",
		"mat_texture_list_txlod_sync",
		"building_cubemaps",
		"setinfo",
		"cl_ignorepackets",
		"vcr_verbose",
		"tv_relay",
		"tv_retry",
		"+mat_texture_list",
		"mat_texture_list",
		"mat_texture_list_content_path",
		"mat_crosshair_edit",
		"mat_crosshair_explorer",
		"startupmenu",
		"movie_fixwave",
		"cl_cloud_settings",
		"stuffcmds",
		"mat_texture_save_fonts",
		"load",
		"physics_budget",
		"physics_highlight_active",
		"physics_report_active",
		"physics_constraints",
		"physics_debug_entity",
		"physics_select",
		"surfaceprop",
		"cl_particle_stats_start",
		"cl_particle_stats_stop",
		"cl_particle_stats_trigger_count",
		"filesystem_unbuffered_io",
		"filesystem_buffer_size",
		"filesystem_native",
		"filesystem_max_stdio_read",
		"filesystem_report_buffered_io",
		"hud_autoreloadscript",
		"hud_freezecamhide",
		"hud_reloadscheme",
		"mat_bufferprimitives",
		"sdl_displayindex",
		"snd_buildcache",
		"__screenshot_internal",
		"devshots_screenshot",
		"mem_max_heapsize",
		"datacachesize",
		"con_logfile",
		"vprof_record_start",
		"vprof_record_stop",
		"test_loop",
		"plugin_load",
		"sv_allow_wait_command",
	];

	static readonly bool[] g_bKeyState = new bool[(int)ButtonCode.KeyCount];

	static readonly string[] s_BannedArguments = ["con_logfile", "quitnoconfirm"];

	public static string? ConCommand_ParseAndCheckBlocked(ReadOnlySpan<char> command) {
		Source.Engine.CommandBuffer buffer = new();
		buffer.AddText(command);
		buffer.BeginProcessingCommands(1);

		string? blocked = null;
		while (buffer.DequeueNextCommand()) {
			ref TokenizedCommand args = ref buffer.GetCommand();
			blocked = ConCommand_IsBlocked(args.ArgC() > 0 ? args.Arg(0) : "");
			for (int i = 1; i < args.ArgC(); i++) {
				if (blocked == null && ConCommand_IsBlockedArg(args.Arg(i)) != null)
					blocked = args.ArgC() > 0 ? args.Arg(0).ToString() : "";
			}

			if (blocked != null) {
				while (buffer.DequeueNextCommand()) ;
				break;
			}
		}

		buffer.EndProcessingCommands();
		return blocked;
	}

	public static string? ConCommand_IsBlockedArg(ReadOnlySpan<char> argument) {
		foreach (string banned in s_BannedArguments)
			if (stricmp(banned, argument) == 0)
				return banned;
		return null;
	}

	public static string? ConCommand_IsBlocked(ReadOnlySpan<char> name) {
#if CLIENT_DLL
		if (garrysmod.BlockRetryCommand || gpGlobals.MaxClients <= 1) {
#else
		if (gpGlobals.MaxClients <= 1) {
#endif
			if (stricmp("retry", name) == 0)
				return name.ToString();
		}

#if CLIENT_DLL
		if (enginevgui.IsGameUIVisible()) {
			if (stricmp("screenshot", name) == 0)
				return name.ToString();
			if (stricmp("jpeg", name) == 0)
				return name.ToString();
		}
#endif

		foreach (string banned in s_BannedConvars) {
			if (stricmp(banned, name) == 0)
				return banned;
		}

		return null;
	}

	public static bool IsInGameButtonDown(ButtonCode code) {
		if ((uint)code >= (uint)ButtonCode.KeyLast)
			return false;
		return g_bKeyState[(int)code];
	}

	public static void MarkButtonStatus(ButtonCode code, bool down) {
		if ((uint)code >= (uint)ButtonCode.KeyLast)
			return;
		g_bKeyState[(int)code] = down;
	}

	static void LuaConCommand(in TokenizedCommand args) {
		if (g_Lua == null || g_Lua.Global() == null)
			return;

#if CLIENT_DLL
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
#elif !MENU_DLL
		BasePlayer? player = Util.GetCommandClient();
		if (player == null && Util.GetCommandClientIndex() > 0) {
			Warning($"Warning: Player issued command but is now vanished (Command was \"{args.ArgS()}\")\n");
			return;
		}
#endif

		LuaObject concommand = new();
		g_Lua.Global().GetMember("concommand", concommand);
		if (concommand.isTable()) {
			LuaObject run = new();
			concommand.GetMember("Run", run);
			if (run.isFunction()) {
				ReadOnlySpan<char> command = args.Arg(0);
#if !MENU_DLL
				if (command.Length > 0 && command[0] == '+') {
					if (args.ArgC() > 1)
						MarkButtonStatus((ButtonCode)atoi(args.Arg(1)), true);
				}
				else if (command.Length > 0 && command[0] == '-') {
					if (args.ArgC() > 1)
						MarkButtonStatus((ButtonCode)atoi(args.Arg(1)), false);
				}
#endif

				run.Push();
#if MENU_DLL
				g_Lua.PushNil();
#else
				LuaEntity.Push_Entity(player);
#endif
				g_Lua.PushString(command);

				LuaTable arguments = new(null, (uint)args.ArgC());
				for (int i = 1; i < args.ArgC(); i++)
					arguments.SetMember(i, args.Arg(i));
				arguments.Push();

				g_Lua.PushString(args.ArgS());
				g_Lua.CallInternalNoReturns(4);
				arguments.UnReference();
			}
			run.UnReference();
		}
		concommand.UnReference();
	}

	static IEnumerable<string> LuaConCommandAutocomplete(string partial) {
		List<string> commands = [];
		if (g_Lua == null || g_Lua.Global() == null)
			return commands;

		Span<char> command = stackalloc char[128];
		strcpy(command, partial);
		int length = (int)strlen(command);
		for (int i = 0; i < length; i++) {
			if (command[i] == ' ') {
				command[i] = '\0';
				length = (int)strlen(command);
				break;
			}
		}

		TokenizedCommand args = new();
		args.Tokenize(partial);

		LuaObject concommand = new();
		g_Lua.Global().GetMember("concommand", concommand);
		if (concommand.isTable()) {
			LuaObject autoComplete = new();
			concommand.GetMember("AutoComplete", autoComplete);
			if (autoComplete.isFunction()) {
				autoComplete.Push();
				g_Lua.PushString(command[..length]);
				g_Lua.PushString(partial.AsSpan(length));

				LuaTable arguments = new(null, 0);
				for (int i = 1; i < args.ArgC(); i++)
					arguments.SetMember(i, args.Arg(i));
				arguments.Push();

				LuaObject ret = new();
				if (g_Lua.CallInternalGet(3, ret) && ret.isTable()) {
					for (int i = 1; ; i++) {
						string? str = ret.GetMemberStr(i, null);
						if (str == null)
							break;
						commands.Add(str.Length > 127 ? str[..127] : str);
						if (i + 1 == 64)
							break;
					}
				}
				ret.UnReference();
				arguments.UnReference();
			}
			autoComplete.UnReference();
		}
		concommand.UnReference();

		return commands;
	}

	static bool IsValidConsoleNameLite(ReadOnlySpan<char> name) {
		foreach (char c in name) {
			if (c == ';' || c <= ' ')
				return false;
		}
		return true;
	}

	[LuaGlobal]
	static int AddConsoleCommand(ILuaInterface lua) {
		string name = g_Lua!.CheckString(1);
		if (ConCommand_IsBlocked(name) != null && stricmp(name, "lua_cookieclear") != 0) {
			g_Lua.ErrorFromLua($"AddConsoleCommand: Command name is blocked! ({name})");
			return 0;
		}

		if (!IsValidConsoleNameLite(name)) {
			g_Lua.ErrorFromLua($"AddConsoleCommand: Invalid command name! ({name})");
			return 0;
		}

		if (cvar.FindCommandBase(name) != null)
			return 0;

		string help = g_Lua.CheckStringOpt(2, null);
		int flags = g_Lua.GetFlags(3);
		g_Lua.CreateConCommand(name, help, flags, LuaConCommand, LuaConCommandAutocomplete);
		return 0;
	}

#if !MENU_DLL
	[LuaGlobal]
	static int AddCSLuaFile(ILuaInterface lua) {
#if GAME_DLL
		g_Lua!.GetCurrentFile(out string current);

		string file;
		if (g_Lua.GetType(1) == LuaType.Nil) {
			g_Lua.GetCurrentFile(out file);
			Bootil.String.File.ExtractFilename(ref file);
		}
		else
			file = g_Lua.CheckString(1);

		Bootil.String.Lower(ref file);
		Bootil.String.Lower(ref current);

		LuaFile? luaFile = FileServ.AddCSLuaFile(file, null);
		if (luaFile != null && luaFile.Source.Length == 0)
			luaFile.Source = current;
#endif
		return 0;
	}
#endif
}
#endif
