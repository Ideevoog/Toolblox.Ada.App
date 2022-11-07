using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Toolblox.Ada.App.Functions.Helpers
{
	internal static class StringExtensions
	{
		internal static string Sanitize(this string obj)
		{
			return obj == null ? null : obj.ToString().Replace("'", "");
		}
	}
}
