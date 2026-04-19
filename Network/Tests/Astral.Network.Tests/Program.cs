using Astral.Network.Tests.Components;
using Astral.Network.Tests.Services;
using Astral.Tick;
using Blazored.LocalStorage;
using System.Security.Principal;

namespace Astral.Network.Tests
{
	public class Program
	{
		//static bool IsRunAsAdmin()
		//{
		//	using var identity = WindowsIdentity.GetCurrent();
		//	var principal = new WindowsPrincipal(identity);
		//	return principal.IsInRole(WindowsBuiltInRole.Administrator);
		//}

		public static void Main(string[] args)
		{
			//if (!IsRunAsAdmin())
			//{
			//	// Relaunch the current process with admin privileges
			//	var exeName = Process.GetCurrentProcess().MainModule.FileName;
			//	var startInfo = new ProcessStartInfo(exeName)
			//	{
			//		UseShellExecute = true,
			//		Verb = "runas" // triggers UAC prompt
			//	};
			//
			//	try
			//	{
			//		Process.Start(startInfo);
			//	}
			//	catch
			//	{
			//		Console.WriteLine("User declined elevation. Exiting...");
			//	}
			//
			//	return; // exit the non-elevated process
			//}

			Context.SetThreadAffinity(2);

            Thread.CurrentThread.Name = "MainThread";

			var builder = WebApplication.CreateBuilder(args);

			// Add services to the container.
			builder.Services.AddRazorComponents()
				.AddInteractiveServerComponents();

			builder.Services.AddSingleton<ThemeService>();
			builder.Services.AddBlazoredLocalStorage();

			var app = builder.Build();

			// Configure the HTTP request pipeline.
			if (!app.Environment.IsDevelopment())
			{
				app.UseExceptionHandler("/Error");
			}

			app.UseStaticFiles();
			app.UseAntiforgery();

			app.MapRazorComponents<App>()
				.AddInteractiveServerRenderMode();

			app.Run();
		}
	}
}
