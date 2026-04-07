namespace Astral.Network.Tests.Services
{
	public interface ITheme
	{
		string Body { get => "bg-dark"; set { } }

		// Buttons
		string PrimaryButton { get => "bg-gray-800 text-white hover:bg-gray-900"; set { } }
		string SecondaryButton { get => "bg-gray-700 text-white hover:bg-gray-800"; set { } }
		string DangerButton { get => "bg-red-600 text-white hover:bg-red-700"; set { } }
		string LinkButton { get => "text-blue-400 hover:text-blue-500"; set { } }
		string IconButton { get => "text-gray-400 hover:text-white"; set { } }


		// Forms
		string InputField { get => "bg-gray-900 border border-gray-700 text-white rounded p-2"; set { } }
		string TextArea { get => "bg-gray-900 border border-gray-700 text-white rounded p-2"; set { } }
		string Label { get => "text-gray-300 font-medium"; set { } }
		string Checkbox { get => "accent-blue-500"; set { } }
		string RadioButton { get => "accent-blue-500"; set { } }
		string Select { get => "bg-gray-900 border border-gray-700 text-white rounded p-2"; set { } }
		string FormGroup { get => "mb-4"; set { } }
		string ValidationError { get => "text-red-500 text-sm"; set { } }

		// Layout
		string Card { get => "bg-gray-800 shadow rounded p-4"; set { } }
		string Container { get => "p-6 max-w-7xl mx-auto"; set { } }
		string Row { get => "flex flex-row flex-wrap"; set { } }
		string Column { get => "flex-1"; set { } }
		string Grid { get => "grid gap-4"; set { } }
		string Sidebar { get => "bg-gray-900 text-white p-4"; set { } }
		string Navbar { get => "bg-gray-900 text-white p-4 flex"; set { } }
		string Footer { get => "bg-gray-900 text-gray-300 p-4 text-center"; set { } }

		// Typography
		string Heading1 { get => "text-4xl font-bold text-white"; set { } }
		string Heading2 { get => "text-3xl font-bold text-white"; set { } }
		string Heading3 { get => "text-2xl font-bold text-white"; set { } }
		string Heading4 { get => "text-xl font-bold text-white"; set { } }
		string Heading5 { get => "text-lg font-bold text-white"; set { } }
		string Heading6 { get => "text-base font-bold text-white"; set { } }
		string Paragraph { get => "text-gray-300"; set { } }
		string SmallText { get => "text-gray-400 text-sm"; set { } }
		string Blockquote { get => "border-l-4 border-gray-600 pl-4 italic text-gray-400"; set { } }
		string LinkText { get => "text-blue-400 hover:text-blue-500"; set { } }


		// Alerts / Notifications
		string SuccessMessage { get => "text-green-500 font-bold"; set { } }
		string ErrorMessage { get => "text-red-500 font-bold"; set { } }
		string WarningMessage { get => "text-yellow-400 font-bold"; set { } }
		string InfoMessage { get => "text-blue-400 font-bold"; set { } }
		string Toast { get => "bg-gray-800 text-white shadow rounded p-2"; set { } }

		// Tables
		string Table { get => "table-auto w-full text-gray-300"; set { } }
		string TableHeader { get => "bg-gray-900 text-white"; set { } }
		string TableRow { get => "border-b border-gray-700"; set { } }
		string TableCell { get => "p-2"; set { } }
		string TableStripedRow { get => "bg-gray-800"; set { } }
		string TableHoverRow { get => "hover:bg-gray-700"; set { } }

		// Modals / Dialogs
		string Modal { get => "bg-gray-900 text-white shadow rounded p-4"; set { } }
		string ModalHeader { get => "font-bold text-xl mb-2"; set { } }
		string ModalBody { get => "mb-4"; set { } }
		string ModalFooter { get => "text-right"; set { } }

		// Navigation
		string Nav { get => "flex space-x-4"; set { } }
		string NavItem { get => ""; set { } }
		string NavLink { get => "text-gray-300 hover:text-white"; set { } }
		string ActiveNavLink { get => "text-white font-bold"; set { } }
		string Breadcrumb { get => "flex space-x-2 text-gray-400"; set { } }
		string BreadcrumbItem { get => ""; set { } }

		// Lists
		string List { get => "list-none"; set { } }
		string ListItem { get => "mb-2"; set { } }
		string OrderedList { get => "list-decimal pl-4"; set { } }
		string UnorderedList { get => "list-disc pl-4"; set { } }
		string ListGroup { get => "bg-gray-800 rounded"; set { } }
		string ListGroupItem { get => "p-2 border-b border-gray-700"; set { } }


		// Badges / Labels
		string Badge { get => "bg-gray-700 text-white rounded px-2 py-1 text-sm"; set { } }
		string BadgePrimary { get => "bg-blue-500 text-white"; set { } }
		string BadgeSecondary { get => "bg-gray-600 text-white"; set { } }
		string BadgeSuccess { get => "bg-green-500 text-white"; set { } }
		string BadgeDanger { get => "bg-red-500 text-white"; set { } }
		string BadgeWarning { get => "bg-yellow-400 text-black"; set { } }
		string BadgeInfo { get => "bg-blue-400 text-white"; set { } }

		// Cards / Panels
		string CardHeader { get => "font-bold mb-2"; set { } }
		string CardBody { get => ""; set { } }
		string CardFooter { get => "text-right"; set { } }

		// Progress / Loading
		string ProgressBar { get => "bg-gray-700 rounded h-2"; set { } }
		string ProgressBarStriped { get => "bg-gray-700 bg-gradient-to-r from-gray-600 via-gray-700 to-gray-600 animate-stripes h-2 rounded"; set { } }
		string Spinner { get => "border-4 border-gray-700 border-t-blue-500 rounded-full w-6 h-6 animate-spin"; set { } }

		// Misc / Utility
		string Divider { get => "border-t border-gray-700 my-2"; set { } }
		string Tooltip { get => "bg-gray-800 text-white text-sm p-1 rounded shadow"; set { } }
		string Popover { get => "bg-gray-900 text-white shadow rounded p-2"; set { } }
		string Avatar { get => "rounded-full border border-gray-700"; set { } }
		string Tag { get => "bg-gray-700 text-white rounded px-2 py-1 text-sm"; set { } }
		string Link { get => "text-blue-400 hover:text-blue-500"; set { } }
	}



	public class DefaultDarkTheme : ITheme
	{
		public string PrimaryButton { get; set; } = "bg-blue-500 text-white hover:bg-blue-600";
		public string SecondaryButton { get; set; } = "bg-gray-200 text-black hover:bg-gray-300";

		public string InputField { get; set; } = "border border-gray-300 rounded p-2";
		public string Label { get; set; } = "text-gray-700 font-medium";

		public string Card { get; set; } = "bg-white shadow rounded p-4";
		public string Container { get; set; } = "p-6 max-w-7xl mx-auto";

		public string Heading { get; set; } = "text-2xl font-bold";
		public string Paragraph { get; set; } = "text-base text-gray-800";

		public string SuccessMessage { get; set; } = "text-green-600 font-bold";
		public string ErrorMessage { get; set; } = "text-red-600 font-bold";
	}

	public class ThemeService
	{
		static Dictionary<string, ITheme> Themes { get; set; } = new Dictionary<string, ITheme>();
		public string ThemeClass { get; private set; } = "dark";

		static bool IsDark = true;
		static string DarkThemeName = "";
		static string LightThemeName = "";

		static ITheme PrivateCurrentTheme;
		public ITheme CurrentTheme => PrivateCurrentTheme;

		public event Action? OnChanged;

		static ThemeService()
		{
			DarkThemeName = "default";
			PrivateCurrentTheme = new DefaultDarkTheme();
			Themes.Add(DarkThemeName, PrivateCurrentTheme);
		}

		public void SetDarkThemeName(string Name)
		{
			if (!Themes.ContainsKey(Name)) return;
			DarkThemeName = Name;
		}

		public void SetLightThemeName(string Name)
		{
			if (!Themes.ContainsKey(Name)) return;
			LightThemeName = Name;
		}

		public void Toggle()
		{
			if (IsDark)
			{
				if (Themes.TryGetValue(LightThemeName, out var LightTheme))
				{
					PrivateCurrentTheme = LightTheme;
					NotifyThemeChanged();
				}
			}
			else
			{
				if (Themes.TryGetValue(DarkThemeName, out var DarkTheme))
				{
					PrivateCurrentTheme = DarkTheme;
					NotifyThemeChanged();
				}
			}
			//ThemeClass = ThemeClass == "dark" ? "" : "dark";
			//NotifyThemeChanged();
		}

		private void NotifyThemeChanged() => OnChanged?.Invoke();
	}
}
