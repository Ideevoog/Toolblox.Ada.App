using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Toolblox.Ada.App.Functions;
using DinkToPdf;

[assembly: FunctionsStartup(typeof(Startup))]
namespace Toolblox.Ada.App.Functions
{
	public class Startup : FunctionsStartup
	{
		public override void Configure(IFunctionsHostBuilder builder)
		{
			builder.Services.AddSingleton<SynchronizedConverter>((s)
				=> new(new PdfTools()));
		}
	}
}
