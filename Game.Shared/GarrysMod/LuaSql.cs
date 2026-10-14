#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source.Common.GarrysMod.Lua;

using SQLitePCL;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaSql
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_sql = new("sql");

	static sqlite3? pDatabase;
	static int iResultCount;
	static LuaTable? pResultTable;

	static int DBCallback(object user_data, string[] values, string[] names) {
		LuaTable row = new(null, 0);
		for (int i = 0; i < values.Length; i++)
			row.SetMember(names[i], values[i] ?? "NULL");

		pResultTable!.SetMember(iResultCount, row);
		iResultCount++;
		row.UnReference();
		return 0;
	}

	static void CreateDatabase() {
		Batteries_V2.Init();

#if CLIENT_DLL
		string path = $"{get.GameDir()}/cl.db";
#else
		string path = $"{get.GameDir()}/sv.db";
#endif
		if (path.Length > 0x103)
			path = path[..0x103];

		iResultCount = 1;
		pDatabase = null;
		if (raw.sqlite3_open(path, out pDatabase) != raw.SQLITE_OK) {
			raw.sqlite3_close(pDatabase);
			pDatabase = null;
			raw.sqlite3_open(":memory:", out pDatabase);
		}

		raw.sqlite3_exec(pDatabase, "PRAGMA synchronous = OFF; PRAGMA read_uncommitted = 1; PRAGMA temp_store = MEMORY;");
		raw.sqlite3_db_config(pDatabase, raw.SQLITE_DBCONFIG_DEFENSIVE, 1, out _);
	}

	static void SetLastError(ILuaInterface lua, string error) {
		LuaObject sql = new();
		lua.Global().GetMember("sql", sql);
		if (!sql.isNil())
			sql.SetMember("m_strError", error);
		sql.UnReference();
	}

	static int SetLastErrorAndReturn(ILuaInterface lua, string error) {
		SetLastError(lua, error);
		g_Lua!.PushBool(false);
		return 1;
	}

	[LuaFunction]
	static int Query(ILuaInterface lua) {
		string query = g_Lua!.CheckString(1);
		if (pDatabase == null)
			CreateDatabase();

		if (string.IsNullOrEmpty(query))
			return SetLastErrorAndReturn(lua, "No Query");

		LuaTable result = new(null, 0);
		iResultCount = 1;
		pResultTable = result;

		int ret;
		if (raw.sqlite3_exec(pDatabase, query, DBCallback, null, out string? errMsg) != raw.SQLITE_OK) {
			if (errMsg != null)
				SetLastError(lua, errMsg);
			g_Lua.PushBool(false);
			ret = 1;
		}
		else {
			pResultTable = null;
			if (iResultCount > 1) {
				result.Push();
				ret = 1;
			}
			else
				ret = 0;
		}

		result.UnReference();
		return ret;
	}

	[LuaFunction]
	static int QueryTyped(ILuaInterface lua) {
		string query = g_Lua!.CheckString(1);
		if (pDatabase == null)
			CreateDatabase();

		if (raw.sqlite3_prepare_v2(pDatabase, query, out sqlite3_stmt stmt) != raw.SQLITE_OK)
			return SetLastErrorAndReturn(lua, raw.sqlite3_errmsg(pDatabase).utf8_to_string());

		int top = lua.Top();
		int count = top - 1;
		if (count != raw.sqlite3_bind_parameter_count(stmt)) {
			raw.sqlite3_finalize(stmt);
			return SetLastErrorAndReturn(lua, "incorrect number of parameters provided");
		}

		for (int i = 1; i <= count; i++) {
			int rc;
			switch (lua.GetType(i + 1)) {
				case LuaType.Nil:
					rc = raw.sqlite3_bind_null(stmt, i);
					break;
				case LuaType.Bool:
					rc = raw.sqlite3_bind_int(stmt, i, lua.GetBool(i + 1) ? 1 : 0);
					break;
				case LuaType.Number:
					double number = lua.GetNumber(i + 1);
					double intPart = Math.Truncate(number);
					double fracPart = double.IsInfinity(number) ? 0.0 : number - intPart;
					if (fracPart == 0.0)
						rc = raw.sqlite3_bind_int64(stmt, i, LuaHelper.cvttsd2si64(intPart));
					else
						rc = raw.sqlite3_bind_double(stmt, i, number);
					break;
				case LuaType.String:
					rc = raw.sqlite3_bind_text(stmt, i, lua.GetStringBytes(i + 1));
					break;
				default:
					raw.sqlite3_finalize(stmt);
					return SetLastErrorAndReturn(lua, "unsupported parameter type for binding");
			}

			if (rc != raw.SQLITE_OK) {
				raw.sqlite3_finalize(stmt);
				return SetLastErrorAndReturn(lua, raw.sqlite3_errmsg(pDatabase).utf8_to_string());
			}
		}

		LuaTable results = new(null, 0);
		int rowIndex = 1;
		int step;
		while ((step = raw.sqlite3_step(stmt)) == raw.SQLITE_ROW) {
			int columns = raw.sqlite3_column_count(stmt);
			LuaTable row = new(null, 0);
			for (int i = 0; i < columns; i++) {
				string name = raw.sqlite3_column_name(stmt, i).utf8_to_string();
				switch (raw.sqlite3_column_type(stmt, i)) {
					case raw.SQLITE_INTEGER:
						long value = raw.sqlite3_column_int64(stmt, i);
						string? declType = raw.sqlite3_column_decltype(stmt, i).utf8_to_string();
						if (declType != null && (declType.Length == 4 || declType.Length == 7) && stricmp(declType, declType.Length == 4 ? "bool" : "boolean") == 0)
							row.SetMember(name, value != 0);
						else if ((ulong)(value + 0x1FFFFFFFFFFFFF) <= 0x3FFFFFFFFFFFFE)
							row.SetMemberDouble(name, value);
						else
							row.SetMember(name, value.ToString());
						break;
					case raw.SQLITE_FLOAT:
						row.SetMemberDouble(name, raw.sqlite3_column_double(stmt, i));
						break;
					case raw.SQLITE_TEXT:
					case raw.SQLITE_BLOB:
						row.SetMember(name, raw.sqlite3_column_blob(stmt, i));
						break;
					case raw.SQLITE_NULL:
						row.SetMemberNil(name);
						break;
				}
			}

			results.SetMember(rowIndex, row);
			row.UnReference();
			rowIndex++;
		}

		if (step == raw.SQLITE_DONE) {
			raw.sqlite3_finalize(stmt);
			results.Push();
			results.UnReference();
			return 1;
		}

		raw.sqlite3_finalize(stmt);
		results.UnReference();
		return SetLastErrorAndReturn(lua, raw.sqlite3_errmsg(pDatabase).utf8_to_string());
	}
}
#endif
