using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;

using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace Source.GUI.Controls;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class UiMessageAttribute(string message) : Attribute
{
	public string Message { get; } = message;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class UiCommandAttribute(string command) : Attribute
{
	public string Command { get; } = command;
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class MessageParamAttribute(string key) : Attribute
{
	public string Key { get; } = key;
}

public delegate void PanelMessageHandler(Panel panel, KeyValues message, IPanel? sender);
public delegate void PanelCommandHandler(Panel panel, IPanel? sender);

public sealed class PanelHandlerTable
{
	readonly FrozenDictionary<string, PanelMessageHandler> messages;
	readonly FrozenDictionary<string, PanelCommandHandler>.AlternateLookup<ReadOnlySpan<char>> commands;

	internal PanelHandlerTable(Dictionary<string, PanelMessageHandler> messages, Dictionary<string, PanelCommandHandler> commands) {
		this.messages = messages.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
		this.commands = commands.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase).GetAlternateLookup<ReadOnlySpan<char>>();
	}

	public bool TryGetMessage(string message, out PanelMessageHandler handler) => messages.TryGetValue(message, out handler!);
	public bool TryGetCommand(ReadOnlySpan<char> command, out PanelCommandHandler handler) => commands.TryGetValue(command, out handler!);
}

public static class PanelMessageMap
{
	static readonly Dictionary<Type, Dictionary<string, PanelMessageHandler>> messageHandlers = [];
	static readonly Dictionary<Type, Dictionary<string, PanelCommandHandler>> commandHandlers = [];
	static readonly ConcurrentDictionary<Type, PanelHandlerTable> tables = [];

	public static void AddMessageHandler(Type type, string message, PanelMessageHandler handler) {
		lock (messageHandlers) {
			if (!messageHandlers.TryGetValue(type, out Dictionary<string, PanelMessageHandler>? map))
				messageHandlers[type] = map = [];
			map[message] = handler;
		}
	}

	public static void AddCommandHandler(Type type, string command, PanelCommandHandler handler) {
		lock (messageHandlers) {
			if (!commandHandlers.TryGetValue(type, out Dictionary<string, PanelCommandHandler>? map))
				commandHandlers[type] = map = [];
			map[command] = handler;
		}
	}

	public static PanelHandlerTable GetTable(Type type) => tables.GetOrAdd(type, BuildTable);

	static PanelHandlerTable BuildTable(Type type) {
		List<Type> chain = [];
		for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
			chain.Add(t);

		Dictionary<string, PanelMessageHandler> messages = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, PanelCommandHandler> commands = new(StringComparer.OrdinalIgnoreCase);
		lock (messageHandlers) {
			for (int i = chain.Count - 1; i >= 0; i--) {
				if (messageHandlers.TryGetValue(chain[i], out Dictionary<string, PanelMessageHandler>? m))
					foreach (KeyValuePair<string, PanelMessageHandler> entry in m)
						messages[entry.Key] = entry.Value;
				if (commandHandlers.TryGetValue(chain[i], out Dictionary<string, PanelCommandHandler>? c))
					foreach (KeyValuePair<string, PanelCommandHandler> entry in c)
						commands[entry.Key] = entry.Value;
			}
		}

		return new PanelHandlerTable(messages, commands);
	}
}
