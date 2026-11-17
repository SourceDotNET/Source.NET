
using Game.Shared;

using Source.Common;

namespace Game.Server.NextBot;

[LinkEntityToClass("func_nav_prerequisite")]
class FuncNavPrerequisite : BaseTrigger
{
	int Task;
	string TaskEntityName;
	float TaskValue;
	new bool Disabled;
	EHANDLE TaskEntity;
}