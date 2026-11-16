#if CLIENT_DLL
global using BaseCombatCharacter = Game.Client.C_BaseCombatCharacter;
global using BaseAnimating = Game.Client.C_BaseAnimating;
global using BoneManipulate = Game.Client.C_BoneManipulate;
#endif

#if GAME_DLL
global using BaseCombatCharacter = Game.Server.BaseCombatCharacter;
#endif
