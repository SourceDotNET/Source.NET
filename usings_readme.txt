These Usings files are to allow:
    1. Defining typedefs among multiple projects
    2. Static libraries being defined only once in a project (see Dbg, Platform, etc.)

- Keep all using Type = type; statements in these files, where possible
- Do NOT overpopulate GlobalUsings; every engine project will probably use this file!!!
	- Instead, consider making a new Usings file, then including it both in Common and in your project
- For project-specific usings that do not require multi-projects, the naming convention is just "ProjectUsings.cs".