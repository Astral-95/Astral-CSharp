using Astral.Diagnostics;
using System.Collections.Concurrent;
using System.Diagnostics;

public static class PooledObjectsTracker
{
	enum InfoDetail
	{
		None,
		Name,
		Stack
	}

	static readonly ConcurrentDictionary<object, string> Active = new();

	static InfoDetail LogDetail = InfoDetail.Name;
	static long TotalPooledObjects = 0;

	[Conditional("DEBUG")]
	[Conditional("DEVELOPMENT")]
	static public void OnNewPoolObject() { Interlocked.Increment(ref TotalPooledObjects); }
	static public long GetTotalPooledObjects() { return Interlocked.Read(ref TotalPooledObjects); }
	static public long GetRentedObjectsCount() { return Active.Count; }

	[Conditional("DEBUG")]
	[Conditional("DEVELOPMENT")]
	public static void Register<T>(object obj)
	{
		Debug.Assert(Active.TryGetValue(obj, out _) == false);

		switch (LogDetail)
		{
			case InfoDetail.None:
				Active[obj] = "";
				return;
			case InfoDetail.Name:
				Active[obj] = typeof(T).Name;
				return;
			case InfoDetail.Stack:
				break;
		}

		var trace = new StackTrace(fNeedFileInfo: true);
		var lines = new List<string>();

		int framesToUse = Math.Min(trace.FrameCount - 1, 5);

		for (int i = 1; i <= framesToUse; i++)
		{
			var frame = trace.GetFrame(i);
			if (frame == null)
				continue;

			var method = frame.GetMethod();
			if (method == null || method.DeclaringType == null)
				continue;

			var type = method.DeclaringType;

			// Skip purely internal system/framework methods
			if (type.Namespace != null &&
				(type.Namespace.StartsWith("System.") || type.Namespace.StartsWith("Microsoft.")))
				continue;

			string methodName = method.Name;

			string file = frame.GetFileName() ?? "<unknown>";
			int line = frame.GetFileLineNumber();

			lines.Add($"\t\t{type.FullName}.{methodName} ({Path.GetFileName(file)}:{line})");
		}

		Active[obj] = string.Join(Environment.NewLine, lines);
	}

	[Conditional("DEBUG")]
	[Conditional("DEVELOPMENT")]
	public static void Register(object obj)
	{
		Debug.Assert(Active.TryGetValue(obj, out _) == false);

		switch (LogDetail)
		{
			case InfoDetail.None:
				Active[obj] = "";
				return;
			case InfoDetail.Name:
				Active[obj] = "";
				return;
			case InfoDetail.Stack:
				break;
		}

		var trace = new StackTrace(fNeedFileInfo: true);
		var lines = new List<string>();

		int framesToUse = Math.Min(trace.FrameCount - 1, 5);

		for (int i = 1; i <= framesToUse; i++)
		{
			var frame = trace.GetFrame(i);
			if (frame == null)
				continue;

			var method = frame.GetMethod();
			if (method == null || method.DeclaringType == null)
				continue;

			var type = method.DeclaringType;

			// Skip purely internal system/framework methods
			if (type.Namespace != null &&
				(type.Namespace.StartsWith("System.") || type.Namespace.StartsWith("Microsoft.")))
				continue;

			string methodName = method.Name;

			string file = frame.GetFileName() ?? "<unknown>";
			int line = frame.GetFileLineNumber();

			lines.Add($"\t\t{type.FullName}.{methodName} ({Path.GetFileName(file)}:{line})");
		}

		Active[obj] = string.Join(Environment.NewLine, lines);
	}

	[Conditional("DEBUG")]
	[Conditional("DEVELOPMENT")]
	public static void Unregister(object obj)
	{
		if (!Active.TryRemove(obj, out _))
		{
            Guard.Fail("Tried to unregister an object that wasnt registered.");
        }
	}

	public static List<string>? ReportLeaks()
	{
		if (Active.IsEmpty) return null;

		List<string> Leaks = new List<string>();
		Leaks.Add($"[PooledObjectsTracker]: {Active.Count} objects were leaked.");

		int Num = 0;
		foreach (var Kvp in Active)
		{
			switch (LogDetail)
			{
				case InfoDetail.None:
					Leaks.Add($"\t{Num}: Type: [{Kvp.Key.GetType().Name}].");
					break;
				case InfoDetail.Name:
					if (Kvp.Value == "")
					{
						Leaks.Add($"\t{Num}: Type: [{Kvp.Key.GetType().Name}].");
					}
					else
					{
						Leaks.Add($"\t{Num}: Type: [{Kvp.Key.GetType().Name}] Rented by: [{Kvp.Value}].");
					}
					break;
				case InfoDetail.Stack:
					Leaks.Add($"\t{Num}: Object: [{Kvp.Key.GetType().Name}] Rented at:\n{Kvp.Value}");
					break;
			}

			Num++;
		}

		return Leaks;
	}

	// ONLY FOR TEST CODES
	public static void ClearForTests()
	{
		Active.Clear();
	}
}