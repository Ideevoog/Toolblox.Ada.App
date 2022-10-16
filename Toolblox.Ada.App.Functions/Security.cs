//using Microsoft.IdentityModel.Tokens;
//using System;
//using System.Collections.Generic;
//using System.IdentityModel.Tokens.Jwt;
//using System.Linq;
//using System.Net.Http;
//using System.Security.Authentication;
//using System.Security.Claims;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.IdentityModel.Protocols;
//using Microsoft.IdentityModel.Protocols.OpenIdConnect;

//namespace Toolblox.Ada.App.Functions
//{
//    public static class Security
//    {
//        private static readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

//        //TODO:Config
//        private static readonly string ISSUER = "https://toolblox.eu.auth0.com/";
//        private static readonly string AUDIENCE = "http://localhost:7071/api/Function1";

//        static Security()
//        {
//            var documentRetriever = new HttpDocumentRetriever { RequireHttps = ISSUER.StartsWith("https://") };

//            _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
//                $"{ISSUER}.well-known/openid-configuration",
//                new OpenIdConnectConfigurationRetriever(),
//                documentRetriever
//            );
//        }

//        public static async Task<string> GetUser(HttpRequestMessage req, bool throwOnError = true)
//        {
//            var user = await Security.ValidateTokenAsync(req.Headers.Authorization?.ToString());
//            if (user == null && throwOnError)
//            {
//                throw new AuthenticationException();
//            }
//            var userId = user?.Claims.FirstOrDefault(c => c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
//            return userId;
//        }

//        public static async Task<ClaimsPrincipal> ValidateTokenAsync(string authorizationHeader)
//        {
//            if (string.IsNullOrEmpty(authorizationHeader))
//            {
//                return null;
//            }

//            if (!authorizationHeader.Contains("Bearer"))
//            {
//                return null;
//            }

//            var accessToken = authorizationHeader.Substring("Bearer ".Length);

//            var config = await _configurationManager.GetConfigurationAsync(CancellationToken.None);

//            var validationParameter = new TokenValidationParameters
//            {
//                RequireSignedTokens = true,
//                ValidAudience = AUDIENCE,
//                ValidateAudience = true,
//                ValidIssuer = ISSUER,
//                ValidateIssuer = true,
//                ValidateIssuerSigningKey = true,
//                ValidateLifetime = true,
//                IssuerSigningKeys = config.SigningKeys
//            };

//            ClaimsPrincipal result = null;
//            var tries = 0;

//            while (result == null && tries <= 1)
//            {
//                try
//                {
//                    var handler = new JwtSecurityTokenHandler();
//                    result = handler.ValidateToken(accessToken, validationParameter, out var token);
//                }
//                catch (SecurityTokenSignatureKeyNotFoundException ex1)
//                {
//                    // This exception is thrown if the signature key of the JWT could not be found.
//                    // This could be the case when the issuer changed its signing keys, so we trigger a 
//                    // refresh and retry validation.
//                    _configurationManager.RequestRefresh();
//                    tries++;
//                }
//                catch (SecurityTokenException ex2)
//                {
//                    return null;
//                }
//            }

//            return result;
//        }
//    }
//}
