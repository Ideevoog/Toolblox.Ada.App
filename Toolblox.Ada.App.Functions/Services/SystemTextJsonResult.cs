using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Toolblox.Ada.App.Model;

namespace Toolblox.Ada.App.Functions.Services
{
    public class SystemTextJsonResult : ContentResult
    {
        private const string ContentTypeApplicationJson = "application/json";

        public SystemTextJsonResult(object value, JsonSerializerOptions options = null)
        {
            ContentType = ContentTypeApplicationJson;
            Content = options == null ? JsonSerializer.Serialize(value, new JsonSerializerOptions().ConfigureToolbloxInheritance()) : JsonSerializer.Serialize(value, options);
        }
    }
}
