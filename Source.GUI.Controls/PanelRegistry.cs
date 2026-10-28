namespace Source.GUI.Controls;

public static class PanelRegistry
{
	public readonly record struct Entry(Type Type, string Name, Func<Panel>? Factory, string? Alias);

	static readonly List<Entry> entries = [];
	static readonly Dictionary<Type, Action> animationVars = [];

	public static IReadOnlyList<Entry> Entries => entries;

	public static void Add(Type type, string name, Func<Panel>? factory, string? alias, Action? addAnimationVars) {
		lock (entries) {
			entries.Add(new(type, name, factory, alias));
			if (addAnimationVars != null)
				animationVars[type] = addAnimationVars;
		}
	}

	public static void AddAnimationVars(Type type) {
		Action? addAnimationVars;
		lock (entries)
			animationVars.TryGetValue(type, out addAnimationVars);

		addAnimationVars?.Invoke();
	}
}
