using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
			//builder.Services.AddHttpClient();

			builder.Services.AddSingleton<SynchronizedConverter>((s)
				=> new(new PdfTools()));

			//builder.Services.AddSingleton<ILoggerProvider, MyLoggerProvider>();
		}
	}
}
